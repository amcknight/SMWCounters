using LiveSplit.SmwCounters.Snes;

namespace LiveSplit.SmwCounters.Counters;

internal enum LevelLeave { None, Intro, Other }

// "Back on the map after a level" edge. Death is the only thing that
// discards unbanked collects: every other way out of a level keeps them, and
// so banks them — start+select keeps the mushroom, the coins and the moons;
// a side exit, an exit-to-map screen exit or a pipe-to-map likewise. The
// exit flag cannot tell these apart (2026-09-26 session: death, start+select
// and an exit-to-map all write $0DD5 = 80; a pipe-to-map and the intro
// finish leave it at 00), and it does not need to: the dying edge already
// handled the one case that reverts, before Mario ever reaches the map.
//
// The intro is reported separately for Exits: the intro finish fires no
// exit event, yet it is progress you can never replay, so an Exit collected
// on its orb banks. Any other leave without the exit event is an abandoned
// finish, which Exits revert (re-doing it would count twice).
internal sealed class LevelLeaveDetector
{
    private const int GameModeOffset = 0x0100;
    private const int RoomOffset = 0x010B;
    private const byte OverworldMode = 0x0E;
    private const byte LevelMainMode = 0x14;
    private const byte IntroRoom = 0xC5;   // the vanilla intro level number

    private bool inLevelSince;   // level-main seen since the last map visit
    private byte leaveRoom;      // last room seen while in the level

    // A failed read forgets the level: no verdict on arrival is the safe
    // failure (collects stay pending until the next bank or death).
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

        if (mode != OverworldMode || !inLevelSince) { return LevelLeave.None; }

        inLevelSince = false;
        return leaveRoom == IntroRoom ? LevelLeave.Intro : LevelLeave.Other;
    }

    public void Clear()
    {
        inLevelSince = false;
        leaveRoom = 0;
    }
}
