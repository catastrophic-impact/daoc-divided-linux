using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Night 2026-11-04: Vampiirs 18-24 lost 18% of solo fights, casting 10-30 damage bolts and debuffs in melee.</summary>
[TestFixture]
public sealed class UT_VampiirMeleeCasts
{
    private static Spell Make(eSpellType type, int damage = 0) =>
        new(new DbSpell { Type = type.ToString(), Target = "Enemy", Damage = damage, Range = 250 }, 20);

    [Test]
    public void InMeleeAVampiirCastsItsClaws()
    {
        Assert.That(ShamanBotCombatPolicy.VampiirCastsInMelee(Make(eSpellType.MagicalStrike, 80), 5), Is.True);
        Assert.That(ShamanBotCombatPolicy.VampiirCastsInMelee(Make(eSpellType.EnduranceDrain, 45), 60), Is.True);
    }

    [Test]
    public void InMeleeAVampiirLeavesBoltsDebuffsAndLowPowerDrainsAlone()
    {
        Assert.That(ShamanBotCombatPolicy.VampiirCastsInMelee(Make(eSpellType.DirectDamage, 20), 90), Is.False);
        Assert.That(ShamanBotCombatPolicy.VampiirCastsInMelee(Make(eSpellType.VampiirEffectivenessDeBuff), 90), Is.False);
        Assert.That(ShamanBotCombatPolicy.VampiirCastsInMelee(Make(eSpellType.FumbleChanceDebuff), 90), Is.False);
        Assert.That(ShamanBotCombatPolicy.VampiirCastsInMelee(Make(eSpellType.EnduranceDrain, 45), 10), Is.False);
        Assert.That(ShamanBotCombatPolicy.VampiirCastsInMelee(null, 90), Is.False);
    }
}
