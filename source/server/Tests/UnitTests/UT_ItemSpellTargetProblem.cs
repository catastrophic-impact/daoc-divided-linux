using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// DR-058 (2026-11-08): the Javelin of Flame said "has been used", spent a charge and did nothing when its target
/// could not be attacked, because StartSpell skips the cast checks and the target list drops such a target quietly.
/// </summary>
[TestFixture]
public sealed class UT_ItemSpellTargetProblem
{
    [Test]
    public void AnAttackableTargetInRangeHasNoProblem()
    {
        Assert.That(GamePlayer.ItemSpellTargetProblem(true, true, () => true), Is.Null);
    }

    [Test]
    public void ADeadTargetIsRefusedBeforeTheAttackRules()
    {
        bool asked = false;
        Assert.That(GamePlayer.ItemSpellTargetProblem(false, true, () => asked = true), Is.EqualTo("Your target is dead!"));
        Assert.That(asked, Is.False);
    }

    [Test]
    public void ATargetOutOfRangeIsRefused()
    {
        Assert.That(GamePlayer.ItemSpellTargetProblem(true, false, () => true), Is.EqualTo("That target is too far away!"));
    }

    [Test]
    public void ATargetTheAttackRulesRefuseIsRefused()
    {
        Assert.That(GamePlayer.ItemSpellTargetProblem(true, true, () => false), Is.Not.Null);
    }
}
