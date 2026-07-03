using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

public class SetValueTests
{
    [Fact]
    public void SimpleCounter_SetValue_SetsValue()
    {
        var c = new DeathCounter();
        c.SetValue(37);
        Assert.Equal(37, c.Value);
    }

    [Fact]
    public void BankedCounter_SetValue_SetsValueAndClearsAlert()
    {
        var c = new ExitCounter();
        c.SetValue(12);
        Assert.Equal(12, c.Value);
        Assert.False(c.ValueIsAlert);   // total == saved
    }
}
