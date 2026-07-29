using LiveSplit.SmwCounters.Snes;

using Xunit;

namespace SMWCounters.Tests;

// Pins the status-pixel mapping (v0.5.0 live-test revision): red detached,
// orange no-content, yellow searching/discovering, green degraded (working,
// rivals live), blue resolved (settled), purple held, gray cooldown. The
// witness vouch no longer changes the dot — it lives in the SNS log lines.
// Mirrored in the SNES.dll consumer contract
// (snes_offsets/docs/status-first-consumption.md).
public class StatusDotTests
{
    [Fact]
    public void Resolved_IsBlue_RegardlessOfVouch()
        => Assert.Equal(StatusDot.Blue,
            StatusDot.ColorFor("Resolved", false, "Real", 0x1000, 0x1000));

    [Fact]
    public void Resolved_WithVerdictStampedOnRivalBase_IsStillBlue()
        => Assert.Equal(StatusDot.Blue,
            StatusDot.ColorFor("Resolved", false, "Real", 0x2000, 0x1000));

    [Fact]
    public void Resolved_WithoutRealVerdict_IsStillBlue()
        => Assert.Equal(StatusDot.Blue,
            StatusDot.ColorFor("Resolved", false, "Ambiguous", 0x1000, 0x1000));

    [Fact]
    public void Held_IsPurple()
        => Assert.Equal(StatusDot.Purple,
            StatusDot.ColorFor("Held", false, "Real", 0x1000, 0x1000));

    [Fact]
    public void Held_IsPurple_EvenUnvouched()
        => Assert.Equal(StatusDot.Purple,
            StatusDot.ColorFor("Held", false, "", 0, 0x1000));

    [Fact]
    public void Degraded_IsGreen_EvenWhenCoolingDown()
        => Assert.Equal(StatusDot.Green,
            StatusDot.ColorFor("Degraded", true, "Real", 0x1000, 0x1000));

    [Fact]
    public void Discovering_IsYellow()
        => Assert.Equal(StatusDot.Yellow,
            StatusDot.ColorFor("Discovering", false, "", 0, 0));

    [Fact]
    public void Discovering_WhileCoolingDown_IsGray()
        => Assert.Equal(StatusDot.Gray,
            StatusDot.ColorFor("Discovering", true, "", 0, 0));

    [Fact]
    public void Searching_IsYellow()
        => Assert.Equal(StatusDot.Yellow,
            StatusDot.ColorFor("Searching", false, "", 0, 0));

    [Fact]
    public void Searching_WhileCoolingDown_IsGray()
        => Assert.Equal(StatusDot.Gray,
            StatusDot.ColorFor("Searching", true, "", 0, 0));

    [Fact]
    public void NoContent_IsOrange()
        => Assert.Equal(StatusDot.Orange,
            StatusDot.ColorFor("NoContent", false, "", 0, 0));

    [Fact]
    public void NoContent_WhileCoolingDown_IsGray()
        => Assert.Equal(StatusDot.Gray,
            StatusDot.ColorFor("NoContent", true, "", 0, 0));

    [Fact]
    public void Detached_IsRed_EvenWhenCoolingDown()
        => Assert.Equal(StatusDot.Red,
            StatusDot.ColorFor("Detached", true, "", 0, 0));

    [Fact]
    public void UnknownFutureState_FallsBackToYellow()
        => Assert.Equal(StatusDot.Yellow,
            StatusDot.ColorFor("SomeNewState", false, "", 0, 0));
}
