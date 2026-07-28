using LiveSplit.SmwCounters.Snes;

using Xunit;

namespace SMWCounters.Tests;

// Pins the consumer contract's log-on-change idiom: one line per change of
// (StateName, Generation, WramBase, IsCoolingDown, LastError); everything
// else is payload and must not retrigger logging.
public class StatusChangeFilterTests
{
    [Fact]
    public void FirstStatus_ProducesALine()
    {
        var f = new StatusChangeFilter();
        string line = f.OnStatus("Searching", 0, 0, false, "", "None", "None", 0);
        Assert.NotNull(line);
        Assert.Contains("Searching", line);
    }

    [Fact]
    public void UnchangedKey_ProducesNull()
    {
        var f = new StatusChangeFilter();
        f.OnStatus("Searching", 0, 0, false, "", "None", "None", 0);
        Assert.Null(f.OnStatus("Searching", 0, 0, false, "", "None", "None", 0));
    }

    [Fact]
    public void PayloadOnlyChange_DoesNotRetrigger()
    {
        var f = new StatusChangeFilter();
        f.OnStatus("Searching", 0, 0, false, "", "None", "None", 0);
        // Method/rebind/scanMs are payload, not part of the change-key.
        Assert.Null(f.OnStatus("Searching", 0, 0, false, "", "Structural", "Fresh", 1234));
    }

    [Fact]
    public void GenerationBump_Retriggers()
    {
        var f = new StatusChangeFilter();
        f.OnStatus("Resolved", 1, 0x7E0000, false, "", "Structural", "Fresh", 900);
        string line = f.OnStatus("Resolved", 2, 0x7E0000, false, "", "Structural", "RivalEviction", 900);
        Assert.NotNull(line);
        Assert.Contains("gen=2", line);
        Assert.Contains("rebind=RivalEviction", line);
    }

    [Fact]
    public void ResolvedLine_CarriesBaseMethodAndScanMs()
    {
        var f = new StatusChangeFilter();
        string line = f.OnStatus("Resolved", 1, 0x7E12A000, false, "", "Structural", "Fresh", 8300);
        Assert.Contains("state=Resolved", line);
        Assert.Contains("base=0x7E12A000", line);
        Assert.Contains("method=Structural", line);
        Assert.Contains("scanMs=8300", line);
    }

    [Fact]
    public void UnresolvedLine_OmitsScanMs()
    {
        var f = new StatusChangeFilter();
        string line = f.OnStatus("Searching", 0, 0, false, "", "None", "None", 4200);
        Assert.DoesNotContain("scanMs", line);
    }

    [Fact]
    public void ErrorAndCooldown_AppearInTheLine()
    {
        var f = new StatusChangeFilter();
        string line = f.OnStatus("Searching", 0, 0, true,
            "no committable WRAM candidate in the verified set", "None", "None", 0);
        Assert.Contains("cooldown", line);
        Assert.Contains("no committable WRAM candidate", line);
    }

    [Fact]
    public void Reset_MakesTheNextStatusLogAgain()
    {
        var f = new StatusChangeFilter();
        f.OnStatus("Searching", 0, 0, false, "", "None", "None", 0);
        f.Reset();
        Assert.NotNull(f.OnStatus("Searching", 0, 0, false, "", "None", "None", 0));
    }

    [Fact]
    public void NullLastError_IsTreatedAsEmpty()
    {
        var f = new StatusChangeFilter();
        f.OnStatus("Searching", 0, 0, false, null, "None", "None", 0);
        Assert.Null(f.OnStatus("Searching", 0, 0, false, "", "None", "None", 0));
    }
}
