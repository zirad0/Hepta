using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;

namespace Hepta
{
    internal static class WindowsChecks
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Bounds
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr h, out Bounds rect);
        public static void Monitors()
        {
            var w = new Window
            {
                Width = 320,
                Height = 320,
                WindowStyle = WindowStyle.None,
                ShowActivated = false,
                ShowInTaskbar = false,
                Opacity = 0
            };
            try
            {
                w.Show();
                var hwnd = new WindowInteropHelper(w).Handle;
                foreach (var screen in Screen.AllScreens)
                {
                    var a = screen.WorkingArea;
                    foreach (var p in new[]
                    {
                        new System.Drawing.Point(a.Left, a.Top),
                        new System.Drawing.Point(a.Right - 1, a.Bottom - 1)
                    }

                    )
                    {
                        Screens.Position(w, screen, p, false, false, 0);
                        Bounds b;
                        if (!GetWindowRect(hwnd, out b) || b.Left < a.Left || b.Top < a.Top || b.Right > a.Right || b.Bottom > a.Bottom)
                            throw new Exception("Monitor bounds " + screen.DeviceName);
                    }

                    Console.WriteLine("MONITOR " + screen.DeviceName + " " + a);
                }
            }
            finally
            {
                w.Close();
            }
        }

        public static void ShortcutConflict()
        {
            var first = new WindowInteropHelper(new Window()).EnsureHandle();
            var second = new WindowInteropHelper(new Window()).EnsureHandle();
            // Use a four-modifier function-key chord to avoid interfering with normal shortcuts.
            if (!ShellIntegration.RegisterHotKey(first, 900, 0x400f, 0x87))
            {
                if (Marshal.GetLastWin32Error() == 5)
                    throw new UnauthorizedAccessException("Win32 access denied (5)");
                throw new Exception("Test key unavailable");
            }

            try
            {
                if (ShellIntegration.RegisterHotKey(second, 901, 0x400f, 0x87))
                {
                    ShellIntegration.UnregisterHotKey(second, 901);
                    throw new Exception("Duplicate global shortcut registered");
                }
            }
            finally
            {
                ShellIntegration.UnregisterHotKey(first, 900);
            }
        }

        public static void Xls(string path)
        {
            LegacyWorkbook.Storage storage;
            Marshal.ThrowExceptionForHR(LegacyWorkbook.StgCreateDocfile(path, 0x1012, 0, out storage));
            try
            {
                System.Runtime.InteropServices.ComTypes.IStream stream;
                storage.CreateStream("Workbook", 0x1012, 0, 0, out stream);
                try
                {
                    using (var bytes = new MemoryStream())
                    using (var w = new BinaryWriter(bytes))
                    {
                        Action<ushort, byte[]> rec = (id, raw) =>
                        {
                            w.Write(id);
                            w.Write((ushort)raw.Length);
                            w.Write(raw);
                        };
                        byte[] bof =
                        {
                            0,
                            6,
                            5,
                            0,
                            0xbb,
                            0x0d,
                            0xcc,
                            7,
                            0,
                            0,
                            0,
                            0,
                            6,
                            0,
                            0,
                            0
                        };
                        rec(0x809, bof);
                        long patch = bytes.Position + 4;
                        rec(0x85, new byte[] { 0, 0, 0, 0, 0, 0, 1, 0, 65 });
                        rec(0xa, new byte[0]);
                        long start = bytes.Position;
                        bytes.Position = patch;
                        w.Write((int)start);
                        bytes.Position = start;
                        bof[2] = 0x10;
                        rec(0x809, bof);
                        using (var body = new MemoryStream())
                        using (var b = new BinaryWriter(body))
                        {
                            b.Write((ushort)0);
                            b.Write((ushort)0);
                            b.Write((ushort)0);
                            b.Write((ushort)5);
                            b.Write((byte)1);
                            b.Write(System.Text.Encoding.Unicode.GetBytes("Şehir"));
                            rec(0x204, body.ToArray());
                        }

                        rec(0xa, new byte[0]);
                        byte[] data = bytes.ToArray();
                        stream.Write(data, data.Length, IntPtr.Zero);
                        stream.Commit(0);
                    }
                }
                finally
                {
                    Marshal.FinalReleaseComObject(stream);
                }

                storage.Commit(0);
            }
            finally
            {
                Marshal.FinalReleaseComObject(storage);
            }
        }

        public static void Rar(string path)
        {
            Func<byte[], uint> crc = bytes =>
            {
                uint value = 0xffffffff;
                foreach (byte b in bytes)
                {
                    value ^= b;
                    for (int j = 0; j < 8; j++)
                        value = (value & 1) != 0 ? 0xedb88320 ^ (value >> 1) : value >> 1;
                }

                return ~value;
            };
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 7, 0 });
                Action<byte, ushort, byte[]> header = (type, flags, payload) =>
                {
                    using (var ms = new MemoryStream())
                    using (var h = new BinaryWriter(ms))
                    {
                        h.Write(type);
                        h.Write(flags);
                        h.Write((ushort)(payload.Length + 7));
                        h.Write(payload);
                        byte[] b = ms.ToArray();
                        w.Write((ushort)crc(b));
                        w.Write(b);
                    }
                };
                header(0x73, 0, new byte[6]);
                byte[] data = System.Text.Encoding.UTF8.GetBytes("Bursa");
                using (var f = new MemoryStream())
                using (var h = new BinaryWriter(f))
                {
                    h.Write((uint)data.Length);
                    h.Write((uint)data.Length);
                    h.Write((byte)2);
                    h.Write(crc(data));
                    h.Write(0u);
                    h.Write((byte)20);
                    h.Write((byte)0x30);
                    h.Write((ushort)5);
                    h.Write(32u);
                    h.Write(System.Text.Encoding.ASCII.GetBytes("a.txt"));
                    header(0x74, 0x8000, f.ToArray());
                }

                w.Write(data);
                header(0x7b, 0, new byte[0]);
            }
        }
    }
}
