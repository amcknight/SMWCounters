using LiveSplit.UI.Components;

using Xunit;

namespace SMWCounters.Tests;

// Digit-width stability: a value cell reserves room for N digits so a
// rollover (9->10, 99->100) inside the reserve does not shift the counters to
// its right. The cell only grows once the value outruns the reserve.
public class ValueWidthTests
{
    // Fake measurer: '1' is a narrow glyph (5px), every other digit is 8px.
    // Mirrors proportional-digit fonts, where the widest run is not "111".
    private static float Measure(string s)
    {
        float w = 0;
        foreach (char c in s) { w += c == '1' ? 5f : 8f; }
        return w;
    }

    [Fact]
    public void Reserve_IsTheWidestRunOfTheRequestedDigitCount()
    {
        Assert.Equal(24f, ValueWidth.Reserve(Measure, 3));
        Assert.Equal(32f, ValueWidth.Reserve(Measure, 4));
    }

    [Fact]
    public void Reserve_ClampsTheDigitCountToTheSupportedRange()
    {
        Assert.Equal(ValueWidth.Reserve(Measure, ValueWidth.MinDigits), ValueWidth.Reserve(Measure, 0));
        Assert.Equal(ValueWidth.Reserve(Measure, ValueWidth.MaxDigits), ValueWidth.Reserve(Measure, 99));
    }

    [Fact]
    public void Cell_HoldsTheReserveUntilTheValueOutgrowsIt()
    {
        Assert.Equal(24f, ValueWidth.Cell(measured: 10f, reserve: 24f));
        Assert.Equal(24f, ValueWidth.Cell(measured: 24f, reserve: 24f));
        Assert.Equal(30f, ValueWidth.Cell(measured: 30f, reserve: 24f));
    }

    [Fact]
    public void ClampDigits_BoundsAndDefault()
    {
        Assert.Equal(ValueWidth.MinDigits, ValueWidth.ClampDigits(-4));
        Assert.Equal(ValueWidth.MaxDigits, ValueWidth.ClampDigits(50));
        Assert.Equal(4, ValueWidth.ClampDigits(4));
        Assert.InRange(ValueWidth.DefaultDigits, ValueWidth.MinDigits, ValueWidth.MaxDigits);
    }
}
