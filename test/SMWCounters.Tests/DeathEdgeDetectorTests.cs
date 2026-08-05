using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

// The shared "$0071 rising edge to 9" death rule, extracted from
// BankedCounter so KillCounter (dual tally, not a BankedCounter subclass)
// can compose the identical discard edge.
public class DeathEdgeDetectorTests
{
    private const int Anim = 0x0071;
    private const byte Dying = 0x09;

    private static bool Poll(DeathEdgeDetector d, FakeSnesMemory m, byte anim)
    {
        m.SetByte(Anim, anim);
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
}
