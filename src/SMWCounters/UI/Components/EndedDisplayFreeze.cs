using System.Collections.Generic;

using LiveSplit.SmwCounters.Counters;

namespace LiveSplit.UI.Components;

// Timer parity across the final split: LiveSplit's timer logically keeps
// running after the last split, so undoing it jumps to where the time would
// have been. The counters mirror that — during TimerPhase.Ended they keep
// polling (the component's gate allows it) while the *display* pins to the
// values captured at the split; undoing the split unfreezes and reveals the
// live tally. Purely cosmetic: persistence and the settings UI always see
// the live counter state.
internal sealed class EndedDisplayFreeze
{
    private readonly Dictionary<string, (int Value, bool Alert)> frozen = new();
    private bool active;

    // Call once per poll tick, before the counters poll, so the capture on
    // the not-ended -> ended transition sees the values as of the split.
    public void OnPhase(bool isEnded, IReadOnlyList<ISmwCounter> counters)
    {
        if (isEnded == active) { return; }
        frozen.Clear();
        active = isEnded;
        if (!isEnded) { return; }
        foreach (ISmwCounter c in counters)
        {
            frozen[c.Id] = (c.Value, c.ValueIsAlert);
        }
    }

    public int ValueFor(ISmwCounter counter)
        => active && frozen.TryGetValue(counter.Id, out (int Value, bool Alert) f)
            ? f.Value : counter.Value;

    public bool AlertFor(ISmwCounter counter)
        => active && frozen.TryGetValue(counter.Id, out (int Value, bool Alert) f)
            ? f.Alert : counter.ValueIsAlert;
}
