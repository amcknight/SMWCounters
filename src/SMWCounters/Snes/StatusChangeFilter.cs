namespace LiveSplit.SmwCounters.Snes;

// Log-on-change filter for SNES.dll status transitions: the consumer
// contract's idiom is to log only on change of (StateName, Generation,
// WramBase, IsCoolingDown, LastError), which kills the rotating-message
// noise of per-tick logging. The ROM identity slug (v1.7.0) joins that key,
// so a ROM swap that never disturbs the WRAM bind still leaves a trace —
// including the corrective second edge the release documents near loads.
// The window title does NOT: emulators churn it (FPS counters and the like),
// and it is decoration for a human reading the log. Pure primitives in,
// formatted line (or null) out, so tests can drive it without SNES.dll types.
internal sealed class StatusChangeFilter
{
    private string lastKey;

    public string OnStatus(string stateName, int generation, long wramBase,
                           bool isCoolingDown, string lastError,
                           string methodName, string rebindReasonName,
                           long scanTotalMs,
                           int rivalCount = 0, bool isContested = false,
                           int regressionCount = 0,
                           string romSlug = "", string windowTitle = "")
    {
        lastError = lastError ?? "";
        romSlug = romSlug ?? "";
        windowTitle = windowTitle ?? "";
        string key = $"{stateName}|{generation}|{wramBase}|{isCoolingDown}|{lastError}|{romSlug}";
        if (key == lastKey) { return null; }
        lastKey = key;

        string line = $"SNS state={stateName} gen={generation} base=0x{wramBase:X}"
            + $" method={methodName} rebind={rebindReasonName}"
            + (rivalCount > 0 ? $" rivals={rivalCount}" : "")
            + (isContested ? " contested" : "")
            + (regressionCount > 0 ? $" reg={regressionCount}" : "")
            + (isCoolingDown ? " cooldown" : "")
            + (lastError.Length == 0 ? "" : $" err=\"{lastError}\"")
            + (romSlug.Length == 0 ? "" : $" rom={romSlug}")
            + (windowTitle.Length == 0 ? "" : $" win=\"{windowTitle}\"");

        if (SnesState.IsResolvedFamily(stateName) && scanTotalMs > 0)
        {
            line += $" scanMs={scanTotalMs}";
        }
        return line;
    }

    public void Reset() => lastKey = null;
}
