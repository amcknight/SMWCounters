using LiveSplit.SmwCounters.Snes;

namespace LiveSplit.SmwCounters.Counters;

// "The player is in a level" gate for the collect counters: game mode ($0100)
// is level-main (0x14). Gating on mode rather than the legacy $1935 flag keeps
// custom Yoshi Houses working. Unreadable mode = closed, like PlayGate.
internal static class LevelGate
{
    private const int GameModeOffset = 0x0100;
    private const byte LevelMainMode = 0x14;

    public static bool IsLevelMain(ISnesMemory memory)
        => memory.ReadWramByte(GameModeOffset, out byte mode) && mode == LevelMainMode;
}
