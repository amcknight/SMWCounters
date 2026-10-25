using System.Drawing;
using System.Xml;

using LiveSplit.SmwCounters.Snes;
using LiveSplit.UI;

namespace LiveSplit.SmwCounters.Counters;

// One count per level in which the fifth Dragon Coin is taken: the moment
// $1420 (Yoshi coins collected this level; kaizosplits' "yoshiCoin") crosses
// from below five to five or more. That is the 1-up moment, and the game
// marks the level's coins collected right then — they stay gone across a
// death — so the count is permanent: no bank, no discard, never gold. A
// sixth coin in a six-coin level moves the byte 5->6 and adds nothing. The
// byte is per level and zeroed on every level load, so a death before the
// fifth coin starts the level's tally over, which this counter never sees.
// Gated on level-main like the other collects; the first in-level sample
// only baselines, so a sublevel pipe carries the count across without an
// edge and a stale byte on entry can't count.
internal sealed class DragonCoinCounter : ISmwCounter
{
    private const int YoshiCoinsOffset = 0x1420;

    private const byte CoinsForTheOneUp = 5;

    private static readonly Bitmap icon = IconLoader.Load("LiveSplit.SmwCounters.Assets.dragoncoin.png");

    private readonly PreviousByte previousCoins = new();

    public string Id => "dragoncoins";
    public Image DefaultIcon => icon;
    public string DefaultLabel => "Dragon Coins";

    public int Value { get; private set; }

    public bool ValueIsAlert => false;

    public int StateHash => Value;

    public void Reset()
    {
        Value = 0;
        previousCoins.Clear();
    }

    public void SetValue(int value) => Value = value;

    public void Poll(ISnesMemory memory)
    {
        if (!memory.IsAttached
            || !LevelGate.IsLevelMain(memory)
            || !memory.ReadWramByte(YoshiCoinsOffset, out byte coins))
        {
            previousCoins.Clear();
            return;
        }

        bool fifth = previousCoins.HasPrevious
            && previousCoins.Value < CoinsForTheOneUp && coins >= CoinsForTheOneUp;
        previousCoins.Set(coins);
        if (fifth) { Value++; }
    }

    public void SaveState(XmlDocument doc, XmlElement parent)
    {
        SettingsHelper.CreateSetting(doc, parent, "DragonCoins", Value);
    }

    public void LoadState(XmlElement parent)
    {
        Value = SettingsHelper.ParseInt(parent["DragonCoins"], 0);
        previousCoins.Clear();
    }
}
