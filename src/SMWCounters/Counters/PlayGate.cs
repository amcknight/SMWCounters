using LiveSplit.SmwCounters.Snes;

namespace LiveSplit.SmwCounters.Counters;

// "The player is actually playing" gate for the two edges that have no
// in-level requirement of their own (deaths, level exits). Game mode ($0100)
// families: 00-07 Nintendo Presents and the title screen — whose attract demo
// runs real level code, so $0071 can hit 9 there — 08-0B file/player select,
// 0C onward overworld and levels. The exit-flag edge lands at 0C (fade to
// overworld after a level; log 2026-08-04) and the saved-exits backstop moves
// on the overworld (0E), so the gate opens at 0C rather than at level-main.
// Unreadable mode = closed: consistent with the collect gates, which refuse
// to count on a failed read.
internal static class PlayGate
{
    private const int GameModeOffset = 0x0100;
    private const byte FirstPlayMode = 0x0C;

    public static bool IsInPlay(ISnesMemory memory)
        => memory.ReadWramByte(GameModeOffset, out byte mode) && mode >= FirstPlayMode;
}
