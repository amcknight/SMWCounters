using System.Drawing;
using System.Xml;

using LiveSplit.SmwCounters.Snes;

namespace LiveSplit.SmwCounters.Counters;

internal interface ISmwCounter
{
    // Stable serialization key. Must not change once shipped.
    string Id { get; }

    // Drawn in the layout row when the user hasn't set a per-counter label
    // override. Null means the DefaultLabel text is used as a fallback.
    Image DefaultIcon { get; }

    // Human-readable name shown in the settings dialog row label.
    string DefaultLabel { get; }

    int Value { get; }

    // True when the value should be drawn in the layout's negative/alert color
    // instead of the normal text color. Default-style: false for most counters.
    bool ValueIsAlert { get; }

    void Reset();

    // Called once per poll tick when the component is attached. The counter
    // performs its own reads and decides whether to increment Value.
    void Poll(ISnesMemory memory);

    // Save/restore counter-owned state (the current value plus any
    // counter-specific config like dedupe mode). Each counter is responsible
    // for choosing element names under `parent`.
    void SaveState(XmlDocument doc, XmlElement parent);
    void LoadState(XmlElement parent);
}
