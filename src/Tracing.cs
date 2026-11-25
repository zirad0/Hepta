using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace Hepta
{
    internal static class Tracing
    {
        // Directed contour edges share grid vertices; only boundaries between colours survive.
        public static void Write(Image image, string output, int detail, CancellationToken token)
        {
            int limit = Math.Max(64, Math.Min(768, detail * 6));
            double factor = Math.Min(1, limit / (double)Math.Max(image.Width, image.Height));
            using (var bitmap = new Bitmap(image, Math.Max(1, (int)(image.Width * factor)), Math.Max(1, (int)(image.Height * factor))))
            using (var w = new StreamWriter(output))
            {
                int width = bitmap.Width, height = bitmap.Height, stride = width + 1;
                var pixels = new int[width * height];
                for (int y = 0; y < height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (int x = 0; x < width; x++)
                    {
                        var c = bitmap.GetPixel(x, y);
                        pixels[y * width + x] = c.A < 8 ? 0 : (c.A & 0xf0) << 24 | (c.R & 0xf0) << 16 | (c.G & 0xf0) << 8 | (c.B & 0xf0);
                    }
                }

                var groups = new Dictionary<int, Dictionary<int, byte>>();
                int edges = 0;
                for (int y = 0; y < height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (int x = 0; x < width; x++)
                    {
                        int i = y * width + x, color = pixels[i];
                        if (color == 0)
                            continue;
                        Dictionary<int, byte> map;
                        if (!groups.TryGetValue(color, out map))
                            groups[color] = map = new Dictionary<int, byte>();
                        int v = y * stride + x;
                        if (y == 0 || pixels[i - width] != color)
                        {
                            Edge(map, v, 0);
                            edges++;
                        }

                        if (x == width - 1 || pixels[i + 1] != color)
                        {
                            Edge(map, v + 1, 1);
                            edges++;
                        }

                        if (y == height - 1 || pixels[i + width] != color)
                        {
                            Edge(map, v + stride + 1, 2);
                            edges++;
                        }

                        if (x == 0 || pixels[i - 1] != color)
                        {
                            Edge(map, v + stride, 3);
                            edges++;
                        }

                        if (edges > 1800000)
                            throw new InvalidDataException("Vector detail limit");
                    }
                }

                w.WriteLine("<svg xmlns='http://www.w3.org/2000/svg' width='{0}' height='{1}' viewBox='0 0 {2} {3}'>", image.Width, image.Height, width, height);
                int[] step =
                {
                    1,
                    stride,
                    -1,
                    -stride
                };
                foreach (var g in groups)
                {
                    token.ThrowIfCancellationRequested();
                    w.Write("<path fill='#{0:X6}' fill-opacity='{1}' d='", g.Key & 0xffffff, (((uint)g.Key >> 24) / 240d).ToString("0.###", CultureInfo.InvariantCulture));
                    var map = g.Value;
                    foreach (int start in map.Keys.ToArray())
                    {
                        while (map.ContainsKey(start))
                        {
                            token.ThrowIfCancellationRequested();
                            int at = start, dir = 0;
                            while ((map[at] & (1 << dir)) == 0)
                                dir++;
                            w.Write("M{0} {1}", at % stride, at / stride);
                            do
                            {
                                int mask = map[at] & ~(1 << dir);
                                if (mask == 0)
                                    map.Remove(at);
                                else
                                    map[at] = (byte)mask;
                                at += step[dir];
                                if (at == start)
                                    break;
                                int next = dir;
                                foreach (int turn in new[]
                                {
                                    1,
                                    0,
                                    3,
                                    2
                                }

                                )
                                {
                                    int candidate = (dir + turn) % 4;
                                    if ((map[at] & (1 << candidate)) != 0)
                                    {
                                        next = candidate;
                                        break;
                                    }
                                }

                                if (next != dir)
                                    w.Write("L{0} {1}", at % stride, at / stride);
                                dir = next;
                            }
                            while (true);
                            w.Write("Z");
                        }
                    }

                    w.WriteLine("'/>");
                }

                w.Write("</svg>");
            }
        }

        static void Edge(Dictionary<int, byte> map, int vertex, int direction)
        {
            byte mask;
            map.TryGetValue(vertex, out mask);
            map[vertex] = (byte)(mask | 1 << direction);
        }
    }
}
