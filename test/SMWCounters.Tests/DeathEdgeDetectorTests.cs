using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

// The shared "$0071 rising edge to 9" death rule, extracted from
// BankedCounter so KillCounter (dual tally, not a BankedCounter subclass)
// can compose the identical discard edge. Gated on the game mode being past
// the title/file-select screens, so the attract demo's level code can't fire it.
public class DeathEdgeDetectorTests
{
    private const int Anim = 0x0071, GameMode = 0x0100;
    private const byte Dying = 0x09;
    private const byte LevelMode = 0x14, TitleMode = 0x07, FileSelectMode = 0x09, FadeToOverworld = 0x0C;

    private static bool Poll(DeathEdgeDetector d, FakeSnesMemory m, byte anim, byte mode = LevelMode)
    {
        m.SetByte(Anim, anim);
        m.SetByte(GameMode, mode);
        return d.Detect(m);
    }

    [Fact]
    public void RisingEdgeToDying_FiresOnce_NotWhileHeld()
    {
        var d = new DeathEdgeDetector();
        var m = new FakeSnesMemory();
        Assert.False(Poll(d, m, 0));      // baseline
        Assert.True(Poll(d, m, Dying));   // 0 -> 9: the edge
        Assert.False(Poll(d, m, Dying));  // held at 9: no re-fire
    }

    [Fact]
    public void FirstSampleAlreadyDying_NoEdge()
    {
        var d = new DeathEdgeDetector();
        var m = new FakeSnesMemory();
        Assert.False(Poll(d, m, Dying));  // no previous sample: not an edge
    }

    [Fact]
    public void ReadFailure_ClearsBaseline_NoBridgedEdge()
    {
        var d = new DeathEdgeDetector();
        var m = new FakeSnesMemory();
        Assert.False(Poll(d, m, 0));
        Assert.False(d.Detect(new FakeSnesMemory()));  // $0071 unreadable: clears
        Assert.False(Poll(d, m, Dying));  // re-baseline only, no 0->9 bridge
    }

    [Fact]
    public void Clear_ResetsBaseline()
    {
        var d = new DeathEdgeDetector();
        var m = new FakeSnesMemory();
        Assert.False(Poll(d, m, 0));
        d.Clear();
        Assert.False(Poll(d, m, Dying));  // baseline gone: no edge
    }

    [Fact]
    public void TitleScreenDemoDeath_Ignored()
    {
        var d = new DeathEdgeDetector();
        var m = new FakeSnesMemory();
        Assert.False(Poll(d, m, 0, TitleMode));
        Assert.False(Poll(d, m, Dying, TitleMode));   // the attract demo dying: not a death
        Assert.False(Poll(d, m, 0, FileSelectMode));
        Assert.False(Poll(d, m, Dying, FileSelectMode));
    }

    [Fact]
    public void GateOpensFromFadeToOverworldOnward()
    {
        var d = new DeathEdgeDetector();
        var m = new FakeSnesMemory();
        Assert.False(Poll(d, m, 0, FadeToOverworld));
        Assert.True(Poll(d, m, Dying, FadeToOverworld));
    }

    [Fact]
    public void UnreadableGameMode_Ignored()
    {
        var d = new DeathEdgeDetector();
        var m = new FakeSnesMemory();
        Assert.False(Poll(d, m, 0));
        m.SetByte(Anim, Dying);
        var noMode = new FakeSnesMemory();
        noMode.SetByte(Anim, Dying);
        Assert.False(d.Detect(noMode));   // $0100 unreadable: gate closed
    }
}
