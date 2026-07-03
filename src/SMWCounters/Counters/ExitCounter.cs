using System.Drawing;
using System.Xml;

using LiveSplit.SmwCounters.Snes;
using LiveSplit.UI;

namespace LiveSplit.SmwCounters.Counters;

internal sealed class ExitCounter : ISmwCounter
{
    // SNES WRAM $1F2E (ExitsCompleted): single byte, 0–96. Incremented in
    // bank_04 when the overworld event-process routine resolves a goal or
    // secret-goal event — the cleanest "exit was banked into the save file"
    // signal in the game. NOT to be confused with $1FE2, which is sprite
    // slot 0 of SpriteMisc1FE2 and ticks on sprite spawns (goal-tape
    // sequence spawns several sprites, which previously caused +4 per exit).
    // Source: SMWDisX rammap.asm + bank_04.asm `INC.W ExitsCompleted`.
    private const int ExitCountOffset = 0x1F2E;

    private static readonly Bitmap icon = IconLoader.Load("LiveSplit.SmwCounters.Assets.exit.png");

    private readonly PreviousByte previousExits = new();

    public string Id => "exits";
    public Image DefaultIcon => icon;
    public string DefaultLabel => "Exits";

    public int Value { get; private set; }

    public void Reset()
    {
        Value = 0;
        previousExits.Clear();
    }

    public void Poll(ISnesMemory memory)
    {
        if (!memory.IsAttached)
        {
            previousExits.Clear();
            return;
        }

        if (!memory.ReadWramByte(ExitCountOffset, out byte exits))
        {
            previousExits.Clear();
            return;
        }

        if (previousExits.HasPrevious && exits > previousExits.Value)
        {
            Value++;
        }
        previousExits.Set(exits);
    }

    public void SaveState(XmlDocument doc, XmlElement parent)
    {
        SettingsHelper.CreateSetting(doc, parent, "Exits", Value);
    }

    public void LoadState(XmlElement parent)
    {
        Value = SettingsHelper.ParseInt(parent["Exits"], 0);
        previousExits.Clear();
    }
}
