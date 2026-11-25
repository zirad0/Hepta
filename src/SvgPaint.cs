using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using System.Threading;

namespace Hepta
{
    internal sealed class SvgPaint
    {
        readonly Dictionary<string, Brush> paints = new Dictionary<string, Brush>();
        readonly CancellationToken token;
        readonly XNamespace ns = "http://www.w3.org/2000/svg";
        static readonly string[] Common =
        {
            "id",
            "fill",
            "stroke",
            "stroke-width",
            "fill-opacity",
            "stroke-opacity",
            "opacity",
            "transform",
            "stroke-linecap",
            "stroke-linejoin",
            "fill-rule",
            "style",
            "color",
            "font-size",
            "font-family",
            "font-weight",
            "text-anchor"
        };
        static double N(string s, double d = 0)
        {
            if (s == null)
                return d;
            double x = double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (double.IsNaN(x) || double.IsInfinity(x) || Math.Abs(x) > 1e7)
                throw new InvalidDataException("SVG coordinate");
            return x;
        }

        static double A(XElement e, string key, double d = 0)
        {
            return N((string)e.Attribute(key), d);
        }

        static double Fraction(string s, double d)
        {
            return s == null ? d : s.EndsWith("%") ? N(s.TrimEnd('%')) / 100 : N(s);
        }

        static double[] Numbers(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return new double[0];
            return Regex.Split(s.Trim(), @"[\s,]+").Select(t => N(t)).ToArray();
        }

        static Color Colour(string s)
        {
            if (!Regex.IsMatch(s ?? "", @"^(#[0-9a-fA-F]{3,8}|[A-Za-z]{1,24})$"))
                throw new InvalidDataException("SVG colour");
            return (Color)ColorConverter.ConvertFromString(s);
        }

        static void Attributes(XElement e, IEnumerable<string> allowed)
        {
            if (e.Attributes().Any(a => !a.IsNamespaceDeclaration && !allowed.Contains(a.Name.ToString())))
                throw new InvalidDataException("Unsupported SVG attribute: " + e.Name.LocalName);
        }

        public SvgPaint(XElement root, CancellationToken t)
        {
            token = t;
            int count = 0;
            foreach (var node in root.Descendants())
            {
                if (++count > 160000 || node.Name.Namespace != ns)
                    throw new InvalidDataException("SVG size or namespace");
            }

            foreach (var def in root.Elements(ns + "defs"))
                foreach (var e in def.Elements())
                {
                    GradientBrush brush;
                    if (e.Name == ns + "linearGradient")
                    {
                        Attributes(e, new[] { "id", "x1", "x2", "y1", "y2" });
                        brush = new LinearGradientBrush
                        {
                            StartPoint = new Point(Fraction((string)e.Attribute("x1"), 0), Fraction((string)e.Attribute("y1"), 0)),
                            EndPoint = new Point(Fraction((string)e.Attribute("x2"), 1), Fraction((string)e.Attribute("y2"), 0))
                        };
                    }
                    else if (e.Name == ns + "radialGradient")
                    {
                        Attributes(e, new[] { "id", "cx", "cy", "r" });
                        var center = new Point(Fraction((string)e.Attribute("cx"), .5), Fraction((string)e.Attribute("cy"), .5));
                        double r = Fraction((string)e.Attribute("r"), .5);
                        brush = new RadialGradientBrush
                        {
                            Center = center,
                            GradientOrigin = center,
                            RadiusX = r,
                            RadiusY = r
                        };
                    }
                    else
                        throw new InvalidDataException("SVG definition");
                    foreach (var stop in e.Elements())
                    {
                        if (stop.Name != ns + "stop")
                            throw new InvalidDataException("SVG gradient stop");
                        Attributes(stop, new[] { "offset", "stop-color", "stop-opacity" });
                        var c = Colour((string)stop.Attribute("stop-color") ?? "black");
                        c.A = (byte)(c.A * A(stop, "stop-opacity", 1));
                        brush.GradientStops.Add(new GradientStop(c, Fraction((string)stop.Attribute("offset"), 0)));
                    }

                    paints.Add((string)e.Attribute("id"), brush);
                }
        }

        Brush Paint(string value, double opacity, string current)
        {
            if (value == "none")
                return null;
            if (value == "currentColor")
                value = current;
            Brush b;
            var match = Regex.Match(value, @"^url\(#([a-zA-Z_][\w.-]*)\)$");
            if (match.Success)
            {
                if (!paints.TryGetValue(match.Groups[1].Value, out b))
                    throw new InvalidDataException("Unknown SVG gradient");
                b = b.Clone();
            }
            else
                b = new SolidColorBrush(Colour(value));
            b.Opacity = opacity;
            return b;
        }

        static Transform TransformOf(string raw)
        {
            var group = new TransformGroup();
            var match = Regex.Matches(raw, @"([A-Za-z]+)\s*\(([^()]*)\)");
            string remainder = Regex.Replace(raw, @"([A-Za-z]+)\s*\(([^()]*)\)", "").Trim(' ', ',');
            if (remainder.Length != 0)
                throw new InvalidDataException("SVG transform");
            foreach (Match m in match)
            {
                var n = Numbers(m.Groups[2].Value);
                Transform t;
                switch (m.Groups[1].Value)
                {
                    case "translate":
                        if (n.Length < 1 || n.Length > 2)
                            throw new InvalidDataException();
                        t = new TranslateTransform(n[0], n.Length == 1 ? 0 : n[1]);
                        break;
                    case "scale":
                        if (n.Length < 1 || n.Length > 2)
                            throw new InvalidDataException();
                        t = new ScaleTransform(n[0], n.Length == 1 ? n[0] : n[1]);
                        break;
                    case "rotate":
                        if (n.Length != 1 && n.Length != 3)
                            throw new InvalidDataException();
                        t = new RotateTransform(n[0], n.Length == 3 ? n[1] : 0, n.Length == 3 ? n[2] : 0);
                        break;
                    case "matrix":
                        if (n.Length != 6)
                            throw new InvalidDataException();
                        t = new MatrixTransform(n[0], n[1], n[2], n[3], n[4], n[5]);
                        break;
                    default:
                        throw new InvalidDataException("SVG transform");
                }

                group.Children.Insert(0, t);
            }

            return group;
        }

        public void Draw(XElement e, DrawingContext dc, Dictionary<string, string> parent, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (depth > 36)
                throw new InvalidDataException("SVG nesting");
            string tag = e.Name.LocalName;
            if (tag == "defs" || tag == "title" || tag == "desc")
                return;
            var style = new Dictionary<string, string>(parent);
            foreach (string prop in Common)
            {
                var a = e.Attribute(prop);
                if (a != null)
                    style[prop] = a.Value;
            }

            foreach (string rule in ((string)e.Attribute("style") ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = rule.Split(':');
                if (kv.Length != 2 || !Common.Contains(kv[0].Trim()))
                    throw new InvalidDataException("SVG style");
                style[kv[0].Trim()] = kv[1].Trim();
            }

            Func<string, string, string> get = (k, d) => style.ContainsKey(k) ? style[k] : d;
            string[] coords;
            switch (tag)
            {
                case "g":
                    coords = new string[0];
                    break;
                case "path":
                    coords = new[]
                    {
                        "d"
                    };
                    break;
                case "rect":
                    coords = new[]
                    {
                        "x",
                        "y",
                        "rx",
                        "ry",
                        "width",
                        "height"
                    };
                    break;
                case "circle":
                    coords = new[]
                    {
                        "cx",
                        "cy",
                        "r"
                    };
                    break;
                case "ellipse":
                    coords = new[]
                    {
                        "cx",
                        "cy",
                        "rx",
                        "ry"
                    };
                    break;
                case "line":
                    coords = new[]
                    {
                        "x1",
                        "y1",
                        "x2",
                        "y2"
                    };
                    break;
                case "polygon":
                case "polyline":
                    coords = new[]
                    {
                        "points"
                    };
                    break;
                case "text":
                    coords = new[]
                    {
                        "x",
                        "y"
                    };
                    break;
                default:
                    throw new InvalidDataException("Unsupported SVG element: " + tag);
            }

            Attributes(e, Common.Concat(coords));
            if (tag != "g" && e.HasElements)
                throw new InvalidDataException("SVG nesting");
            int push = 0;
            if (e.Attribute("transform") != null)
            {
                dc.PushTransform(TransformOf((string)e.Attribute("transform")));
                push++;
            }

            if (e.Attribute("opacity") != null)
            {
                dc.PushOpacity(A(e, "opacity", 1));
                push++;
            }

            try
            {
                if (tag == "g")
                {
                    foreach (var child in e.Elements())
                        Draw(child, dc, style, depth + 1);
                    return;
                }

                var fill = Paint(get("fill", "black"), N(get("fill-opacity", "1")), get("color", "black"));
                var stroke = Paint(get("stroke", "none"), N(get("stroke-opacity", "1")), get("color", "black"));
                Pen pen = stroke == null ? null : new Pen(stroke, N(get("stroke-width", "1")));
                if (pen != null)
                {
                    string cap = get("stroke-linecap", "butt");
                    pen.StartLineCap = pen.EndLineCap = cap == "round" ? PenLineCap.Round : cap == "square" ? PenLineCap.Square : PenLineCap.Flat;
                    pen.LineJoin = get("stroke-linejoin", "miter") == "round" ? PenLineJoin.Round : PenLineJoin.Miter;
                }

                if (tag == "path")
                    dc.DrawGeometry(fill, pen, Geometry.Parse((get("fill-rule", "nonzero") == "evenodd" ? "F0 " : "F1 ") + (string)e.Attribute("d")));
                else if (tag == "rect")
                    dc.DrawRoundedRectangle(fill, pen, new Rect(A(e, "x"), A(e, "y"), A(e, "width"), A(e, "height")), A(e, "rx", A(e, "ry")), A(e, "ry", A(e, "rx")));
                else if (tag == "line")
                    dc.DrawLine(pen, new Point(A(e, "x1"), A(e, "y1")), new Point(A(e, "x2"), A(e, "y2")));
                else if (tag == "circle" || tag == "ellipse")
                    dc.DrawEllipse(fill, pen, new Point(A(e, "cx"), A(e, "cy")), A(e, "rx", A(e, "r")), A(e, "ry", A(e, "r")));
                else if (tag == "text")
                {
                    var ft = new FormattedText(e.Value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, get("font-weight", "400") == "600" ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), N(get("font-size", "16")), fill ?? Brushes.Transparent, 1);
                    string anchor = get("text-anchor", "start");
                    dc.DrawText(ft, new Point(A(e, "x") - (anchor == "middle" ? ft.Width / 2 : anchor == "end" ? ft.Width : 0), A(e, "y") - ft.Baseline));
                }
                else
                {
                    var n = Numbers((string)e.Attribute("points"));
                    if (n.Length < 4 || n.Length % 2 != 0)
                        throw new InvalidDataException("SVG points");
                    var geometry = new StreamGeometry();
                    using (var ctx = geometry.Open())
                    {
                        ctx.BeginFigure(new Point(n[0], n[1]), true, tag == "polygon");
                        for (int i = 2; i < n.Length; i += 2)
                            ctx.LineTo(new Point(n[i], n[i + 1]), true, true);
                    }

                    dc.DrawGeometry(fill, pen, geometry);
                }
            }
            finally
            {
                while (push-- > 0)
                    dc.Pop();
            }
        }
    }
}
