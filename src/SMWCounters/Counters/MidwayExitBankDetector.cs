using LiveSplit.SmwCounters.Snes;

namespace LiveSplit.SmwCounters.Counters;

// Shared "reached a checkpoint" edge detector for BankedCounters that bank on
// the midway flag ($13CE stepping to 1) or a saved exit ($1F2E incrementing).
internal sealed class MidwayExitBankDetector
{
    private const int MidwayOffset = 0x13CE;
    private const int ExitsCompletedOffset = 0x1F2E;

    private readonly PreviousByte previousMidway = new();
    private readonly PreviousByte previousExits = new();

    public bool DetectBank(ISnesMemory memory)
    {
        bool banked = false;

        if (memory.ReadWramByte(MidwayOffset, out byte midway))
        {
            if (previousMidway.HasPrevious && midway == 1 && previousMidway.Value != 1)
            {
                banked = true;
            }
            previousMidway.Set(midway);
        }
        else { previousMidway.Clear(); }

        if (memory.ReadWramByte(ExitsCompletedOffset, out byte exits))
        {
            if (previousExits.HasPrevious && exits > previousExits.Value) { banked = true; }
            previousExits.Set(exits);
        }
        else { previousExits.Clear(); }

        return banked;
    }

    public void Clear()
    {
        previousMidway.Clear();
        previousExits.Clear();
    }
}
