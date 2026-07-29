using LiveSplit.UI.Components;
using Xunit;

namespace SMWCounters.Tests;

// The settings hash previously XORed enabled ids and bankDisabled ids into
// one accumulator, so an id present in both sets cancelled out:
// {enabled:[coins], bankDisabled:[coins]} hashed identically to {} and the
// user's change never dirtied the layout. These tests pin the salted fold.
// CombineSetHashes is static so no WinForms control (and no CompositeHook)
// is constructed in tests.
public class SettingsHashTests
{
    private static readonly string[] None = System.Array.Empty<string>();

    [Fact]
    public void IdInBothSets_DoesNotCancelToEmpty()
    {
        int both = SmwCountersComponentSettings.CombineSetHashes(0, new[] { "coins" }, new[] { "coins" });
        int neither = SmwCountersComponentSettings.CombineSetHashes(0, None, None);
        Assert.NotEqual(neither, both);
    }

    [Fact]
    public void SameId_DifferentSet_HashesDifferently()
    {
        int inEnabled = SmwCountersComponentSettings.CombineSetHashes(0, new[] { "coins" }, None);
        int inBank = SmwCountersComponentSettings.CombineSetHashes(0, None, new[] { "coins" });
        Assert.NotEqual(inEnabled, inBank);
    }

    [Fact]
    public void TogglingBankDisabled_ChangesHash()
    {
        int on = SmwCountersComponentSettings.CombineSetHashes(0, new[] { "deaths", "coins" }, None);
        int off = SmwCountersComponentSettings.CombineSetHashes(0, new[] { "deaths", "coins" }, new[] { "coins" });
        Assert.NotEqual(on, off);
    }

    [Fact]
    public void SetIterationOrder_DoesNotMatter()
    {
        // enabled/bankDisabled are HashSets — the hash must not depend on
        // iteration order, only on membership.
        int ab = SmwCountersComponentSettings.CombineSetHashes(7, new[] { "a", "b" }, None);
        int ba = SmwCountersComponentSettings.CombineSetHashes(7, new[] { "b", "a" }, None);
        Assert.Equal(ab, ba);
    }
}
