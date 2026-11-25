using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Globalization;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace Hepta
{
    internal sealed class FileLabel : TextBlock
    {
        readonly string full;
        string last;
        double width = -1, size = -1;
        public FileLabel(string name)
        {
            full = name;
            ToolTip = name;
            ToolTipService.SetInitialShowDelay(this, 500);
            AutomationProperties.SetName(this, name);
            Text = name;
            TextWrapping = TextWrapping.NoWrap;
            SizeChanged += (s, e) => Fit();
            LayoutUpdated += (s, e) => Fit();
        }

        double MeasureString(string s)
        {
            return new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Foreground ?? Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;
        }

        void Fit()
        {
            if (ActualWidth <= 0 || (ActualWidth == width && FontSize == size))
                return;
            width = ActualWidth;
            size = FontSize;
            string result = full;
            if (MeasureString(full) > width)
            {
                string ext = Path.GetExtension(full), stem = full.Substring(0, full.Length - ext.Length);
                var idx = StringInfo.ParseCombiningCharacters(stem);
                int lo = 0, hi = idx.Length;
                while (lo < hi)
                {
                    int m = (lo + hi + 1) / 2;
                    string s = stem.Substring(0, m == idx.Length ? stem.Length : idx[m]) + "…" + ext;
                    if (MeasureString(s) <= width)
                        lo = m;
                    else
                        hi = m - 1;
                }

                result = stem.Substring(0, lo == idx.Length ? stem.Length : idx[lo]) + "…" + ext;
            }

            if (last != result)
            {
                last = Text = result;
            }
        }
    }

    internal static class Screens
    {
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("shcore.dll")]
        static extern int GetDpiForMonitor(IntPtr mon, int kind, out uint x, out uint y);
        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint flags);
        public static void Position(Window w, Forms.Screen screen, System.Drawing.Point cursor, bool center, bool bottom, int stack)
        {
            var area = screen.WorkingArea;
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero)
                return;
            uint dx, dy;
            var mon = MonitorFromPoint(new System.Drawing.Point(area.Left + area.Width / 2, area.Top + area.Height / 2), 2);
            if (GetDpiForMonitor(mon, 0, out dx, out dy) != 0)
                dx = dy = GetDpiForWindow(hwnd);
            double factor = dx / 96d;
            int width = Math.Min(area.Width, (int)Math.Ceiling(w.Width * factor)), height = Math.Min(area.Height, (int)Math.Ceiling((double.IsNaN(w.Height) ? w.ActualHeight : w.Height) * factor));
            if (height <= 0)
                return;
            int left = bottom ? area.Right - width - 12 : center ? area.Left + (area.Width - width) / 2 : cursor.X - width / 2;
            int top = bottom ? area.Bottom - height - 12 - stack : center ? area.Top + (area.Height - height) / 2 : cursor.Y - height / 2;
            SetWindowPos(hwnd, IntPtr.Zero, Math.Max(area.Left, Math.Min(area.Right - width, left)), Math.Max(area.Top, Math.Min(area.Bottom - height, top)), width, height, 0x14);
        }

        public static Forms.Screen Resolve(string device)
        {
            return Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == device) ?? Forms.Screen.FromPoint(Forms.Cursor.Position);
        }
    }
}
