using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;
using System.Web.Script.Serialization;
using Forms = System.Windows.Forms;
using Microsoft.Win32;

namespace Hepta
{
    internal static class Entry
    {
        [STAThread]
        public static void Main(string[] args)
        {
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
            if (args.Contains("--self-test"))
            {
                Environment.ExitCode = Smoke();
                return;
            }

            bool first;
            using (var mutex = new Mutex(true, "Local\\Hepta.Zirad.Single", out first))
            using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\Hepta.Zirad.Show"))
            {
                if (!first)
                {
                    signal.Set();
                    return;
                }

                var app = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
                var main = new Dashboard(args.Contains("--tray"));
                app.MainWindow = main;
                var listener = ThreadPool.RegisterWaitForSingleObject(signal, (s, t) => main.Dispatcher.BeginInvoke(new Action(main.Reveal)), null, -1, false);
                try
                {
                    app.Run();
                }
                finally
                {
                    listener.Unregister(null);
                }
            }
        }

        static int Smoke()
        {
            var folder = Path.Combine(Path.GetTempPath(), "hepta-check-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                Preferences.DirectoryPath = folder;
                var window = new Dashboard(true, true);
                try
                {
                    for (int i = 0; i < 12; i++)
                        window.RenderPage(i);
                    var f = Path.Combine(folder, "test.txt");
                    File.WriteAllText(f, "Türkçe <ok>");
                    var output = Engine.Run(new[] { f }, "html", 90, CancellationToken.None);
                    if (!File.ReadAllText(output).Contains("&lt;ok&gt;"))
                        return 1;
                    var pdf = Engine.Run(new[] { f }, "pdf", 90, CancellationToken.None);
                    var joined = Documents.Merge(new[] { pdf, pdf }, 90, CancellationToken.None);
                    return Task.Run(() => NativeMedia.Pages(joined, CancellationToken.None)).Result == 2 ? 0 : 1;
                }
                finally
                {
                    window.EndTest();
                }
            }
            catch
            {
                return 1;
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }

    internal class Job
    {
        public string Name, State, Output, Error, Operation;
        public string[] Sources;
        [ScriptIgnore]
        public double Percent = -1;
        public string SourceName
        {
            get
            {
                return Sources != null && Sources.Length > 0 ? Path.GetFileName(Sources[0]) : Name;
            }
        }

        public Pending Decision;
        public DateTime Time = DateTime.Now;
        [ScriptIgnore]
        public CancellationTokenSource Cancel = new CancellationTokenSource();
    }

    internal partial class Dashboard : Window
    {
        Preferences p = new Preferences();
        readonly List<Job> jobs = new List<Job>();
        readonly StackPanel nav = new StackPanel(), body = new StackPanel
        {
            Margin = new Thickness(24)
        };
        readonly Forms.NotifyIcon tray = new Forms.NotifyIcon();
        readonly string history = Path.Combine(Preferences.DirectoryPath, "history.json");
        ShortcutManager bindings;
        IntPtr handle;
        bool shortcuts, exit;
        int page;
        Window popup;
        string L(string id)
        {
            return Locale.Get(p.English, id);
        }

        public Dashboard(bool hidden, bool testMode = false)
        {
            string warning = null;
            try
            {
                var path = Path.Combine(Preferences.DirectoryPath, "pending.json");
                if (File.Exists(path))
                    jobs.AddRange(new JavaScriptSerializer().Deserialize<List<Job>>(File.ReadAllText(path)).Where(j => j.Decision != null && File.Exists(j.Decision.Stage())));
            }
            catch (Exception ex)
            {
                warning = ex.Message;
            }

            try
            {
                if (File.Exists(Preferences.Location))
                    p = Preferences.Read(Preferences.Location);
            }
            catch (Exception e)
            {
                warning = e.Message;
                if (File.Exists(Preferences.Location))
                    File.Copy(Preferences.Location, Preferences.Location + "." + Guid.NewGuid().ToString("N") + ".bak");
            }

            try
            {
                if (p.History && File.Exists(history))
                    jobs.AddRange(new JavaScriptSerializer().Deserialize<List<Job>>(File.ReadAllText(history)).Where(j => j != null).Take(250));
            }
            catch
            {
                warning = L("history.could.not.be.read");
            }

            Title = "Hepta";
            Width = 940;
            Height = 625;
            MinWidth = 640;
            MinHeight = 440;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var dock = new DockPanel();
            nav.Margin = new Thickness(10);
            var sidebar = new ScrollViewer
            {
                Content = nav,
                Width = 230,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            DockPanel.SetDock(sidebar, Dock.Left);
            dock.Children.Add(sidebar);
            dock.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = dock;
            handle = new WindowInteropHelper(this).EnsureHandle();
            HwndSource.FromHwnd(handle).AddHook(Hook);
            bindings = new ShortcutManager((id, mods, key) => ShellIntegration.RegisterHotKey(handle, id, (uint)mods | 0x4000, (uint)key), id => ShellIntegration.UnregisterHotKey(handle, id));
            shortcuts = Register(p.ConvertKey, p.EditKey);
            tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
            tray.Text = "Hepta";
            tray.DoubleClick += (s, e) => Reveal();
            tray.Visible = true;
            Closing += (s, e) =>
            {
                if (!exit)
                {
                    e.Cancel = true;
                    Hide();
                }
            };
            Closed += (s, e) =>
            {
                Unregister();
                tray.Dispose();
                if (Application.Current != null)
                    Application.Current.Shutdown();
            };
            Refresh();
            if (!testMode && (!hidden || p.Welcome))
                Reveal();
            if (!testMode && !p.Welcome)
            {
                try
                {
                    Autorun();
                }
                catch (UnauthorizedAccessException ex)
                {
                    warning = ex.Message;
                }
            }

            if (p.Welcome && !testMode)
                Dispatcher.BeginInvoke(new Action(Welcome));
            if (warning != null && !testMode)
                Dispatcher.BeginInvoke(new Action(() => MessageBox.Show(warning, "Hepta")));
        }

        internal void RenderPage(int number)
        {
            page = number;
            Refresh();
        }

        internal void EndTest()
        {
            exit = true;
            Close();
        }

        void Save()
        {
            try
            {
                Preferences.Write(Preferences.Location, p);
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Hepta");
            }
        }

        void History()
        {
            var excess = jobs.Where(j => j.Decision == null && (j.State == "done" || j.State == "cancel" || j.State == "error")).Skip(250).ToArray();
            foreach (var old in excess)
                jobs.Remove(old);
            try
            {
                Preferences.Write(Path.Combine(Preferences.DirectoryPath, "pending.json"), jobs.Where(j => j.Decision != null).ToList());
            }
            catch (Exception e)
            {
                Toast(e.Message);
            }

            if (p.History)
                try
                {
                    Preferences.Write(history, jobs.Where(j => j.State == "done" || j.State == "error" || j.State == "cancel").Take(250).ToList());
                }
                catch (Exception e)
                {
                    Toast(e.Message);
                }
        }

        public void Reveal()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        bool Register(int c, int e)
        {
            return bindings.Apply(c, p.ConvertModifiers, e, p.EditModifiers);
        }

        void Unregister()
        {
            bindings.Clear();
        }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x312)
            {
                handled = true;
                var files = ShellIntegration.Selection();
                Menu(files, bindings.IsEdit(w.ToInt32()));
            }

            return IntPtr.Zero;
        }

        void Autorun()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (p.Startup)
                    k.SetValue("Hepta", "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\" --tray");
                else
                    k.DeleteValue("Hepta", false);
            }
        }

        Button Button(string title, Action click)
        {
            var b = new Button
            {
                Content = title,
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(2),
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            StyleButton(b);
            b.Click += (s, e) =>
            {
                try
                {
                    click();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Hepta");
                }
            };
            return b;
        }

        TextBlock Text(string s, double size)
        {
            return new TextBlock
            {
                Text = s,
                FontSize = size,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Foreground,
                Margin = new Thickness(2, 5, 2, 5)
            };
        }

        void Check(Panel panel, string label, bool value, Action<bool> set)
        {
            var c = new CheckBox
            {
                Content = label,
                Foreground = Foreground,
                IsChecked = value,
                Margin = new Thickness(3, 7, 3, 7)
            };
            c.Click += (s, e) =>
            {
                set(c.IsChecked == true);
                Save();
            };
            panel.Children.Add(c);
        }

        void Slider(Panel panel, string label, int val, int lo, int hi, Action<int> set)
        {
            var t = Text(label + " · " + val, 14);
            panel.Children.Add(t);
            var s = new Slider
            {
                Minimum = lo,
                Maximum = hi,
                Value = val,
                Margin = new Thickness(4, 2, 4, 8)
            };
            s.ValueChanged += (o, e) =>
            {
                set((int)s.Value);
                t.Text = label + " · " + (int)s.Value;
            };
            s.LostMouseCapture += (o, e) => Save();
            s.LostKeyboardFocus += (o, e) => Save();
            panel.Children.Add(s);
        }

        string[] Titles()
        {
            return new[]
            {
                L("overview"),
                L("radial.menu"),
                L("file.profiles"),
                L("pinned.actions"),
                L("shortcuts"),
                L("appearance"),
                L("conversion.defaults"),
                L("notifications"),
                L("job.queue"),
                L("history"),
                L("settings"),
                L("about")
            };
        }

        void Refresh()
        {
            Background = Radial.Ink(Light ? "#f5f0e8" : "#191a1c");
            Foreground = Light ? Brushes.Black : Brushes.WhiteSmoke;
            ((DockPanel)Content).Background = Background;
            nav.Children.Clear();
            var brand = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            brand.Children.Add(Logo(34));
            brand.Children.Add(Text("Hepta", 25));
            nav.Children.Add(brand);
            var titles = Titles();
            for (int i = 0; i < titles.Length; i++)
            {
                int n = i;
                var b = Button(titles[i], () =>
                {
                    page = n;
                    Refresh();
                });
                if (n == page)
                {
                    b.Foreground = Accent;
                    b.Background = Radial.Ink(Light ? "#ede1c9" : "#393229");
                }

                nav.Children.Add(b);
            }

            nav.Children.Add(Text("Made by Zirad.", 12));
            body.Children.Clear();
            body.Children.Add(Text(titles[page], 28));
            var ctx = new Forms.ContextMenuStrip();
            ctx.Items.Add(L("open.hepta"), null, (o, e) => Reveal());
            ctx.Items.Add(titles[10], null, (o, e) =>
            {
                page = 10;
                Refresh();
                Reveal();
            });
            ctx.Items.Add(titles[8], null, (o, e) =>
            {
                page = 8;
                Refresh();
                Reveal();
            });
            ctx.Items.Add(shortcuts ? L("pause.shortcuts") : L("enable.shortcuts"), null, (o, e) =>
            {
                if (shortcuts)
                {
                    Unregister();
                    shortcuts = false;
                }
                else
                    shortcuts = Register(p.ConvertKey, p.EditKey);
                Refresh();
            });
            ctx.Items.Add(L("exit"), null, (o, e) =>
            {
                if (jobs.Any(j => j.State == "run" || j.State == "wait"))
                {
                    Toast(L("finish.or.cancel.queued.jobs.before.exiting"));
                    return;
                }

                exit = true;
                Close();
            });
            var old = tray.ContextMenuStrip;
            tray.ContextMenuStrip = ctx;
            if (old != null)
                old.Dispose();
            switch (page)
            {
                case 0:
                    Overview();
                    break;
                case 1:
                    var radial = new Radial(new string[0], Engine.Targets, p, null)
                    {
                        Width = p.MenuSize,
                        Height = p.MenuSize,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    Slider(body, L("menu.size"), p.MenuSize, 240, 680, v =>
                    {
                        p.MenuSize = v;
                        radial.Width = radial.Height = v;
                    });
                    Slider(body, L("animation.ms"), p.Duration, 0, 1000, v => p.Duration = v);
                    Slider(body, L("font.size"), p.FontSize, 10, 22, v =>
                    {
                        p.FontSize = v;
                        radial.Paint();
                    });
                    Slider(body, L("icon.size"), p.IconSize, 12, 32, v =>
                    {
                        p.IconSize = v;
                        radial.Paint();
                    });
                    Slider(body, L("opacity"), p.OpacityPercent, 40, 100, v =>
                    {
                        p.OpacityPercent = v;
                        radial.Paint();
                    });
                    Check(body, L("hover.highlight"), p.Hover, v => p.Hover = v);
                    body.Children.Add(Button(L("accent.colour"), () => PickColor(true, radial)));
                    body.Children.Add(Button(L("petal.colour"), () => PickColor(false, radial)));
                    Check(body, L("open.at.monitor.center"), p.Center, v => p.Center = v);
                    body.Children.Add(radial);
                    break;
                case 2:
                case 3:
                    Profiles();
                    break;
                case 4:
                    ShortcutPage();
                    break;
                case 5:
                    var theme = new ComboBox
                    {
                        ItemsSource = new[]
                        {
                            L("system"),
                            L("light"),
                            L("dark")
                        },
                        SelectedIndex = p.Theme == "system" ? 0 : p.Theme == "light" ? 1 : 2
                    };
                    theme.SelectionChanged += (s, args) =>
                    {
                        p.Theme = new[]
                        {
                            "system",
                            "light",
                            "dark"
                        }[theme.SelectedIndex];
                        Save();
                        Refresh();
                    };
                    body.Children.Add(theme);
                    break;
                case 6:
                    Slider(body, L("concurrent.jobs"), p.Concurrency, 1, 8, v =>
                    {
                        p.Concurrency = v;
                        DispatchWaiting();
                    });
                    Slider(body, L("jpeg.quality"), p.Quality, 1, 100, v => p.Quality = v);
                    MediaFields(body, p.Media, true, true);
                    OutputFields(body, p.Output, "", true);
                    break;
                case 7:
                    Check(body, L("show.notifications"), p.Notify, v => p.Notify = v);
                    Check(body, L("progress.notifications"), p.ShowProgress, v => p.ShowProgress = v);
                    Slider(body, L("success.duration.s"), p.SuccessSeconds, 1, 60, v => p.SuccessSeconds = v);
                    Slider(body, L("error.duration.s"), p.ErrorSeconds, 1, 60, v => p.ErrorSeconds = v);
                    break;
                case 8:
                    JobView(false);
                    break;
                case 9:
                    JobView(true);
                    break;
                case 10:
                    Check(body, L("start.with.windows"), p.Startup, v =>
                    {
                        p.Startup = v;
                        Autorun();
                    });
                    Check(body, "English", p.English, v =>
                    {
                        p.English = v;
                        Refresh();
                    });
                    body.Children.Add(Button(L("export.settings"), () =>
                    {
                        var d = new SaveFileDialog
                        {
                            Filter = "JSON|*.json",
                            FileName = "Hepta.json"
                        };
                        if (d.ShowDialog() == true)
                            Preferences.Write(d.FileName, p);
                    }));
                    body.Children.Add(Button(L("import.settings"), () =>
                    {
                        var d = new OpenFileDialog
                        {
                            Filter = "JSON|*.json"
                        };
                        if (d.ShowDialog() == true)
                            Apply(Preferences.Read(d.FileName));
                    }));
                    body.Children.Add(Button(L("restore.defaults"), () => Apply(new Preferences { Welcome = false })));
                    break;
                case 11:
                    body.Children.Add(Logo(68));
                    body.Children.Add(Text("Hepta 1.0.0\nMade by Zirad.\n© 2026 Zirad", 20));
                    body.Children.Add(Text(L("files.are.processed.on.your.computer.free.to.use"), 15));
                    foreach (var link in Locale.Map("links.json"))
                    {
                        var item = link;
                        body.Children.Add(Button(item.Key, () => Process.Start(item.Value)));
                    }

                    body.Children.Add(Button(L("components.and.licenses"), () => MessageBox.Show(Locale.Resource("licenses.md"), "Hepta")));
                    body.Children.Add(Button("PDFium · LICENSE", () =>
                    {
                        var dialog = Modal("PDFium", 700);
                        var scroll = new TextBox
                        {
                            Text = Locale.Resource("pdfium-notices.txt"),
                            IsReadOnly = true,
                            TextWrapping = TextWrapping.Wrap,
                            Height = 410,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                        };
                        ((StackPanel)dialog.Content).Children.Add(scroll);
                        dialog.ShowDialog();
                    }));
                    break;
            }
        }

        void Apply(Preferences next)
        {
            if (!bindings.Apply(next.ConvertKey, next.ConvertModifiers, next.EditKey, next.EditModifiers))
            {
                throw new Exception(L("shortcut.in.use"));
            }

            shortcuts = true;
            p = next;
            Autorun();
            Save();
            Refresh();
        }

        void PickColor(bool accent, Radial preview)
        {
            using (var d = new Forms.ColorDialog())
            {
                if (d.ShowDialog() != Forms.DialogResult.OK)
                    return;
                var c = d.Color;
                var hex = string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
                if (accent)
                    p.AccentColor = hex;
                else
                    p.PetalColor = hex;
                Save();
                preview.Paint();
            }
        }

        static string[] Default(bool edit)
        {
            var edits = new[]
            {
                "pdf-merge",
                "pdf-split",
                "media-join",
                "audio-join",
                "txt-merge",
                "zip",
                "tar",
                "7z",
                "gz",
                "extract",
                "names",
                "paths"
            };
            return edit ? edits : Engine.Targets.Except(edits).ToArray();
        }

        void Welcome()
        {
            var w = Modal("Hepta", 420);
            var panel = (StackPanel)w.Content;
            panel.Children.Add(Logo(72));
            panel.Children.Add(Text("Hepta · " + L("welcome"), 24));
            var english = new CheckBox
            {
                Content = "English",
                IsChecked = p.English,
                Foreground = Foreground
            };
            panel.Children.Add(english);
            var start = new CheckBox
            {
                Content = "Windows ile başlat / Start with Windows",
                Foreground = Foreground,
                IsChecked = p.Startup
            };
            var notify = new CheckBox
            {
                Content = "Bildirimler / Notifications",
                Foreground = Foreground,
                IsChecked = p.Notify
            };
            panel.Children.Add(start);
            panel.Children.Add(notify);
            panel.Children.Add(Text("Ctrl+Alt+C  ·  Ctrl+Alt+E", 15));
            panel.Children.Add(Button("Kullanmaya başla / Get started", () =>
            {
                p.English = english.IsChecked == true;
                p.Startup = start.IsChecked == true;
                p.Notify = notify.IsChecked == true;
                p.Welcome = false;
                Autorun();
                Save();
                w.Close();
                Refresh();
            }));
            w.ShowDialog();
        }

        Window Modal(string title, int width)
        {
            var dialog = new Window
            {
                Title = title,
                Width = width,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Background,
                Foreground = Foreground,
                Content = new StackPanel
                {
                    Margin = new Thickness(16)
                },
                Topmost = true
            };
            var screen = Screens.Resolve(sourceMonitor);
            dialog.ContentRendered += (s, e) =>
            {
                if (dialog.ShowActivated)
                    Screens.Position(dialog, screen, Forms.Cursor.Position, true, false, 0);
            };
            return dialog;
        }

        void Pick(bool edit)
        {
            var d = new OpenFileDialog
            {
                Multiselect = true
            };
            if (d.ShowDialog() == true)
                Menu(d.FileNames, edit);
        }

        void Menu(string[] files, bool edit)
        {
            if (popup != null)
                popup.Close();
            if (files.Length == 0)
            {
                Toast(L("select.files.in.explorer.or.on.the.desktop"));
                return;
            }

            var exts = files.Select(Engine.Ext).Distinct().ToArray();
            string key = (exts.Length == 1 ? exts[0] : "mixed") + "/" + (edit ? 1 : 0);
            string[] ids;
            if (!p.Profiles.TryGetValue(key, out ids))
                ids = Default(edit).OrderByDescending(id => files.All(f => Engine.Accept(f, id))).ToArray();
            var cursor = Forms.Cursor.Position;
            var screen = Forms.Screen.FromPoint(cursor);
            sourceMonitor = screen.DeviceName;
            var w = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
                ShowInTaskbar = false,
                Width = p.MenuSize,
                Height = p.MenuSize
            };
            popup = w;
            var radial = new Radial(files, ids, p, (id, custom) =>
            {
                w.Close();
                Start(files, id, custom);
            });
            w.Content = radial;
            w.SourceInitialized += (s, e) =>
            {
                Screens.Position(w, screen, cursor, p.Center, false, 0);
                var source = (HwndSource)PresentationSource.FromVisual(w);
                source.AddHook(delegate (IntPtr hwnd, int message, IntPtr wp, IntPtr lp, ref bool handled)
                {
                    if (message == 0x84)
                    {
                        int packed = unchecked((int)lp.ToInt64());
                        var pt = w.PointFromScreen(new Point((short)(packed & 65535), (short)(packed >> 16)));
                        if (!(w.InputHitTest(pt) is System.Windows.Shapes.Path))
                        {
                            handled = true;
                            return new IntPtr(-1);
                        }
                    }

                    return IntPtr.Zero;
                });
            };
            w.MouseLeftButtonDown += (s, e) =>
            {
                if (e.OriginalSource == w || e.OriginalSource == radial)
                    w.Close();
            };
            w.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                    w.Close();
            };
            bool closing = false;
            w.Closing += (s, e) => closing = true;
            w.Deactivated += (s, e) =>
            {
                if (!closing && w.IsVisible)
                    w.Close();
            };
            w.Closed += (s, e) => popup = null;
            w.Show();
            w.Activate();
            radial.Animate();
        }

        async void Start(string[] files, string id, bool custom)
        {
            if (id == "paths" || id == "names")
            {
                try
                {
                    Clipboard.SetText(string.Join(Environment.NewLine, id == "paths" ? files : files.Select(Path.GetFileName)));
                }
                catch (Exception e)
                {
                    Toast(e.Message);
                }

                return;
            }

            int quality = p.Quality;
            var options = p.Media.Copy();
            var outputOptions = p.Output.Copy();
            string points = null;
            string mergedName = Path.GetFileNameWithoutExtension(files[0]);
            if (custom || id == "media-join" || id == "audio-join" || id == "pdf-split" || id == "pdf-merge" || id == "txt-merge")
            {
                var w = Modal(Radial.Caption(id, p.English), 360);
                var panel = (StackPanel)w.Content;
                panel.Children.Add(Text(Radial.Caption(id, p.English), 20));
                if (id == "jpg" || (id == "pdf" && Engine.Images.Contains(Engine.Ext(files[0]))))
                {
                    panel.Children.Add(Text(L("white.background.and.lossy.jpeg.compression.will.be.applie"), 14));
                    if (custom)
                        Slider(panel, L("quality"), quality, 1, 100, v => quality = v);
                }

                if (id == "svg")
                {
                    panel.Children.Add(Text(L("contours.follow.regions.of.similar.colour.photos.produce.a"), 14));
                    Slider(panel, L("detail"), quality, 1, 100, v => quality = v);
                }

                if (id == "pdf-split" || id == "pdf-merge")
                    panel.Children.Add(Text(L("pages.are.rebuilt.as.images.selectable.text.forms.links.an"), 14));
                if (id == "pdf-split")
                {
                    int count;
                    try
                    {
                        count = await NativeMedia.Pages(files[0], CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        Toast(ex.Message);
                        return;
                    }

                    panel.Children.Add(new FileLabel(Path.GetFileName(files[0])) { Foreground = Foreground });
                    panel.Children.Add(Text(count + L("pages"), 14));
                    var field = new TextBox();
                    var ranges = Text("", 14);
                    panel.Children.Add(Text(L("split.after.pages.comma.separated"), 14));
                    panel.Children.Add(field);
                    panel.Children.Add(ranges);
                    field.TextChanged += (s, e) =>
                    {
                        try
                        {
                            var cuts = Documents.Cuts(field.Text, count);
                            int first = 1;
                            ranges.Text = string.Join(", ", cuts.Concat(new[] { count }).Select(last =>
                            {
                                var label = first + "–" + last;
                                first = last + 1;
                                return label;
                            }));
                            points = field.Text;
                        }
                        catch
                        {
                            points = null;
                            ranges.Text = L("enter.valid.split.points");
                        }
                    };
                }

                if (id == "txt-merge" || id == "media-join" || id == "audio-join" || id == "pdf-merge")
                {
                    panel.Children.Add(MergeList(files));
                }

                if (custom)
                {
                    OutputFields(panel, outputOptions, id, false);
                }

                if (custom && (Engine.Audio.Contains(id) || Engine.Video.Contains(id)))
                    MediaFields(panel, options, Engine.Video.Contains(id), false);
                if (Pending.IsMerge(id))
                {
                    panel.Children.Add(Text(L("output.name"), 14));
                    var name = new TextBox
                    {
                        Text = mergedName
                    };
                    name.TextChanged += (s, e) => mergedName = name.Text.Trim();
                    panel.Children.Add(name);
                }

                panel.Children.Add(Button(L("start"), () =>
                {
                    if (mergedName.Length == 0 || mergedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || mergedName.TrimEnd('.', ' ') != mergedName)
                        throw new InvalidDataException("File name");
                    if (id != "pdf-split" || points != null)
                        w.DialogResult = true;
                }));
                if (w.ShowDialog() != true)
                    return;
            }

            if (id == "media-join" || id == "audio-join" || id == "pdf-split" || id == "pdf-merge" || id == "zip" || id == "tar" || id == "7z" || id == "txt-merge")
                Queue(files, id, quality, points, mergedName, options, outputOptions);
            else
                foreach (var f in files)
                    Queue(new[] { f }, id, quality, null, null, options, outputOptions);
        }

        async void Queue(string[] files, string id, int quality, string points = null, string mergedName = null, MediaOptions options = null, OutputOptions outputOptions = null)
        {
            string monitor = sourceMonitor;
            var j = new Job
            {
                Sources = files,
                Operation = id,
                Name = Path.GetFileName(files[0]) + " → " + Radial.Caption(id, p.English),
                State = "wait"
            };
            jobs.Insert(0, j);
            var progress = new Progress<double>(n => j.Percent = Math.Max(0, Math.Min(100, n)));
            Refresh();
            try
            {
                await Slot();
                try
                {
                    j.Cancel.Token.ThrowIfCancellationRequested();
                    j.State = "run";
                    Refresh();
                    Notice(j.Name, monitor, p.SuccessSeconds, j, true);
                    j.Output = await Task.Run(() => OutputOptions.With(outputOptions, () => OutputOptions.Watch(files, () => Pending.IsMerge(id) ? Pending.Run(files, id, quality, mergedName, j.Cancel.Token, out j.Decision) : id == "pdf-split" ? Documents.Split(files[0], points, quality, j.Cancel.Token) : Engine.Run(files, id, quality, j.Cancel.Token, options, progress))));
                    j.State = j.Decision == null ? "done" : "decision";
                }
                finally
                {
                    ReleaseSlot();
                }
            }
            catch (OperationCanceledException)
            {
                j.State = "cancel";
            }
            catch (Exception e)
            {
                j.State = "error";
                j.Error = e.Message;
            }

            History();
            Refresh();
            Notice(State(j) + "\n" + (j.Error ?? j.Name), monitor, j.Error == null ? p.SuccessSeconds : p.ErrorSeconds, j, false);
        }

        string State(Job j)
        {
            switch (j.State)
            {
                case "decision":
                    return L("awaiting.decision");
                case "wait":
                    return L("queued");
                case "run":
                    return L("running");
                case "done":
                    return L("completed");
                case "cancel":
                    return L("cancelled");
                default:
                    return L("failed");
            }
        }

        void ListJobs(IEnumerable<Job> selection)
        {
            foreach (var j in selection.Take(250))
                Row(body, j);
        }

        void Row(Panel panel, Job j)
        {
            var row = new StackPanel();
            JobHeading(row, j);
            if (j.Decision != null)
            {
                row.Children.Add(Text(L("included") + ": " + string.Join(", ", j.Decision.Included) + "\n" + L("skipped") + ": " + string.Join("\n", j.Decision.Failures), 13));
                row.Children.Add(Button(L("keep"), () =>
                {
                    j.Output = j.Decision.Keep();
                    j.Decision = null;
                    j.State = "done";
                    History();
                    Refresh();
                }));
                row.Children.Add(Button(L("discard.partial.output"), () =>
                {
                    j.Decision.Discard();
                    j.Decision = null;
                    j.State = "cancel";
                    History();
                    Refresh();
                }));
            }

            if (j.Error != null)
                row.Children.Add(Text(j.Error, 12));
            if (j.State == "wait" || j.State == "run")
                row.Children.Add(Button(L("cancel"), () => j.Cancel.Cancel()));
            if (j.Output != null && (File.Exists(j.Output) || Directory.Exists(j.Output)))
                row.Children.Add(Button(L("open.output.folder"), () => Process.Start(Directory.Exists(j.Output) ? j.Output : Path.GetDirectoryName(j.Output))));
            panel.Children.Add(row);
        }

        void Toast(string message)
        {
            Notice(message, sourceMonitor, p.ErrorSeconds, null, false);
        }
    }
}
