using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Night 2026-11-04: Darkness Falls bots "fought" enemy bots 4,951 units above them (104 watchdog rescues).</summary>
[TestFixture]
public sealed class UT_CrossFloorEnemy
{
    [Test]
    public void AnEnemyOnAnotherDungeonFloorIsNoTarget()
    {
        Assert.That(AutonomousWorldBotController.IsCrossFloorEnemy(inDungeon: true, 4951, firingOnRealm: false), Is.True);
        Assert.That(AutonomousWorldBotController.IsCrossFloorEnemy(inDungeon: true, -4951, firingOnRealm: true), Is.True);
        Assert.That(AutonomousWorldBotController.IsCrossFloorEnemy(inDungeon: true, 450, firingOnRealm: false), Is.True);
    }

    [Test]
    public void ASameFloorOrLedgeEnemyStaysATarget()
    {
        Assert.That(AutonomousWorldBotController.IsCrossFloorEnemy(inDungeon: true, 120, firingOnRealm: false), Is.False);
        Assert.That(AutonomousWorldBotController.IsCrossFloorEnemy(inDungeon: true, 600, firingOnRealm: true), Is.False);
        // Open frontier: walls and hills are the keep rules' business (BreakUnreachableTarget).
        Assert.That(AutonomousWorldBotController.IsCrossFloorEnemy(inDungeon: false, 4951, firingOnRealm: false), Is.False);
    }
}
