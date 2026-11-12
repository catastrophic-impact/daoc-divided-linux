using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// Night 2026-11-04: raids stranded their raiders (Hall of the Corrupt 72 minutes, Summoner's Hall and Darkness Falls
/// 15 minutes after the raid ended), about 220 bot-hours.
/// </summary>
[TestFixture]
public sealed class UT_RaidStrandedRelease
{
    [Test]
    public void AMemberWithNoRegroupPointIsReleasedAfterRepeatedFailuresAtOneSpot()
    {
        Assert.That(AutonomousWorldBotController.ShouldReleaseStrandedMember(3, alive: true, inCombat: false), Is.False);
        Assert.That(AutonomousWorldBotController.ShouldReleaseStrandedMember(4, alive: true, inCombat: false), Is.True);
        Assert.That(AutonomousWorldBotController.ShouldReleaseStrandedMember(9, alive: false, inCombat: false), Is.False);
        Assert.That(AutonomousWorldBotController.ShouldReleaseStrandedMember(9, alive: true, inCombat: true), Is.False);
        // The regroup (inside the raid region) comes first.
        Assert.That(AutonomousWorldBotController.StrandedExpeditionReleaseLimit,
            Is.GreaterThan(AutonomousWorldBotController.StrandedExpeditionFailureLimit));
    }

    [Test]
    public void NoExitIsTakenWithoutARaidEnding()
    {
        Assert.That(AutonomousRealmRaid.TryTakeDungeonExit(null), Is.False);
        Assert.That(AutonomousRealmRaid.PendingDungeonExits, Is.Zero);
        Assert.That(AutonomousRealmRaid.DungeonExitWindowMilliseconds, Is.LessThan(15 * 60_000), "before the 15-minute watchdog");
    }
}
