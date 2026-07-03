using System.Drawing;

using LiveSplit.SmwCounters.Snes;
using LiveSplit.UI;

namespace LiveSplit.SmwCounters.Counters;

// Collect on the exit event (exitMode $0DD5 shift to a valid exit type); bank
// when the exit is written to the save file ($1F2E increments). After-goal
// deaths revert the unbanked collect (base die-to-discard). Banking off $1F2E
// (not counting off it) keeps the save-load 0->24 populate from phantom-counting.
internal sealed class ExitCounter : BankedCounter
{
    private const int ExitModeOffset = 0x0DD5;
    private const int ExitsCompletedOffset = 0x1F2E;

    private static readonly Bitmap icon = IconLoader.Load("LiveSplit.SmwCounters.Assets.exit.png");

    private readonly PreviousByte previousExitMode = new();
    private readonly PreviousByte previousExits = new();

    public override string Id => "exits";
    public override Image DefaultIcon => icon;
    public override string DefaultLabel => "Exits";
    protected override string SaveName => "Exits";

    protected override bool DetectCollect(ISnesMemory memory)
    {
        if (!memory.ReadWramByte(ExitModeOffset, out byte exitMode))
        {
            previousExitMode.Clear();
            return false;
        }
        bool collected = previousExitMode.HasPrevious
            && exitMode != previousExitMode.Value
            && exitMode != 0 && exitMode != 128;
        previousExitMode.Set(exitMode);
        return collected;
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
        previousExitMode.Clear();
        previousExits.Clear();
    }
}
