using System.Drawing;

using LiveSplit.SmwCounters.Snes;

namespace LiveSplit.SmwCounters.Counters;

// Counts 3-up moon collections. A BankedCounter since v0.6.x — but the
// "Discard on death" toggle defaults OFF for moons (settings-level default),
// so out of the box this displays the plain never-reverted history, identical
// to the old standalone counter. Toggled on, moons show gold until a
// midway/exit banks them and a death discards unbanked ones.
//
// The per-level dedupe mode was dropped 2026-08-04: it served one
// hypothetical challenge run and interacted badly with death-reverts (the
// dedupe would remember a discarded moon and refuse to recount it). Legacy
// DedupeMode/DedupePerRoom layout elements load fine — they are simply never
// read.
internal sealed class MoonCounter : BankedCounter
{
    // SNES WRAM addresses (from kaizosplits Memory.cs).
    private const int MoonCounterOffset = 0x13C5; // # of 3-up moons collected, per scene
    private const int GameModeOffset    = 0x0100;

    private const byte LevelMainMode    = 0x14;

    private static readonly Bitmap icon = IconLoader.Load("LiveSplit.SmwCounters.Assets.moon.png");

    private readonly PreviousByte previousMoon = new();
    private readonly MidwayExitBankDetector bank = new();

    public override string Id => "moons";
    public override Image DefaultIcon => icon;
    public override string DefaultLabel => "Moons";
    protected override string SaveName => "Moons";

    protected override int DetectCollectDelta(ISnesMemory memory)
    {
        // Only count while actually in a level. Outside a level (title,
        // file-select, overworld, load transitions) $13C5 holds transient data
        // whose changes fire spuriously — clear the baseline so re-entry
        // establishes a fresh one instead of registering an edge.
        // Gate on game mode (level-main), not the legacy $1935 in-level flag:
        // custom Yoshi Houses never set $1935, so moons there wouldn't count
        // (live-confirmed 2026-07-27). Mirrors JumpCounter's gate.
        if (!memory.ReadWramByte(GameModeOffset, out byte gameMode) || gameMode != LevelMainMode)
        {
            previousMoon.Clear();
            return 0;
        }
        if (!memory.ReadWramByte(MoonCounterOffset, out byte moon))
        {
            previousMoon.Clear();
            return 0;
        }
        bool collected = previousMoon.HasPrevious && moon > previousMoon.Value;
        previousMoon.Set(moon);
        return collected ? 1 : 0;
    }

    protected override bool DetectBank(ISnesMemory memory) => bank.DetectBank(memory);

    protected override void ClearDetectors()
    {
        previousMoon.Clear();
        bank.Clear();
    }
}
