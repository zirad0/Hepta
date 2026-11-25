using System;
using System.Runtime.InteropServices;

namespace Hepta
{
    internal static class ShellView
    {
        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface Provider
        {
            [PreserveSig]
            int QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out Browser browser);
        }

        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface Browser
        {
            void GetWindow(out IntPtr hwnd);
            void ContextSensitiveHelp();
            void InsertMenusSB();
            void SetMenuSB();
            void RemoveMenusSB();
            void SetStatusTextSB();
            void EnableModelessSB();
            void TranslateAcceleratorSB();
            void BrowseObject();
            void GetViewStateStream();
            void GetControlWindow();
            void SendControlMsgSB();
            void QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out View view);
        }

        [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface View
        {
            void GetWindow(out IntPtr hwnd);
        }

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hwnd);
        public static bool Visible(object window)
        {
            Browser browser = null;
            View view = null;
            try
            {
                var p = window as Provider;
                if (p == null)
                    return false;
                Guid service = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"), iid = typeof(Browser).GUID;
                if (p.QueryService(ref service, ref iid, out browser) < 0 || browser == null)
                    return false;
                browser.QueryActiveShellView(out view);
                IntPtr hwnd;
                view.GetWindow(out hwnd);
                return IsWindowVisible(hwnd);
            }
            catch (COMException)
            {
                return false;
            }
            finally
            {
                if (view != null)
                    Marshal.ReleaseComObject(view);
                if (browser != null)
                    Marshal.ReleaseComObject(browser);
            }
        }

        [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface Windows
        {
            void GetTypeInfoCount();
            void GetTypeInfo();
            void GetIDsOfNames();
            void Invoke();
            void Count();
            void Item();
            void NewEnum();
            void Register();
            void RegisterPending();
            void Revoke();
            void OnNavigate();
            void OnActivated();
            [PreserveSig]
            int FindWindowSW([In, MarshalAs(UnmanagedType.Struct)] ref object loc, [In, MarshalAs(UnmanagedType.Struct)] ref object root, int kind, out int hwnd, int flags, [MarshalAs(UnmanagedType.IDispatch)] out object dispatch);
        }

        public static object Desktop(object windows)
        {
            object loc = 0, root = null, result;
            int hwnd;
            return ((Windows)windows).FindWindowSW(ref loc, ref root, 8, out hwnd, 1, out result) == 0 ? result : null;
        }
    }
}
