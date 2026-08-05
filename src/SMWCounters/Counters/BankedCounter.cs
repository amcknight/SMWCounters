using System.Drawing;
using System.Xml;

using LiveSplit.SmwCounters.Snes;
using LiveSplit.UI;

namespace LiveSplit.SmwCounters.Counters;

// Shared "collect, then bank or discard-on-death" counter.
//   collect => total += delta     (subclass DetectCollectDelta)
//   die     => total = saved      (default: $0071 rising-edge to 9)
//   bank    => saved = total      (subclass DetectBank)
// Value shows total; ValueIsAlert is true while total != saved (unbanked).
internal abstract class BankedCounter : ISmwCounter
{
    private readonly DeathEdgeDetector deathEdge = new();

    protected int total;
    protected int saved;

    // When false the counter is a plain permanent tally: collects advance saved
    // with total (no alert), and die-to-discard is a no-op. Driven by the
    // per-counter "Bank on save" setting.
    public bool Banked { get; set; } = true;

    // Whether the settings UI exposes a "Discard on death" checkbox for this
    // counter (drives both the extras row and the per-poll Banked sync).
    // Exits bank on the save write itself, so a toggle is meaningless there.
    public virtual bool HasBankToggle => true;

    public abstract string Id { get; }
    public abstract Image DefaultIcon { get; }
    public abstract string DefaultLabel { get; }

    // Serialization element base name (e.g. "Exits" -> <Exits>, <ExitsSaved>).
    protected abstract string SaveName { get; }

    public int Value => total;
    public bool ValueIsAlert => total != saved;

    // saved is persisted but invisible to Value, so it must feed the hash:
    // a bank commit (saved = total) changes what SaveState writes without
    // moving Value at all.
    public int StateHash => total * 397 ^ saved;

    public void Reset()
    {
        total = 0;
        saved = 0;
        deathEdge.Clear();
        ClearDetectors();
    }

    public void SetValue(int value)
    {
        total = value;
        saved = value;
    }

    public void Poll(ISnesMemory memory)
    {
        if (!memory.IsAttached)
        {
            deathEdge.Clear();
            ClearDetectors();
            return;
        }

        if (!Banked && total != saved) { saved = total; }

        if (DetectDeath(memory)) { total = saved; }
        int delta = DetectCollectDelta(memory);
        if (delta > 0) { total += delta; if (!Banked) { saved = total; } }
        if (DetectBank(memory)) { saved = total; }
    }

    // Default die-to-discard: rising edge of $0071 to the dying value.
    protected virtual bool DetectDeath(ISnesMemory memory) => deathEdge.Detect(memory);

    // Number of collects detected this poll (0 = none). Most counters are
    // 0/1 edge detectors; coins arrive as multi-unit deltas.
    protected abstract int DetectCollectDelta(ISnesMemory memory);
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
        deathEdge.Clear();
        ClearDetectors();
    }
}
