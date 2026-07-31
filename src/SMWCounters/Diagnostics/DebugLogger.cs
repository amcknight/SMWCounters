using System;
using System.Collections.Generic;
using System.IO;

using LiveSplit.SmwCounters.Counters;
using LiveSplit.SmwCounters.Snes;

namespace LiveSplit.SmwCounters.Diagnostics;

// Opt-in debug instrumentation for investigating counter behavior. When enabled
// (per the "Debug log" setting), each poll appends event lines to a log file:
//
//   CTR <id> <old>-><new> [unbanked,hidden] | phase=.. <emu> | mode=.. inLvl=..
//       anim=.. fanfare=.. io=.. boss=.. exits=.. moon=.. coins=..
//                                             (a counter incremented; context is
//                                              the WRAM the counters key off of.
//                                              `unbanked` = the value is showing
//                                              gold; `hidden` = the counter is
//                                              counting but not on the overlay)
//   BNK <signal> <old>-><new> | mode=.. inLvl=.. lvl=.. room=.. cp=.. exitMode=..
//       exits=.. midway=..
//                                             (a byte the banking logic keys off
//                                              of changed — answers "did the
//                                              checkpoint actually fire, and
//                                              which signal saw it?")
//   SPR slot<n> #<spriteNum> <old>-><new> | mode=..
//                                             (a sprite slot's $14C8 status changed)
//   SPR slot<n> id #<old>->#<new> | status=.. mode=..
//                                             (sprite-number change while status
//                                              unchanged; suppressed when status=00)
//   PRP slot<n> #<spriteNum> ->ss 1656=.. 1662=.. 166E=.. 167A=.. 1686=.. 190F=..
//                                             (the slot's six tweaker property bytes,
//                                              dumped when a sprite enters a dead/mouth
//                                              status 02-07 — builds the evidence table
//                                              for a property-based creature filter that
//                                              could replace the NotAlive ID blacklist)
//
// The SPR trace answers questions like "what status does a fireballed enemy pass
// through?" and "does the coin reuse the enemy's slot?"; the CTR line answers
// "what game mode was active when the Exit counter incremented?".
//
// All file I/O is best-effort and swallows exceptions so logging can never
// disrupt polling. Edge state clears on Idle()/Close() so a pause or detach
// doesn't bridge a stale sample to a fresh one and fabricate a transition.
internal sealed class DebugLogger
{
    // Context addresses the built-in counters key off of.
    private const int GameMode = 0x0100;   // $14 = level main
    private const int InLevel = 0x1935;    // 1 = in a level
    private const int PlayerAnim = 0x0071; // $09 = dying (deaths)
    private const int Fanfare = 0x0906;    // exit: level-clear fanfare
    private const int Io = 0x1DFB;         // exit: 3=orb 4=goal 7=key
    private const int BossDefeat = 0x13C6; // exit: boss defeated
    private const int ExitsSaved = 0x1F2E; // exit: saved exit count
    private const int MoonByte = 0x13C5;   // moons collected this scene
    private const int CoinCount = 0x0DBF;        // fireball-coin collection correlation

    // Everything the bank ("progress is safe now") edge keys off of, traced
    // byte-by-byte so a session can show which signal fired at a checkpoint.
    private static readonly (string Name, int Offset)[] BankSignals =
    {
        ("midway",   0x13CE),   // vanilla midway flag
        ("cp",       0x1B403),  // level entrance — custom kaizo checkpoints
        ("exitMode", 0x0DD5),   // kaizosplits' Level Exit event
        ("exits",    0x1F2E),   // saved exit count (late backstop)
        ("lvl",      0x13BF),   // level number
        ("room",     0x010B),   // room number
    };

    // Sprite tables.
    private const int SpriteStatusBase = 0x14C8; // per-slot status ($14C8..$14D3)
    private const int SpriteNumberBase = 0x009E; // per-slot sprite id ($9E..$A9)
    private const int SlotCount = 12;

    // Per-slot "tweaker" property tables (copied from ROM at spawn). Dumped on
    // death/mouth entries to research whether some bit combination separates
    // creatures from objects better than the ID blacklist does.
    private static readonly int[] TweakerBases = { 0x1656, 0x1662, 0x166E, 0x167A, 0x1686, 0x190F };

    private readonly string logPath;
    private readonly PreviousByte[] prevStatus;
    private readonly PreviousByte[] prevSpriteNum;
    private readonly PreviousByte[] prevBankSignal;
    private readonly Dictionary<string, int> lastValue = new();
    private readonly StatusChangeFilter statusFilter = new();
    private StreamWriter writer;

    public string LogPath => logPath;

    public DebugLogger()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SMWCounters");
        logPath = Path.Combine(dir, "counters-debug.log");

        prevStatus = new PreviousByte[SlotCount];
        prevSpriteNum = new PreviousByte[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            prevStatus[i] = new PreviousByte();
            prevSpriteNum[i] = new PreviousByte();
        }

        prevBankSignal = new PreviousByte[BankSignals.Length];
        for (int i = 0; i < BankSignals.Length; i++) { prevBankSignal[i] = new PreviousByte(); }
    }

    // Log this poll's counter increments and sprite-status transitions.
    public void Poll(ISnesMemory mem, IReadOnlyList<ISmwCounter> counters,
                     Func<string, bool> isEnabled, string phase, string emuDesc)
    {
        LogCounterChanges(mem, counters, isEnabled, phase, emuDesc);
        LogBankSignals(mem);
        LogSpriteTransitions(mem);
    }

    // Log SNES.dll status transitions ("SNS ..." lines): one line per change
    // of (StateName, Generation, WramBase, IsCoolingDown, LastError), plus
    // method/rebind provenance and scan latency on resolves. Called every
    // tick while debug logging is enabled — including when the timer is not
    // running, since always-on discovery transitions happen pre-run too.
    public void LogStatus(SNES.EmuStatus status)
    {
        string line = statusFilter.OnStatus(
            status.StateName, status.Generation, status.WramBase,
            status.IsCoolingDown, status.LastError,
            status.MethodName, status.RebindReasonName,
            status.Diag != null ? status.Diag.ScanTotalMs : 0,
            status.RivalCount, status.IsContested, status.RegressionCount);
        if (line != null) { Write(line); }
    }

    // Clear edge-detection state without closing the file (pause / detach).
    public void Idle()
    {
        lastValue.Clear();
        for (int i = 0; i < SlotCount; i++)
        {
            prevStatus[i].Clear();
            prevSpriteNum[i].Clear();
        }
        foreach (PreviousByte p in prevBankSignal) { p.Clear(); }
    }

    // Clear state and release the file (logging disabled / component disposed).
    public void Close()
    {
        Idle();
        statusFilter.Reset();
        try { writer?.Dispose(); } catch { }
        writer = null;
    }

    private void LogCounterChanges(ISnesMemory mem, IReadOnlyList<ISmwCounter> counters,
                                   Func<string, bool> isEnabled, string phase, string emuDesc)
    {
        foreach (ISmwCounter c in counters)
        {
            int cur = c.Value;
            bool had = lastValue.TryGetValue(c.Id, out int prev);
            lastValue[c.Id] = cur;
            if (had && cur > prev)
            {
                string ctx = $"mode={Hex(mem, GameMode)} inLvl={Hex(mem, InLevel)} "
                    + $"anim={Hex(mem, PlayerAnim)} fanfare={Hex(mem, Fanfare)} "
                    + $"io={Hex(mem, Io)} boss={Hex(mem, BossDefeat)} "
                    + $"exits={Hex(mem, ExitsSaved)} moon={Hex(mem, MoonByte)} "
                    + $"coins={Hex(mem, CoinCount)}";
                Write($"CTR {c.Id} {prev}->{cur}{Tags(c, isEnabled)} | phase={phase} {emuDesc} | {ctx}");
            }
        }
    }

    // "[unbanked]" / "[hidden]" / "[unbanked,hidden]", or "" when neither
    // applies. Both are invisible in the raw value but change how a session
    // reads: a counter that never loses `unbanked` never banked.
    private static string Tags(ISmwCounter counter, Func<string, bool> isEnabled)
    {
        var tags = new List<string>();
        if (counter.ValueIsAlert) { tags.Add("unbanked"); }
        if (!isEnabled(counter.Id)) { tags.Add("hidden"); }
        return tags.Count == 0 ? "" : " [" + string.Join(",", tags) + "]";
    }

    private void LogBankSignals(ISnesMemory mem)
    {
        for (int i = 0; i < BankSignals.Length; i++)
        {
            (string name, int offset) = BankSignals[i];
            if (!mem.ReadWramByte(offset, out byte value))
            {
                prevBankSignal[i].Clear();
                continue;
            }
            if (prevBankSignal[i].HasPrevious && prevBankSignal[i].Value != value)
            {
                Write($"BNK {name} {prevBankSignal[i].Value:X2}->{value:X2} | "
                    + $"mode={Hex(mem, GameMode)} inLvl={Hex(mem, InLevel)} " + BankContext(mem));
            }
            prevBankSignal[i].Set(value);
        }
    }

    private static string BankContext(ISnesMemory mem)
    {
        string ctx = "";
        foreach ((string name, int offset) in BankSignals)
        {
            ctx += $"{name}={Hex(mem, offset)} ";
        }
        return ctx.TrimEnd();
    }

    private void LogSpriteTransitions(ISnesMemory mem)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            bool haveStatus = mem.ReadWramByte(SpriteStatusBase + i, out byte status);
            bool haveSprite = mem.ReadWramByte(SpriteNumberBase + i, out byte spriteNum);
            if (!haveStatus)
            {
                prevStatus[i].Clear();
                prevSpriteNum[i].Clear();
                continue;
            }

            if (prevStatus[i].HasPrevious && prevStatus[i].Value != status)
            {
                Write($"SPR slot{i} #{Hex(mem, SpriteNumberBase + i)} "
                    + $"{prevStatus[i].Value:X2}->{status:X2} | mode={Hex(mem, GameMode)}");

                // Death or mouth entry: dump the slot's property bytes so the
                // candidate table grows with every observed casualty.
                if (status >= 0x02 && status <= 0x07)
                {
                    string props = "";
                    foreach (int b in TweakerBases)
                    {
                        props += $" {b:X4}={Hex(mem, b + i)}";
                    }
                    Write($"PRP slot{i} #{Hex(mem, SpriteNumberBase + i)} ->{status:X2}{props}");
                }
            }
            prevStatus[i].Set(status);

            // Sprite-number changes without a status change (fireball -> coin
            // conversions) were previously invisible; log them explicitly.
            if (haveSprite)
            {
                if (prevSpriteNum[i].HasPrevious && prevSpriteNum[i].Value != spriteNum
                    && status != 0x00)
                {
                    Write($"SPR slot{i} id #{prevSpriteNum[i].Value:X2}->#{spriteNum:X2} "
                        + $"| status={status:X2} mode={Hex(mem, GameMode)}");
                }
                prevSpriteNum[i].Set(spriteNum);
            }
            else
            {
                prevSpriteNum[i].Clear();
            }
        }
    }

    private static string Hex(ISnesMemory mem, int offset)
        => mem.ReadWramByte(offset, out byte b) ? b.ToString("X2") : "??";

    private void Write(string line)
    {
        try
        {
            if (writer == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                writer = new StreamWriter(logPath, append: true) { AutoFlush = true };
            }
            writer.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + line);
        }
        catch { /* logging is best-effort; never disrupt polling */ }
    }
}
