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
    private const int MidwayOffset = 0x13CE;
    private const int ExitsCompletedOffset = 0x1F2E;

    private static readonly Bitmap icon = IconLoader.Load("LiveSplit.SmwCounters.Assets.mushroom.png");

    private readonly PreviousByte previousCollectAnim = new();
    private readonly PreviousByte previousMidway = new();
    private readonly PreviousByte previousExits = new();

    public override string Id => "powerups";
    public override Image DefaultIcon => icon;
    public override string DefaultLabel => "Powerups";
    protected override string SaveName => "Powerups";

    protected override bool DetectCollect(ISnesMemory memory)
    {
        // Gate to in-level so overworld/load/garbage animation values don't count.
        if (!memory.ReadWramByte(GameModeOffset, out byte gameMode) || gameMode != LevelMainMode)
        {
            previousCollectAnim.Clear();
            return false;
        }
        if (!memory.ReadWramByte(PlayerAnimationOffset, out byte anim))
        {
            previousCollectAnim.Clear();
            return false;
        }
        bool got = previousCollectAnim.HasPrevious
            && anim != previousCollectAnim.Value
            && (anim == 2 || anim == 3 || anim == 4);
        previousCollectAnim.Set(anim);
        return got;
    }

    protected override bool DetectBank(ISnesMemory memory)
    {
        bool banked = false;

        if (memory.ReadWramByte(MidwayOffset, out byte midway))
        {
            if (previousMidway.HasPrevious && midway == 1 && previousMidway.Value != 1)
            {
                banked = true;
            }
            previousMidway.Set(midway);
        }
        else { previousMidway.Clear(); }

        if (memory.ReadWramByte(ExitsCompletedOffset, out byte exits))
        {
            if (previousExits.HasPrevious && exits > previousExits.Value) { banked = true; }
            previousExits.Set(exits);
        }
        else { previousExits.Clear(); }

        return banked;
    }

    protected override void ClearDetectors()
    {
        previousCollectAnim.Clear();
        previousMidway.Clear();
        previousExits.Clear();
    }
}
