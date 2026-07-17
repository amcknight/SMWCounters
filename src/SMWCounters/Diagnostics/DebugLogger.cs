using System;
using System.Collections.Generic;
using System.IO;

using LiveSplit.SmwCounters.Counters;
using LiveSplit.SmwCounters.Snes;

namespace LiveSplit.SmwCounters.Diagnostics;

// Opt-in debug instrumentation for investigating counter behavior. When enabled
// (per the "Debug log" setting), each poll appends event lines to a log file:
//
//   CTR <id> <old>-><new> | phase=.. <emu> | mode=.. inLvl=.. anim=.. fanfare=..
//       io=.. boss=.. exits=.. moon=.. coins=..
//                                             (a counter incremented; context is
//                                              the WRAM the counters key off of)
//   SPR slot<n> #<spriteNum> <old>-><new> | mode=..
//                                             (a sprite slot's $14C8 status changed)
//   SPR slot<n> id #<old>->#<new> | status=.. mode=..
//                                             (sprite-number change while status
//                                              unchanged; suppressed when status=00)
//   YOS 18AC <old>-><new>                    (Yoshi swallow timer; research candidate)
//   YOS slot<n> 160E/1594 <old>-><new>      (Yoshi tongue/mouth bytes; unverified
//                                              addresses — see v2 spec)
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

    // Yoshi insta-eat research candidates (unverified — the whole point of
    // logging them is to confirm which signal is reliable before the counter
    // uses any of them; see the v2 spec, "Yoshi insta-eat coverage").
    private const int YoshiSwallowTimer = 0x18AC;
    private const int TongueTargetBase = 0x160E; // sprite misc table, per slot
    private const int MouthFlagBase = 0x1594;    // sprite misc table, per slot
    private const byte YoshiSpriteId = 0x35;

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
    private readonly PreviousByte[] prevTongueTarget;
    private readonly PreviousByte[] prevMouthFlag;
    private readonly PreviousByte prevSwallowTimer = new();
    private readonly Dictionary<string, int> lastValue = new();
    private StreamWriter writer;

    public string LogPath => logPath;

    public DebugLogger()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SMWCounters");
        logPath = Path.Combine(dir, "counters-debug.log");

        prevStatus = new PreviousByte[SlotCount];
        for (int i = 0; i < SlotCount; i++) { prevStatus[i] = new PreviousByte(); }

        prevSpriteNum = new PreviousByte[SlotCount];
        prevTongueTarget = new PreviousByte[SlotCount];
        prevMouthFlag = new PreviousByte[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            prevSpriteNum[i] = new PreviousByte();
            prevTongueTarget[i] = new PreviousByte();
            prevMouthFlag[i] = new PreviousByte();
        }
    }

    // Log this poll's counter increments and sprite-status transitions.
    public void Poll(ISnesMemory mem, IReadOnlyList<ISmwCounter> counters,
                     Func<string, bool> isEnabled, string phase, string emuDesc)
    {
        LogCounterChanges(mem, counters, isEnabled, phase, emuDesc);
        LogSpriteTransitions(mem);
    }

    // Clear edge-detection state without closing the file (pause / detach).
    public void Idle()
    {
        lastValue.Clear();
        prevSwallowTimer.Clear();
        for (int i = 0; i < SlotCount; i++)
        {
            prevStatus[i].Clear();
            prevSpriteNum[i].Clear();
            prevTongueTarget[i].Clear();
            prevMouthFlag[i].Clear();
        }
    }

    // Clear state and release the file (logging disabled / component disposed).
    public void Close()
    {
        Idle();
        try { writer?.Dispose(); } catch { }
        writer = null;
    }

    private void LogCounterChanges(ISnesMemory mem, IReadOnlyList<ISmwCounter> counters,
                                   Func<string, bool> isEnabled, string phase, string emuDesc)
    {
        foreach (ISmwCounter c in counters)
        {
            if (!isEnabled(c.Id)) { continue; }
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
                Write($"CTR {c.Id} {prev}->{cur} | phase={phase} {emuDesc} | {ctx}");
            }
        }
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

            LogYoshiCandidates(mem, i, haveSprite ? spriteNum : (byte)0);
        }

        if (mem.ReadWramByte(YoshiSwallowTimer, out byte swallow))
        {
            if (prevSwallowTimer.HasPrevious && prevSwallowTimer.Value != swallow)
            {
                Write($"YOS 18AC {prevSwallowTimer.Value:X2}->{swallow:X2}");
            }
            prevSwallowTimer.Set(swallow);
        }
        else
        {
            prevSwallowTimer.Clear();
        }
    }

    // Research instrumentation: dump candidate Yoshi tongue/mouth bytes for
    // slots holding the Yoshi sprite, so a play session can establish which
    // signal reliably marks insta-eaten sprites (v2 spec, "Yoshi insta-eat
    // coverage"). Remove or repurpose once the signal is confirmed.
    private void LogYoshiCandidates(ISnesMemory mem, int slot, byte spriteNum)
    {
        if (spriteNum != YoshiSpriteId)
        {
            prevTongueTarget[slot].Clear();
            prevMouthFlag[slot].Clear();
            return;
        }

        if (mem.ReadWramByte(TongueTargetBase + slot, out byte tongue))
        {
            if (prevTongueTarget[slot].HasPrevious && prevTongueTarget[slot].Value != tongue)
            {
                Write($"YOS slot{slot} 160E {prevTongueTarget[slot].Value:X2}->{tongue:X2}");
            }
            prevTongueTarget[slot].Set(tongue);
        }
        else
        {
            prevTongueTarget[slot].Clear();
        }

        if (mem.ReadWramByte(MouthFlagBase + slot, out byte mouth))
        {
            if (prevMouthFlag[slot].HasPrevious && prevMouthFlag[slot].Value != mouth)
            {
                Write($"YOS slot{slot} 1594 {prevMouthFlag[slot].Value:X2}->{mouth:X2}");
            }
            prevMouthFlag[slot].Set(mouth);
        }
        else
        {
            prevMouthFlag[slot].Clear();
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
