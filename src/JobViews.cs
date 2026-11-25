using System;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Diagnostics;

namespace Hepta
{
    internal partial class Dashboard
    {
        string searchText = "", stateChoice = "", typeChoice = "";
        int dateDays = 0;
        void JobView(bool historical)
        {
            var eligible = jobs.Where(j => !historical || (j.State != "wait" && j.State != "run")).ToArray();
            if (historical)
            {
                Check(body, L("save.history"), p.History, v => p.History = v);
                body.Children.Add(Button(L("clear.history"), () =>
                {
                    jobs.RemoveAll(j => j.State != "run" && j.State != "wait" && j.Decision == null);
                    if (File.Exists(history))
                        File.Delete(history);
                    Refresh();
                }));
            }

            var counters = Text(string.Join("   ·   ", eligible.GroupBy(j => j.State).Select(g => State(g.First()) + " " + g.Count())), 13);
            body.Children.Add(counters);
            var search = new TextBox
            {
                Text = searchText,
                Margin = new Thickness(2),
                ToolTip = L("search.filename")
            };
            body.Children.Add(search);
            var filters = new WrapPanel();
            var states = new[]
            {
                ""
            }.Concat(eligible.Select(j => j.State).Distinct()).ToArray();
            var status = new ComboBox
            {
                MinWidth = 120,
                ItemsSource = states.Select(s => s == "" ? L("all.states") : State(new Job { State = s })).ToArray(),
                SelectedIndex = Math.Max(0, Array.IndexOf(states, stateChoice))
            };
            var types = new[]
            {
                ""
            }.Concat(eligible.Select(j => j.Operation ?? "").Where(s => s.Length > 0).Distinct()).ToArray();
            var type = new ComboBox
            {
                MinWidth = 100,
                ItemsSource = types.Select(s => s == "" ? L("all.actions") : Radial.Caption(s, p.English)).ToArray(),
                SelectedIndex = Math.Max(0, Array.IndexOf(types, typeChoice))
            };
            int[] days =
            {
                0,
                1,
                7,
                30
            };
            var date = new ComboBox
            {
                ItemsSource = new[]
                {
                    L("all.time"),
                    L("last.24.hours"),
                    L("last.7.days"),
                    L("last.30.days")
                },
                SelectedIndex = Array.IndexOf(days, dateDays)
            };
            filters.Children.Add(status);
            filters.Children.Add(type);
            filters.Children.Add(date);
            body.Children.Add(filters);
            var list = new StackPanel();
            body.Children.Add(list);
            Action update = () =>
            {
                list.Children.Clear();
                searchText = search.Text;
                stateChoice = states[status.SelectedIndex];
                typeChoice = types[type.SelectedIndex];
                dateDays = days[date.SelectedIndex];
                foreach (var j in eligible.Where(j => (stateChoice == "" || stateChoice == j.State) && (typeChoice == "" || typeChoice == j.Operation) && (dateDays == 0 || j.Time >= DateTime.Now.AddDays(-dateDays)) && j.Name.IndexOf(searchText, StringComparison.CurrentCultureIgnoreCase) >= 0).Take(250))
                    Row(list, j);
            };
            search.TextChanged += (s, e) => update();
            status.SelectionChanged += (s, e) => update();
            type.SelectionChanged += (s, e) => update();
            date.SelectionChanged += (s, e) => update();
            update();
        }

        void JobHeading(Panel row, Job j)
        {
            row.Children.Add(new FileLabel(j.SourceName) { Foreground = Foreground, FontSize = 15, Margin = new Thickness(2, 6, 2, 2) });
            row.Children.Add(Text(j.Time.ToString("g") + "  ·  " + State(j) + (j.Operation == null ? "" : "  ·  " + Radial.Caption(j.Operation, p.English)), 12));
            if (j.State == "run")
            {
                var bar = new ProgressBar
                {
                    Height = 4,
                    Maximum = 100,
                    IsIndeterminate = j.Percent < 0
                };
                row.Children.Add(bar);
                var timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(300)
                };
                timer.Tick += (s, e) =>
                {
                    bar.IsIndeterminate = j.Percent < 0;
                    if (j.Percent >= 0)
                    {
                        bar.Value = j.Percent;
                        bar.ToolTip = Math.Round(j.Percent) + "%";
                    }
                };
                bar.Loaded += (s, e) => timer.Start();
                bar.Unloaded += (s, e) => timer.Stop();
            }

            if (j.Output != null && File.Exists(j.Output))
                row.Children.Add(Button(L("open.output"), () => Process.Start(new ProcessStartInfo(j.Output) { UseShellExecute = true })));
        }
    }
}
