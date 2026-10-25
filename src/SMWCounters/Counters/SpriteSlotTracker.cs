using LiveSplit.SmwCounters.Snes;

namespace LiveSplit.SmwCounters.Counters;

// One poll's snapshot of the 12 sprite slots (status table $14C8, sprite
// number table $9E) plus the previous poll's values, so counters can detect
// per-slot edges without each keeping their own arrays. A slot whose bytes
// can't both be read is not Ok and forgets its previous, so a counter never
// sees a vanish edge across a failed read.
internal sealed class SpriteSlotTracker
{
    public const int SlotCount = 12;
    private const int StatusBase = 0x14C8;
    private const int SpriteBase = 0x009E;

    private readonly byte[] status = new byte[SlotCount];
    private readonly byte[] sprite = new byte[SlotCount];
    private readonly bool[] ok = new bool[SlotCount];
    private readonly PreviousByte[] prevStatus = NewPrevious();
    private readonly PreviousByte[] prevSprite = NewPrevious();

    public void Read(ISnesMemory memory)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            ok[i] = memory.ReadWramByte(StatusBase + i, out status[i])
                & memory.ReadWramByte(SpriteBase + i, out sprite[i]);
            if (!ok[i]) ClearSlot(i);
        }
    }

    public bool Ok(int slot) => ok[slot];
    public byte Status(int slot) => status[slot];
    public byte Sprite(int slot) => sprite[slot];
    public bool HasPrevious(int slot) => prevStatus[slot].HasPrevious && prevSprite[slot].HasPrevious;
    public byte PreviousStatus(int slot) => prevStatus[slot].Value;
    public byte PreviousSprite(int slot) => prevSprite[slot].Value;

    public void Commit()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (!ok[i]) continue;
            prevStatus[i].Set(status[i]);
            prevSprite[i].Set(sprite[i]);
        }
    }

    public void ClearSlot(int slot)
    {
        prevStatus[slot].Clear();
        prevSprite[slot].Clear();
    }

    public void Clear()
    {
        for (int i = 0; i < SlotCount; i++) ClearSlot(i);
    }

    private static PreviousByte[] NewPrevious()
    {
        var array = new PreviousByte[SlotCount];
        for (int i = 0; i < SlotCount; i++) array[i] = new PreviousByte();
        return array;
    }
}
