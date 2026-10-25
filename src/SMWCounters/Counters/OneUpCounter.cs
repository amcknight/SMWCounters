using System;
using System.Drawing;

using LiveSplit.SmwCounters.Snes;

namespace LiveSplit.SmwCounters.Counters;

// Counts collected 1-Ups. A candidate is a 1-Up mushroom (0x78) or flying
// golden mushroom (0x7F) sprite that vanishes from an alive-ish slot (status
// >= 0x07), or Fishin' Lakitu's dangled 1-Up: Lakitu's $151C (X-position low
// byte & $10, set at init) goes nonzero -> zero when Mario takes the bait. A
// candidate only counts if the game's own "1UP" score sprite (sprite 0x0D..0x10
// in $16E1..$16E6) appears within ConfirmWindowPolls polls: the sprite also
// vanishes by scrolling off or dying, which writes no score sprite. The score
// sprite is used rather than the lives byte because hacks pin lives at 99 and
// 100 coins writes no score sprite; the game writes it in the collect frame
// (spec §3).
internal sealed class OneUpCounter : BankedCounter, ITraceSource
{
    public const int ConfirmWindowPolls = 3;

    private const int ScoreSpriteBase    = 0x16E1;
    private const int ScoreSpriteSlots   = 6;
    private const int LakituFishingBase  = 0x151C;
    private const int LoadIndexBase      = 0x161A;
    private const int RiseTimerBase      = 0x1540;
    private const int ReserveDropBase    = 0x1534;

    private const byte OneUpMushroom       = 0x78;
    private const byte FlyingGoldenMushroom = 0x7F;
    private const byte Lakitu              = 0x1E;
    private const byte FirstLifeText       = 0x0D;
    private const byte LastLifeText        = 0x10;
    private const byte MinCandidateStatus  = 0x07;
    private const byte AliveStatus         = 0x08;

    private static readonly Bitmap icon = IconLoader.Load("LiveSplit.SmwCounters.Assets.oneup.png");

    private readonly SpriteSlotTracker slots = new();
    private readonly ConfirmationWindow window =
        new(SpriteSlotTracker.SlotCount, ConfirmWindowPolls, "1UP");
    private readonly PreviousByte[] scorePrevious = NewPrevious(ScoreSpriteSlots);
    private readonly PreviousByte[] lakituPrevious = NewPrevious(SpriteSlotTracker.SlotCount);
    private readonly MidwayExitBankDetector bank = new();

    private Action<string> trace;

    public override string Id => "oneups";
    public override Image DefaultIcon => icon;
    public override string DefaultLabel => "1-Ups";
    protected override string SaveName => "OneUps";

    public Action<string> Trace
    {
        get => trace;
        set
        {
            trace = value;
            window.Trace = value;
        }
    }

    protected override int DetectCollectDelta(ISnesMemory memory)
    {
        // Collect state only: the bank detector must keep its in-level memory
        // across this gate, since Poll asks it for the exit / map-arrival
        // verdict right after this returns.
        if (!LevelGate.IsLevelMain(memory))
        {
            ClearCollectState();
            return 0;
        }

        // Score table first, slots second: reads aren't frame-atomic, so a
        // frame boundary between them can only delay a confirmation by a poll,
        // never put it ahead of its candidate.
        int scoreAppearances = 0;
        for (int i = 0; i < ScoreSpriteSlots; i++)
        {
            if (!memory.ReadWramByte(ScoreSpriteBase + i, out byte current))
            {
                scorePrevious[i].Clear();
                continue;
            }
            if (scorePrevious[i].HasPrevious && current >= FirstLifeText && current <= LastLifeText
                && current != scorePrevious[i].Value)
            {
                scoreAppearances++;
            }
            scorePrevious[i].Set(current);
        }

        slots.Read(memory);
        for (int i = 0; i < SpriteSlotTracker.SlotCount; i++)
        {
            if (!slots.Ok(i))
            {
                lakituPrevious[i].Clear();
                window.Disarm(i);
                continue;
            }

            byte s = slots.Sprite(i);
            byte t = slots.Status(i);
            // $9E keeps a stale number on an empty slot, so a fresh spawn into
            // a slot whose stale number already matches is still a sighting.
            if (Trace != null && (s == OneUpMushroom || s == FlyingGoldenMushroom) && t != 0x00
                && (!slots.HasPrevious(i) || slots.PreviousSprite(i) != s || slots.PreviousStatus(i) == 0x00))
            {
                Trace($"1UP seen slot{i} #{s:X2} idx={Hex(memory, LoadIndexBase + i)} rise={Hex(memory, RiseTimerBase + i)} drop={Hex(memory, ReserveDropBase + i)}");
            }

            bool lakituFlagRead = false;
            byte lakituFlag = 0;
            if (s == Lakitu)
            {
                lakituFlagRead = memory.ReadWramByte(LakituFishingBase + i, out lakituFlag);
            }

            if (slots.HasPrevious(i))
            {
                byte ps = slots.PreviousSprite(i);
                byte pt = slots.PreviousStatus(i);
                if ((ps == OneUpMushroom || ps == FlyingGoldenMushroom)
                    && pt >= MinCandidateStatus && (t == 0x00 || s != ps))
                {
                    Trace?.Invoke($"1UP candidate slot{i} #{ps:X2} from={pt:X2} idx={Hex(memory, LoadIndexBase + i)} rise={Hex(memory, RiseTimerBase + i)} drop={Hex(memory, ReserveDropBase + i)}");
                    window.Arm(i);
                }
                else if (ps == Lakitu && pt == AliveStatus && s == Lakitu
                    && lakituPrevious[i].HasPrevious && lakituPrevious[i].Value != 0
                    && lakituFlagRead && lakituFlag == 0)
                {
                    Trace?.Invoke($"1UP candidate slot{i} lakitu idx={Hex(memory, LoadIndexBase + i)} rise={Hex(memory, RiseTimerBase + i)} drop={Hex(memory, ReserveDropBase + i)}");
                    window.Arm(i);
                }
            }

            if (lakituFlagRead)
            {
                lakituPrevious[i].Set(lakituFlag);
            }
            else
            {
                lakituPrevious[i].Clear();
            }
        }
        slots.Commit();

        int confirmed = window.Confirm(scoreAppearances);
        window.Tick();
        return confirmed;
    }

    protected override bool DetectBank(ISnesMemory memory) => bank.DetectBank(memory);

    protected override void ClearDetectors()
    {
        ClearCollectState();
        bank.Clear();
    }

    private void ClearCollectState()
    {
        slots.Clear();
        window.Clear();
        foreach (PreviousByte p in scorePrevious) p.Clear();
        foreach (PreviousByte p in lakituPrevious) p.Clear();
    }

    private static string Hex(ISnesMemory memory, int offset)
        => memory.ReadWramByte(offset, out byte value) ? value.ToString("X2") : "??";

    private static PreviousByte[] NewPrevious(int count)
    {
        var array = new PreviousByte[count];
        for (int i = 0; i < count; i++) array[i] = new PreviousByte();
        return array;
    }
}
