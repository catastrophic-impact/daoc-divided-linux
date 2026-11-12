using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Night playtest 2026-11-04: a bot logged out dead came back alive on its corpse and died 11 s after login.</summary>
[TestFixture]
public sealed class UT_DeadLogoutLogin
{
    [Test]
    public void ARecordSavedDeadLogsInAfterDeath()
    {
        var record = new OfflineWorldBotRecord { IsAlive = false, RegionId = 200, X = 315618, Y = 719682, LastSavedUtc = "2026-11-04T20:56:00Z" };
        Assert.That(AutonomousPopulationController.LoggedOutDead(record), Is.True);
    }

    [Test]
    public void ALivingOrNeverSavedRecordDoesNot()
    {
        Assert.That(AutonomousPopulationController.LoggedOutDead(
            new OfflineWorldBotRecord { IsAlive = true, RegionId = 200, LastSavedUtc = "2026-11-04T20:56:00Z" }), Is.False);
        // A fresh record's IsAlive is only the column default.
        Assert.That(AutonomousPopulationController.LoggedOutDead(new OfflineWorldBotRecord { IsAlive = false, RegionId = 200 }), Is.False);
        Assert.That(AutonomousPopulationController.LoggedOutDead(
            new OfflineWorldBotRecord { IsAlive = false, RegionId = 0, LastSavedUtc = "2026-11-04T20:56:00Z" }), Is.False);
        Assert.That(AutonomousPopulationController.LoggedOutDead(null), Is.False);
    }
}
