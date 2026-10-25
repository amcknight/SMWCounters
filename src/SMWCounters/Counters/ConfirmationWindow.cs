using System;

namespace LiveSplit.SmwCounters.Counters;

// Shared "despawn, then confirm within N polls" window. A slot is armed when a
// sprite vanishes; a later signal (coin, 1UP score sprite) confirms the oldest
// armed slot, otherwise the slot expires. The caller runs Confirm then Tick at
// the end of a poll, so arming sets polls + 1 and a confirmation k polls after
// arming reports polls=k, with k == WindowPolls the last confirmable poll.
internal sealed class ConfirmationWindow
{
    private readonly int[] remaining;
    private readonly string traceTag;

    public ConfirmationWindow(int slotCount, int windowPolls, string traceTag)
    {
        remaining = new int[slotCount];
        WindowPolls = windowPolls;
        this.traceTag = traceTag;
    }

    public int WindowPolls { get; }

    public Action<string> Trace { get; set; }

    public void Arm(int slot) => remaining[slot] = WindowPolls + 1;

    public bool IsArmed(int slot) => remaining[slot] > 0;

    public void Disarm(int slot) => remaining[slot] = 0;

    public int Confirm(int count)
    {
        int confirmed = 0;
        while (confirmed < count)
        {
            int best = -1;
            for (int i = 0; i < remaining.Length; i++)
            {
                if (remaining[i] > 0 && (best < 0 || remaining[i] < remaining[best]))
                {
                    best = i;
                }
            }
            if (best < 0)
            {
                break;
            }
            Trace?.Invoke($"{traceTag} confirmed slot{best} polls={WindowPolls + 1 - remaining[best]}");
            remaining[best] = 0;
            confirmed++;
        }
        return confirmed;
    }

    public void Tick()
    {
        for (int i = 0; i < remaining.Length; i++)
        {
            if (remaining[i] > 0 && --remaining[i] == 0)
            {
                Trace?.Invoke($"{traceTag} expired slot{i}");
            }
        }
    }

    public void Clear() => Array.Clear(remaining, 0, remaining.Length);
}
