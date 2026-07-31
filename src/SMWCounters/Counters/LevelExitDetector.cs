using LiveSplit.SmwCounters.Snes;

namespace LiveSplit.SmwCounters.Counters;

// "The level just ended" edge, ported from kaizosplits' Level Exit split
// (Watchers.ToExit): $0DD5 shifting to anything that isn't 0 or 128.
//
// The saved exit count $1F2E is kept as a late backstop rather than the primary
// signal. It only moves once the overworld event runs, several seconds after the
// level is over (live log 2026-07-31: still 00 seven seconds past the goal
// collect), and on a save file that already owns the exit it never moves at all
// — which used to strand every banked counter in the gold alert until a death
// wiped it.
internal sealed class LevelExitDetector
{
    private const int ExitModeOffset = 0x0DD5;
    private const int ExitsCompletedOffset = 0x1F2E;

    // 0 is "no exit pending"; 128 is the value the level-load path parks there.
    // kaizosplits excludes both.
    private const byte NoExit = 0x00;
    private const byte ExitModeIdle = 0x80;

    private readonly PreviousByte previousExitMode = new();
    private readonly PreviousByte previousExits = new();

    public bool DetectExit(ISnesMemory memory)
    {
        bool exited = false;

        if (memory.ReadWramByte(ExitModeOffset, out byte exitMode))
        {
            if (previousExitMode.HasPrevious && exitMode != previousExitMode.Value
                && exitMode != NoExit && exitMode != ExitModeIdle)
            {
                exited = true;
            }
            previousExitMode.Set(exitMode);
        }
        else { previousExitMode.Clear(); }

        if (memory.ReadWramByte(ExitsCompletedOffset, out byte exits))
        {
            if (previousExits.HasPrevious && exits > previousExits.Value) { exited = true; }
            previousExits.Set(exits);
        }
        else { previousExits.Clear(); }

        return exited;
    }

    public void Clear()
    {
        previousExitMode.Clear();
        previousExits.Clear();
    }
}
