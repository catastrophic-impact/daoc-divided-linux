using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture, NonParallelizable]
    public class UT_StallAndRecoveryPolicies
    {
        [SetUp] public void Reset()
        {
            AiTurnStallMonitor.TakeMinute();
            AiTurnStallMonitor.TakeSerialMinute();
        }

        [Test]
        public void SerialNpcStepsAreKeptPerMinuteAndNamedWhenTheyExplainTheStall()
        {
            // DR-203: 2.7 s NpcService ticks whose slowest AI turn was 370 ms.
            AiTurnStallMonitor.RecordSerial("GroupCoordinator.TryFormGroups", 5);
            AiTurnStallMonitor.RecordSerial("RealmRaid.Pulse", 1900);
            AiTurnStallMonitor.RecordSerial("RealmRaid.Pulse", 400);
            AiTurnStallMonitor.RecordSerial("NpcService.BrainDispatch", 600);
            var serial = AiTurnStallMonitor.TakeSerialMinute();
            Assert.Multiple(() =>
            {
                Assert.That(serial.Select(s => s.Step), Is.EqualTo(new[] { "RealmRaid.Pulse", "NpcService.BrainDispatch" }),
                    "under 20 ms is dropped; slowest first");
                Assert.That(serial[0].Milliseconds, Is.EqualTo(1900), "the minute keeps each step's worst");
                Assert.That(AiTurnStallMonitor.TakeSerialMinute(), Is.Empty, "each minute starts empty");
                AiTurnStallMonitor.Turn[] top = [new("Yrildborg", "BotBrain", 372)];
                Assert.That(AiTurnStallMonitor.LikelyCause(2700, 406, top, serial), Is.EqualTo("serial NPC step: RealmRaid.Pulse"));
                Assert.That(AiTurnStallMonitor.LikelyCause(2700, 406, top, [("RealmRaid.Pulse", 200L)]), Does.StartWith("mixed"));
                Assert.That(AiTurnStallMonitor.DescribeSerial(serial), Is.EqualTo("RealmRaid.Pulse 1900ms; NpcService.BrainDispatch 600ms"));
            });
        }

        [Test]
        public void CampTargetSearchProvesAFewCandidatesPerTurnAndRetriesFailuresLater()
        {
            // DR-203: one "Searching the blue element camp" turn proved every candidate (500 corridor checks, 1.16 s).
            int cap = AutonomousWorldBotController.CampTargetRouteProofsPerSearch;
            long retry = AutonomousWorldBotController.UnreachableCampTargetRetryMilliseconds;
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousWorldBotController.MayProveCampTarget(false, 0, 1_000, 0), Is.True);
                Assert.That(AutonomousWorldBotController.MayProveCampTarget(false, 0, 1_000, cap - 1), Is.True);
                Assert.That(AutonomousWorldBotController.MayProveCampTarget(false, 0, 1_000, cap), Is.False);
                Assert.That(AutonomousWorldBotController.MayProveCampTarget(true, 1_000 + retry, 1_000, 0), Is.False,
                    "a candidate that just failed waits");
                Assert.That(AutonomousWorldBotController.MayProveCampTarget(true, 1_000 + retry, 1_000 + retry, 0), Is.True);
                Assert.That(retry, Is.LessThan(75_000), "shorter than the empty-camp timer, so a good camp is not given up");
            });
        }

        [Test]
        public void StallMonitorKeepsOnlyTheFiveSlowestTurnsAndCountsTheRest()
        {
            for (int i = 1; i <= 40; i++)
                AiTurnStallMonitor.Record("bot" + i, "BotBrain", 25 + i * 10);
            var (slow, big, top) = AiTurnStallMonitor.TakeMinute();
            Assert.That(slow, Is.EqualTo(40));
            Assert.That(big, Is.EqualTo(40 - 22), "turns of 250 ms or more");
            Assert.That(top.Select(t => t.Milliseconds), Is.EqualTo(new long[] { 425, 415, 405, 395, 385 }));
            Assert.That(AiTurnStallMonitor.TakeMinute().Slow, Is.Zero, "each minute starts empty");
        }

        [Test]
        public void OnlyBigTurnsStillGetTheirOwnWarningLine()
        {
            Assert.That(AiTurnStallMonitor.ShouldLogIndividually(99), Is.False);
            Assert.That(AiTurnStallMonitor.ShouldLogIndividually(100), Is.True);
        }

        [Test]
        public void StallMinutesAreNamedByTheirLikelyCause()
        {
            var top = new[] { new AiTurnStallMonitor.Turn("Conainren", "BotBrain", 4800) };
            Assert.That(AiTurnStallMonitor.IsStallMinute(400, 200), Is.False, "quiet minutes write nothing");
            Assert.That(AiTurnStallMonitor.IsStallMinute(5100, 4900), Is.True);
            Assert.That(AiTurnStallMonitor.LikelyCause(5100, 4900, top), Does.StartWith("garbage-collection"));
            Assert.That(AiTurnStallMonitor.LikelyCause(5100, 0, top), Does.Contain("Conainren"));
            Assert.That(AiTurnStallMonitor.LikelyCause(5100, 0, []), Does.StartWith("mixed"));
        }

        [Test]
        public void StuckBotPetIsRecoveredOnlyAfterTenSecondsWithoutProgress()
        {
            Assert.That(BotPetStuckRecovery.IsStuck(0, 50_000), Is.False);
            Assert.That(BotPetStuckRecovery.IsStuck(40_001, 50_000), Is.False);
            Assert.That(BotPetStuckRecovery.IsStuck(40_000, 50_000), Is.True);
        }

        [Test]
        public void RallyMemberEscapesAfterThreeFailedRoutes()
        {
            Assert.That(RealmRaidHubEscape.ShouldEscape(2), Is.False);
            Assert.That(RealmRaidHubEscape.ShouldEscape(3), Is.True);
        }
    }
}
