using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Night playtest 2026-11-04: Skalds died at 92% power with their chants unused.</summary>
[TestFixture]
public sealed class UT_SkaldCombatChant
{
    private static readonly eSpellType[] Battlesongs =
        [eSpellType.HealthRegenBuff, eSpellType.FatigueConsumptionBuff, eSpellType.SpeedEnhancement, eSpellType.DamageAdd];

    [Test]
    public void ASkaldFightsWithItsDamageAddChant()
    {
        Assert.That(BotSongTwistPolicy.SkaldCombatHold(Battlesongs, 90), Is.EqualTo(eSpellType.DamageAdd));
    }

    [Test]
    public void ASkaldSingsRestWhenTheGroupIsHurt()
    {
        Assert.That(BotSongTwistPolicy.SkaldCombatHold(Battlesongs, 40), Is.EqualTo(eSpellType.HealthRegenBuff));
    }

    [Test]
    public void ALevelOneSkaldUsesWhatItHasAndNeverSpeed()
    {
        Assert.That(BotSongTwistPolicy.SkaldCombatHold([eSpellType.HealthRegenBuff], 90), Is.EqualTo(eSpellType.HealthRegenBuff));
        Assert.That(BotSongTwistPolicy.SkaldCombatHold([eSpellType.SpeedEnhancement], 90), Is.Null);
    }
}
