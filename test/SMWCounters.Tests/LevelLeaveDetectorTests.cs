using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

// "Back on the map after a level: was the route advanced?" Kept when the
// level was the intro or when Mario stands somewhere else on the map than
// where he entered (a pipe-to-map moved him); NotKept when he is back where
// he started (start+select, side exit, death). Fires once per arrival.
public class LevelLeaveDetectorTests
{
    private const int GameMode = 0x0100, Room = 0x010B, Submap = 0x1F11,
                      OwX = 0x1F17, OwXh = 0x1F18, OwY = 0x1F19, OwYh = 0x1F1A;
    private const byte Overworld = 0x0E, FadeToLevel = 0x0F, LoadLevel = 0x11, Level = 0x14,
                       FadeToMap = 0x0B, LoadMap = 0x0C;
    private const byte IntroRoom = 0xC5, SomeRoom = 0x1E;

    private static LevelLeave Poll(LevelLeaveDetector d, FakeSnesMemory m, byte mode, byte room,
                                   byte x = 10, byte y = 20, byte submap = 0, byte xh = 0, byte yh = 0)
    {
        m.SetByte(GameMode, mode); m.SetByte(Room, room); m.SetByte(Submap, submap);
        m.SetByte(OwX, x); m.SetByte(OwXh, xh); m.SetByte(OwY, y); m.SetByte(OwYh, yh);
        return d.Detect(m);
    }

    private static void EnterLevel(LevelLeaveDetector d, FakeSnesMemory m, byte room, byte x = 10, byte y = 20)
    {
        Assert.Equal(LevelLeave.None, Poll(d, m, Overworld, SomeRoom, x, y));   // standing on the map
        Assert.Equal(LevelLeave.None, Poll(d, m, FadeToLevel, SomeRoom, x, y));
        Assert.Equal(LevelLeave.None, Poll(d, m, LoadLevel, room, x, y));
        Assert.Equal(LevelLeave.None, Poll(d, m, Level, room, x, y));
    }

    [Fact]
    public void PipeMovesMario_Kept()
    {
        var d = new LevelLeaveDetector(); var m = new FakeSnesMemory();
        EnterLevel(d, m, SomeRoom);
        Assert.Equal(LevelLeave.None, Poll(d, m, LoadMap, SomeRoom, x: 40));
        Assert.Equal(LevelLeave.Kept, Poll(d, m, Overworld, SomeRoom, x: 40));
    }

    [Fact]
    public void BackWhereHeStarted_NotKept()
    {
        var d = new LevelLeaveDetector(); var m = new FakeSnesMemory();
        EnterLevel(d, m, SomeRoom);
        Assert.Equal(LevelLeave.None, Poll(d, m, FadeToMap, SomeRoom));
        Assert.Equal(LevelLeave.NotKept, Poll(d, m, Overworld, SomeRoom));
    }

    [Fact]
    public void SubmapChange_CountsAsMoved()
    {
        var d = new LevelLeaveDetector(); var m = new FakeSnesMemory();
        EnterLevel(d, m, SomeRoom);
        Assert.Equal(LevelLeave.Kept, Poll(d, m, Overworld, SomeRoom, submap: 1));
    }

    [Fact]
    public void HighByteChange_CountsAsMoved()
    {
        var d = new LevelLeaveDetector(); var m = new FakeSnesMemory();
        EnterLevel(d, m, SomeRoom);
        Assert.Equal(LevelLeave.Kept, Poll(d, m, Overworld, SomeRoom, yh: 1));
    }

    [Fact]
    public void IntroFinish_Kept_EvenWithNoMapPositionOnRecord()
    {
        var d = new LevelLeaveDetector(); var m = new FakeSnesMemory();
        // Fresh file: title -> intro without ever standing on the map.
        Assert.Equal(LevelLeave.None, Poll(d, m, LoadLevel, IntroRoom));
        Assert.Equal(LevelLeave.None, Poll(d, m, Level, IntroRoom));
        Assert.Equal(LevelLeave.None, Poll(d, m, FadeToMap, IntroRoom));
        Assert.Equal(LevelLeave.Kept, Poll(d, m, Overworld, IntroRoom));
    }

    [Fact]
    public void NoMapPositionOnRecord_NotIntro_None()
    {
        var d = new LevelLeaveDetector(); var m = new FakeSnesMemory();
        Assert.Equal(LevelLeave.None, Poll(d, m, Level, SomeRoom));   // e.g. savestate into a level
        Assert.Equal(LevelLeave.None, Poll(d, m, Overworld, SomeRoom));
    }

    [Fact]
    public void FiresOncePerArrival_ThenWalkingOnTheMapIsSilent()
    {
        var d = new LevelLeaveDetector(); var m = new FakeSnesMemory();
        EnterLevel(d, m, SomeRoom);
        Assert.Equal(LevelLeave.NotKept, Poll(d, m, Overworld, SomeRoom));
        Assert.Equal(LevelLeave.None, Poll(d, m, Overworld, SomeRoom));
        Assert.Equal(LevelLeave.None, Poll(d, m, Overworld, SomeRoom, x: 50));   // walked: no level since
        Assert.Equal(LevelLeave.None, Poll(d, m, Overworld, SomeRoom, x: 60));
    }

    [Fact]
    public void EntryPositionIsWhereHeStoodLast_NotWhereHeStoodFirst()
    {
        var d = new LevelLeaveDetector(); var m = new FakeSnesMemory();
        Assert.Equal(LevelLeave.None, Poll(d, m, Overworld, SomeRoom, x: 10));
        Assert.Equal(LevelLeave.None, Poll(d, m, Overworld, SomeRoom, x: 30));   // walked to the level
        Assert.Equal(LevelLeave.None, Poll(d, m, Level, SomeRoom, x: 30));
        Assert.Equal(LevelLeave.NotKept, Poll(d, m, Overworld, SomeRoom, x: 30));
    }

    [Fact]
    public void LeaveRoomIsTheLastRoomInTheLevel()
    {
        var d = new LevelLeaveDetector(); var m = new FakeSnesMemory();
        EnterLevel(d, m, SomeRoom);
        Assert.Equal(LevelLeave.None, Poll(d, m, Level, 0x50));      // sublevel
        Assert.Equal(LevelLeave.None, Poll(d, m, Level, IntroRoom)); // a hack reusing C5 as a sublevel...
        Assert.Equal(LevelLeave.None, Poll(d, m, Level, 0x20));      // ...then leaving from 20
        Assert.Equal(LevelLeave.NotKept, Poll(d, m, Overworld, 0x20));
    }

    [Fact]
    public void ReadFailure_ForgetsTheLevel_NoVerdictOnArrival()
    {
        var d = new LevelLeaveDetector(); var m = new FakeSnesMemory();
        EnterLevel(d, m, SomeRoom);
        Assert.Equal(LevelLeave.None, d.Detect(new FakeSnesMemory()));   // detach-like blip
        Assert.Equal(LevelLeave.None, Poll(d, m, Overworld, SomeRoom));
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        var d = new LevelLeaveDetector(); var m = new FakeSnesMemory();
        EnterLevel(d, m, SomeRoom);
        d.Clear();
        Assert.Equal(LevelLeave.None, Poll(d, m, Overworld, SomeRoom, x: 40));
    }
}
