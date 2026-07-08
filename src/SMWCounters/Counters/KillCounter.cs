using System.Drawing;
using System.Xml;

using LiveSplit.SmwCounters.Snes;
using LiveSplit.UI;

namespace LiveSplit.SmwCounters.Counters;

// Counts enemy kills by edge-detecting each sprite slot's status transitioning
// from a live state into the "dead" set. The 12-byte sprite-status table lives
// at $14C8 ($14C8..$14D3, one byte per slot).
//
//   $7E:0100 (GameMode):      $14 = level main routine. Gate to this so
//                             overworld / load-time garbage in the status table
//                             doesn't count.
//   $7E:14C8[i] (SpriteStatus): per-slot status. Dead set = 02 (killed, falling
//                             off screen), 03 (smushed), 04 (spinjumped),
//                             05 (lava/mud).
//
// Rule: count once per slot when the previous sample was NOT in the dead set and
// not empty ($00), and the current sample IS in the dead set. Treating the dead
// set as one absorbing region means shuffling within it (e.g. 04 -> 02) does not
// double-count, and offscreening (status -> 00) is naturally excluded. Only $00
// is excluded as a "from" state; $01 (slot taken, uninitialized) is intentionally
// countable, though a $01 -> dead transition is near-impossible in practice.
//
// v1 deliberately does no sprite-ID filtering: a P-switch press can register as
// 03 and will count. This is an observation instrument; filtering (if any) waits
// on real-play data. Goal-tape conversions (06/0C) and Yoshi's mouth (07) are
// excluded only by virtue of not being in the dead set.
internal sealed class KillCounter : ISmwCounter
{
    private const int GameModeOffset = 0x0100;
    private const byte LevelMainMode = 0x14;
    private const int SpriteStatusBase = 0x14C8;
    private const int SlotCount = 12;

    private static readonly Bitmap icon = IconLoader.Load("LiveSplit.SmwCounters.Assets.kill.png");

    private readonly PreviousByte[] previousStatus;

    public KillCounter()
    {
        previousStatus = new PreviousByte[SlotCount];
        for (int i = 0; i < SlotCount; i++) { previousStatus[i] = new PreviousByte(); }
    }

    public string Id => "kills";
    public Image DefaultIcon => icon;
    public string DefaultLabel => "Kills";

    public int Value { get; private set; }

    public bool ValueIsAlert => false;

    public void Reset()
    {
        Value = 0;
        ClearAll();
    }

    public void SetValue(int value) => Value = value;

    public void Poll(ISnesMemory memory)
    {
        if (!memory.IsAttached)
        {
            ClearAll();
            return;
        }

        if (!memory.ReadWramByte(GameModeOffset, out byte gameMode) || gameMode != LevelMainMode)
        {
            ClearAll();
            return;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (!memory.ReadWramByte(SpriteStatusBase + i, out byte status))
            {
                previousStatus[i].Clear();
                continue;
            }

            PreviousByte prev = previousStatus[i];
            if (prev.HasPrevious && prev.Value != 0x00 && !IsDead(prev.Value) && IsDead(status))
            {
                Value++;
            }

            prev.Set(status);
        }
    }

    private static bool IsDead(byte status) => status >= 0x02 && status <= 0x05;

    private void ClearAll()
    {
        for (int i = 0; i < SlotCount; i++) { previousStatus[i].Clear(); }
    }

    public void SaveState(XmlDocument doc, XmlElement parent)
    {
        SettingsHelper.CreateSetting(doc, parent, "Kills", Value);
    }

    public void LoadState(XmlElement parent)
    {
        Value = SettingsHelper.ParseInt(parent["Kills"], 0);
        ClearAll();
    }
}
