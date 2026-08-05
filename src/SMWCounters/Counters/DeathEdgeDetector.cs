using LiveSplit.SmwCounters.Snes;

namespace LiveSplit.SmwCounters.Counters;

// Shared "$0071 rising edge to 9" death rule — the discard edge for every
// banked history. Extracted from BankedCounter so KillCounter (dual tally,
// not a BankedCounter) composes the same rule instead of duplicating it.
internal sealed class DeathEdgeDetector
{
    private const int PlayerAnimationOffset = 0x0071;
    private const byte DyingValue = 0x09;

    private readonly PreviousByte previousAnim = new();

    public bool Detect(ISnesMemory memory)
    {
        if (!memory.ReadWramByte(PlayerAnimationOffset, out byte anim))
        {
            previousAnim.Clear();
            return false;
        }
        bool died = previousAnim.HasPrevious
            && previousAnim.Value != DyingValue && anim == DyingValue;
        previousAnim.Set(anim);
        return died;
    }

    public void Clear() => previousAnim.Clear();
}
