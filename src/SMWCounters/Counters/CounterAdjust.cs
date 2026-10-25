using System;
using System.Collections.Generic;

namespace LiveSplit.SmwCounters.Counters;

// The adjust hotkeys' arithmetic. A nudge is a human correction, not a game
// event, so it goes through SetValue (which banks every history at once) and
// is never discarded by a later death.
internal static class CounterAdjust
{
    public static ISmwCounter Find(IReadOnlyList<ISmwCounter> counters, string id)
    {
        foreach (ISmwCounter c in counters)
        {
            if (c.Id == id) { return c; }
        }
        return null;
    }

    public static void Apply(ISmwCounter counter, int delta)
        => counter.SetValue(Math.Max(0, counter.Value + delta));
}
