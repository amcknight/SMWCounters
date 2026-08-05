using System.Collections.Generic;

using LiveSplit.UI.Components;
using Xunit;

namespace SMWCounters.Tests;

// The settings hash folds the enabled set and the BankOnSave map with
// distinct salts so an id appearing in both cannot cancel out, and so
// flipping a toggle VALUE (not just membership) dirties the layout.
// Static so no WinForms control (and no CompositeHook) is constructed.
public class SettingsHashTests
{
    private static readonly string[] None = System.Array.Empty<string>();
    private static readonly KeyValuePair<string, bool>[] NoBank =
        System.Array.Empty<KeyValuePair<string, bool>>();

    private static KeyValuePair<string, bool> On(string id) => new(id, true);
    private static KeyValuePair<string, bool> Off(string id) => new(id, false);

    [Fact]
    public void IdInBothStructures_DoesNotCancelToEmpty()
    {
        int both = SmwCountersComponentSettings.CombineSetHashes(0, new[] { "coins" }, new[] { Off("coins") });
        int neither = SmwCountersComponentSettings.CombineSetHashes(0, None, NoBank);
        Assert.NotEqual(neither, both);
    }

    [Fact]
    public void SameId_DifferentStructure_HashesDifferently()
    {
        int inEnabled = SmwCountersComponentSettings.CombineSetHashes(0, new[] { "coins" }, NoBank);
        int inBank = SmwCountersComponentSettings.CombineSetHashes(0, None, new[] { Off("coins") });
        Assert.NotEqual(inEnabled, inBank);
    }

    [Fact]
    public void FlippingAToggleValue_ChangesTheHash()
    {
        int on = SmwCountersComponentSettings.CombineSetHashes(0, None, new[] { On("coins") });
        int off = SmwCountersComponentSettings.CombineSetHashes(0, None, new[] { Off("coins") });
        Assert.NotEqual(on, off);
    }

    [Fact]
    public void IterationOrder_DoesNotMatter()
    {
        int ab = SmwCountersComponentSettings.CombineSetHashes(7, new[] { "a", "b" }, new[] { On("x"), Off("y") });
        int ba = SmwCountersComponentSettings.CombineSetHashes(7, new[] { "b", "a" }, new[] { Off("y"), On("x") });
        Assert.Equal(ab, ba);
    }

    [Fact]
    public void DefaultBankOnSave_MoonsOff_EverythingElseOn()
    {
        Assert.False(SmwCountersComponentSettings.DefaultBankOnSave("moons"));
        Assert.True(SmwCountersComponentSettings.DefaultBankOnSave("kills"));
        Assert.True(SmwCountersComponentSettings.DefaultBankOnSave("jumps"));
        Assert.True(SmwCountersComponentSettings.DefaultBankOnSave("coins"));
        Assert.True(SmwCountersComponentSettings.DefaultBankOnSave("powerups"));
    }
}
