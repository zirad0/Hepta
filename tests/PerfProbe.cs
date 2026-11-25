using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace Hepta
{
    internal static class PerfProbe
    {
        [STAThread]
        public static int Main()
        {
            var folder = Path.Combine(Path.GetTempPath(), "hepta-perf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                Preferences.DirectoryPath = folder;
                var app = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
                var dashboard = new Dashboard(true, true);
                var process = Process.GetCurrentProcess();
                var timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                TimeSpan cpu = TimeSpan.Zero;
                var wall = new Stopwatch();
                bool warmed = false;
                timer.Tick += (s, e) =>
                {
                    if (!warmed)
                    {
                        warmed = true;
                        timer.Interval = TimeSpan.FromSeconds(5);
                        process.Refresh();
                        cpu = process.TotalProcessorTime;
                        wall.Start();
                        return;
                    }

                    timer.Stop();
                    wall.Stop();
                    process.Refresh();
                    Console.WriteLine("idle_seconds=" + wall.Elapsed.TotalSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                    Console.WriteLine("cpu_ms=" + (process.TotalProcessorTime - cpu).TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Console.WriteLine("working_set_bytes=" + process.WorkingSet64);
                    var f = Path.Combine(folder, "sample.txt");
                    File.WriteAllText(f, "Hepta");
                    var settings = new Preferences();
                    var samples = new double[40];
                    for (int i = -2; i < samples.Length; i++)
                    {
                        var start = Stopwatch.StartNew();
                        var radial = new Radial(new[] { f }, new[] { "html", "pdf", "zip", "names", "paths", "gz" }, settings, null);
                        radial.Measure(new Size(360, 360));
                        radial.Arrange(new Rect(0, 0, 360, 360));
                        radial.UpdateLayout();
                        start.Stop();
                        if (i >= 0)
                            samples[i] = start.Elapsed.TotalMilliseconds;
                    }

                    Array.Sort(samples);
                    Console.WriteLine("radial_median_ms=" + samples[20].ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                    Console.WriteLine("radial_p95_ms=" + samples[37].ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                    dashboard.EndTest();
                };
                timer.Start();
                app.Run();
                return 0;
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
