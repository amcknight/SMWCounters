using System.Drawing;
using System.IO;
using System.Reflection;

namespace LiveSplit.SmwCounters.Counters;

internal static class IconLoader
{
    public static Bitmap Load(string resourceName)
    {
        Assembly asm = typeof(IconLoader).Assembly;
        using Stream s = asm.GetManifestResourceStream(resourceName);
        if (s == null) { return null; }
        // Bitmap(Stream) requires the stream to stay open for the bitmap's
        // lifetime (GDI+ reads lazily), but `using` closes it on return.
        // Copy into a stream-free bitmap so per-frame draws can't hit a
        // "generic error occurred in GDI+".
        using var streamBound = new Bitmap(s);
        return new Bitmap(streamBound);
    }
}
