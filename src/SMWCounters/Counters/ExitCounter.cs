using System.Drawing;
using System.Xml;

using LiveSplit.SmwCounters.Snes;
using LiveSplit.UI;

namespace LiveSplit.SmwCounters.Counters;

internal sealed class ExitCounter : ISmwCounter
{
    // SNES WRAM $0DD5 (exitMode): the end-of-level exit type. It shifts to a
    // non-zero, non-128 value exactly when a level exit is completed. Ported
    // from kaizosplits Watchers.cs (ToExit => Shifted(exitMode) && curr != 0
    // && curr != 128). Event-based, so loading a save (which populates the
    // $1F2E ExitsCompleted byte) does not fire it — unlike the old $1F2E watch.
    private const int ExitModeOffset = 0x0DD5;

    private static readonly Bitmap icon = IconLoader.Load("LiveSplit.SmwCounters.Assets.exit.png");

    private readonly PreviousByte previousExitMode = new();

    public string Id => "exits";
    public Image DefaultIcon => icon;
    public string DefaultLabel => "Exits";

    public int Value { get; private set; }

    public void Reset()
    {
        Value = 0;
        previousExitMode.Clear();
    }

    public void Poll(ISnesMemory memory)
    {
        if (!memory.IsAttached)
        {
            previousExitMode.Clear();
            return;
        }

        if (!memory.ReadWramByte(ExitModeOffset, out byte exitMode))
        {
            previousExitMode.Clear();
            return;
        }

        if (previousExitMode.HasPrevious
            && exitMode != previousExitMode.Value
            && exitMode != 0
            && exitMode != 128)
        {
            Value++;
        }
        previousExitMode.Set(exitMode);
    }

    public void SaveState(XmlDocument doc, XmlElement parent)
    {
        SettingsHelper.CreateSetting(doc, parent, "Exits", Value);
    }

    public void LoadState(XmlElement parent)
    {
        Value = SettingsHelper.ParseInt(parent["Exits"], 0);
        previousExitMode.Clear();
    }
}
