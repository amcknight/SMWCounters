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

    public SnesConnection()
    {
        Status = emu.Status(); // Detached snapshot; Diag is never null
    }

    public EmuStatus Status { get; private set; }

    public Color DotColor => StatusDot.ColorFor(
        Status.StateName, Status.IsCoolingDown,
        Status.WitnessVerdict, Status.WitnessBase, Status.WramBase);

    public bool IsAttached => ready && process != null && !process.HasExited;

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
            case "Detached":
                return "No emulator found";
            case "NoContent":
                return $"{proc} · no game detected{CooldownSuffix(s)}";
            case "Searching":
                return $"{proc} · searching for game{CooldownSuffix(s)}{ErrorSuffix(s)}";
            case "Discovering":
                return $"{proc} · discovering WRAM…";
            case "Resolved":
            case "Held":
            case "Degraded":
                return $"{proc} · WRAM @ 0x{s.WramBase:X} ({s.MethodName})";
            default:
                return $"{proc} · {s.StateName}";
        }
    }

    private static string CooldownSuffix(EmuStatus s)
        => s.IsCoolingDown ? " (retry pending)" : "";

    private static string ErrorSuffix(EmuStatus s)
        => string.IsNullOrEmpty(s.LastError) ? "" : $" — {s.LastError}";
}
