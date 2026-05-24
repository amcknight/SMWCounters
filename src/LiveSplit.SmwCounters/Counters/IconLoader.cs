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
        return new Bitmap(s);
    }
}
