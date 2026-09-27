using System.Drawing;

using LiveSplit.SmwCounters.Snes;

namespace LiveSplit.SmwCounters.Counters;

// Low-coin% tracker. Counts increments of the game's coin counter ($0DBF)
// while in a level; die reverts unbanked coins (the game itself keeps them —
// this counter deliberately doesn't); midway/exit banks them. The counter is
// a lifetime tally: it keeps counting across the game's 100-coin 1-up wrap.
internal sealed class CoinCounter : BankedCounter
{
    private const int GameModeOffset = 0x0100;
    private const byte LevelMainMode = 0x14;
    private const int CoinsOffset = 0x0DBF;

    // Largest gain accepted across a 100-wrap in a single ~15ms poll. A wrap
    // candidate above this is treated as a resync instead (e.g. a hack shop
    // deducting coins must never register as a gain).
    private const int MaxWrapBurst = 15;

    private static readonly Bitmap icon = IconLoader.Load("LiveSplit.SmwCounters.Assets.coin.png");

    private readonly PreviousByte previousCoins = new();
    private readonly MidwayExitBankDetector bank = new();

    public override string Id => "coins";
    public override Image DefaultIcon => icon;
    public override string DefaultLabel => "Coins";
    protected override string SaveName => "Coins";

    protected override int DetectCollectDelta(ISnesMemory memory)
    {
        // Gate to in-level so overworld/load/file-select coin writes don't count.
        if (!memory.ReadWramByte(GameModeOffset, out byte gameMode) || gameMode != LevelMainMode)
        {
            previousCoins.Clear();
            return 0;
        }
        if (!memory.ReadWramByte(CoinsOffset, out byte coins))
        {
            previousCoins.Clear();
            return 0;
        }

        int delta = 0;
        if (previousCoins.HasPrevious)
        {
            if (coins >= previousCoins.Value)
            {
                delta = coins - previousCoins.Value;
            }
            else
            {
                int wrapCandidate = coins + 100 - previousCoins.Value;
                if (wrapCandidate <= MaxWrapBurst) { delta = wrapCandidate; }
            }
        }
        previousCoins.Set(coins);
        return delta;
    }

    protected override bool DetectBank(ISnesMemory memory) => bank.DetectBank(memory);
    protected override bool DetectDiscard(ISnesMemory memory) => bank.LeaveDiscarded;

    protected override void ClearDetectors()
    {
        previousCoins.Clear();
        bank.Clear();
    }
}
