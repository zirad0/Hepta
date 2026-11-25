using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Hepta
{
    internal static class PdfText
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Attr
        {
            public Guid Guid;
            public uint Kind;
            public IntPtr Value;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Chunk
        {
            public uint Id, Break, Flags, Locale;
            public Attr Attribute;
            public uint Source, Start, Length;
        }

        [ComImport, Guid("89BCB740-6119-101A-BCB7-00DD010655AF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface Filter
        {
            [PreserveSig]
            int Init(uint flags, uint count, IntPtr attrs, out uint result);
            [PreserveSig]
            int GetChunk(out Chunk chunk);
            [PreserveSig]
            int GetText(ref uint size, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder buffer);
            [PreserveSig]
            int GetValue(out IntPtr value);
            [PreserveSig]
            int BindRegion(IntPtr region, ref Guid iid, out IntPtr result);
        }

        [ComImport, Guid("B824B49D-22AC-4161-AC8A-9916E8FA3F7F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface InitializeStream
        {
            void Initialize(System.Runtime.InteropServices.ComTypes.IStream stream, uint mode);
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int SHCreateStreamOnFileEx(string path, uint mode, uint attributes, bool create, IntPtr template, out System.Runtime.InteropServices.ComTypes.IStream stream);
        [DllImport("query.dll", CharSet = CharSet.Unicode)]
        static extern int LoadIFilter(string file, IntPtr outer, out Filter filter);
        public static string Read(string file, CancellationToken token)
        {
            Filter filter;
            System.Runtime.InteropServices.ComTypes.IStream stream = null;
            int result = LoadIFilter(file, IntPtr.Zero, out filter);
            if (result < 0)
            {
                Marshal.ThrowExceptionForHR(SHCreateStreamOnFileEx(file, 0x20, 0, false, IntPtr.Zero, out stream));
                object handler = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("6C337B26-3E38-4F98-813B-FBA18BAB64F5")));
                ((InitializeStream)handler).Initialize(stream, 0);
                filter = (Filter)handler;
            }

            try
            {
                uint flags;
                Marshal.ThrowExceptionForHR(filter.Init(1 | 2 | 4 | 8, 0, IntPtr.Zero, out flags));
                var text = new StringBuilder();
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    Chunk chunk;
                    int hr = filter.GetChunk(out chunk);
                    if (hr == unchecked((int)0x80041700))
                        break;
                    Marshal.ThrowExceptionForHR(hr);
                    if ((chunk.Flags & 1) == 0)
                        continue;
                    // Windows Search contents property: storage property set, PID_STG_CONTENTS.
                    if (chunk.Attribute.Guid != new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC") || chunk.Attribute.Kind != 1 || chunk.Attribute.Value.ToInt64() != 19)
                        continue;
                    if (text.Length > 0 && chunk.Break != 0)
                        text.AppendLine();
                    while (true)
                    {
                        uint capacity = 4096;
                        var buffer = new StringBuilder((int)capacity);
                        hr = filter.GetText(ref capacity, buffer);
                        if (hr == unchecked((int)0x80041701))
                            break;
                        Marshal.ThrowExceptionForHR(hr);
                        text.Append(buffer.ToString());
                        if (text.Length > 16000000)
                            throw new InvalidDataException("PDF text limit");
                        if (hr == 0x41709 || capacity == 0)
                            break;
                    }
                }

                if (string.IsNullOrWhiteSpace(text.ToString()))
                    throw new InvalidDataException("No readable PDF text layer");
                return text.ToString();
            }
            finally
            {
                Marshal.FinalReleaseComObject(filter);
                if (stream != null)
                    Marshal.FinalReleaseComObject(stream);
            }
        }
    }
}
