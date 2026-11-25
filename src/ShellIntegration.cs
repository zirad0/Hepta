using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;

namespace Hepta
{
    internal static class ShellIntegration
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr hwnd, StringBuilder cls, int size);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        static void Release(object value)
        {
            if (value != null && Marshal.IsComObject(value))
                Marshal.ReleaseComObject(value);
        }

        public static string[] Selection()
        {
            var hwnd = GetForegroundWindow();
            var cls = new StringBuilder(128);
            GetClassName(hwnd, cls, 128);
            bool desktop = cls.ToString() == "Progman" || cls.ToString() == "WorkerW";
            if (!desktop && cls.ToString() != "CabinetWClass" && cls.ToString() != "ExploreWClass")
                return new string[0];
            dynamic shell = null, windows = null, window = null, doc = null, items = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                windows = shell.Windows();
                if (desktop)
                {
                    window = ShellView.Desktop((object)windows);
                }
                else
                {
                    for (int i = 0; i < windows.Count; i++)
                    {
                        dynamic candidate = windows.Item(i);
                        if (candidate != null && (long)candidate.HWND == hwnd.ToInt64() && ShellView.Visible((object)candidate))
                        {
                            if (window != null)
                            {
                                Release(candidate);
                                return new string[0];
                            }

                            window = candidate;
                        }
                        else
                            Release(candidate);
                    }
                }

                if (window == null)
                    return new string[0];
                doc = window.Document;
                items = doc.SelectedItems();
                var paths = new List<string>();
                for (int i = 0; i < items.Count; i++)
                {
                    dynamic entry = items.Item(i);
                    try
                    {
                        string path = entry.Path;
                        if (File.Exists(path) || Directory.Exists(path))
                            paths.Add(path);
                        else
                            return new string[0];
                    }
                    finally
                    {
                        Release(entry);
                    }
                }

                return paths.ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                return new string[0];
            }
            catch (COMException)
            {
                return new string[0];
            }
            catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                return new string[0];
            }
            finally
            {
                Release(items);
                Release(doc);
                Release(window);
                Release(windows);
                Release(shell);
            }
        }
    }
}
