using LiveSplit.SmwCounters.Snes;

namespace LiveSplit.SmwCounters.Counters;

// Shared "progress is safe now" edge for BankedCounters: the player reached a
// checkpoint or finished the level, so unbanked collects stop being at risk.
//
// Checkpoint mirrors kaizosplits' Watchers.CP = Midway || CPEntrance. The
// vanilla midway flag ($13CE) alone is not enough: kaizo hacks routinely ship
// custom checkpoints that only repoint the level's entrance ($1B403), which is
// why kaizosplits carries both detectors.
internal sealed class MidwayExitBankDetector
{
    private const int MidwayOffset = 0x13CE;
    private const int InLevelOffset = 0x1935;
    private const int LevelNumOffset = 0x13BF;
    private const int RoomNumOffset = 0x010B;
    private const int CpEntranceOffset = 0x1B403;

    private readonly PreviousByte previousMidway = new();
    private readonly PreviousByte previousLevelNum = new();
    private readonly PreviousByte previousCpEntrance = new();
    private readonly LevelExitDetector levelExit = new();
    private readonly LevelLeaveDetector leave = new();

    // The room the current level started in. Entering a level repoints the
    // entrance byte at its own first room, which is setup rather than a
    // checkpoint; kaizosplits suppresses that with the same bookkeeping.
    private byte firstRoom;

    // Banks on a checkpoint, the exit event, or any return to the map: death
    // is the only discard, and the dying edge handles it before Mario gets
    // back to the map (see LevelLeaveDetector).
    public bool DetectBank(ISnesMemory memory)
    {
        bool banked = DetectMidway(memory);
        if (DetectCheckpointEntrance(memory)) { banked = true; }
        if (levelExit.DetectExit(memory)) { banked = true; }
        if (leave.Detect(memory) != LevelLeave.None) { banked = true; }
        return banked;
    }

    public void Clear()
    {
        previousMidway.Clear();
        previousLevelNum.Clear();
        previousCpEntrance.Clear();
        levelExit.Clear();
        leave.Clear();
        firstRoom = 0;
    }

    private bool DetectMidway(ISnesMemory memory)
    {
        if (!memory.ReadWramByte(MidwayOffset, out byte midway))
        {
            previousMidway.Clear();
            return false;
        }
        bool touched = previousMidway.HasPrevious && midway == 1 && previousMidway.Value != 1;
        previousMidway.Set(midway);
        if (touched) { firstRoom = 0; }
        return touched;
    }

    private bool DetectCheckpointEntrance(ISnesMemory memory)
    {
        if (!memory.ReadWramByte(CpEntranceOffset, out byte entrance)
            || !memory.ReadWramByte(LevelNumOffset, out byte levelNum)
            || !memory.ReadWramByte(RoomNumOffset, out byte roomNum)
            || !memory.ReadWramByte(InLevelOffset, out byte inLevel))
        {
            previousLevelNum.Clear();
            previousCpEntrance.Clear();
            return false;
        }

        if (previousLevelNum.HasPrevious && levelNum != previousLevelNum.Value)
        {
            firstRoom = roomNum;
        }
        previousLevelNum.Set(levelNum);

        bool reached = inLevel == 1
            && previousCpEntrance.HasPrevious
            && entrance != previousCpEntrance.Value
            && entrance != firstRoom;
        previousCpEntrance.Set(entrance);
        if (reached) { firstRoom = 0; }
        return reached;
    }
}
