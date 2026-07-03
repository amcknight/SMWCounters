using System.Drawing;
using System.Xml;

using LiveSplit.SmwCounters.Snes;
using LiveSplit.UI;

namespace LiveSplit.SmwCounters.Counters;

// Shared "collect, then bank or discard-on-death" counter.
//   collect => total++            (subclass DetectCollect)
//   die     => total = saved      (default: $0071 rising-edge to 9)
//   bank    => saved = total      (subclass DetectBank)
// Value shows total; ValueIsAlert is true while total != saved (unbanked).
internal abstract class BankedCounter : ISmwCounter
{
    private const int PlayerAnimationOffset = 0x0071;
    private const byte DyingValue = 0x09;

    private readonly PreviousByte previousDeathAnim = new();

    protected int total;
    protected int saved;

    public abstract string Id { get; }
    public abstract Image DefaultIcon { get; }
    public abstract string DefaultLabel { get; }

    // Serialization element base name (e.g. "Exits" -> <Exits>, <ExitsSaved>).
    protected abstract string SaveName { get; }

    public int Value => total;
    public bool ValueIsAlert => total != saved;

    public void Reset()
    {
        total = 0;
        saved = 0;
        previousDeathAnim.Clear();
        ClearDetectors();
    }

    public void Poll(ISnesMemory memory)
    {
        if (!memory.IsAttached)
        {
            previousDeathAnim.Clear();
            ClearDetectors();
            return;
        }

        if (DetectDeath(memory)) { total = saved; }
        if (DetectCollect(memory)) { total++; }
        if (DetectBank(memory)) { saved = total; }
    }

    // Default die-to-discard: rising edge of $0071 to the dying value.
    protected virtual bool DetectDeath(ISnesMemory memory)
    {
        if (!memory.ReadWramByte(PlayerAnimationOffset, out byte anim))
        {
            previousDeathAnim.Clear();
            return false;
        }
        bool died = previousDeathAnim.HasPrevious
            && previousDeathAnim.Value != DyingValue && anim == DyingValue;
        previousDeathAnim.Set(anim);
        return died;
    }

    protected abstract bool DetectCollect(ISnesMemory memory);
    protected abstract bool DetectBank(ISnesMemory memory);
    protected abstract void ClearDetectors();

    public void SaveState(XmlDocument doc, XmlElement parent)
    {
        SettingsHelper.CreateSetting(doc, parent, SaveName, total);
        SettingsHelper.CreateSetting(doc, parent, SaveName + "Saved", saved);
    }

    public void LoadState(XmlElement parent)
    {
        total = SettingsHelper.ParseInt(parent[SaveName], 0);
        // Back-compat: pre-v0.2.0 layouts have no <Name>Saved -> treat as banked.
        saved = SettingsHelper.ParseInt(parent[SaveName + "Saved"], total);
        previousDeathAnim.Clear();
        ClearDetectors();
    }
}
