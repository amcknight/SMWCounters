namespace LiveSplit.SmwCounters.Snes;

// EmuStatus.StateName values (SNES.EmuState.ToString()). Strings rather than
// the SNES.EmuState enum so the pure classes (StatusDot, StatusChangeFilter)
// stay free of SNES.dll types; a typo'd literal would otherwise fail silently
// into a default branch with no compile error. Test files keep raw literals
// deliberately — they pin the wire contract.
internal static class SnesState
{
    public const string Detached    = "Detached";
    public const string NoContent   = "NoContent";
    public const string Searching   = "Searching";
    public const string Discovering = "Discovering";
    public const string Resolved    = "Resolved";
    public const string Held        = "Held";
    public const string Degraded    = "Degraded";

    // Base committed and readable — reads stay valid in all three.
    public static bool IsResolvedFamily(string stateName)
        => stateName == Resolved || stateName == Held || stateName == Degraded;
}
