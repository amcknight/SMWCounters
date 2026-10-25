using System.Collections.Generic;

using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

public class OneUpCounterTests
{
    private const int GameMode = 0x0100, StatusBase = 0x14C8, SpriteBase = 0x009E, Score = 0x16E1,
                      Lakitu151C = 0x151C, Anim = 0x0071, Midway = 0x13CE;
    private const byte Level = 0x14, Overworld = 0x0E, Alive = 0x08, Mouth = 0x07, Goal = 0x0C, Gone = 0x00,
                       OneUp = 0x78, Golden = 0x7F, Lakitu = 0x1E, Galoomba = 0x0F, Text1Up = 0x0D, Text2Up = 0x0E, NoText = 0x00;

    // Slot 0 plus score-sprite slot 0; other slots unset (reads fail).
    private static void Poll(OneUpCounter c, FakeSnesMemory m, byte status, byte sprite, byte score,
                             byte mode = Level, byte anim = 0, byte midway = 0)
    {
        m.SetByte(GameMode, mode); m.SetByte(StatusBase, status); m.SetByte(SpriteBase, sprite);
        m.SetByte(Score, score); m.SetByte(Anim, anim); m.SetByte(Midway, midway);
        c.Poll(m);
    }

    [Fact] public void Collect_ScoreSpriteSamePoll_Counts()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Gone, OneUp, Text1Up);
      Assert.Equal(1, c.Value); Assert.True(c.ValueIsAlert); }

    [Theory] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Collect_ScoreSpriteUpToThreePollsLate_Counts(int late)
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Gone, OneUp, NoText);
      for (int i = 1; i < late; i++) { Poll(c, m, Gone, OneUp, NoText); }
      Poll(c, m, Gone, OneUp, Text1Up); Assert.Equal(1, c.Value); }

    [Fact] public void Collect_ScoreSpriteFourPollsLate_DoesNotCount()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Gone, OneUp, NoText);
      for (int i = 0; i < 3; i++) { Poll(c, m, Gone, OneUp, NoText); }
      Poll(c, m, Gone, OneUp, Text1Up); Assert.Equal(0, c.Value); }

    [Fact] public void Vanish_NoScoreSprite_NoCount()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Gone, OneUp, NoText); Poll(c, m, Gone, OneUp, NoText);
      Assert.Equal(0, c.Value); }

    [Fact] public void ScoreSprite_NoVanish_ThenLaterVanish_NoCount()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Alive, OneUp, Text1Up);   // e.g. fifth Dragon Coin
      Poll(c, m, Alive, OneUp, Text1Up); Poll(c, m, Gone, OneUp, Text1Up);   // mushroom scrolls off, text unchanged
      Assert.Equal(0, c.Value); }

    [Fact] public void FlyingGoldenMushroom_Counts()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, Golden, NoText); Poll(c, m, Gone, Golden, Text1Up); Assert.Equal(1, c.Value); }

    [Fact] public void GoalTapeStatus0C_Counts()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Goal, OneUp, NoText); Poll(c, m, Gone, OneUp, Text2Up); Assert.Equal(1, c.Value); }

    [Fact] public void MouthStatus_SwallowWithText_Counts_WithoutText_DoesNot()
    { var a = new OneUpCounter(); var ma = new FakeSnesMemory();
      Poll(a, ma, Mouth, OneUp, NoText); Poll(a, ma, Gone, OneUp, Text1Up); Assert.Equal(1, a.Value);
      var b = new OneUpCounter(); var mb = new FakeSnesMemory();
      Poll(b, mb, Mouth, OneUp, NoText); Poll(b, mb, Gone, OneUp, NoText); Poll(b, mb, Gone, OneUp, NoText); Assert.Equal(0, b.Value); }

    [Fact] public void DeadSetTransition_IsNotACandidate()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, 0x05, OneUp, Text1Up); Assert.Equal(0, c.Value); }

    [Fact] public void SlotReusedInsideOneGap_Counts()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Alive, Galoomba, Text1Up); Assert.Equal(1, c.Value); }

    [Fact] public void Lakitu_FishingFlagDrops_WithText_Counts()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      m.SetByte(Lakitu151C, 0x10); Poll(c, m, Alive, Lakitu, NoText);
      m.SetByte(Lakitu151C, 0x00); Poll(c, m, Alive, Lakitu, Text1Up); Assert.Equal(1, c.Value); }

    [Fact] public void Lakitu_FishingFlagDrops_NoText_NoCount()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      m.SetByte(Lakitu151C, 0x10); Poll(c, m, Alive, Lakitu, NoText);
      m.SetByte(Lakitu151C, 0x00); Poll(c, m, Alive, Lakitu, NoText);
      for (int i = 0; i < 4; i++) { Poll(c, m, Alive, Lakitu, NoText); } Assert.Equal(0, c.Value); }

    [Fact] public void Lakitu_FlagAlreadyZero_NoCandidate()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      m.SetByte(Lakitu151C, 0x00); Poll(c, m, Alive, Lakitu, NoText); Poll(c, m, Alive, Lakitu, Text1Up); Assert.Equal(0, c.Value); }

    [Fact] public void TwoVanishes_TwoTexts_SamePoll_CountsTwo()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      m.SetByte(StatusBase + 1, Alive); m.SetByte(SpriteBase + 1, OneUp); m.SetByte(Score + 1, NoText);
      Poll(c, m, Alive, OneUp, NoText);
      m.SetByte(StatusBase + 1, Gone); m.SetByte(Score + 1, Text1Up);
      Poll(c, m, Gone, OneUp, Text1Up); Assert.Equal(2, c.Value); }

    [Fact] public void ConsecutiveCollects_EachCount()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Gone, OneUp, Text1Up);
      Poll(c, m, Alive, OneUp, Text1Up); Poll(c, m, Gone, OneUp, Text2Up);   // slot reused by the LRU text table
      Assert.Equal(2, c.Value); }

    [Fact] public void GameModeGate_DropsArmedWindow()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Gone, OneUp, NoText);          // arms
      Poll(c, m, Gone, OneUp, NoText, mode: Overworld);                          // gate closes with the window armed
      Poll(c, m, Gone, OneUp, NoText, mode: Level);                              // baselines the score table
      Poll(c, m, Gone, OneUp, Text1Up); Assert.Equal(0, c.Value); }

    [Fact] public void ReadFailure_ThenReturn_NoCandidate()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText);
      var blank = new FakeSnesMemory(); blank.SetByte(GameMode, Level); blank.SetByte(Score, NoText); c.Poll(blank);   // slot unreadable, score previous survives
      Poll(c, m, Gone, OneUp, Text1Up); Assert.Equal(0, c.Value); }

    [Fact] public void Detach_ClearsState()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); m.Attached = false; c.Poll(m); m.Attached = true;
      Poll(c, m, Gone, OneUp, NoText);    // would arm if the slot were remembered
      Poll(c, m, Gone, OneUp, Text1Up);   // would confirm
      Assert.Equal(0, c.Value); }

    [Fact] public void DiscardOff_DeathKeepsTheCount()
    { var c = new OneUpCounter { Banked = false }; var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Gone, OneUp, Text1Up);
      Assert.Equal(1, c.Value); Assert.False(c.ValueIsAlert);
      Poll(c, m, Gone, OneUp, Text1Up, anim: 9); Assert.Equal(1, c.Value); }

    [Fact] public void DiscardOn_DeathReverts_MidwayBanks()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Gone, OneUp, Text1Up); Assert.True(c.ValueIsAlert);
      Poll(c, m, Gone, OneUp, Text1Up, anim: 9); Assert.Equal(0, c.Value);
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Gone, OneUp, Text2Up); Poll(c, m, Gone, OneUp, Text2Up, midway: 1);
      Assert.False(c.ValueIsAlert); Poll(c, m, Gone, OneUp, Text2Up, anim: 9); Assert.Equal(1, c.Value); }

    [Fact] public void DiscardOn_LeavingToTheMap_Banks_ThenALaterDeathCannotTakeIt()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory(); m.SetByte(0x010B, 0x1E);
      Poll(c, m, Alive, OneUp, NoText, mode: Overworld);
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Gone, OneUp, Text1Up);
      Assert.Equal(1, c.Value); Assert.True(c.ValueIsAlert);
      Poll(c, m, Gone, OneUp, Text1Up, mode: Overworld);          // start+select / side exit / pipe back to the map
      Assert.False(c.ValueIsAlert);
      Poll(c, m, Gone, OneUp, Text1Up); Poll(c, m, Gone, OneUp, Text1Up, anim: 9);   // die in the next level
      Assert.Equal(1, c.Value); }

    [Fact] public void DiscardOn_ExitEvent_Banks()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory(); m.SetByte(0x0DD5, 0x00);
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Gone, OneUp, Text1Up);
      Assert.True(c.ValueIsAlert);
      m.SetByte(0x0DD5, 0x01); Poll(c, m, Gone, OneUp, Text1Up, mode: 0x0C);   // goal exit: $0DD5 0->1 lands at mode 0C
      Assert.False(c.ValueIsAlert); }

    [Fact] public void Trace_EmitsSeenOnceBeforeCandidate()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory(); var log = new List<string>();
      ((ITraceSource)c).Trace = log.Add;
      m.SetByte(0x161A, 0x21); m.SetByte(0x1540, 0x32); m.SetByte(0x1534, 0x43);
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Gone, OneUp, Text1Up);
      Assert.Single(log, l => l.StartsWith("1UP seen slot0 #78 idx="));
      Assert.Contains("1UP seen slot0 #78 idx=21 rise=32 drop=43", log);
      int seen = log.FindIndex(l => l.StartsWith("1UP seen slot0"));
      int candidate = log.FindIndex(l => l.StartsWith("1UP candidate slot0"));
      Assert.True(candidate >= 0 && seen < candidate); }

    [Fact] public void Trace_EmitsCandidateAndConfirmation()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory(); var log = new List<string>();
      ((ITraceSource)c).Trace = log.Add;
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Gone, OneUp, Text1Up);
      Assert.Contains(log, l => l.StartsWith("1UP candidate slot0 #78 from=08"));
      Assert.Contains("1UP confirmed slot0 polls=0", log); }

    [Fact] public void HasEmbeddedIcon() => Assert.NotNull(new OneUpCounter().DefaultIcon);
}
