using System.Collections.Generic;
using System.Drawing;
using System.Xml;

using LiveSplit.SmwCounters.Snes;
using LiveSplit.UI;

namespace LiveSplit.SmwCounters.Counters;

internal enum MoonDedupeMode { All, PerLevel, PerRoom }

internal sealed class MoonCounter : ISmwCounter
{
    // SNES WRAM addresses (from kaizosplits Memory.cs).
    private const int MoonCounterOffset = 0x13C5; // # of 3-up moons collected, per scene
    private const int LevelNumOffset    = 0x13BF; // translevel number
    private const int RoomNumOffset     = 0x010B; // sublevel within current level
    private const int LevelStartOffset  = 0x1935; // in-level == 1 (kaizosplits InLevel)

    private static readonly Bitmap icon = IconLoader.Load("LiveSplit.SmwCounters.Assets.moon.png");

    private readonly PreviousByte previousMoon = new();

    // Keys (level, or level+room) where a moon has been counted this session.
    // Only used in PerLevel / PerRoom modes.
    private readonly HashSet<int> countedKeys = new();

    public string Id => "moons";
    public Image DefaultIcon => icon;
    public string DefaultLabel => "Moons";

    public int Value { get; private set; }

    public bool ValueIsAlert => false;

    public MoonDedupeMode DedupeMode { get; set; } = MoonDedupeMode.All;

    public void Reset()
    {
        Value = 0;
        countedKeys.Clear();
        previousMoon.Clear();
    }

    public void SetValue(int value) => Value = value;

    public void Poll(ISnesMemory memory)
    {
        if (!memory.IsAttached)
        {
            previousMoon.Clear();
            return;
        }

        // Only count while actually in a level. Outside a level (title,
        // file-select, overworld, load transitions) $13C5 holds transient data
        // whose changes fire spuriously — clear the baseline so re-entry
        // establishes a fresh one instead of registering an edge.
        if (!memory.ReadWramByte(LevelStartOffset, out byte levelStart) || levelStart != 1)
        {
            previousMoon.Clear();
            return;
        }

        if (!memory.ReadWramByte(MoonCounterOffset, out byte moon))
        {
            previousMoon.Clear();
            return;
        }

        if (previousMoon.HasPrevious && moon > previousMoon.Value)
        {
            if (DedupeMode == MoonDedupeMode.All)
            {
                Value++;
            }
            else
            {
                if (!memory.ReadWramByte(LevelNumOffset, out byte level)
                    || !memory.ReadWramByte(RoomNumOffset, out byte room))
                {
                    previousMoon.Clear();
                    return;
                }
                int key = DedupeMode == MoonDedupeMode.PerRoom ? ((level << 8) | room) : level;
                if (countedKeys.Add(key)) { Value++; }
            }
        }
        previousMoon.Set(moon);
    }

    public void SaveState(XmlDocument doc, XmlElement parent)
    {
        SettingsHelper.CreateSetting(doc, parent, "Moons", Value);
        SettingsHelper.CreateSetting(doc, parent, "DedupeMode", DedupeMode.ToString());
    }

    public void LoadState(XmlElement parent)
    {
        Value = SettingsHelper.ParseInt(parent["Moons"], 0);

        // Try new DedupeMode string first; fall back to legacy DedupePerRoom bool.
        XmlElement modeEl = parent["DedupeMode"];
        if (modeEl != null && System.Enum.TryParse(modeEl.InnerText, out MoonDedupeMode mode))
        {
            DedupeMode = mode;
        }
        else
        {
            DedupeMode = SettingsHelper.ParseBool(parent["DedupePerRoom"], false)
                ? MoonDedupeMode.PerRoom
                : MoonDedupeMode.PerLevel;
        }

        countedKeys.Clear();
        previousMoon.Clear();
    }
}
