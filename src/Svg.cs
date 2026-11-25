using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading;

namespace Hepta
{
    internal static class Svg
    {
        static double Number(string v, double fallback)
        {
            if (v == null)
                return fallback;
            double d = double.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (double.IsInfinity(d) || double.IsNaN(d) || Math.Abs(d) > 1e7)
                throw new InvalidDataException("SVG coordinate");
            return d;
        }

        static double N(XElement e, string attr, double fallback = 0)
        {
            return Number((string)e.Attribute(attr), fallback);
        }

        static Brush Color(string v, double opacity)
        {
            if (v == "none")
                return null;
            if (v == null)
                v = "black";
            if (!System.Text.RegularExpressions.Regex.IsMatch(v, @"^(#[0-9A-Fa-f]{3,8}|[A-Za-z]{1,30})$"))
                throw new InvalidDataException("SVG paint");
            var brush = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(v));
            brush.Opacity = opacity;
            return brush;
        }

        public static void Render(string input, string output, string target, int quality, CancellationToken token)
        {
            using (var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32000000 }))
            {
                var root = XElement.Load(reader);
                XNamespace ns = "http://www.w3.org/2000/svg";
                if (root.Name != ns + "svg")
                    throw new InvalidDataException("SVG");
                var allowedRoot = new[]
                {
                    "width",
                    "height",
                    "viewBox",
                    "version"
                };
                if (root.Attributes().Any(a => !a.IsNamespaceDeclaration && !allowedRoot.Contains(a.Name.ToString())))
                    throw new InvalidDataException("Unsupported SVG root attribute");
                string view = (string)root.Attribute("viewBox");
                double[] box = view == null ? null : System.Text.RegularExpressions.Regex.Split(view.Trim(), @"[\s,]+").Select(v => Number(v, 0)).ToArray();
                if (box != null && (box.Length != 4 || box[2] <= 0 || box[3] <= 0))
                    throw new InvalidDataException("SVG viewBox");
                int w = (int)Math.Ceiling(N(root, "width", box == null ? 300 : box[2])), h = (int)Math.Ceiling(N(root, "height", box == null ? 150 : box[3]));
                if (w <= 0 || h <= 0 || (long)w * h > 16000000)
                    throw new InvalidDataException("SVG dimensions");
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    if (target != "png")
                        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
                    if (box != null)
                    {
                        double scale = Math.Min(w / box[2], h / box[3]);
                        dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, (w - box[2] * scale) / 2 - box[0] * scale, (h - box[3] * scale) / 2 - box[1] * scale));
                    }

                    var renderer = new SvgPaint(root, token);
                    var defaults = new System.Collections.Generic.Dictionary<string, string>();
                    foreach (var e in root.Elements())
                        renderer.Draw(e, dc, defaults, 0);
                    if (box != null)
                        dc.Pop();
                }

                var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                BitmapEncoder encoder = target == "jpg" ? (BitmapEncoder)new JpegBitmapEncoder
                {
                    QualityLevel = quality
                }

                : new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                if (target == "pdf")
                {
                    using (var stream = new MemoryStream())
                    {
                        encoder.Save(stream);
                        stream.Position = 0;
                        using (var img = System.Drawing.Image.FromStream(stream))
                            Engine.Pdf(img, output, quality);
                    }
                }
                else
                    using (var stream = File.Create(output))
                        encoder.Save(stream);
            }
        }
    }
}
