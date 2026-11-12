using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Night playtest 2026-11-04: solo route pulls with no resource check, no road rest and no break-off.</summary>
[TestFixture]
public sealed class UT_SoloFightReadiness
{
    [Test]
    public void ARoutePullWaitsForEnduranceHealthAndPower()
    {
        // Dagununrun, Shadowblade 26: pulled snow imps with 28% endurance and died.
        Assert.That(AutonomousSoloFightReadiness.ReadyForRoutePull(100, 0, 28, usesPower: false), Is.False);
        Assert.That(AutonomousSoloFightReadiness.ReadyForRoutePull(60, 100, 100, usesPower: true), Is.False);
        Assert.That(AutonomousSoloFightReadiness.ReadyForRoutePull(100, 30, 100, usesPower: true), Is.False);
        Assert.That(AutonomousSoloFightReadiness.ReadyForRoutePull(90, 80, 90, usesPower: true), Is.True);
    }

    [Test]
    public void PowerDoesNotMatterToAClassWithoutIt()
    {
        Assert.That(AutonomousSoloFightReadiness.ReadyForRoutePull(100, 0, 100, usesPower: false), Is.True);
    }

    [Test]
    public void AHurtBotRestsOnTheRoadOnlyRightAfterAFight()
    {
        long now = 1_000_000;
        Assert.That(AutonomousSoloFightReadiness.ShouldRestOnRoad(true, now, now - 5_000, 40, 100, 100, false), Is.True);
        Assert.That(AutonomousSoloFightReadiness.ShouldRestOnRoad(true, now, now - 5_000, 95, 100, 100, false), Is.False);
        // Rest is not a travel prerequisite: a bot that has not fought walks on.
        Assert.That(AutonomousSoloFightReadiness.ShouldRestOnRoad(true, now, now - 600_000, 40, 100, 100, false), Is.False);
        Assert.That(AutonomousSoloFightReadiness.ShouldRestOnRoad(true, now, 0, 40, 100, 100, false), Is.False);
        Assert.That(AutonomousSoloFightReadiness.ShouldRestOnRoad(false, now, now - 5_000, 40, 100, 100, false), Is.False);
    }

    [Test]
    public void ASoloBotLosingBadlyBreaksOff()
    {
        Assert.That(AutonomousSoloFightReadiness.ShouldDisengage(20, 80, 1, ConColor.YELLOW, canSelfHeal: false), Is.True);
        Assert.That(AutonomousSoloFightReadiness.ShouldDisengage(25, 95, 1, ConColor.GREEN, canSelfHeal: false), Is.True);
    }

    [Test]
    public void ANearlyDeadMonsterIsFinishedNotRunFrom()
    {
        Assert.That(AutonomousSoloFightReadiness.ShouldDisengage(20, 30, 1, ConColor.ORANGE, false), Is.False);
        // An even trade is not losing badly.
        Assert.That(AutonomousSoloFightReadiness.ShouldDisengage(25, 45, 1, ConColor.YELLOW, false), Is.False);
    }

    [Test]
    public void AHealthyBotAndAGreyMonsterNeverBreakOff()
    {
        Assert.That(AutonomousSoloFightReadiness.ShouldDisengage(60, 100, 1, ConColor.RED, false), Is.False);
        Assert.That(AutonomousSoloFightReadiness.ShouldDisengage(10, 100, 1, ConColor.GREY, false), Is.False);
        Assert.That(AutonomousSoloFightReadiness.ShouldDisengage(10, 100, 0, ConColor.RED, false), Is.False);
    }

    [Test]
    public void AddsMakeABotBreakOffSooner()
    {
        // Seroenwen, Theurgist 25: two druids on her.
        Assert.That(AutonomousSoloFightReadiness.ShouldDisengage(40, 90, 2, ConColor.YELLOW, false), Is.True);
        Assert.That(AutonomousSoloFightReadiness.ShouldDisengage(40, 90, 1, ConColor.YELLOW, false), Is.False);
    }

    [Test]
    public void ABotThatCanHealItselfOnlyRunsFromAdds()
    {
        Assert.That(AutonomousSoloFightReadiness.ShouldDisengage(20, 80, 1, ConColor.YELLOW, canSelfHeal: true), Is.False);
        Assert.That(AutonomousSoloFightReadiness.ShouldDisengage(20, 80, 2, ConColor.YELLOW, canSelfHeal: true), Is.True);
    }

    [Test]
    public void PartiesStillOnlyBreakOffAgainstOrangeWhenTheHealerIsLost()
    {
        // The prior lesson is untouched: a lost healer is no reason for a party to run from an easy monster.
        Assert.That(AutonomousRouteThreatPolicy.ShouldBreakOff(5, 4, true, false, 90, ConColor.BLUE), Is.False);
        Assert.That(AutonomousRouteThreatPolicy.ShouldBreakOff(5, 4, true, false, 90, ConColor.ORANGE), Is.True);
    }
}
