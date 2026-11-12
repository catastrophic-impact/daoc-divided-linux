using DOL.GS;
using NUnit.Framework;
using static DOL.UnitTests.RvrBotTestKit;

namespace DOL.UnitTests
{
    /// <summary>
    /// RvR perception and what a gamebot is busy with (Living Server's audit 2026-11-03, challenge 2, step 3, without its
    /// duty lease): a gamebot alerted by a spotter's call leaves what it is doing only when it may; the player's own
    /// companions and group bots are never told and count as the player's for the perception rule. Raid and battleground
    /// duty come from static state these tests can't set; their mapping is in UT_RvrPerception. Objects without a world.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrPerceptionDuty : RvrBotFixture
    {
        [Test]
        public void A_free_bot_and_a_bot_group_member_answer_a_call()
        {
            Bot free = Gamebot(objective: "RvR");
            Assert.That(AutonomousWorldBotController.RvrCallDutyOf(free), Is.EqualTo(RvrCallDuty.None));
            Assert.That(AutonomousWorldBotController.MayAnswerRvrCall(free), Is.True, "free");
            Bot grouped = Gamebot(objective: "RvR");
            grouped.Group = Token();
            Assert.That(AutonomousWorldBotController.RvrCallDutyOf(grouped), Is.EqualTo(RvrCallDuty.BotGroup));
            Assert.That(AutonomousWorldBotController.MayAnswerRvrCall(grouped), Is.True);
        }

        [Test]
        public void The_players_own_bots_are_never_told_and_count_as_the_players()
        {
            Player player = RealPlayer();
            Bot inGroup = Gamebot(objective: "RvR");
            JoinPlayersGroup(inGroup, player);
            Bot companion = Companion();
            Bot spotter = Gamebot(objective: "RvR");
            Assert.That(AutonomousWorldBotController.RvrCallDutyOf(inGroup), Is.EqualTo(RvrCallDuty.PlayerGroup));
            Assert.That(AutonomousWorldBotController.MayAnswerRvrCall(inGroup), Is.False);
            Assert.That(AutonomousWorldBotController.CanHearRvrCall(inGroup, spotter), Is.False);
            Assert.That(AutonomousWorldBotController.MayAnswerRvrCall(companion), Is.False);
            Assert.That(AutonomousWorldBotController.CanHearRvrCall(companion, spotter), Is.False);
            Assert.That(AutonomousWorldBotController.CanHearRvrCall(Gamebot(objective: "RvR"), spotter), Is.True);
            Assert.That(AutonomousWorldBotController.CanHearRvrCall(Gamebot(realm: eRealm.Midgard, objective: "RvR"), spotter), Is.False);
            // As targets, the player's bots are perceived like the player (noticed by chance), not acquired at once.
            Assert.That(AutonomousWorldBotController.PerceivedKind(inGroup), Is.EqualTo(RvrPerceivedKind.PlayerCompanion));
            Assert.That(AutonomousWorldBotController.PerceivedKind(companion), Is.EqualTo(RvrPerceivedKind.PlayerCompanion));
            Assert.That(AutonomousWorldBotController.PerceivedKind(player), Is.EqualTo(RvrPerceivedKind.Player));
            Assert.That(AutonomousWorldBotController.PerceivedKind(spotter), Is.EqualTo(RvrPerceivedKind.Other), "an enemy gamebot: as before");
        }
    }
}
