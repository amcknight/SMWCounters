using System.Collections.Generic;
using System.Drawing;
using System.Xml;

using LiveSplit.SmwCounters.Snes;
using LiveSplit.UI;

namespace LiveSplit.SmwCounters.Counters;

internal enum KillCountMode { Kills, Destruction }

// Dual-tally counter over the SMW sprite tables ($14C8 status, $9E sprite
// number, 12 slots). Both tallies are always computed; Mode selects which one
// Value displays.
//
//   Kills:       creatures killed. Dead-set entries, live eats, and collected
//                fireball coins — all gated by the creature filter below.
//                Design priority: err toward NOT counting.
//   Destruction: anything destroyed. Same events without the filter, except
//                the goal tape's self-conversion (#7B -> 06) which destroys
//                nothing.
//
// Dead set = {02 killed/falling, 03 smushed, 04 spinjumped, 05 lava, 06
// goal-tape coin}. Status 07 (Yoshi's mouth) is deliberately NOT in the dead
// set: it is reversible (spit) and is handled by the mouth rules (E2-E5).
//
// Creature filter (evidence-driven, from the 2026-07-14 log session — see the
// v2 spec): a sprite is "not alive" if its ID is on the observed-offender
// list, or if it is a koopa ID (04-07, shared by bare shells) dying from a
// carryable state instead of the normal routine (08).
internal sealed class KillCounter : ISmwCounter
{
    private const int GameModeOffset = 0x0100;
    private const byte LevelMainMode = 0x14;
    private const int SpriteStatusBase = 0x14C8;
    private const int SpriteNumberBase = 0x009E;
    private const int SlotCount = 12;
    private const int CoinCountOffset = 0x0DBF;
    private const byte GoalTapeSprite = 0x7B;
    private const byte MovingCoinSprite = 0x21;

    private static readonly Bitmap icon = IconLoader.Load("LiveSplit.SmwCounters.Assets.kill.png");

    // Observed to enter the dead set without being creatures:
    // 1B football, 21 moving coin, 2F springboard, 3E P-switch, 48 Diggin'
    // Chuck's rock, 53 throw block, 78 1-up, 7B goal tape, B9 message box,
    // C8 accordion block. (0x4B was listed here as "chuck rock" from the
    // 07-14 session; the 07-16 session proved 0x4B is the pipe-dwelling
    // Lakitu — a creature — and the rock is 0x48.)
    private static readonly HashSet<byte> NotAlive = new()
    {
        0x1B, 0x21, 0x2F, 0x3E, 0x48, 0x53, 0x78, 0x7B, 0xB9, 0xC8,
    };

    // Mouth-entry classification per slot: set when a sprite enters status 07,
    // cleared when it leaves (swallow, spit) or the slot is cleared. Decides
    // whether a swallow (07 -> 00) credits Destruction (item) or nothing
    // (creature already counted at the eat; unknown entry counts nothing —
    // conservative per the design priority).
    private enum MouthEntry : byte { None, Creature, Item }

    private readonly PreviousByte[] prevStatus;
    private readonly PreviousByte[] prevSprite;
    private readonly MouthEntry[] mouthEntry = new MouthEntry[SlotCount];

    // Set when a creature was fireball-converted to a moving coin in this
    // slot; resolved by the coin's clean despawn, cancelled by anything else
    // (tongue grab, slot reuse). Conservative: when unsure, no kill.
    private readonly bool[] pendingCoin = new bool[SlotCount];

    // Polls remaining in which a coin-counter change still proves the pending
    // coin that despawned from this slot was collected. The despawn edge and
    // the $0DBF change routinely straddle a poll boundary (WRAM reads are not
    // frame-atomic), so requiring same-poll coincidence dropped most kills.
    private readonly int[] coinWindow = new int[SlotCount];

    // Polls after a pending coin's despawn during which a coin-counter change
    // still counts as its collection. Covers observed one-poll straddles with
    // margin, while staying short enough (~65 ms at 60 Hz polling) that an
    // unrelated coin grab rarely lands inside it.
    private const int CoinKillWindowPolls = 4;

    private readonly PreviousByte prevCoins = new();

    private int kills;
    private int destruction;

    public KillCounter()
    {
        prevStatus = new PreviousByte[SlotCount];
        prevSprite = new PreviousByte[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            prevStatus[i] = new PreviousByte();
            prevSprite[i] = new PreviousByte();
        }
    }

    public string Id => "kills";
    public Image DefaultIcon => icon;
    public string DefaultLabel => "Kills";

    // Display selector: both tallies are always maintained; the radio in
    // settings flips this to choose which one renders. Not a behavior switch.
    public KillCountMode Mode { get; set; } = KillCountMode.Kills;

    public int Value => Mode == KillCountMode.Kills ? kills : destruction;

    // Layout-dirty hash input: both tallies and the mode are persisted, so all
    // three must influence the settings hash even while only one displays.
    internal int StateHash => kills ^ (destruction * 397) ^ (int)Mode;

    public bool ValueIsAlert => false;

    public void SetValue(int value)
    {
        if (Mode == KillCountMode.Kills) { kills = value; }
        else { destruction = value; }
    }

    public void Reset()
    {
        kills = 0;
        destruction = 0;
        ClearAll();
    }

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

        // "Changed", not "increased": the counter wraps 99 -> 0 on the coin
        // that awards a life.
        bool coinsChanged = false;
        if (memory.ReadWramByte(CoinCountOffset, out byte coins))
        {
            coinsChanged = prevCoins.HasPrevious && coins != prevCoins.Value;
            prevCoins.Set(coins);
        }
        else
        {
            prevCoins.Clear();
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (!memory.ReadWramByte(SpriteStatusBase + i, out byte status)
                || !memory.ReadWramByte(SpriteNumberBase + i, out byte sprite))
            {
                ClearSlot(i);
                continue;
            }
            PollSlot(i, status, sprite);
        }

        // Resolve despawned pending coins: one coin-counter change proves one
        // collection (credit the oldest open window only), then age the rest.
        if (coinsChanged)
        {
            int oldest = -1;
            for (int i = 0; i < SlotCount; i++)
            {
                if (coinWindow[i] > 0 && (oldest < 0 || coinWindow[i] < coinWindow[oldest]))
                {
                    oldest = i;
                }
            }
            if (oldest >= 0)
            {
                kills++;
                coinWindow[oldest] = 0;
            }
        }
        for (int i = 0; i < SlotCount; i++)
        {
            if (coinWindow[i] > 0) { coinWindow[i]--; }
        }
    }

    private void PollSlot(int i, byte status, byte sprite)
    {
        PreviousByte pStat = prevStatus[i];
        PreviousByte pSpr = prevSprite[i];
        if (!pStat.HasPrevious || !pSpr.HasPrevious)
        {
            pStat.Set(status);
            pSpr.Set(sprite);
            return;
        }

        byte prevStat = pStat.Value;
        byte prevSpr = pSpr.Value;
        bool liveOrigin = prevStat != 0x00 && prevStat != 0x07 && !IsDead(prevStat);

        // E6: fireball conversion — the sprite number flips to the moving coin
        // while the slot stays in the normal routine. Destruction immediately;
        // the *kill* waits for proof of collection (uncollected coins let the
        // enemy respawn).
        if (prevStat == 0x08 && status == 0x08
            && prevSpr != MovingCoinSprite && sprite == MovingCoinSprite)
        {
            if (IsCreature(prevSpr, prevStat))
            {
                destruction++;
                pendingCoin[i] = true;
            }
        }
        else if (pendingCoin[i])
        {
            // E7: a clean despawn of the still-coin slot opens a short window
            // in which a coin-counter change counts it as a collected kill
            // (resolved centrally in Poll). Anything else — tongue grab, slot
            // reuse, odd status — cancels the pending kill.
            if (sprite == MovingCoinSprite && status == 0x00)
            {
                pendingCoin[i] = false;
                coinWindow[i] = CoinKillWindowPolls + 1;
            }
            else if (sprite != MovingCoinSprite || status != 0x08)
            {
                pendingCoin[i] = false;
            }
        }

        if (prevStat == 0x07 && status != 0x07)
        {
            // E4 (swallow) / E5 (spit): leaving the mouth either way clears
            // the recorded entry. Only a swallowed *item* adds (Destruction);
            // a creature was already counted when it was eaten.
            if (status == 0x00 && mouthEntry[i] == MouthEntry.Item) { destruction++; }
            mouthEntry[i] = MouthEntry.None;
        }
        else if (status == 0x07 && prevStat != 0x07 && liveOrigin)
        {
            // E2 (creature eaten) / E3 (item pickup). Classified by the
            // creature filter, NOT origin status: items can idle at 08 too
            // (springboard), so the ID list is the only reliable separator.
            if (IsCreature(sprite, prevStat))
            {
                kills++;
                destruction++;
                mouthEntry[i] = MouthEntry.Creature;
            }
            else
            {
                mouthEntry[i] = MouthEntry.Item;
            }
        }
        else if (liveOrigin && IsDead(status))
        {
            // E1: dead-set entry from a live origin. Note: a pending fireball
            // coin (E6 already counted Destruction) entering the dead set
            // instead of despawning cleanly is unobserved in evidence (coins
            // are seen to end 08 -> 00); if it happened, Destruction would be
            // double-counted here on top of E6 — accepted as Kills-safe.
            if (IsCreature(sprite, prevStat)) { kills++; }
            if (!(sprite == GoalTapeSprite && status == 0x06)) { destruction++; }
        }

        pStat.Set(status);
        pSpr.Set(sprite);
    }

    // "Alive" gate for the Kills tally: observed-offender IDs never count, and
    // koopa IDs (04-07, shared with their bare shells) only count when the
    // origin was the normal routine (08) — a living koopa, not a shell.
    private static bool IsCreature(byte sprite, byte originStatus)
    {
        if (NotAlive.Contains(sprite)) { return false; }
        if (sprite >= 0x04 && sprite <= 0x07 && originStatus != 0x08) { return false; }
        return true;
    }

    private static bool IsDead(byte status) => status >= 0x02 && status <= 0x06;

    private void ClearSlot(int i)
    {
        prevStatus[i].Clear();
        prevSprite[i].Clear();
        mouthEntry[i] = MouthEntry.None;
        pendingCoin[i] = false;
        coinWindow[i] = 0;
    }

    private void ClearAll()
    {
        for (int i = 0; i < SlotCount; i++) { ClearSlot(i); }
        prevCoins.Clear();
    }

    public void SaveState(XmlDocument doc, XmlElement parent)
    {
        SettingsHelper.CreateSetting(doc, parent, "Kills", kills);
        SettingsHelper.CreateSetting(doc, parent, "Destruction", destruction);
        SettingsHelper.CreateSetting(doc, parent, "KillMode", Mode.ToString());
    }

    public void LoadState(XmlElement parent)
    {
        kills = SettingsHelper.ParseInt(parent["Kills"], 0);
        destruction = SettingsHelper.ParseInt(parent["Destruction"], 0);

        XmlElement modeEl = parent["KillMode"];
        Mode = modeEl != null && System.Enum.TryParse(modeEl.InnerText, out KillCountMode mode)
            ? mode
            : KillCountMode.Kills;

        ClearAll();
    }
}
