using System;
using System.Collections.Generic;
using System.Linq;
using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

public class ConfirmationWindowTests
{
    private static (ConfirmationWindow w, List<string> log) Make(int polls = 4)
    {
        var log = new List<string>();
        var w = new ConfirmationWindow(12, polls, "T") { Trace = log.Add };
        return (w, log);
    }

    [Fact] public void Arm_ConfirmSamePoll_PollsZero()
    { var (w, log) = Make(); w.Arm(0); Assert.Equal(1, w.Confirm(1)); Assert.False(w.IsArmed(0)); Assert.Contains("T confirmed slot0 polls=0", log); }

    [Fact] public void Confirm_TwoPollsLate_ReportsTwo()
    { var (w, log) = Make(); w.Arm(0); w.Tick(); w.Tick(); Assert.Equal(1, w.Confirm(1)); Assert.Contains("T confirmed slot0 polls=2", log); }

    [Fact] public void Confirm_OnLastPoll_StillCounts()
    { var (w, _) = Make(polls: 4); w.Arm(0); for (int i = 0; i < 4; i++) { w.Tick(); } Assert.True(w.IsArmed(0)); Assert.Equal(1, w.Confirm(1)); }

    [Fact] public void Expiry_TracesAndConfirmsNothing()
    { var (w, log) = Make(polls: 4); w.Arm(0); for (int i = 0; i < 5; i++) { w.Tick(); } Assert.False(w.IsArmed(0)); Assert.Equal(0, w.Confirm(1)); Assert.Contains("T expired slot0", log); Assert.Equal(1, log.Count(l => l.Contains("expired"))); }

    [Fact] public void Confirm_OldestFirst()
    { var (w, _) = Make(); w.Arm(2); w.Tick(); w.Arm(0); Assert.Equal(1, w.Confirm(1)); Assert.False(w.IsArmed(2)); Assert.True(w.IsArmed(0)); }

    [Fact] public void Confirm_Ties_LowestSlotFirst()
    { var (w, _) = Make(); w.Arm(3); w.Arm(1); w.Confirm(1); Assert.False(w.IsArmed(1)); Assert.True(w.IsArmed(3)); }

    [Fact] public void Confirm_CountCapsAtArmed()
    { var (w, _) = Make(); w.Arm(0); w.Arm(1); Assert.Equal(2, w.Confirm(5)); Assert.Equal(0, w.Confirm(1)); }

    [Fact] public void Disarm_And_Clear_AreSilent()
    { var (w, log) = Make(); w.Arm(0); w.Disarm(0); w.Arm(1); w.Clear(); Assert.False(w.IsArmed(0)); Assert.False(w.IsArmed(1)); Assert.Empty(log); }

    [Fact] public void NullTrace_DoesNotThrow()
    { var w = new ConfirmationWindow(12, 2, "T"); w.Arm(0); w.Confirm(1); w.Arm(1); w.Tick(); w.Tick(); w.Tick(); }
}
