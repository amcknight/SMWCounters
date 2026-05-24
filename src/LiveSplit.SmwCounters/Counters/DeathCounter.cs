using System.Drawing;
using System.Xml;

using LiveSplit.SmwCounters.Snes;
using LiveSplit.UI;

namespace LiveSplit.SmwCounters.Counters;

internal sealed class DeathCounter : ISmwCounter
{
    // SNES address $7E:0071 — Mario player animation. 0x09 == "dying".
    // Source: kaizosplits Watchers.cs (DiedNow => ShiftTo(playerAnimation, 9)).
    private const int PlayerAnimationOffset = 0x71;
    private const byte DyingValue = 0x09;

    private static readonly Bitmap icon = IconLoader.Load("LiveSplit.SmwCounters.Assets.death.png");

    private readonly PreviousByte previousAnimation = new();

    public string Id => "deaths";
    public Image DefaultIcon => icon;
    public string DefaultLabel => "Deaths";

    public int Value { get; private set; }

    public void Reset()
    {
        Value = 0;
        previousAnimation.Clear();
    }

    public void Poll(ISnesMemory memory)
    {
        if (!memory.IsAttached)
        {
            previousAnimation.Clear();
            return;
        }

        if (!memory.ReadWramByte(PlayerAnimationOffset, out byte anim))
        {
            previousAnimation.Clear();
            return;
        }

        if (previousAnimation.HasPrevious && previousAnimation.Value != DyingValue && anim == DyingValue)
        {
            Value++;
        }
        previousAnimation.Set(anim);
    }

    public void SaveState(XmlDocument doc, XmlElement parent)
    {
        SettingsHelper.CreateSetting(doc, parent, "Deaths", Value);
    }

    public void LoadState(XmlElement parent)
    {
        Value = SettingsHelper.ParseInt(parent["Deaths"], 0);
        previousAnimation.Clear();
    }
}
