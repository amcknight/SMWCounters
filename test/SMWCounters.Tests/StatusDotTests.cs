using LiveSplit.SmwCounters.Snes;

using Xunit;

namespace SMWCounters.Tests;

// Pins the status-pixel mapping from the SNES.dll consumer contract
// (snes_offsets/docs/status-first-consumption.md, "Status-pixel mapping").
public class StatusDotTests
{
    [Fact]
    public void Resolved_WithRealVerdictOnCommittedBase_IsGreen()
        => Assert.Equal(StatusDot.Green,
            StatusDot.ColorFor("Resolved", false, "Real", 0x1000, 0x1000));

    [Fact]
    public void Resolved_WithVerdictStampedOnRivalBase_IsPaleGreen()
        => Assert.Equal(StatusDot.PaleGreen,
            StatusDot.ColorFor("Resolved", false, "Real", 0x2000, 0x1000));

    [Fact]
    public void Resolved_WithoutRealVerdict_IsPaleGreen()
        => Assert.Equal(StatusDot.PaleGreen,
            StatusDot.ColorFor("Resolved", false, "Ambiguous", 0x1000, 0x1000));

    [Fact]
    public void Held_FollowsResolvedRules_GreenWhenVouched()
        => Assert.Equal(StatusDot.Green,
            StatusDot.ColorFor("Held", false, "Real", 0x1000, 0x1000));

    [Fact]
    public void Held_FollowsResolvedRules_PaleGreenOtherwise()
        => Assert.Equal(StatusDot.PaleGreen,
            StatusDot.ColorFor("Held", false, "", 0, 0x1000));

    [Fact]
    public void Degraded_IsYellow_EvenWhenCoolingDown()
        => Assert.Equal(StatusDot.Yellow,
            StatusDot.ColorFor("Degraded", true, "Real", 0x1000, 0x1000));

    [Fact]
    public void Discovering_IsBlue()
        => Assert.Equal(StatusDot.Blue,
            StatusDot.ColorFor("Discovering", false, "", 0, 0));

    [Fact]
    public void Searching_IsGray()
        => Assert.Equal(StatusDot.Gray,
            StatusDot.ColorFor("Searching", false, "", 0, 0));

    [Fact]
    public void Searching_WhileCoolingDown_IsOrange()
        => Assert.Equal(StatusDot.Orange,
            StatusDot.ColorFor("Searching", true, "", 0, 0));

    [Fact]
    public void NoContent_IsDimGray()
        => Assert.Equal(StatusDot.DimGray,
            StatusDot.ColorFor("NoContent", false, "", 0, 0));

    [Fact]
    public void NoContent_WhileCoolingDown_IsOrange()
        => Assert.Equal(StatusDot.Orange,
            StatusDot.ColorFor("NoContent", true, "", 0, 0));

    [Fact]
    public void Detached_IsRed_EvenWhenCoolingDown()
        => Assert.Equal(StatusDot.Red,
            StatusDot.ColorFor("Detached", true, "", 0, 0));

    [Fact]
    public void UnknownFutureState_FallsBackToGray()
        => Assert.Equal(StatusDot.Gray,
            StatusDot.ColorFor("SomeNewState", false, "", 0, 0));
}
