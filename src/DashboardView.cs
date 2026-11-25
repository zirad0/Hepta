using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Diagnostics;
using Forms = System.Windows.Forms;

namespace Hepta
{
    internal partial class Dashboard
    {
        readonly List<Tuple<Window, string>> notices = new List<Tuple<Window, string>>();
        string sourceMonitor;
        int running;
        readonly Queue<TaskCompletionSource<bool>> waiting = new Queue<TaskCompletionSource<bool>>();
        Task Slot()
        {
            var t = new TaskCompletionSource<bool>();
            waiting.Enqueue(t);
            DispatchWaiting();
            return t.Task;
        }

        void DispatchWaiting()
        {
            while (waiting.Count > 0 && running < p.Concurrency)
            {
                running++;
                waiting.Dequeue().SetResult(true);
            }
        }

        void ReleaseSlot()
        {
            running--;
            DispatchWaiting();
        }

        bool Light
        {
            get
            {
                return p.Theme == "light" || (p.Theme == "system" && ((int? )Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) ?? 1) == 1);
            }
        }

        Brush Surface
        {
            get
            {
                return Radial.Ink(Light ? "#ffffff" : "#242529");
            }
        }

        Brush Accent
        {
            get
            {
                return Radial.Ink(Light ? "#79591d" : "#e2bd78");
            }
        }

        Image Logo(double size)
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(tray.Icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight((int)size, (int)size));
            return new Image
            {
                Source = source,
                Width = size,
                Height = size,
                Margin = new Thickness(2, 0, 9, 0)
            };
        }

        Border Card(Panel contents)
        {
            return new Border
            {
                Background = Surface,
                BorderBrush = Radial.Ink(Light ? "#ded9cf" : "#303135"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(14),
                Margin = new Thickness(3, 4, 3, 4),
                Child = contents
            };
        }

        void Overview()
        {
            body.Children.Add(Text(L("manage.quick.actions.while.hepta.runs.in.the.background"), 14));
            var status = new StackPanel();
            status.Children.Add(Text("●  " + L("hepta.is.running"), 18));
            status.Children.Add(Text(shortcuts ? L("shortcuts.active.ready.in.system.tray") : L("shortcuts.paused.or.in.use"), 13));
            body.Children.Add(Card(status));
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());
            body.Children.Add(grid);
            var keys = new StackPanel();
            keys.Children.Add(Text(L("quick.menu.shortcuts"), 16));
            keys.Children.Add(Text(L("select.a.file.in.explorer.or.on.the.desktop"), 12));
            keys.Children.Add(Button(L("convert") + "    " + Chord.Format(p.ConvertKey, p.ConvertModifiers), () => Pick(false)));
            keys.Children.Add(Button(L("edit") + "    " + Chord.Format(p.EditKey, p.EditModifiers), () => Pick(true)));
            grid.Children.Add(Card(keys));
            var choices = new StackPanel();
            choices.Children.Add(Text(L("preferences"), 16));
            Check(choices, L("start.with.windows"), p.Startup, v =>
            {
                p.Startup = v;
                Autorun();
            });
            Check(choices, L("job.notifications"), p.Notify, v => p.Notify = v);
            var card = Card(choices);
            Grid.SetColumn(card, 1);
            grid.Children.Add(card);
            var recent = new StackPanel();
            recent.Children.Add(Text(L("recent.jobs"), 16));
            foreach (var j in jobs.Take(4))
                Row(recent, j);
            if (jobs.Count == 0)
                recent.Children.Add(Text(L("your.first.job.will.appear.here"), 13));
            var historyCard = Card(recent);
            Grid.SetRow(historyCard, 1);
            grid.Children.Add(historyCard);
            var preview = new StackPanel();
            preview.Children.Add(Text(L("radial.menu"), 16));
            preview.Children.Add(new Radial(new string[0], Default(false), p, null) { Width = 155, Height = 155 });
            preview.Children.Add(Button(L("customize.menu"), () =>
            {
                page = 1;
                Refresh();
            }));
            var menuCard = Card(preview);
            Grid.SetColumn(menuCard, 1);
            Grid.SetRow(menuCard, 1);
            grid.Children.Add(menuCard);
        }

        void ShortcutPage()
        {
            int[] keys =
            {
                p.ConvertKey,
                p.EditKey
            }, mods =
            {
                p.ConvertModifiers,
                p.EditModifiers
            };
            body.Children.Add(Text(L("select.a.field.and.press.the.new.shortcut"), 14));
            for (int i = 0; i < 2; i++)
            {
                int n = i;
                body.Children.Add(Text(i == 0 ? L("convert") : L("edit"), 16));
                var box = new TextBox
                {
                    IsReadOnly = true,
                    Text = Chord.Format(keys[i], mods[i]),
                    Margin = new Thickness(2, 4, 2, 8)
                };
                box.PreviewKeyDown += (sender, args) =>
                {
                    var k = args.Key == Key.System ? args.SystemKey : args.Key;
                    if (k == Key.Tab)
                        return;
                    args.Handled = true;
                    int vk = KeyInterop.VirtualKeyFromKey(k), mod = (int)Keyboard.Modifiers;
                    if (Chord.Valid(vk, mod))
                    {
                        keys[n] = vk;
                        mods[n] = mod;
                        box.Text = Chord.Format(vk, mod);
                    }
                };
                body.Children.Add(box);
            }

            body.Children.Add(Button(L("apply"), () =>
            {
                if (!bindings.Apply(keys[0], mods[0], keys[1], mods[1]))
                    throw new ArgumentException(L("shortcuts.conflict.are.in.use.or.are.reserved.previous.reg"));
                p.ConvertKey = keys[0];
                p.ConvertModifiers = mods[0];
                p.EditKey = keys[1];
                p.EditModifiers = mods[1];
                shortcuts = true;
                Save();
                Refresh();
            }));
        }

        void OutputFields(Panel panel, OutputOptions options, string id, bool persist)
        {
            var desc = Text(options.DirectoryName.Length == 0 ? Locale.Get(p.English, "output.source") : options.DirectoryName, 12);
            panel.Children.Add(desc);
            panel.Children.Add(Button(Locale.Get(p.English, "output.choose"), () =>
            {
                using (var d = new Forms.FolderBrowserDialog())
                {
                    if (d.ShowDialog() != Forms.DialogResult.OK)
                        return;
                    desc.Text = options.DirectoryName = d.SelectedPath;
                    if (persist)
                        Save();
                }
            }));
            panel.Children.Add(Button(Locale.Get(p.English, "output.source"), () =>
            {
                options.DirectoryName = "";
                desc.Text = Locale.Get(p.English, "output.source");
                if (persist)
                    Save();
            }));
            if (id == "" || id == "png" || id == "jpg" || id == "pdf-merge" || id == "pdf-split")
                Slider(panel, "PDF · DPI", options.PdfDpi, 72, 300, v => options.PdfDpi = v);
            if (id == "" || id == "zip")
                Slider(panel, Locale.Get(p.English, "zip.level"), options.ZipLevel, 0, 2, v => options.ZipLevel = v);
        }

        void MediaFields(Panel panel, MediaOptions o, bool video, bool save)
        {
            Action<string, int[], int, Action<int>> pick = (label, values, current, set) =>
            {
                panel.Children.Add(Text(label, 13));
                var c = new ComboBox
                {
                    ItemsSource = values.Select(v => v == 0 ? L("automatic") : v.ToString()).ToArray(),
                    SelectedIndex = Array.IndexOf(values, current)
                };
                c.SelectionChanged += (s, e) =>
                {
                    set(values[c.SelectedIndex]);
                    if (save)
                        Save();
                };
                panel.Children.Add(c);
            };
            if (video)
            {
                pick(L("video.height.px"), new[] { 480, 720, 1080 }, o.Height, v => o.Height = v);
                pick("FPS", new[] { 0, 24, 25, 30, 50, 60 }, o.Fps, v => o.Fps = v);
                pick(L("video.bitrate.kb.s"), new[] { 0, 1000, 2500, 5000, 8000, 12000 }, o.VideoKbps, v => o.VideoKbps = v);
            }

            pick(L("audio.bitrate.kb.s"), new[] { 0, 96, 128, 192, 256, 320 }, o.AudioKbps, v => o.AudioKbps = v);
            pick(L("sample.rate.hz"), new[] { 0, 22050, 44100, 48000 }, o.SampleRate, v => o.SampleRate = v);
            pick(L("channels"), new[] { 0, 1, 2 }, o.Channels, v => o.Channels = v);
        }

        void StyleButton(Button b)
        {
            b.Foreground = Foreground;
            b.Background = Radial.Ink(Light ? "#e9e5df" : "#303136");
            b.BorderThickness = new Thickness(0);
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 6, 10, 6));
            border.AppendChild(content);
            var template = new ControlTemplate(typeof(Button))
            {
                VisualTree = border
            };
            var trigger = new Trigger
            {
                Property = UIElement.IsMouseOverProperty,
                Value = true
            };
            trigger.Setters.Add(new Setter(Control.BackgroundProperty, Radial.Ink(Light ? "#e4d6ba" : "#4c4335")));
            template.Triggers.Add(trigger);
            b.Template = template;
        }

        StackPanel MergeList(string[] files)
        {
            var panel = new StackPanel();
            var list = new ListBox
            {
                MaxHeight = 235,
                Background = Surface,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            panel.Children.Add(list);
            Action fill = null;
            fill = () =>
            {
                list.Items.Clear();
                for (int i = 0; i < files.Length; i++)
                {
                    int pos = i;
                    var row = new DockPanel
                    {
                        LastChildFill = true,
                        AllowDrop = true
                    };
                    var grip = new TextBlock
                    {
                        Text = "⠿   " + (i + 1),
                        Width = 48,
                        Foreground = Accent,
                        Cursor = Cursors.SizeNS,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    DockPanel.SetDock(grip, Dock.Left);
                    row.Children.Add(grip);
                    var label = new FileLabel(Path.GetFileName(files[i]))
                    {
                        Margin = new Thickness(4, 5, 4, 5),
                        Foreground = Foreground,
                        Cursor = Cursors.Hand
                    };
                    row.Children.Add(label);
                    label.MouseLeftButtonUp += (s, e) =>
                    {
                        try
                        {
                            var ext = Engine.Ext(files[pos]);
                            if (!new[]
                            {
                                "pdf",
                                "txt",
                                "png",
                                "jpg",
                                "bmp",
                                "gif",
                                "tiff",
                                "wav",
                                "mp3",
                                "m4a",
                                "mp4",
                                "wmv"
                            }.Contains(ext))
                                return;
                            Process.Start(new ProcessStartInfo(files[pos]) { UseShellExecute = true });
                        }
                        catch (Exception ex)
                        {
                            Toast(ex.Message);
                        }
                    };
                    grip.MouseMove += (s, e) =>
                    {
                        if (e.LeftButton == MouseButtonState.Pressed)
                            DragDrop.DoDragDrop(grip, new DataObject("Hepta.MergeIndex", pos), DragDropEffects.Move);
                    };
                    row.Drop += (s, e) =>
                    {
                        if (!e.Data.GetDataPresent("Hepta.MergeIndex"))
                            return;
                        int from = (int)e.Data.GetData("Hepta.MergeIndex");
                        var seq = files.ToList();
                        string f = seq[from];
                        seq.RemoveAt(from);
                        seq.Insert(pos, f);
                        seq.CopyTo(files);
                        fill();
                    };
                    list.Items.Add(row);
                }
            };
            fill();
            return panel;
        }

        void Notice(string message, string device, int seconds, Job job, bool progress)
        {
            if (!p.Notify || (progress && !p.ShowProgress))
                return;
            var w = Modal("Hepta", 340);
            w.WindowStyle = WindowStyle.None;
            w.ResizeMode = ResizeMode.NoResize;
            w.ShowInTaskbar = false;
            w.ShowActivated = false;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            var stack = (StackPanel)w.Content;
            stack.Children.Add(Text(progress ? L("processing") : "Hepta", 16));
            if (job == null)
                stack.Children.Add(Text(message, 14));
            else
            {
                stack.Children.Add(new FileLabel(job.SourceName) { Foreground = Foreground, FontSize = 14 });
                stack.Children.Add(Text(Radial.Caption(job.Operation ?? "", p.English) + " · " + State(job), 13));
                if (job.Error != null)
                    stack.Children.Add(Text(job.Error, 13));
            }

            var bar = new ProgressBar
            {
                IsIndeterminate = true,
                Maximum = 100,
                Height = 4,
                Margin = new Thickness(0, 4, 0, 6)
            };
            if (progress)
            {
                stack.Children.Add(bar);
                stack.Children.Add(Button(L("cancel"), () => job.Cancel.Cancel()));
            }

            if (job != null && job.Decision != null)
                stack.Children.Add(Button(L("review.output"), () =>
                {
                    page = 8;
                    Refresh();
                    Reveal();
                    w.Close();
                }));
            var entry = Tuple.Create(w, device);
            notices.Add(entry);
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(progress ? 300 : seconds * 1000)
            };
            timer.Tick += (s, e) =>
            {
                if (!progress || job.State != "run")
                    w.Close();
                else if (job.Percent >= 0)
                {
                    bar.IsIndeterminate = false;
                    bar.Value = job.Percent;
                    bar.ToolTip = Math.Round(job.Percent) + "%";
                }
            };
            w.ContentRendered += (s, e) => PositionNotices();
            w.Closed += (s, e) =>
            {
                timer.Stop();
                notices.Remove(entry);
                PositionNotices();
            };
            w.Show();
            timer.Start();
        }

        void PositionNotices()
        {
            var offsets = new Dictionary<string, int>();
            foreach (var n in notices.Where(t => t.Item1.IsVisible))
            {
                var screen = Screens.Resolve(n.Item2);
                int offset;
                offsets.TryGetValue(screen.DeviceName, out offset);
                Screens.Position(n.Item1, screen, Forms.Cursor.Position, false, true, offset);
                var source = PresentationSource.FromVisual(n.Item1);
                double scale = source == null ? 1 : source.CompositionTarget.TransformToDevice.M22;
                offsets[screen.DeviceName] = offset + (int)(n.Item1.ActualHeight * scale) + 8;
            }
        }
    }
}
