using DOL.AI.Brain;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Night playtest 2026-11-04: grouped Eldritch bots nuked until dry, pulled threat off the tank, then stood in melee.</summary>
[TestFixture]
public sealed class UT_GroupCasterThreat
{
    [Test]
    public void AGroupedCasterWaitsForItsTankToTakeTheMonster()
    {
        Assert.That(BotBrain.GroupCasterMayNuke(grouped: true, partyHasTank: true, isPuller: false, targetOnMe: false, targetOnOtherMember: false), Is.False);
        Assert.That(BotBrain.GroupCasterMayNuke(true, true, false, false, targetOnOtherMember: true), Is.True);
    }

    [Test]
    public void SoloPullerOrTanklessCastersNukeAsBefore()
    {
        Assert.That(BotBrain.GroupCasterMayNuke(grouped: false, false, false, false, false), Is.True);
        Assert.That(BotBrain.GroupCasterMayNuke(true, partyHasTank: false, false, false, false), Is.True);
        Assert.That(BotBrain.GroupCasterMayNuke(true, true, isPuller: true, false, false), Is.True);
        Assert.That(BotBrain.GroupCasterMayNuke(true, true, false, targetOnMe: true, false), Is.True);
    }
}
