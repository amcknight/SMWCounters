using System.Drawing;

namespace LiveSplit.SmwCounters.Snes;

// Pure EmuStatus -> status-dot color mapping, per the SNES.dll consumer
// contract's "Status-pixel mapping (SMWCounters)" section
// (snes_offsets/docs/status-first-consumption.md). Takes primitives rather
// than SNES.EmuStatus so tests can drive it (EmuStatus setters are internal
// to SNES.dll).
//
// v0.5.0 live-test revision: the three read-valid states get the calm colors
// (green degraded / blue resolved / purple held) because Degraded is the
// everyday steady state on RetroArch (rivals never retire); warm colors mean
// "not counting" (yellow still looking, orange no game, red no emulator).
// Gray = cooldown (deliberately idle). The witness vouch no longer changes
// the dot; it stays visible in the SNS debug-log lines.
internal static class StatusDot
{
    public static readonly Color Blue   = Color.FromArgb(0x39, 0x8F, 0xE5); // resolved (settled)
    public static readonly Color Purple = Color.FromArgb(0x9B, 0x59, 0xB6); // held (paused, base kept)
    public static readonly Color Green  = Color.FromArgb(0x2E, 0xCC, 0x40); // degraded (working, rivals live)
    public static readonly Color Yellow = Color.FromArgb(0xFF, 0xDC, 0x00); // searching / discovering
    public static readonly Color Orange = Color.FromArgb(0xFF, 0x85, 0x1B); // attached, no content
    public static readonly Color Gray   = Color.FromArgb(0x9A, 0x9A, 0x9A); // retry cooldown armed
    public static readonly Color Red    = Color.FromArgb(0xE5, 0x3E, 0x3E); // detached

    // witnessVerdict/witnessBase are retained for signature stability (and
    // possible future use) but no longer affect the color. Cooldown-gray
    // overrides only the unresolved states: on the read-valid family
    // (Resolved/Held/Degraded) reads stay valid, so the dot keeps reporting
    // that.
    public static Color ColorFor(string stateName, bool isCoolingDown,
                                 string witnessVerdict, long witnessBase, long wramBase)
    {
        switch (stateName)
        {
            case SnesState.Resolved:
                return Blue;
            case SnesState.Held:
                return Purple;
            case SnesState.Degraded:
                return Green;
            case SnesState.Detached:
                return Red;
            case SnesState.Discovering:
            case SnesState.Searching:
                return isCoolingDown ? Gray : Yellow;
            case SnesState.NoContent:
                return isCoolingDown ? Gray : Orange;
            default:
                return Yellow; // unknown future state: render as searching-ish
        }
    }
}
