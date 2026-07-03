using Xunit;

namespace SMWCounters.Tests;

public class SmokeTest
{
    [Fact]
    public void FakeMemory_ReadsWhatWasSet()
    {
        var mem = new FakeSnesMemory();
        mem.SetByte(0x0DD5, 4);
        Assert.True(mem.ReadWramByte(0x0DD5, out byte v));
        Assert.Equal(4, v);
    }
}
