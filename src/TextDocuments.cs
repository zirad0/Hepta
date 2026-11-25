using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Hepta
{
    internal static class TextDocuments
    {
        static string Inline(string s)
        {
            string safe = System.Net.WebUtility.HtmlEncode(s);
            safe = Regex.Replace(safe, @"\*\*([^*\n]+)\*\*", "<strong>$1</strong>");
            return Regex.Replace(safe, @"\x60([^\x60\n]+)\x60", "<code>$1</code>");
        }

        public static string Markdown(string text)
        {
            var html = new StringBuilder("<!doctype html><meta charset=\"utf-8\"><article>");
            bool code = false, list = false;
            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (line.StartsWith(new string ((char)96, 3)))
                {
                    if (list)
                    {
                        html.Append("</ul>");
                        list = false;
                    }

                    html.Append(code ? "</code></pre>" : "<pre><code>");
                    code = !code;
                    continue;
                }

                if (code)
                {
                    html.AppendLine(System.Net.WebUtility.HtmlEncode(line));
                    continue;
                }

                bool bullet = line.StartsWith("- ") || line.StartsWith("* ");
                if (list && !bullet)
                {
                    html.Append("</ul>");
                    list = false;
                }

                if (bullet)
                {
                    if (!list)
                    {
                        html.Append("<ul>");
                        list = true;
                    }

                    html.Append("<li>" + Inline(line.Substring(2)) + "</li>");
                    continue;
                }

                var h = Regex.Match(line, @"^(#{1,6}) (.+)$");
                if (h.Success)
                {
                    string tag = "h" + h.Groups[1].Length;
                    html.Append("<" + tag + ">" + Inline(h.Groups[2].Value) + "</" + tag + ">");
                }
                else if (line.Length > 0)
                    html.Append("<p>" + Inline(line) + "</p>");
            }

            if (code)
                html.Append("</code></pre>");
            if (list)
                html.Append("</ul>");
            return html.Append("</article>").ToString();
        }

        // Type 3 vector glyphs from the system typeface, with a Unicode map for text extraction.
        public static void Pdf(string text, string output, bool markdown, CancellationToken token)
        {
            if (text.Length > 500000)
                throw new InvalidDataException("Text PDF limit");
            var tf = new Typeface("Segoe UI");
            GlyphTypeface face;
            if (!tf.TryGetGlyphTypeface(out face))
                throw new InvalidDataException("Segoe UI");
            var chars = text.Replace("\r", "").Replace("\n", "").Replace("\t", " ").AsEnumerable().Concat(" ").Distinct().OrderBy(c => c).ToList();
            if (chars.Any(c => char.IsSurrogate(c) || !face.CharacterToGlyphMap.ContainsKey(c)))
                throw new InvalidDataException("Unsupported glyph");
            var objects = new List<byte[]>();
            Func<string, int> add = s =>
            {
                objects.Add(Encoding.ASCII.GetBytes(s));
                return objects.Count;
            };
            Func<string, int> stream = s => add("<</Length " + Encoding.ASCII.GetByteCount(s) + ">>\nstream\n" + s + "\nendstream");
            int catalog = add(""), tree = add("");
            var fontIds = new List<int>();
            var lookup = new Dictionary<char, Tuple<int, int, double>>();
            Func<double, string> num = d => d.ToString("0.###", CultureInfo.InvariantCulture);
            for (int group = 0; group * 250 < chars.Count; group++)
            {
                token.ThrowIfCancellationRequested();
                var set = chars.Skip(group * 250).Take(250).ToArray();
                var proc = new StringBuilder();
                var widths = new List<string>();
                var names = new List<string>();
                var cmap = new StringBuilder();
                for (int j = 0; j < set.Length; j++)
                {
                    char c = set[j];
                    ushort glyph = face.CharacterToGlyphMap[c];
                    double width = face.AdvanceWidths[glyph] * 1000;
                    var geo = face.GetGlyphOutline(glyph, 1000, 1000).GetFlattenedPathGeometry(.5, ToleranceType.Absolute);
                    var path = new StringBuilder(num(width) + " 0 d0\n");
                    Action<Point, string> point = (pt, op) => path.Append(num(pt.X) + " " + num(-pt.Y) + " " + op + "\n");
                    foreach (var fig in geo.Figures)
                    {
                        point(fig.StartPoint, "m");
                        foreach (var seg in fig.Segments)
                        {
                            var poly = seg as PolyLineSegment;
                            if (poly != null)
                                foreach (var pt in poly.Points)
                                    point(pt, "l");
                            else if (seg is LineSegment)
                                point(((LineSegment)seg).Point, "l");
                        }

                        if (fig.IsClosed)
                            path.Append("h\n");
                    }

                    path.Append("f\n");
                    int id = stream(path.ToString());
                    string name = "/g" + j;
                    proc.Append(name + " " + id + " 0 R ");
                    names.Add(name);
                    widths.Add(num(width));
                    int code = j + 1;
                    if (j % 90 == 0)
                        cmap.Append(Math.Min(90, set.Length - j) + " beginbfchar\n");
                    cmap.Append("<" + code.ToString("X2") + "><" + ((int)c).ToString("X4") + ">\n");
                    if (j % 90 == 89 || j == set.Length - 1)
                        cmap.Append("endbfchar\n");
                    lookup[c] = Tuple.Create(group, code, width / 1000);
                }

                string uni = "/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /HeptaUnicode def\n/CMapType 2 def\n1 begincodespacerange\n<00><FF>\nendcodespacerange\n" + cmap + "endcmap\nCMapName currentdict /CMap defineresource pop\nend end";
                fontIds.Add(add("<</Type/Font/Subtype/Type3/FontBBox[-1000 -1000 2500 2500]/FontMatrix[0.001 0 0 0.001 0 0]/CharProcs<<" + proc + ">>/Encoding<</Type/Encoding/Differences[1 " + string.Join(" ", names) + "]>>/FirstChar 1/LastChar " + set.Length + "/Widths[" + string.Join(" ", widths) + "]/Resources<<>>/ToUnicode " + stream(uni) + " 0 R>>"));
            }

            string fonts = string.Join(" ", fontIds.Select((v, i) => "/F" + i + " " + v + " 0 R"));
            var pageIds = new List<int>();
            var commands = new StringBuilder();
            double y = 790;
            Action flush = () =>
            {
                int c = stream(commands.ToString());
                pageIds.Add(add("<</Type/Page/Parent " + tree + " 0 R/MediaBox[0 0 595 842]/Resources<</Font<<" + fonts + ">>>>/Contents " + c + " 0 R>>"));
                commands.Clear();
                y = 790;
            };
            foreach (string raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ").Split('\n'))
            {
                token.ThrowIfCancellationRequested();
                string line = raw;
                double size = 11;
                if (markdown)
                {
                    var h = Regex.Match(raw, @"^(#{1,6}) (.*)$");
                    if (h.Success)
                    {
                        size = 21 - h.Groups[1].Length;
                        line = h.Groups[2].Value;
                    }
                }

                double x = 42;
                foreach (char c in line)
                {
                    var glyph = lookup[c];
                    if (x + glyph.Item3 * size > 553)
                    {
                        x = 42;
                        y -= size * 1.5;
                    }

                    if (y < 42)
                        flush();
                    commands.Append("BT /F" + glyph.Item1 + " " + num(size) + " Tf " + num(x) + " " + num(y) + " Td <" + glyph.Item2.ToString("X2") + "> Tj ET\n");
                    x += glyph.Item3 * size;
                }

                y -= size * 1.6;
                if (y < 42)
                    flush();
            }

            if (commands.Length > 0 || pageIds.Count == 0)
                flush();
            objects[catalog - 1] = Encoding.ASCII.GetBytes("<</Type/Catalog/Pages " + tree + " 0 R>>");
            objects[tree - 1] = Encoding.ASCII.GetBytes("<</Type/Pages/Count " + pageIds.Count + "/Kids[" + string.Join(" ", pageIds.Select(n => n + " 0 R")) + "]>>");
            using (var f = File.Create(output))
            {
                Action<string> put = s =>
                {
                    var bytes = Encoding.ASCII.GetBytes(s);
                    f.Write(bytes, 0, bytes.Length);
                };
                var offsets = new List<long>();
                put("%PDF-1.4\n");
                for (int i = 0; i < objects.Count; i++)
                {
                    offsets.Add(f.Position);
                    put((i + 1) + " 0 obj\n");
                    f.Write(objects[i], 0, objects[i].Length);
                    put("\nendobj\n");
                }

                long x = f.Position;
                put("xref\n0 " + (objects.Count + 1) + "\n0000000000 65535 f \n");
                foreach (var off in offsets)
                    put(off.ToString("D10") + " 00000 n \n");
                put("trailer<</Size " + (objects.Count + 1) + "/Root 1 0 R>>\nstartxref\n" + x + "\n%%EOF\n");
            }
        }
    }
}
