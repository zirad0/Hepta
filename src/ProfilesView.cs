using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace Hepta
{
    internal partial class Dashboard
    {
        void Profiles()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            body.Children.Add(grid);
            var left = new StackPanel();
            var right = new StackPanel();
            Grid.SetColumn(right, 1);
            grid.Children.Add(left);
            grid.Children.Add(right);
            var category = new ComboBox
            {
                ItemsSource = new[]
                {
                    "mixed"
                }.Concat(Engine.Images).Concat(Engine.Audio).Concat(Engine.Video).Concat(new[] { "pdf", "txt", "csv", "tsv", "svg", "xlsx", "ods", "json", "yaml", "srt", "vtt", "md", "ico", "zip", "tar", "gz", "7z" }).Distinct().ToArray(),
                SelectedIndex = 0
            };
            var mode = new ComboBox
            {
                ItemsSource = new[]
                {
                    L("convert"),
                    L("edit")
                },
                SelectedIndex = 0
            };
            left.Children.Add(category);
            left.Children.Add(mode);
            var rows = new StackPanel();
            left.Children.Add(rows);
            Func<string> key = () => category.SelectedItem + "/" + mode.SelectedIndex;
            Action render = null;
            render = () =>
            {
                rows.Children.Clear();
                right.Children.Clear();
                string k = key();
                string[] defaults = Default(mode.SelectedIndex == 1), order, pins;
                if (!p.Profiles.TryGetValue(k, out order))
                    order = defaults;
                if (!p.Pins.TryGetValue(k, out pins))
                    pins = new string[0];
                var ids = order.ToList();
                var pinned = pins.ToList();
                Action persist = () =>
                {
                    p.Profiles[k] = ids.Where(pinned.Contains).Concat(ids.Where(a => !pinned.Contains(a))).ToArray();
                    p.Pins[k] = pinned.ToArray();
                    Save();
                    render();
                };
                foreach (string id in ids.Concat(defaults.Except(ids)).ToArray())
                {
                    int pos = ids.IndexOf(id);
                    bool visible = pos >= 0, star = pinned.Contains(id);
                    if (page == 3 && !star)
                        continue;
                    var line = new DockPanel
                    {
                        AllowDrop = true,
                        Margin = new Thickness(1, 2, 1, 2)
                    };
                    var hide = Button(visible ? "◉" : "○", () =>
                    {
                        if (visible)
                        {
                            ids.Remove(id);
                            pinned.Remove(id);
                        }
                        else
                            ids.Add(id);
                        persist();
                    });
                    hide.ToolTip = L("show.hide");
                    DockPanel.SetDock(hide, Dock.Right);
                    line.Children.Add(hide);
                    var pin = Button(star ? "★" : "☆", () =>
                    {
                        if (star)
                            pinned.Remove(id);
                        else
                        {
                            if (!visible)
                                ids.Add(id);
                            pinned.Add(id);
                        }

                        persist();
                    });
                    pin.ToolTip = L("pin.unpin");
                    DockPanel.SetDock(pin, Dock.Right);
                    line.Children.Add(pin);
                    var grip = new TextBlock
                    {
                        Text = "⠿",
                        Foreground = Accent,
                        Cursor = Cursors.SizeNS,
                        VerticalAlignment = VerticalAlignment.Center,
                        Width = 20
                    };
                    DockPanel.SetDock(grip, Dock.Left);
                    line.Children.Add(grip);
                    var label = Text((visible ? (pos / 6 + 1) + " · " : "") + Radial.Caption(id, p.English), 14);
                    if (!visible)
                        label.Opacity = .45;
                    line.Children.Add(label);
                    grip.MouseMove += (s, e) =>
                    {
                        if (visible && e.LeftButton == MouseButtonState.Pressed)
                            DragDrop.DoDragDrop(grip, new DataObject("Hepta.ProfileAction", id), DragDropEffects.Move);
                    };
                    line.Drop += (s, e) =>
                    {
                        if (!e.Data.GetDataPresent("Hepta.ProfileAction") || !visible)
                            return;
                        string moved = (string)e.Data.GetData("Hepta.ProfileAction");
                        if (!ids.Remove(moved))
                            return;
                        ids.Insert(Math.Min(pos, ids.Count), moved);
                        if (star && !pinned.Contains(moved))
                            pinned.Add(moved);
                        if (!star)
                            pinned.Remove(moved);
                        persist();
                    };
                    rows.Children.Add(line);
                }

                if (page == 3 && pinned.Count == 0)
                    rows.Children.Add(Text(L("no.pinned.actions.in.this.profile"), 14));
                right.Children.Add(Text(L("live.preview"), 16));
                right.Children.Add(new Radial(new string[0], ids.ToArray(), p, null) { MaxWidth = 280 });
                right.Children.Add(Button(L("reset.profile"), () =>
                {
                    p.Profiles.Remove(k);
                    p.Pins.Remove(k);
                    Save();
                    render();
                }));
            };
            category.SelectionChanged += (s, e) => render();
            mode.SelectionChanged += (s, e) => render();
            render();
        }
    }
}
