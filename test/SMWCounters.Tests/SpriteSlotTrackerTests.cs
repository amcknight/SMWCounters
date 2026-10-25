using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

public class SpriteSlotTrackerTests
{
    private const int StatusBase = 0x14C8, SpriteBase = 0x009E;
    private static void Set(FakeSnesMemory m, int slot, byte status, byte sprite)
    { m.SetByte(StatusBase + slot, status); m.SetByte(SpriteBase + slot, sprite); }

    [Fact] public void FirstRead_IsOk_HasNoPrevious()
    { var t = new SpriteSlotTracker(); var m = new FakeSnesMemory(); Set(m, 0, 0x08, 0x78); t.Read(m);
      Assert.True(t.Ok(0)); Assert.Equal(0x08, t.Status(0)); Assert.Equal(0x78, t.Sprite(0)); Assert.False(t.HasPrevious(0)); }

    [Fact] public void Commit_ThenRead_ExposesPrevious()
    { var t = new SpriteSlotTracker(); var m = new FakeSnesMemory(); Set(m, 0, 0x08, 0x78); t.Read(m); t.Commit();
      Set(m, 0, 0x00, 0x78); t.Read(m);
      Assert.True(t.HasPrevious(0)); Assert.Equal(0x08, t.PreviousStatus(0)); Assert.Equal(0x78, t.PreviousSprite(0)); Assert.Equal(0x00, t.Status(0)); }

    [Fact] public void UnreadableSlot_IsNotOk_AndForgetsPrevious()
    { var t = new SpriteSlotTracker(); var m = new FakeSnesMemory(); Set(m, 0, 0x08, 0x78); t.Read(m); t.Commit();
      var empty = new FakeSnesMemory(); t.Read(empty);
      Assert.False(t.Ok(0)); Assert.False(t.HasPrevious(0)); Assert.False(t.Ok(5)); }

    [Fact] public void StatusOnly_IsNotOk()
    { var t = new SpriteSlotTracker(); var m = new FakeSnesMemory(); m.SetByte(StatusBase, 0x08); t.Read(m); Assert.False(t.Ok(0)); }

    [Fact] public void ClearSlot_And_Clear_ForgetPrevious()
    { var t = new SpriteSlotTracker(); var m = new FakeSnesMemory(); Set(m, 0, 0x08, 0x78); Set(m, 1, 0x08, 0x0F); t.Read(m); t.Commit();
      t.ClearSlot(0); Assert.False(t.HasPrevious(0)); Assert.True(t.HasPrevious(1)); t.Clear(); Assert.False(t.HasPrevious(1)); }
}
