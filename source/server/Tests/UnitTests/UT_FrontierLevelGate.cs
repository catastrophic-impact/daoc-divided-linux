using System;
using NUnit.Framework;
using OfflineDaoc.Configuration;

namespace DOL.GS.Tests;

/// <summary>Night playtest 2026-11-04: level 20-39 bots on frontier RvR goals, and a level 17-23 party wiped in DF.</summary>
[TestFixture, NonParallelizable]
public sealed class UT_FrontierLevelGate
{
    private static readonly BotGoalWeights MidRow = BotGoalSettings.Defaults.Levels20To49;

    [TestCase(20)]
    [TestCase(30)]
    [TestCase(39)]
    public void NoFrontierRvrBelowLevelForty(int level)
    {
        BotGoalWeights row = AutonomousBotGoalPolicy.ScaleForLevel(MidRow, level);
        Assert.That(row.RvR, Is.Zero);
        Assert.That(row.Total, Is.EqualTo(100));
        Assert.That(AutonomousBotGoalPolicy.LevelAllowsFrontierRvr(level), Is.False);
    }

    [Test]
    public void TheRvrShareRisesFromFortyToFifty()
    {
        int at40 = AutonomousBotGoalPolicy.ScaleForLevel(MidRow, 40).RvR;
        int at45 = AutonomousBotGoalPolicy.ScaleForLevel(MidRow, 45).RvR;
        int at49 = AutonomousBotGoalPolicy.ScaleForLevel(MidRow, 49).RvR;
        Assert.That(at40, Is.EqualTo(10));
        Assert.That(at45, Is.GreaterThan(at40));
        Assert.That(at49, Is.GreaterThan(at45).And.LessThanOrEqualTo(MidRow.RvR));
        Assert.That(AutonomousBotGoalPolicy.ScaleForLevel(BotGoalSettings.Defaults.Level50, 50),
            Is.EqualTo(BotGoalSettings.Defaults.Level50));
    }

    [Test]
    public void TheFreedShareGoesBackToSoloAndGroupInTheirRatio()
    {
        BotGoalWeights row = AutonomousBotGoalPolicy.ScaleForLevel(new BotGoalWeights(40, 40, 20), 25);
        Assert.That(row, Is.EqualTo(new BotGoalWeights(50, 50, 0)));
        // An RvR-only row still leaves a low bot something to do.
        Assert.That(AutonomousBotGoalPolicy.ScaleForLevel(new BotGoalWeights(0, 0, 100), 25), Is.EqualTo(new BotGoalWeights(100, 0, 0)));
        // Battlegrounds (15-35) are untouched.
        Assert.That(AutonomousBotGoalPolicy.ScaleForLevel(new BotGoalWeights(30, 30, 20, 20), 25).Battlegrounds, Is.EqualTo(20));
    }

    [Test]
    public void ALowBotNeverDrawsFrontierRvr()
    {
        var random = new Random(7);
        for (int i = 0; i < 2_000; i++)
            Assert.That(AutonomousBotGoalPolicy.Choose(25, random), Is.Not.EqualTo(eAutonomousObjectiveKind.RvR));
        Assert.That(AutonomousBotGoalPolicy.Allows(25, eAutonomousObjectiveKind.RvR), Is.False);
    }

    [Test]
    public void TheMidBandRvrTargetComesOnlyFromLevelFortyAndUp()
    {
        var low = AutonomousObjectiveAssignments.TargetForMidLevelPopulation([22, 24, 26, 28, 30, 32, 34, 36, 38, 39]);
        Assert.That(low.RvR, Is.Zero);
        Assert.That(low.SoloPve + low.GroupPve + low.Battlegrounds, Is.EqualTo(10));
        var high = AutonomousObjectiveAssignments.TargetForMidLevelPopulation([49, 49, 49, 49, 49, 49, 49, 49, 49, 49]);
        Assert.That(high.RvR, Is.EqualTo(2));
    }

    [Test]
    public void ASavedFrontierTourBelowFortyEnds()
    {
        var record = new OfflineWorldBotRecord { Level = 25, ObjectiveKind = eAutonomousObjectiveKind.RvR.ToString(), ObjectiveAssignmentId = "x" };
        Assert.That(AutonomousBotGoalPolicy.ReconcileSavedAssignment(record), Is.True);
        Assert.That(record.ObjectiveKind, Is.Not.EqualTo(eAutonomousObjectiveKind.RvR.ToString()));
        var high = new OfflineWorldBotRecord { Level = 45, ObjectiveKind = eAutonomousObjectiveKind.RvR.ToString(), ObjectiveAssignmentId = "x" };
        AutonomousBotGoalPolicy.ReconcileSavedAssignment(high);
        Assert.That(high.ObjectiveKind, Is.EqualTo(eAutonomousObjectiveKind.RvR.ToString()));
    }

    [Test]
    public void ALowPartyIsKeptOutOfADarknessFallsRoomFarAboveItsLowestMember()
    {
        // Level 17-23 party, average 20, demoniac familiar room at 33.
        Assert.That(AutonomousDarknessFallsPolicy.PartyFitsCamp(20, 17, 33), Is.False);
        Assert.That(AutonomousDarknessFallsPolicy.PartyFitsCamp(20, 17, 27), Is.True);
        Assert.That(AutonomousDarknessFallsPolicy.PartyFitsCamp(18, 17, 18), Is.False);
    }

    [Test]
    public void DarknessFallsStaysOpenToSoloBotsFromTwenty()
    {
        Assert.That(AutonomousDarknessFallsPolicy.SoloMinimumLevel, Is.EqualTo(20));
        Assert.That(AutonomousDarknessFallsPolicy.RollGoal(20, 0, 0), Is.EqualTo(eDarknessFallsGoal.Pve));
        Assert.That(AutonomousDarknessFallsPolicy.RollGoal(45, 0, AutonomousDarknessFallsPolicy.PveGoalPercent), Is.EqualTo(eDarknessFallsGoal.Pvp));
    }
}
