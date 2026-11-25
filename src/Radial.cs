using System;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Xml.Linq;
using Shape = System.Windows.Shapes.Path;

namespace Hepta
{
    internal class Radial : Viewbox
    {
        readonly Canvas canvas = new Canvas
        {
            Width = 400,
            Height = 400
        };
        readonly string[] files, actions;
        readonly Preferences prefs;
        readonly Action<string, bool> selected;
        readonly XElement svg;
        int page;
        public static Brush Ink(string hex)
        {
            return (Brush)new BrushConverter().ConvertFromString(hex);
        }

        public Radial(string[] paths, string[] ids, Preferences settings, Action<string, bool> click)
        {
            files = paths;
            actions = ids;
            prefs = settings;
            selected = click;
            using (var stream = typeof(Radial).Assembly.GetManifestResourceStream("menu.svg"))
                svg = XElement.Load(stream);
            Child = canvas;
            Paint();
            MouseWheel += (s, e) =>
            {
                Turn(e.Delta < 0 ? 1 : -1);
                e.Handled = true;
            };
        }

        public static string Caption(string id, bool en)
        {
            switch (id)
            {
                case "gif-frames":
                    return Locale.Get(en, "png.frames");
                case "media-join":
                    return Locale.Get(en, "join.video");
                case "audio-join":
                    return Locale.Get(en, "join.audio");
                case "pdf-merge":
                    return Locale.Get(en, "join.pdf");
                case "pdf-split":
                    return Locale.Get(en, "split.pdf");
                case "extract":
                    return Locale.Get(en, "extract");
                case "paths":
                    return Locale.Get(en, "copy.paths");
                case "names":
                    return Locale.Get(en, "copy.names");
                case "txt-merge":
                    return Locale.Get(en, "join.txt");
                default:
                    return id.ToUpperInvariant();
            }
        }

        int Pages
        {
            get
            {
                return Math.Max(1, (actions.Length + 5) / 6);
            }
        }

        void Turn(int delta)
        {
            page = (page + Pages + delta) % Pages;
            Paint();
        }

        void Label(double x, double y, double width, string value, double size, Brush color)
        {
            var t = new TextBlock
            {
                Width = width,
                Text = value,
                FontSize = size,
                Foreground = color,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Canvas.SetLeft(t, x - width / 2);
            Canvas.SetTop(t, y);
            canvas.Children.Add(t);
        }

        public void Paint()
        {
            Opacity = prefs.OpacityPercent / 100d;
            canvas.Children.Clear();
            for (int i = 0; i <= 7; i++)
            {
                bool center = i == 7, more = i == 0;
                int index = page * 6 + i - 1;
                string action = index >= 0 && index < actions.Length ? actions[index] : null;
                string id = center ? "center-heptagon" : "petal-" + (i + 1);
                var source = svg.Descendants().First(e => (string)e.Attribute("id") == id);
                var geometry = Geometry.Parse((string)source.Attribute("d")).Clone();
                double angle = i * 360d / 7;
                if (!center)
                {
                    var transforms = new TransformGroup();
                    transforms.Children.Add(new RotateTransform(angle));
                    transforms.Children.Add(new TranslateTransform(200, 193));
                    geometry.Transform = transforms;
                }

                bool enabled = more || center || (action != null && files.Length > 0 && files.All(f => Engine.Accept(f, action)) && ((action != "txt-merge" && action != "pdf-merge" && action != "media-join" && action != "audio-join") || files.Length >= 2) && (action != "pdf-split" || files.Length == 1));
                var shape = new Shape
                {
                    Data = geometry,
                    Fill = Ink(center ? "#322b22" : more ? prefs.AccentColor : prefs.PetalColor),
                    Stroke = Ink(center ? "#efd08b" : "#92794c"),
                    StrokeThickness = center ? 2.6 : 1.2,
                    Opacity = enabled ? 1 : .45
                };
                canvas.Children.Add(shape);
                if (center)
                {
                    Label(200, 169, 137, files.Length + (Locale.Get(prefs.English, "files")), 19, Brushes.White);
                    string ext = string.Join(" / ", files.Select(f => Directory.Exists(f) ? "▣" : Path.GetExtension(f)).Distinct(StringComparer.OrdinalIgnoreCase));
                    Marquee(ext);
                    shape.ToolTip = ext;
                    continue;
                }

                double a = angle * Math.PI / 180;
                double x = 200 - 139 * Math.Sin(a), y = 193 + 139 * Math.Cos(a);
                if (more || action != null)
                {
                    var glyph = new TextBlock
                    {
                        Text = more ? "\uE712" : action == "pdf" || action == "pdf-merge" ? "\uE8A5" : action == "zip" || action == "extract" ? "\uE8B7" : action == "paths" || action == "names" ? "\uE8C8" : "\uE8B9",
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        FontSize = prefs.IconSize,
                        Width = 52,
                        TextAlignment = TextAlignment.Center,
                        Foreground = more ? Brushes.Black : Brushes.WhiteSmoke,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(glyph, x - 26);
                    Canvas.SetTop(glyph, y - 27);
                    canvas.Children.Add(glyph);
                }

                Label(x, y + 2, 112, more ? (Locale.Get(prefs.English, "more")) : action == null ? "" : Caption(action, prefs.English), prefs.FontSize, more ? Brushes.Black : Brushes.White);
                if (more)
                    Label(x, y + 23, 80, (page + 1) + "/" + Pages, 12, Brushes.Black);
                if (!more && action != null)
                    shape.ToolTip = OperationInfo.Describe(files, action, prefs.English);
                if (!enabled)
                    continue;
                shape.Cursor = Cursors.Hand;
                var fill = shape.Fill;
                shape.RenderTransformOrigin = new Point(.5, .5);
                shape.MouseEnter += (s, e) =>
                {
                    if (prefs.Hover)
                    {
                        shape.Fill = Ink(more ? "#f4d298" : "#484037");
                        shape.RenderTransform = new ScaleTransform(1.018, 1.018);
                    }
                };
                shape.MouseLeave += (s, e) =>
                {
                    shape.Fill = fill;
                    shape.RenderTransform = Transform.Identity;
                };
                shape.MouseLeftButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    if (more)
                        Turn(1);
                    else if (selected != null)
                        selected(action, false);
                };
                shape.MouseRightButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    if (!more && selected != null)
                        selected(action, true);
                };
            }
        }

        void Marquee(string ext)
        {
            var label = new TextBlock
            {
                Text = ext,
                Foreground = Brushes.Wheat,
                FontSize = 14
            };
            label.Measure(new Size(double.PositiveInfinity, 30));
            double width = label.DesiredSize.Width;
            if (width <= 126)
            {
                Label(200, 201, 130, ext, 14, Brushes.Wheat);
                return;
            }

            var clip = new Canvas
            {
                Width = 126,
                Height = 22,
                ClipToBounds = true,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(clip, 137);
            Canvas.SetTop(clip, 201);
            canvas.Children.Add(clip);
            var move = new TranslateTransform();
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                RenderTransform = move
            };
            row.Children.Add(label);
            row.Children.Add(new TextBlock { Text = ext, FontSize = 14, Foreground = Brushes.Wheat, Margin = new Thickness(20, 0, 0, 0) });
            clip.Children.Add(row);
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -width - 20, TimeSpan.FromSeconds((width + 20) / 26)) { RepeatBehavior = RepeatBehavior.Forever });
        }

        public void Animate()
        {
            if (prefs.Duration > 0)
            {
                var scale = new ScaleTransform();
                RenderTransformOrigin = new Point(.5, .5);
                RenderTransform = scale;
                var a = new DoubleAnimation(.42, 1, TimeSpan.FromMilliseconds(prefs.Duration))
                {
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseOut
                    }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
        }
    }
}
