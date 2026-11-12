using NUnit.Framework;
using static DOL.GS.CommanderPet;

namespace DOL.GS.Tests;

/// <summary>
/// Night 2026-11-04: seven level 50 Bonedancers froze in Jordheim all night. Summon Dread Lord (level 50, template 52)
/// summons a DreadCommander (rank 45), and 45 &lt; 50 made the bot replace its commander on every think.
/// </summary>
[TestFixture]
public sealed class UT_BonedancerCommanderUpgrade
{
    [Test]
    public void ADreadCommanderIsNotReplacedByTheLevel50DreadLordSpell()
    {
        Assert.That(AutonomousPetSupport.CommanderNeedsUpgrade(eCommanderType.DreadCommander, 52, 50), Is.False);
        Assert.That(AutonomousPetSupport.CommanderNeedsUpgrade(eCommanderType.DreadCommander, 52, 40), Is.False);
    }

    [Test]
    public void ALowerTierIsStillReplaced()
    {
        Assert.That(AutonomousPetSupport.CommanderNeedsUpgrade(eCommanderType.ReturnedCommander, 50, 15), Is.True);
        Assert.That(AutonomousPetSupport.CommanderNeedsUpgrade(eCommanderType.BoneCommander, 52, 40), Is.True);
        Assert.That(AutonomousPetSupport.CommanderNeedsUpgrade(eCommanderType.SkeletalCommander, 50, 22), Is.False);
    }

    [Test]
    public void AnUnknownCommanderIsNeverReplacedInALoop()
    {
        Assert.That(AutonomousPetSupport.CommanderNeedsUpgrade(eCommanderType.Unknown, 52, 50), Is.False);
    }

    // DR-097 (2026-11-08): the Dread spells now summon their own templates.
    [Test]
    public void TheDreadLordOutranksTheDreadCommanderOnce()
    {
        Assert.That(AutonomousPetSupport.CommanderNeedsUpgrade(eCommanderType.DreadCommander, AutonomousPetSupport.DreadLordTemplateId, 50), Is.True);
        Assert.That(AutonomousPetSupport.CommanderNeedsUpgrade(eCommanderType.DreadLord, AutonomousPetSupport.DreadLordTemplateId, 50), Is.False);
    }

    [Test]
    public void TheDreadGuardianAndLichRankWithTheDreadCommander()
    {
        foreach (int template in new[] { 211, 212, 213 })
        {
            Assert.That(AutonomousPetSupport.CommanderNeedsUpgrade(eCommanderType.DreadCommander, template, 40), Is.False, $"template {template}");
            Assert.That(AutonomousPetSupport.CommanderNeedsUpgrade(eCommanderType.BoneCommander, template, 40), Is.True, $"template {template}");
            Assert.That(AutonomousPetSupport.CommanderNeedsUpgrade(eCommanderType.DreadLich, template, 40), Is.False, $"template {template}");
        }
    }

    // DR-097: subpets are 100% of the caster's level (owner 2026-11-01), so the old 75 total left a level 50 Bonedancer one.
    [Test]
    public void ALevel50BonedancerControlsTwoFullLevelSubpets()
    {
        Assert.That(AutonomousPetSupport.BonedancerMinionLevelBudget(50), Is.EqualTo(100));
        Assert.That(AutonomousPetSupport.BonedancerMinionLevelBudget(45), Is.GreaterThanOrEqualTo(2 * 45));
        Assert.That(AutonomousPetSupport.BonedancerMinionLevelBudget(30), Is.EqualTo(75));
        Assert.That(AutonomousPetSupport.BonedancerMinionLevelBudget(0), Is.EqualTo(75));
    }
}
