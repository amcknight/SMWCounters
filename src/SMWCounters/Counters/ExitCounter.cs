using System.Drawing;

using LiveSplit.SmwCounters.Snes;
using LiveSplit.UI;

namespace LiveSplit.SmwCounters.Counters;

// Collect on the early level-finish event (goal / orb / key / boss), matching
// kaizosplits' finish detection, so the alert spans finish -> exit-write. Bank
// when the exit is written to the save file ($1F2E increments). Switch palaces
// are intentionally excluded: they don't increment $1F2E, so collecting on one
// would leave the alert stuck with no bank to clear it.
internal sealed class ExitCounter : BankedCounter
{
    private const int FanfareOffset = 0x0906;        // level-clear fanfare trigger
    private const int IoOffset = 0x1DFB;             // 3=Orb, 4=Goal, 7=Key
    private const int BossDefeatOffset = 0x13C6;     // 0 = boss not (yet) defeated
    private const int ExitsCompletedOffset = 0x1F2E; // saved exit count

    private static readonly Bitmap icon = IconLoader.Load("LiveSplit.SmwCounters.Assets.exit.png");

    private readonly PreviousByte previousFanfare = new();
    private readonly PreviousByte previousIo = new();
    private readonly PreviousByte previousExits = new();

    public override string Id => "exits";
    public override Image DefaultIcon => icon;
    public override string DefaultLabel => "Exits";
    protected override string SaveName => "Exits";

    protected override bool DetectCollect(ISnesMemory memory)
    {
        if (!memory.ReadWramByte(FanfareOffset, out byte fanfare)
            || !memory.ReadWramByte(IoOffset, out byte io)
            || !memory.ReadWramByte(BossDefeatOffset, out byte bossDefeat))
        {
            previousFanfare.Clear();
            previousIo.Clear();
            return false;
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
        return goal || orb || key || boss;
    }

    protected override bool DetectBank(ISnesMemory memory)
    {
        if (!memory.ReadWramByte(ExitsCompletedOffset, out byte exits))
        {
            previousExits.Clear();
            return false;
        }
        bool banked = previousExits.HasPrevious && exits > previousExits.Value;
        previousExits.Set(exits);
        return banked;
    }

    protected override void ClearDetectors()
    {
        previousFanfare.Clear();
        previousIo.Clear();
        previousExits.Clear();
    }
}
