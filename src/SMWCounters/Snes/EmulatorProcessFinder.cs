using System.Diagnostics;

namespace LiveSplit.SmwCounters.Snes;

// Finds the SNES emulator process the same way the kaizosplits autosplitter
// does, so both components always attach to the same emulator when several
// are open. Deliberately standalone (no SNES.dll or component dependencies)
// so future LiveSplit plugins can lift it wholesale.
internal static class EmulatorProcessFinder
{
    // Mirrors ../kaizosplits/Kaizo.asl's state() declarations, in order.
    // The kaizosplits autosplitter is the source of truth for this list —
    // do not reorder or extend without changing Kaizo.asl first.
    private static readonly string[] ProcessNames =
    {
        "snes9x", "snes9x-x64", "bsnes", "retroarch", "higan",
        "snes9x-rr", "mesen", "emuhawk", "ares", "mednafen",
    };

    // First running match in list order, null when none. Losing candidates
    // are disposed; the winner is the caller's to hold (and eventually
    // dispose or leak — Process finalizers cover the rare-churn case).
    public static Process Find()
    {
        foreach (string name in ProcessNames)
        {
            Process[] found = Process.GetProcessesByName(name);
            Process alive = null;
            foreach (Process p in found)
            {
                if (alive == null && !p.HasExited) { alive = p; }
                else { p.Dispose(); }
            }
            if (alive != null) { return alive; }
        }
        return null;
    }
}
