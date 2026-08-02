using System.Drawing;
using System.Xml;

using LiveSplit.SmwCounters.Counters;
using LiveSplit.SmwCounters.Snes;
using LiveSplit.UI.Components;

using Xunit;

namespace SMWCounters.Tests;

// Timer parity across the final split: at Ended the display pins to the
// split-time values while the counters keep tallying underneath; undoing the
// split reveals the live tally, exactly like the timer jumping to where it
// would have been.
public class EndedDisplayFreezeTests
{
    // Minimal ISmwCounter with settable value/alert; the freeze only reads
    // Id/Value/ValueIsAlert, everything else is inert.
    private sealed class StubCounter : ISmwCounter
    {
        public string Id { get; set; } = "stub";
        public int Value { get; set; }
        public bool ValueIsAlert { get; set; }
        public Image DefaultIcon => null;
        public string DefaultLabel => "Stub";
        public int StateHash => Value;
        public void SetValue(int value) => Value = value;
        public void Reset() => Value = 0;
        public void Poll(ISnesMemory memory) { }
        public void SaveState(XmlDocument doc, XmlElement parent) { }
        public void LoadState(XmlElement parent) { }
    }

    private static ISmwCounter[] Counters(params StubCounter[] counters) => counters;

    [Fact]
    public void BeforeEnded_ShowsLiveValue()
    {
        var c = new StubCounter { Value = 7 };
        var freeze = new EndedDisplayFreeze();
        freeze.OnPhase(isEnded: false, Counters(c));
        c.Value = 9;
        Assert.Equal(9, freeze.ValueFor(c));
    }

    [Fact]
    public void EnteringEnded_FreezesTheDisplayedValue()
    {
        var c = new StubCounter { Value = 10 };
        var freeze = new EndedDisplayFreeze();
        freeze.OnPhase(isEnded: true, Counters(c));
        c.Value = 13; // background tallying continues underneath
        Assert.Equal(10, freeze.ValueFor(c));
    }

    [Fact]
    public void EnteringEnded_FreezesTheAlertState()
    {
        var c = new StubCounter { Value = 10, ValueIsAlert = true };
        var freeze = new EndedDisplayFreeze();
        freeze.OnPhase(isEnded: true, Counters(c));
        c.ValueIsAlert = false; // e.g. a background bank commits
        Assert.True(freeze.AlertFor(c));
    }

    [Fact]
    public void StayingEnded_DoesNotRecapture()
    {
        var c = new StubCounter { Value = 10 };
        var freeze = new EndedDisplayFreeze();
        freeze.OnPhase(isEnded: true, Counters(c));
        c.Value = 13;
        freeze.OnPhase(isEnded: true, Counters(c)); // every subsequent poll tick
        Assert.Equal(10, freeze.ValueFor(c));
    }

    [Fact]
    public void UndoingTheFinalSplit_RevealsTheLiveTally()
    {
        var c = new StubCounter { Value = 10 };
        var freeze = new EndedDisplayFreeze();
        freeze.OnPhase(isEnded: true, Counters(c));
        c.Value = 13;
        freeze.OnPhase(isEnded: false, Counters(c)); // back was pressed
        Assert.Equal(13, freeze.ValueFor(c));
        Assert.False(freeze.AlertFor(c) && !c.ValueIsAlert);
    }

    [Fact]
    public void EndingAgainAfterUndo_CapturesTheNewValues()
    {
        var c = new StubCounter { Value = 10 };
        var freeze = new EndedDisplayFreeze();
        freeze.OnPhase(isEnded: true, Counters(c));
        freeze.OnPhase(isEnded: false, Counters(c));
        c.Value = 13;
        freeze.OnPhase(isEnded: true, Counters(c)); // real final split this time
        c.Value = 15;
        Assert.Equal(13, freeze.ValueFor(c));
    }

    [Fact]
    public void FreezesEachCounterIndependently()
    {
        var jumps = new StubCounter { Id = "jumps", Value = 40 };
        var deaths = new StubCounter { Id = "deaths", Value = 3, ValueIsAlert = true };
        var freeze = new EndedDisplayFreeze();
        freeze.OnPhase(isEnded: true, Counters(jumps, deaths));
        jumps.Value = 45;
        deaths.Value = 5;
        deaths.ValueIsAlert = false;
        Assert.Equal(40, freeze.ValueFor(jumps));
        Assert.Equal(3, freeze.ValueFor(deaths));
        Assert.True(freeze.AlertFor(deaths));
    }
}
