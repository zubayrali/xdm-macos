using System;
using System.Runtime.InteropServices;
using Gtk;

namespace XDM.GtkUI.Utils
{
    // GTK's Window.Present() raises a window within GTK's own stacking, but on macOS
    // it does NOT make the application active — so XDM windows open behind the browser
    // and the app never steals focus like a native app. This calls
    // [[NSApplication sharedApplication] activateIgnoringOtherApps:YES] via the Obj-C
    // runtime to bring XDM to the foreground, then presents the window.
    internal static class MacUtil
    {
        private const string Libobjc = "/usr/lib/libobjc.A.dylib";

        [DllImport(Libobjc, EntryPoint = "objc_getClass")]
        private static extern IntPtr objc_getClass(string name);

        [DllImport(Libobjc, EntryPoint = "sel_registerName")]
        private static extern IntPtr sel_registerName(string name);

        [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_bool(IntPtr receiver, IntPtr selector, bool arg);

        private static readonly bool IsMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        public static void ActivateApp()
        {
            if (!IsMac) return;
            try
            {
                var nsAppClass = objc_getClass("NSApplication");
                if (nsAppClass == IntPtr.Zero) return;
                var sharedApp = objc_msgSend(nsAppClass, sel_registerName("sharedApplication"));
                if (sharedApp == IntPtr.Zero) return;
                objc_msgSend_bool(sharedApp, sel_registerName("activateIgnoringOtherApps:"), true);
            }
            catch
            {
                // best-effort; never let focus handling break the window
            }
        }

        // Present the window AND bring the app to the foreground (macOS-aware).
        public static void PresentAndActivate(Window window)
        {
            ActivateApp();
            window.Present();
        }
    }
}
