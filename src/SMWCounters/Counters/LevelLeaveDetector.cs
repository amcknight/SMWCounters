using LiveSplit.SmwCounters.Snes;

namespace LiveSplit.SmwCounters.Counters;

internal enum LevelLeave { None, Kept, NotKept }

// "Back on the map after a level: did the route advance?" The exit event
// already banks a beaten level, and the dying edge already reverts a death.
// This covers every other way out — start+select, side exits, exit-to-map
// screen exits, pipes that drop Mario elsewhere on the map — which the exit
// flag cannot tell apart (2026-09-26 session: death, start+select and an
// exit-to-map all write $0DD5 = 80; a pipe-to-map and the intro finish leave
// it at 00).
//
// Verdict, on the first poll back on the overworld after level-main:
//   Kept     the level was the intro (room $C5, the vanilla intro level;
//            finishing it is progress you can never replay), or Mario stands
//            somewhere else on the map than where he entered — the level
//            moved him, so the run continues from a new place.
//   NotKept  Mario is back where he started: nothing kept.
//   None     no level since the last map visit, or no entry position on
//            record (savestate into a level) and not the intro.
//
// Position is the map-relative model the banked view already uses: Exits bank
// on the exit event, not on the save prompt, so "kept" means "the run goes on
// from here", not "written to SRAM" (which WRAM cannot see anyway).
internal sealed class LevelLeaveDetector
{
    private const int GameModeOffset = 0x0100;
    private const int RoomOffset = 0x010B;
    private const int SubmapOffset = 0x1F11;
    private const int OwXOffset = 0x1F17;      // 16-bit
    private const int OwYOffset = 0x1F19;      // 16-bit
    private const byte OverworldMode = 0x0E;
    private const byte LevelMainMode = 0x14;
    private const byte IntroRoom = 0xC5;

    private bool hasEntryPos;
    private long entryPos;       // packed submap/x/y where Mario last stood on the map
    private bool inLevelSince;   // level-main seen since the last map visit
    private byte leaveRoom;      // last room seen while in the level

    // A failed read forgets the level: no verdict on arrival is the safe
    // failure (collects stay pending, as they did before this detector
    // existed). Only the bytes the current mode needs are read — mode and
    // room in a level, the map position on the map.
    public LevelLeave Detect(ISnesMemory memory)
    {
        if (!memory.ReadWramByte(GameModeOffset, out byte mode))
        {
            inLevelSince = false;
            return LevelLeave.None;
        }

        if (mode == LevelMainMode)
        {
            if (!memory.ReadWramByte(RoomOffset, out byte room))
            {
                inLevelSince = false;
                return LevelLeave.None;
            }
            inLevelSince = true;
            leaveRoom = room;
            return LevelLeave.None;
        }

        if (mode != OverworldMode) { return LevelLeave.None; }

        if (!TryReadMapPos(memory, out long pos))
        {
            inLevelSince = false;
            hasEntryPos = false;
            return LevelLeave.None;
        }

        LevelLeave verdict = LevelLeave.None;
        if (inLevelSince)
        {
            if (leaveRoom == IntroRoom) { verdict = LevelLeave.Kept; }
            else if (hasEntryPos) { verdict = pos != entryPos ? LevelLeave.Kept : LevelLeave.NotKept; }
            inLevelSince = false;
        }
        entryPos = pos;
        hasEntryPos = true;
        return verdict;
    }

    public void Clear()
    {
        hasEntryPos = false;
        entryPos = 0;
        inLevelSince = false;
        leaveRoom = 0;
    }

    // Five bytes packed into one value so "moved" is a single comparison.
    private static bool TryReadMapPos(ISnesMemory memory, out long pos)
    {
        pos = 0;
        if (!memory.ReadWramByte(SubmapOffset, out byte submap)
            || !memory.ReadWramByte(OwXOffset, out byte xl)
            || !memory.ReadWramByte(OwXOffset + 1, out byte xh)
            || !memory.ReadWramByte(OwYOffset, out byte yl)
            || !memory.ReadWramByte(OwYOffset + 1, out byte yh))
        {
            return false;
        }
        pos = ((long)submap << 32) | ((long)xh << 24) | ((long)xl << 16) | ((long)yh << 8) | yl;
        return true;
    }
}
