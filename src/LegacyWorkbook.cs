using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;

namespace Hepta
{
    internal static class LegacyWorkbook
    {
        [ComImport, Guid("0000000b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface Storage
        {
            void CreateStream([MarshalAs(UnmanagedType.LPWStr)] string name, uint mode, uint r1, uint r2, out IStream stream);
            void OpenStream([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr reserved, uint mode, uint other, out IStream stream);
            void CreateStorage();
            void OpenStorage();
            void CopyTo();
            void MoveElementTo();
            void Commit(uint flags);
        }

        [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
        static extern int StgOpenStorage(string path, IntPtr priority, uint mode, IntPtr exclude, uint reserved, out Storage storage);
        [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
        internal static extern int StgCreateDocfile(string path, uint mode, uint reserved, out Storage storage);
        static string Unicode(BinaryReader r, int count)
        {
            byte flags = r.ReadByte();
            int runs = (flags & 8) != 0 ? r.ReadUInt16() : 0;
            uint extra = (flags & 4) != 0 ? r.ReadUInt32() : 0;
            int bytes = count * ((flags & 1) != 0 ? 2 : 1);
            byte[] data = r.ReadBytes(bytes);
            if (data.Length != bytes)
                throw new InvalidDataException("Spanned XLS string");
            string s = ((flags & 1) != 0 ? Encoding.Unicode : Encoding.GetEncoding(28591)).GetString(data);
            r.BaseStream.Position += runs * 4L + extra;
            if (r.BaseStream.Position > r.BaseStream.Length)
                throw new EndOfStreamException();
            return s;
        }

        static string Num(double d)
        {
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        static double Rk(uint rk)
        {
            double d = (rk & 2) != 0 ? (int)rk >> 2 : BitConverter.Int64BitsToDouble((long)(rk & 0xfffffffc) << 32);
            return (rk & 1) != 0 ? d / 100 : d;
        }

        public static List<string[]> Read(string path, CancellationToken token)
        {
            Storage storage;
            Marshal.ThrowExceptionForHR(StgOpenStorage(path, IntPtr.Zero, 0x20, IntPtr.Zero, 0, out storage));
            byte[] data;
            try
            {
                IStream stream;
                storage.OpenStream("Workbook", IntPtr.Zero, 0x10, 0, out stream);
                try
                {
                    System.Runtime.InteropServices.ComTypes.STATSTG stat;
                    stream.Stat(out stat, 1);
                    if (stat.cbSize < 8 || stat.cbSize > 16000000)
                        throw new InvalidDataException("XLS stream size");
                    data = new byte[(int)stat.cbSize];
                    stream.Read(data, data.Length, IntPtr.Zero);
                }
                finally
                {
                    Marshal.FinalReleaseComObject(stream);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(storage);
            }

            var strings = new List<string>();
            var formats = new List<ushort>();
            int sheet = -1, sheets = 0;
            var cells = new SortedDictionary<int, SortedDictionary<int, string>>();
            using (var input = new BinaryReader(new MemoryStream(data)))
            {
                while (input.BaseStream.Position + 4 <= data.Length)
                {
                    token.ThrowIfCancellationRequested();
                    int offset = (int)input.BaseStream.Position, id = input.ReadUInt16(), length = input.ReadUInt16();
                    if (offset + 4 + length > data.Length)
                        throw new InvalidDataException("XLS record length");
                    using (var record = new BinaryReader(new MemoryStream(input.ReadBytes(length))))
                    {
                        if (id == 0x2f)
                            throw new InvalidDataException("Encrypted workbook");
                        if (id == 0x85)
                        {
                            int loc = record.ReadInt32();
                            record.ReadByte();
                            if (record.ReadByte() == 0)
                            {
                                sheet = loc;
                                sheets++;
                            }

                            continue;
                        }

                        if (id == 0xe0)
                        {
                            record.ReadUInt16();
                            formats.Add(record.ReadUInt16());
                            continue;
                        }

                        if (id == 0xfc)
                        {
                            record.ReadUInt32();
                            uint unique = record.ReadUInt32();
                            if (unique > 1000000)
                                throw new InvalidDataException("XLS shared strings limit");
                            for (uint i = 0; i < unique; i++)
                                strings.Add(Unicode(record, record.ReadUInt16()));
                            continue;
                        }

                        if (sheet < 0 || offset < sheet)
                            continue;
                        if (sheets != 1)
                            throw new InvalidDataException("Single worksheet required");
                        if (id == 0x0a)
                            break;
                        if (id == 0x06 || id == 0xbd || id == 0xe5 || id == 0xd6)
                            throw new InvalidDataException("Formula, merged or complex XLS cells");
                        if (id != 0xfd && id != 0x203 && id != 0x27e && id != 0x204 && id != 0x205)
                            continue;
                        int row = record.ReadUInt16(), col = record.ReadUInt16(), xf = record.ReadUInt16();
                        if (col >= 256)
                            throw new InvalidDataException("XLS column");
                        string val;
                        if (id == 0xfd)
                            val = strings[record.ReadInt32()];
                        else if (id == 0x204)
                            val = Unicode(record, record.ReadUInt16());
                        else if (id == 0x205)
                        {
                            var b = record.ReadByte();
                            if (record.ReadByte() != 0)
                                throw new InvalidDataException("XLS cell error");
                            val = b == 0 ? "false" : "true";
                        }
                        else
                        {
                            if (xf >= formats.Count || formats[xf] != 0)
                                throw new InvalidDataException("Formatted numeric cells");
                            val = Num(id == 0x203 ? record.ReadDouble() : Rk(record.ReadUInt32()));
                        }

                        SortedDictionary<int, string> entries;
                        if (!cells.TryGetValue(row, out entries))
                            cells[row] = entries = new SortedDictionary<int, string>();
                        entries.Add(col, val);
                    }
                }
            }

            if (sheets != 1 || cells.Count == 0)
                throw new InvalidDataException("Unsupported or empty BIFF8 workbook");
            var rows = new List<string[]>();
            long count = 0;
            foreach (var pair in cells)
            {
                while (rows.Count < pair.Key)
                    rows.Add(new string[0]);
                int n = pair.Value.Keys.Max() + 1;
                if ((count += n) > 1000000)
                    throw new InvalidDataException("Workbook cell limit");
                var row = Enumerable.Repeat("", n).ToArray();
                foreach (var c in pair.Value)
                    row[c.Key] = c.Value;
                rows.Add(row);
            }

            return rows;
        }
    }
}
