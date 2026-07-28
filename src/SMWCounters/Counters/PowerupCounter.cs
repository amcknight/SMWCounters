using System.Drawing;

using LiveSplit.SmwCounters.Snes;
using LiveSplit.UI;

namespace LiveSplit.SmwCounters.Counters;

// Low% powerup tracker. Collect (mushroom/feather/flower get) bumps total while
// in a level; die reverts unbanked collects; reaching a checkpoint (midway) or
// completing an exit banks them. total shows in the alert color until banked.
internal sealed class PowerupCounter : BankedCounter
{
    private const int GameModeOffset = 0x0100;
    private const byte LevelMainMode = 0x14;
    private const int PlayerAnimationOffset = 0x0071;

    private static readonly Bitmap icon = IconLoader.Load("LiveSplit.SmwCounters.Assets.mushroom.png");

    private readonly PreviousByte previousCollectAnim = new();
    private readonly MidwayExitBankDetector bank = new();

    public override string Id => "powerups";
    public override Image DefaultIcon => icon;
    public override string DefaultLabel => "Powerups";
    protected override string SaveName => "Powerups";

    protected override int DetectCollectDelta(ISnesMemory memory)
    {
        // Gate to in-level so overworld/load/garbage animation values don't count.
        if (!memory.ReadWramByte(GameModeOffset, out byte gameMode) || gameMode != LevelMainMode)
        {
            previousCollectAnim.Clear();
            return 0;
        }
        if (!memory.ReadWramByte(PlayerAnimationOffset, out byte anim))
        {
            previousCollectAnim.Clear();
            return 0;
        }
        bool got = previousCollectAnim.HasPrevious
            && anim != previousCollectAnim.Value
            && (anim == 2 || anim == 3 || anim == 4);
        previousCollectAnim.Set(anim);
        return got ? 1 : 0;
    }

    protected override bool DetectBank(ISnesMemory memory) => bank.DetectBank(memory);

    protected override void ClearDetectors()
    {
        previousCollectAnim.Clear();
        bank.Clear();
    }
}
