using System.Drawing;
using System.Xml;

using LiveSplit.SmwCounters.Snes;
using LiveSplit.UI;

namespace LiveSplit.SmwCounters.Counters;

// One count per death edge. The rule itself ($0071 rising to 9, past the
// title/file-select screens) lives in DeathEdgeDetector, shared with every
// banked counter's discard edge so all of them agree on what a death is.
internal sealed class DeathCounter : ISmwCounter
{
    private static readonly Bitmap icon = IconLoader.Load("LiveSplit.SmwCounters.Assets.death.png");

    private readonly DeathEdgeDetector deathEdge = new();

    public string Id => "deaths";
    public Image DefaultIcon => icon;
    public string DefaultLabel => "Deaths";

    public int Value { get; private set; }

    public bool ValueIsAlert => false;

    public int StateHash => Value;

    public void Reset()
    {
        Value = 0;
        deathEdge.Clear();
    }

    public void SetValue(int value) => Value = value;

    public void Poll(ISnesMemory memory)
    {
        if (!memory.IsAttached)
        {
            deathEdge.Clear();
            return;
        }

        if (deathEdge.Detect(memory)) { Value++; }
    }

    public void SaveState(XmlDocument doc, XmlElement parent)
    {
        SettingsHelper.CreateSetting(doc, parent, "Deaths", Value);
    }

    public void LoadState(XmlElement parent)
    {
        Value = SettingsHelper.ParseInt(parent["Deaths"], 0);
        deathEdge.Clear();
    }
}
