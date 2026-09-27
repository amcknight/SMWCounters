using System.Drawing;
using System.Xml;

using LiveSplit.SmwCounters.Snes;
using LiveSplit.UI;

namespace LiveSplit.SmwCounters.Counters;

// Shared "collect, then bank or discard-on-death" counter tracking BOTH
// histories every poll:
//   banked:  collect => total += delta; die (or DetectDiscard) => total = saved;
//            bank => saved = total (subclass DetectBank)
//   plain:   collect => plain += delta; never reverted
// Banked is a pure display selector — it never changes what is tracked:
//   Value       = Banked ? total : plain
//   ValueIsAlert= Banked && total != saved   (plain view never alerts)
// Flipping the "Discard on death" toggle mid-run therefore snaps the shown
// value to what it would have been had the setting been that way all along.
internal abstract class BankedCounter : ISmwCounter, IBankToggleCounter
{
    private readonly DeathEdgeDetector deathEdge = new();

    protected int total;
    protected int saved;
    protected int plain;   // the never-reverted "discard off" history

    // Display selector (see class comment). Driven per-poll from the
    // "Discard on death" setting for counters with HasBankToggle.
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

    public int Value => Banked ? total : plain;
    public bool ValueIsAlert => Banked && total != saved;

    // Everything SaveState persists must feed the hash — including values the
    // current display hides (saved, and the non-displayed history).
    public int StateHash => (total * 397 ^ saved) * 397 ^ plain;

    public void Reset()
    {
        total = 0;
        saved = 0;
        plain = 0;
        deathEdge.Clear();
        ClearDetectors();
    }

    public void SetValue(int value)
    {
        total = value;
        saved = value;
        plain = value;
    }

    public void Poll(ISnesMemory memory)
    {
        if (!memory.IsAttached)
        {
            deathEdge.Clear();
            ClearDetectors();
            return;
        }

        if (DetectDeath(memory)) { total = saved; }
        int delta = DetectCollectDelta(memory);
        if (delta > 0) { total += delta; plain += delta; }
        if (DetectBank(memory)) { saved = total; }
        if (DetectDiscard(memory)) { total = saved; }
    }

    // Default die-to-discard: rising edge of $0071 to the dying value.
    protected virtual bool DetectDeath(ISnesMemory memory) => deathEdge.Detect(memory);

    // A non-death discard. Only Exits use it (an abandoned finish, see
    // ExitCounter.DetectBank). Runs after DetectBank so a subclass can derive
    // it from the same detector that produced this poll's bank verdict.
    protected virtual bool DetectDiscard(ISnesMemory memory) => false;

    // Number of collects detected this poll (0 = none). Most counters are
    // 0/1 edge detectors; coins arrive as multi-unit deltas.
    protected abstract int DetectCollectDelta(ISnesMemory memory);
    protected abstract bool DetectBank(ISnesMemory memory);
    protected abstract void ClearDetectors();

    public void SaveState(XmlDocument doc, XmlElement parent)
    {
        SettingsHelper.CreateSetting(doc, parent, SaveName, total);
        SettingsHelper.CreateSetting(doc, parent, SaveName + "Saved", saved);
        SettingsHelper.CreateSetting(doc, parent, SaveName + "Plain", plain);
    }

    public void LoadState(XmlElement parent)
    {
        total = SettingsHelper.ParseInt(parent[SaveName], 0);
        // Back-compat: pre-v0.2.0 layouts have no <Name>Saved -> treat as banked.
        saved = SettingsHelper.ParseInt(parent[SaveName + "Saved"], total);
        plain = SettingsHelper.ParseInt(parent[SaveName + "Plain"], total);
        deathEdge.Clear();
        ClearDetectors();
    }
}
