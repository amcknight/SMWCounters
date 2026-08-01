using System.Diagnostics;
using System.Drawing;

using SNES;

namespace LiveSplit.SmwCounters.Snes;

// Bridges SNES.dll's structural WRAM discovery to the counters' ISnesMemory
// seam, driving the status-first consumer idiom
// (snes_offsets/docs/status-first-consumption.md) once per poll tick:
//   Ready() throw => not ready; GetOffset() retry while not ready (silent —
//   discovery runs on SNES.dll's own background task); Status() for telemetry.
// Exceptions are control flow, not telemetry: messages are never parsed.
internal sealed class SnesConnection : ISnesMemory
{
    // Process enumeration is comparatively expensive; at the 15 ms poll rate
    // an unthrottled scan would run ~66x/sec while no emulator is open.
    private const int AcquireIntervalMs = 1000;

    private readonly Emu emu = new();
    private readonly Stopwatch acquireClock = Stopwatch.StartNew();
    private Process process;
    private bool ready;
    private int lastGeneration = -1;
    private long lastAcquireMs = -AcquireIntervalMs;
    private int titleProcessId = -1;
    private string titleSlug;
    private long lastTitleAttemptMs = -AcquireIntervalMs;

    public SnesConnection()
    {
        Status = emu.Status(); // Detached snapshot, so consumers never see null
    }

    public EmuStatus Status { get; private set; }

    public Color DotColor => StatusDot.ColorFor(
        Status.StateName, Status.IsCoolingDown,
        Status.WitnessVerdict, Status.WitnessBase, Status.WramBase);

    public bool IsAttached => ready && process != null && !process.HasExited;

    // Raw emulator window title — the human-readable half of the ROM identity
    // in the debug log ("clean - Snes9x 1.63"). Deliberately unparsed, per the
    // consumer contract: every emulator decorates it differently and a
    // prefix-stripping table would be exactly the kind of per-build table
    // SNES.dll deleted. "" when detached or unavailable.
    public string WindowTitle { get; private set; } = "";

    // ROM identity slug (v1.7.0), "" before SNES.dll finds one. Render-only:
    // the contract puts it in the churn tier, and an identity can commit
    // transiently wrong around a ROM load before correcting itself.
    private string RomSlug => Status.Rom?.Slug ?? "";

    // Drive attach/discovery one step. Called every poll tick regardless of
    // timer phase (always-on discovery: the dot should be green before a run
    // starts). Non-blocking: discovery runs on SNES.dll's background task and
    // GetOffset() throws while it is in flight.
    public void Tick()
    {
        if (process != null && process.HasExited)
        {
            process = null;
            ready = false;
        }

        if (process == null && acquireClock.ElapsedMilliseconds - lastAcquireMs >= AcquireIntervalMs)
        {
            lastAcquireMs = acquireClock.ElapsedMilliseconds;
            process = EmulatorProcessFinder.Find();
            if (process != null)
            {
                emu.Attach(process);
                ready = false;
            }
        }

        if (process != null)
        {
            bool wasReady = ready;

            // A rival eviction rebinds silently (no Ready() throw) but bumps
            // Generation; every watcher bound to the old base must re-baseline,
            // which dropping `ready` achieves (IsAttached goes false for a
            // tick, so counters flush their PreviousByte state).
            if (ready && emu.Generation != lastGeneration) { ready = false; }

            try { emu.Ready(); } catch { ready = false; }

            // On a rebind (wasReady but not anymore), the Emu already has the
            // new base committed — GetOffset() would return immediately and
            // re-arm `ready` within this same tick, so IsAttached would never
            // read false and the counters would bridge PreviousByte across the
            // old and new WRAM bases. Skip the re-arm this tick so IsAttached
            // is false for exactly one poll; the next Tick() re-arms normally.
            // Fresh attach has wasReady == false already, so first-time
            // discovery is unaffected and re-arms without delay.
            if (!ready && !wasReady)
            {
                try
                {
                    emu.GetOffset();
                    ready = true;
                    lastGeneration = emu.Generation;
                }
                catch { /* in flight, cooling down, or declined — Status() carries the news */ }
            }
        }

        Status = emu.Status();
        RefreshWindowTitle(); // after the snapshot: keys off this tick's identity
    }

    public bool ReadWramByte(int snesOffset, out byte value)
    {
        value = 0;
        if (!IsAttached) { return false; }
        try
        {
            value = emu.Read1(snesOffset);
            return true;
        }
        catch
        {
            return false; // lost mid-tick; next Tick() re-enters the idiom
        }
    }

    // Human status-line fragment for the settings panel.
    public string Describe()
    {
        EmuStatus s = Status;
        string proc = process != null ? process.ProcessName : "?";
        switch (s.StateName)
        {
            case SnesState.Detached:
                return "No emulator found";
            case SnesState.NoContent:
                return $"{proc} · no game detected{CooldownSuffix(s)}";
            case SnesState.Searching:
                return $"{proc} · searching for game{CooldownSuffix(s)}{ErrorSuffix(s)}";
            case SnesState.Discovering:
                return $"{proc} · discovering WRAM…";
            case SnesState.Resolved:
            case SnesState.Held:
            case SnesState.Degraded:
                return $"{proc} · WRAM @ 0x{s.WramBase:X} ({s.MethodName})";
            default:
                return $"{proc} · {s.StateName}";
        }
    }

    private static string CooldownSuffix(EmuStatus s)
        => s.IsCoolingDown ? " (retry pending)" : "";

    private static string ErrorSuffix(EmuStatus s)
        => string.IsNullOrEmpty(s.LastError) ? "" : $" — {s.LastError}";

    // emu.WindowTitle() does a Process.Refresh() + MainWindowTitle read, which
    // has no business running 66x/sec on the poll tick. Three things make the
    // title newly informative, and nothing else does: a new process, a new ROM
    // identity, and a cached title that is still blank — EmulatorProcessFinder
    // can attach before the emulator's window exists, and a session that never
    // commits an identity would otherwise log an empty `win=` forever. That
    // last one is the only repeating case, so it retries at the 1 s acquire
    // cadence rather than per tick.
    private void RefreshWindowTitle()
    {
        if (process == null)
        {
            WindowTitle = "";
            titleProcessId = -1;
            titleSlug = null;
            lastTitleAttemptMs = -AcquireIntervalMs;
            return;
        }

        string slug = RomSlug;
        bool sameSource = process.Id == titleProcessId && slug == titleSlug;
        if (sameSource && WindowTitle.Length > 0) { return; }
        if (sameSource && acquireClock.ElapsedMilliseconds - lastTitleAttemptMs < AcquireIntervalMs) { return; }

        lastTitleAttemptMs = acquireClock.ElapsedMilliseconds;
        titleProcessId = process.Id;
        titleSlug = slug;
        WindowTitle = emu.WindowTitle(); // never throws; "" when unavailable
    }
}
