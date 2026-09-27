using System.Drawing;

using LiveSplit.SmwCounters.Snes;

namespace LiveSplit.SmwCounters.Counters;

// Collect on the early level-finish event (goal / orb / key / boss), matching
// kaizosplits' finish detection, so the alert spans finish -> level exit. Bank
// on the level-exit event (see LevelExitDetector), or on the intro finish,
// which fires no event but can never be replayed. Any other return to the map
// with a finish still unbanked reverts it (see LevelLeaveDetector), so re-doing
// it can't count twice. Switch palaces are intentionally excluded: they end
// without a finish event, so collecting on one would leave the alert stuck
// with no bank to clear it.
internal sealed class ExitCounter : BankedCounter
{
    private const int FanfareOffset = 0x0906;        // level-clear fanfare trigger
    private const int IoOffset = 0x1DFB;             // 3=Orb, 4=Goal, 7=Key
    private const int BossDefeatOffset = 0x13C6;     // 0 = boss not (yet) defeated

    private static readonly Bitmap icon = IconLoader.Load("LiveSplit.SmwCounters.Assets.exit.png");

    private readonly PreviousByte previousFanfare = new();
    private readonly PreviousByte previousIo = new();
    private readonly LevelExitDetector levelExit = new();
    private readonly LevelLeaveDetector leave = new();
    private bool leaveDiscarded;

    public override bool HasBankToggle => false;

    public override string Id => "exits";
    public override Image DefaultIcon => icon;
    public override string DefaultLabel => "Exits";
    protected override string SaveName => "Exits";

    protected override int DetectCollectDelta(ISnesMemory memory)
    {
        if (!memory.ReadWramByte(FanfareOffset, out byte fanfare)
            || !memory.ReadWramByte(IoOffset, out byte io)
            || !memory.ReadWramByte(BossDefeatOffset, out byte bossDefeat))
        {
            previousFanfare.Clear();
            previousIo.Clear();
            return 0;
        }

        // StepTo(fanfare, 1): 0 -> 1 this poll.
        bool fanfareStep = previousFanfare.HasPrevious
            && fanfare == 1 && previousFanfare.Value + 1 == fanfare;
        // ShiftTo(io, v): previous != v and current == v.
        bool IoTo(byte v) => previousIo.HasPrevious && previousIo.Value != v && io == v;
        bool bossUndead = bossDefeat == 0;

        bool goal = fanfareStep && bossUndead && io != 3;
        bool orb = IoTo(3) && bossUndead;
        bool key = IoTo(7);
        bool boss = fanfareStep && !bossUndead;

        previousFanfare.Set(fanfare);
        previousIo.Set(io);
        return goal || orb || key || boss ? 1 : 0;
    }

    // Bank on the exit event, or on the intro finish (which fires no event
    // yet can never be replayed). Any other return to the map with a finish
    // collected but no exit event is an abandoned finish: discard it, since
    // re-doing it would otherwise count twice.
    protected override bool DetectBank(ISnesMemory memory)
    {
        bool banked = levelExit.DetectExit(memory);
        LevelLeave verdict = leave.Detect(memory);
        leaveDiscarded = verdict == LevelLeave.Other && !banked;
        return banked || verdict == LevelLeave.Intro;
    }

    protected override bool DetectDiscard(ISnesMemory memory) => leaveDiscarded;

    protected override void ClearDetectors()
    {
        previousFanfare.Clear();
        previousIo.Clear();
        levelExit.Clear();
        leave.Clear();
        leaveDiscarded = false;
    }
}
