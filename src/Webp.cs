using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Hepta
{
    internal static class Webp
    {
        sealed class Bits
        {
            readonly Stream stream;
            uint value;
            int used;
            public Bits(Stream dest)
            {
                stream = dest;
            }

            public void Put(int bits, int n)
            {
                value |= (uint)bits << used;
                used += n;
                while (used >= 8)
                {
                    stream.WriteByte((byte)value);
                    value >>= 8;
                    used -= 8;
                }
            }

            public void Finish()
            {
                if (used > 0)
                    stream.WriteByte((byte)value);
            }
        }

        static int Reverse(int value, int n)
        {
            int result = 0;
            while (n-- > 0)
            {
                result = (result << 1) | (value & 1);
                value >>= 1;
            }

            return result;
        }

        sealed class Codes
        {
            readonly int[] lengths = new int[256], codes = new int[256];
            readonly int[] values;
            public Codes(bool[] seen)
            {
                values = Enumerable.Range(0, 256).Where(i => seen[i]).ToArray();
                if (values.Length < 2)
                    return;
                int k = 1;
                while ((1 << k) < values.Length)
                    k++;
                int shortCount = (1 << k) - values.Length;
                for (int i = 0; i < values.Length; i++)
                {
                    int symbol = values[i];
                    lengths[symbol] = i < shortCount ? k - 1 : k;
                    codes[symbol] = Reverse(i < shortCount ? i : shortCount + i, lengths[symbol]);
                }
            }

            public void Header(Bits writer, bool green)
            {
                if (values.Length == 1)
                {
                    writer.Put(1, 1);
                    writer.Put(0, 1);
                    int v = values[0];
                    writer.Put(v > 1 ? 1 : 0, 1);
                    writer.Put(v, v > 1 ? 8 : 1);
                    return;
                }

                // Encode literal length values 0..15 using a complete four-bit prefix tree.
                writer.Put(0, 1);
                writer.Put(15, 4);
                int[] order =
                {
                    17,
                    18,
                    0,
                    1,
                    2,
                    3,
                    4,
                    5,
                    16,
                    6,
                    7,
                    8,
                    9,
                    10,
                    11,
                    12,
                    13,
                    14,
                    15
                };
                foreach (int n in order)
                    writer.Put(n < 16 ? 4 : 0, 3);
                writer.Put(0, 1);
                for (int i = 0; i < (green ? 280 : 256); i++)
                    writer.Put(Reverse(i < 256 ? lengths[i] : 0, 4), 4);
            }

            public void Pixel(Bits w, int value)
            {
                w.Put(codes[value], lengths[value]);
            }
        }

        public static void Save(Image image, string path, CancellationToken token)
        {
            int w = image.Width, h = image.Height;
            if (w > 16384 || h > 16384 || (long)w * h > 16000000)
                throw new InvalidDataException("WebP dimensions");
            using (var b = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(b))
                {
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.DrawImageUnscaled(image, 0, 0);
                }

                var data = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new byte[w * 4];
                    var present = new[]
                    {
                        new bool[256],
                        new bool[256],
                        new bool[256],
                        new bool[256]
                    };
                    int[] order =
                    {
                        1,
                        2,
                        0,
                        3
                    };
                    for (int y = 0; y < h; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                        for (int x = 0; x < row.Length; x++)
                            present[x % 4][row[x]] = true;
                    }

                    var codes = present.Select(seen => new Codes(seen)).ToArray();
                    using (var stream = File.Create(path))
                    using (var channel = new BinaryWriter(stream))
                    {
                        channel.Write(Encoding.ASCII.GetBytes("RIFF"));
                        channel.Write(0);
                        channel.Write(Encoding.ASCII.GetBytes("WEBPVP8L"));
                        channel.Write(0);
                        channel.Write((byte)0x2f);
                        var bits = new Bits(stream);
                        bits.Put(w - 1, 14);
                        bits.Put(h - 1, 14);
                        bits.Put(present[3].Take(255).Any(v => v) ? 1 : 0, 1);
                        bits.Put(0, 3);
                        bits.Put(0, 3);
                        foreach (int c in order)
                            codes[c].Header(bits, c == 1);
                        bits.Put(1, 1);
                        bits.Put(0, 3);
                        for (int y = 0; y < h; y++)
                        {
                            token.ThrowIfCancellationRequested();
                            Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                            for (int i = 0; i < row.Length; i += 4)
                                foreach (int c in order)
                                    codes[c].Pixel(bits, row[i + c]);
                        }

                        bits.Finish();
                        int chunk = checked((int)stream.Length - 20);
                        if ((chunk & 1) != 0)
                            channel.Write((byte)0);
                        long length = stream.Length;
                        stream.Position = 4;
                        channel.Write((int)length - 8);
                        stream.Position = 16;
                        channel.Write(chunk);
                    }
                }
                finally
                {
                    b.UnlockBits(data);
                }
            }
        }
    }
}
