using System.Drawing;

namespace LiveSplit.SmwCounters.Snes;

// Pure EmuStatus -> status-dot color mapping, per the SNES.dll consumer
// contract's "Status-pixel mapping (SMWCounters)" section
// (snes_offsets/docs/status-first-consumption.md). Takes primitives rather
// than SNES.EmuStatus so tests can drive it (EmuStatus setters are internal
// to SNES.dll).
internal static class StatusDot
{
    public static readonly Color Green     = Color.FromArgb(0x2E, 0xCC, 0x40); // resolved, witness-vouched
    public static readonly Color PaleGreen = Color.FromArgb(0x94, 0xD8, 0x9C); // resolved, unvouched
    public static readonly Color Yellow    = Color.FromArgb(0xFF, 0xDC, 0x00); // degraded (rivals live)
    public static readonly Color Blue      = Color.FromArgb(0x39, 0x8F, 0xE5); // discovering (scan running)
    public static readonly Color Gray      = Color.FromArgb(0x9A, 0x9A, 0x9A); // searching
    public static readonly Color DimGray   = Color.FromArgb(0x5A, 0x5A, 0x5A); // attached, no content
    public static readonly Color Orange    = Color.FromArgb(0xFF, 0x85, 0x1B); // retry cooldown armed
    public static readonly Color Red       = Color.FromArgb(0xE5, 0x3E, 0x3E); // detached

    // witnessVerdict/witnessBase describe the LAST candidate the identity
    // witness classified, which is only attributable to the committed base
    // when witnessBase == wramBase (an in-flight discovery can stamp a rival).
    // Cooldown-orange overrides only the unresolved states: on the
    // resolved family (Resolved/Held/Degraded) reads stay valid, so the dot
    // keeps reporting that.
    public static Color ColorFor(string stateName, bool isCoolingDown,
                                 string witnessVerdict, long witnessBase, long wramBase)
    {
        switch (stateName)
        {
            case "Resolved":
            case "Held":
                return witnessVerdict == "Real" && witnessBase == wramBase ? Green : PaleGreen;
            case "Degraded":
                return Yellow;
            case "Detached":
                return Red;
            case "Discovering":
                return isCoolingDown ? Orange : Blue;
            case "Searching":
                return isCoolingDown ? Orange : Gray;
            case "NoContent":
                return isCoolingDown ? Orange : DimGray;
            default:
                return Gray; // unknown future state: render as idle searching
        }
    }
}
