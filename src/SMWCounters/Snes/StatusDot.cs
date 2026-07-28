using System.Drawing;

namespace LiveSplit.SmwCounters.Snes;

// Pure EmuStatus -> status-dot color mapping, per the SNES.dll consumer
// contract's "Status-pixel mapping (SMWCounters)" section
// (snes_offsets/docs/status-first-consumption.md). Takes primitives rather
// than SNES.EmuStatus so tests can drive it (EmuStatus setters are internal
// to SNES.dll).
//
// v0.5.0 severity ladder: warm colors are trouble, cool colors are good,
// blue is the best state. Gray = cooldown (deliberately idle).
internal static class StatusDot
{
    public static readonly Color Blue    = Color.FromArgb(0x39, 0x8F, 0xE5); // resolved, witness-vouched
    public static readonly Color Green   = Color.FromArgb(0x2E, 0xCC, 0x40); // resolved, unvouched
    public static readonly Color Yellow  = Color.FromArgb(0xFF, 0xDC, 0x00); // searching / discovering
    public static readonly Color Orange  = Color.FromArgb(0xFF, 0x85, 0x1B); // degraded (rivals live)
    public static readonly Color Gray    = Color.FromArgb(0x9A, 0x9A, 0x9A); // retry cooldown armed
    public static readonly Color DimGray = Color.FromArgb(0x5A, 0x5A, 0x5A); // attached, no content
    public static readonly Color Red     = Color.FromArgb(0xE5, 0x3E, 0x3E); // detached

    // witnessVerdict/witnessBase describe the LAST candidate the identity
    // witness classified, which is only attributable to the committed base
    // when witnessBase == wramBase (an in-flight discovery can stamp a rival).
    // Cooldown-gray overrides only the unresolved states: on the
    // resolved family (Resolved/Held/Degraded) reads stay valid, so the dot
    // keeps reporting that.
    public static Color ColorFor(string stateName, bool isCoolingDown,
                                 string witnessVerdict, long witnessBase, long wramBase)
    {
        switch (stateName)
        {
            case SnesState.Resolved:
            case SnesState.Held:
                return witnessVerdict == "Real" && witnessBase == wramBase ? Blue : Green;
            case SnesState.Degraded:
                return Orange;
            case SnesState.Detached:
                return Red;
            case SnesState.Discovering:
            case SnesState.Searching:
                return isCoolingDown ? Gray : Yellow;
            case SnesState.NoContent:
                return isCoolingDown ? Gray : DimGray;
            default:
                return Yellow; // unknown future state: render as searching-ish
        }
    }
}
