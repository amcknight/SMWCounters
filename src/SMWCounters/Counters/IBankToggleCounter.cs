namespace LiveSplit.SmwCounters.Counters;

// Counters carrying the "Discard on death" toggle. Banked is a display
// selector (see BankedCounter): both histories are always tracked; the flag
// only picks which one Value/ValueIsAlert render. HasBankToggle=false means
// the settings UI shows no checkbox and Banked stays at its default (true).
internal interface IBankToggleCounter
{
    bool Banked { get; set; }
    bool HasBankToggle { get; }
}
