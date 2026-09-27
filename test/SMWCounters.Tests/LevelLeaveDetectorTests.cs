using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

// "Back on the map after a level" edge, once per arrival, with the intro
// (room $C5) reported separately. Death is the only discard, so every leave
// is a bank for the collect counters; Exits alone care which kind.
public class LevelLeaveDetectorTests
{
    private const int GameMode = 0x0100, Room = 0x010B;
    private const byte Overworld = 0x0E, FadeToLevel = 0x0F, LoadLevel = 0x11, Level = 0x14,
                       FadeToMap = 0x0B, LoadMap = 0x0C;
    private const byte IntroRoom = 0xC5, SomeRoom = 0x1E;

    private static LevelLeave Poll(LevelLeaveDetector d, FakeSnesMemory m, byte mode, byte room)
    {
        m.SetByte(GameMode, mode); m.SetByte(Room, room);
        return d.Detect(m);
    }

    private static void EnterLevel(LevelLeaveDetector d, FakeSnesMemory m, byte room)
    {
        Assert.Equal(LevelLeave.None, Poll(d, m, Overworld, SomeRoom));
        Assert.Equal(LevelLeave.None, Poll(d, m, FadeToLevel, SomeRoom));
        Assert.Equal(LevelLeave.None, Poll(d, m, LoadLevel, room));
        Assert.Equal(LevelLeave.None, Poll(d, m, Level, room));
    }

    [Fact]
    public void LeaveViaFadeToMap_Other()
    {
        var d = new LevelLeaveDetector(); var m = new FakeSnesMemory();
        EnterLevel(d, m, SomeRoom);
        Assert.Equal(LevelLeave.None, Poll(d, m, FadeToMap, SomeRoom));   // death / start+select path
        Assert.Equal(LevelLeave.None, Poll(d, m, LoadMap, SomeRoom));
        Assert.Equal(LevelLeave.Other, Poll(d, m, Overworld, SomeRoom));
    }

    [Fact]
    public void LeaveViaLevelLoadModes_Other()
    {
        var d = new LevelLeaveDetector(); var m = new FakeSnesMemory();
        EnterLevel(d, m, SomeRoom);
        Assert.Equal(LevelLeave.None, Poll(d, m, FadeToLevel, SomeRoom));  // pipe-to-map path: 0F 10 11 0C
        Assert.Equal(LevelLeave.None, Poll(d, m, LoadLevel, SomeRoom));
        Assert.Equal(LevelLeave.None, Poll(d, m, LoadMap, SomeRoom));
        Assert.Equal(LevelLeave.Other, Poll(d, m, Overworld, SomeRoom));
    }

    [Fact]
    public void IntroFinish_Intro_EvenWithNoMapVisitBefore()
    {
        var d = new LevelLeaveDetector(); var m = new FakeSnesMemory();
        Assert.Equal(LevelLeave.None, Poll(d, m, LoadLevel, IntroRoom));   // fresh file: title -> intro
        Assert.Equal(LevelLeave.None, Poll(d, m, Level, IntroRoom));
        Assert.Equal(LevelLeave.None, Poll(d, m, FadeToMap, IntroRoom));
        Assert.Equal(LevelLeave.Intro, Poll(d, m, Overworld, IntroRoom));
    }

    [Fact]
    public void FiresOncePerArrival_MapIsSilentUntilTheNextLevel()
    {
        var d = new LevelLeaveDetector(); var m = new FakeSnesMemory();
        EnterLevel(d, m, SomeRoom);
        Assert.Equal(LevelLeave.Other, Poll(d, m, Overworld, SomeRoom));
        Assert.Equal(LevelLeave.None, Poll(d, m, Overworld, SomeRoom));
        Assert.Equal(LevelLeave.None, Poll(d, m, Overworld, SomeRoom));
    }

    [Fact]
    public void LeaveRoomIsTheLastRoomInTheLevel()
    {
        var d = new LevelLeaveDetector(); var m = new FakeSnesMemory();
        EnterLevel(d, m, SomeRoom);
        Assert.Equal(LevelLeave.None, Poll(d, m, Level, IntroRoom));   // a hack reusing C5 as a sublevel...
        Assert.Equal(LevelLeave.None, Poll(d, m, Level, 0x20));        // ...then leaving from 20
        Assert.Equal(LevelLeave.Other, Poll(d, m, Overworld, 0x20));
    }

    [Fact]
    public void ReadFailure_ForgetsTheLevel_NoVerdictOnArrival()
    {
        var d = new LevelLeaveDetector(); var m = new FakeSnesMemory();
        EnterLevel(d, m, SomeRoom);
        Assert.Equal(LevelLeave.None, d.Detect(new FakeSnesMemory()));
        Assert.Equal(LevelLeave.None, Poll(d, m, Overworld, SomeRoom));
    }

    [Fact]
    public void Clear_ForgetsTheLevel()
    {
        var d = new LevelLeaveDetector(); var m = new FakeSnesMemory();
        EnterLevel(d, m, SomeRoom);
        d.Clear();
        Assert.Equal(LevelLeave.None, Poll(d, m, Overworld, SomeRoom));
    }
}
