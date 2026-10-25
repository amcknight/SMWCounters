using System;

namespace LiveSplit.SmwCounters.Counters;

// A debug-log sink the component wires in; counters write their own
// diagnostic lines through it. Null means silent.
internal interface ITraceSource { Action<string> Trace { get; set; } }
