using System.Drawing;
using System.Xml;

using LiveSplit.SmwCounters.Snes;
using LiveSplit.UI;

namespace LiveSplit.SmwCounters.Counters;

// Counts player-initiated jumps by edge-detecting Mario's in-air state.
//
// Algorithm: count when the previous poll had Mario on the ground AND the
// current poll has him in a rising state.
//
//   $7E:0072 (PlayerInAir):     $00 on ground, $0B rising, $0C P-speed
//                               rising, $24 falling.
//   $7E:0077 (PlayerBlockedDir): bit 2 ($04) = blocked below (= touching
//                               ground/solid). Used as a gate against the
//                               bounce-off-enemy routine, which briefly
//                               writes $0072 = $00 then $0B if A/B is held
//                               — a poller can sample that intermediate
//                               frame and would false-positive a Koopa
//                               bounce without the blocked-below gate.
//
// Naturally excludes:
//   - running off a ledge ($0072 goes $00 → $24, not to $0B)
//   - re-pressing B/A in the air (no rising-edge from on-ground)
//   - bouncing off enemies (blocked-below bit clear, fails the gate)
// Includes (correctly per spec): jumping out of water, jumping off Yoshi.
//
// Edge cases not yet addressed: spring/note-block bounces (they do come off
// ground), romhack wall jumps / vine-jumps / P-balloon flight.
//
// Source: SMWDisX rammap.asm (PlayerInAir, PlayerBlockedDir) and bank_01
// bounce routine.
internal sealed class JumpCounter : ISmwCounter
{
    private const int PlayerInAirOffset = 0x0072;
    private const int BlockedDirOffset  = 0x0077;

    private const byte OnGround     = 0x00;
    private const byte AirRising    = 0x0B;
    private const byte AirRisingP   = 0x0C;
    private const byte BlockedBelow = 0x04;

    private static readonly Bitmap icon = IconLoader.Load("LiveSplit.SmwCounters.Assets.jump.png");

    private readonly PreviousByte previousAir = new();
    private readonly PreviousByte previousBlocked = new();

    public string Id => "jumps";
    public Image DefaultIcon => icon;
    public string DefaultLabel => "Jumps";

    public int Value { get; private set; }

    public bool ValueIsAlert => false;

    public void Reset()
    {
        Value = 0;
        previousAir.Clear();
        previousBlocked.Clear();
    }

    public void Poll(ISnesMemory memory)
    {
        if (!memory.IsAttached)
        {
            previousAir.Clear();
            previousBlocked.Clear();
            return;
        }

        if (!memory.ReadWramByte(PlayerInAirOffset, out byte air)
            || !memory.ReadWramByte(BlockedDirOffset, out byte blocked))
        {
            previousAir.Clear();
            previousBlocked.Clear();
            return;
        }

        if (previousAir.HasPrevious && previousBlocked.HasPrevious)
        {
            bool wasOnGround = previousAir.Value == OnGround
                && (previousBlocked.Value & BlockedBelow) != 0;
            bool isRising = air == AirRising || air == AirRisingP;
            if (wasOnGround && isRising) { Value++; }
        }

        previousAir.Set(air);
        previousBlocked.Set(blocked);
    }

    public void SaveState(XmlDocument doc, XmlElement parent)
    {
        SettingsHelper.CreateSetting(doc, parent, "Jumps", Value);
    }

    public void LoadState(XmlElement parent)
    {
        Value = SettingsHelper.ParseInt(parent["Jumps"], 0);
        previousAir.Clear();
        previousBlocked.Clear();
    }
}
