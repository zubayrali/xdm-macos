using System;
using System.Runtime.InteropServices;
using Gtk;
using TraceLog;

namespace XDM.GtkUI.Utils
{
    // Thin P/Invoke wrapper over gtk-mac-integration's GtkosxApplication (no GtkSharp
    // binding exists) — reparents a GTK MenuBar into the native macOS top menu bar.
    // Same best-effort style as MacUtil: if the dylib is missing the call no-ops and
    // the app keeps working with its in-window hamburger menu only.
    internal static class GtkosxApp
    {
        // Resolved via DYLD_FALLBACK_LIBRARY_PATH set by the .app launcher (Homebrew lib dir).
        private const string Lib = "libgtkmacintegration-gtk3.dylib";

        [DllImport(Lib)] private static extern IntPtr gtkosx_application_get();
        [DllImport(Lib)] private static extern void gtkosx_application_set_menu_bar(IntPtr app, IntPtr menuShell);
        [DllImport(Lib)] private static extern void gtkosx_application_insert_app_menu_item(IntPtr app, IntPtr menuItem, int index);
        [DllImport(Lib)] private static extern void gtkosx_application_set_window_menu(IntPtr app, IntPtr menuItem);
        [DllImport(Lib)] private static extern void gtkosx_application_ready(IntPtr app);

        // GtkosxApplication emits "NSApplicationDidBecomeActive" (a GObject signal) on
        // every app activation — including dock-icon clicks. That's our hook to re-show
        // a window hidden by close-to-background, since GTK-quartz ignores dock reopen.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VoidSignalHandler(IntPtr instance, IntPtr userData);
        [DllImport("libgobject-2.0.0.dylib")]
        private static extern ulong g_signal_connect_data(IntPtr instance, string detailedSignal,
            VoidSignalHandler handler, IntPtr data, IntPtr destroyData, int connectFlags);
        private static VoidSignalHandler? didBecomeActiveHandler; // keeps the native callback alive

        public static bool IsMac { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        // Attach the menu bar + app-menu items (About/Preferences slots) and finish
        // bootstrap. Call once, after the main window is realized. Quit (⌘Q) is added
        // by the library itself.
        // Connect the app-activation hook. Works from app startup — the library's
        // notification observer is registered when the singleton is created, before
        // ready() — so this also covers --background launches where the main window
        // is never realized. Delivered on the main NSRunLoop == the GTK main loop
        // thread, so touching GTK widgets from the callback is safe.
        public static void ConnectDidBecomeActive(System.Action callback)
        {
            if (!IsMac) return;
            try
            {
                var app = gtkosx_application_get();
                if (app == IntPtr.Zero) return;
                didBecomeActiveHandler = (_, _) => callback();
                g_signal_connect_data(app, "NSApplicationDidBecomeActive",
                    didBecomeActiveHandler, IntPtr.Zero, IntPtr.Zero, 0);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "GtkosxApp.ConnectDidBecomeActive failed");
            }
        }

        public static bool Install(MenuBar menuBar, Widget[] appMenuItems, MenuItem? windowMenu)
        {
            if (!IsMac) return false;
            try
            {
                var app = gtkosx_application_get();
                if (app == IntPtr.Zero) return false;
                gtkosx_application_set_menu_bar(app, menuBar.Handle);
                for (var i = 0; i < appMenuItems.Length; i++)
                {
                    gtkosx_application_insert_app_menu_item(app, appMenuItems[i].Handle, i);
                }
                if (windowMenu != null)
                {
                    gtkosx_application_set_window_menu(app, windowMenu.Handle);
                }
                gtkosx_application_ready(app);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "GtkosxApp.Install failed (is gtk-mac-integration installed?)");
                return false;
            }
        }
    }
}
