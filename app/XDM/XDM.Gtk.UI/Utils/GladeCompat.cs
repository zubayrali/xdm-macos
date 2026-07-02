using Gtk;

namespace XDM.GtkUI.Utils
{
    internal static class GladeCompat
    {
        // ponytail: GtkSharp's Builder.AddFromFile is broken on macOS —
        // GLib.Marshaller.StringToFilenamePtr throws "Invalid byte sequence in
        // conversion input" for any path. Read the file in managed code and
        // pass the XML as a string instead, which marshals cleanly everywhere.
        public static void AddGladeFile(this Builder builder, string path)
        {
            builder.AddFromString(System.IO.File.ReadAllText(path));
        }
    }
}
