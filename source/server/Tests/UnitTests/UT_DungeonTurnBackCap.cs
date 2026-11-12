using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Night 2026-11-04: 7,904 dungeon turn-backs, Bjornarik 531 at one spot in Iarnvidiur's Lair.</summary>
[TestFixture]
public sealed class UT_DungeonTurnBackCap
{
    [Test]
    public void ABotLeavingTheDungeonIsRelocatedSoonerThanOneHuntingInIt()
    {
        Assert.That(AutonomousDungeonPolicy.TurnBacksBeforeRelocation(leaving: true), Is.EqualTo(2));
        Assert.That(AutonomousDungeonPolicy.TurnBacksBeforeRelocation(leaving: false), Is.EqualTo(3));
    }

    [Test]
    public void TurnBacksAtOneSpotCountAsOnePocket()
    {
        var spot = new System.Numerics.Vector3(33519, 33675, 14473);
        Assert.That(AutonomousRouteRecoveryPolicy.IsSameRepeatedFailurePocket(161, 161, spot, spot + new System.Numerics.Vector3(40, 30, 0), 8_000), Is.True);
        Assert.That(AutonomousRouteRecoveryPolicy.IsSameRepeatedFailurePocket(161, 150, spot, spot, 8_000), Is.False);
    }
}
