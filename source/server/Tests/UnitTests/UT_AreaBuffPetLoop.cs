using DOL.AI.Brain;
using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Night 2026-11-04: 1,600 buff recast loops of point-blank baseline buffs aimed at pets and charmed monsters.</summary>
[TestFixture]
public sealed class UT_AreaBuffPetLoop
{
    [Test]
    public void APointBlankRealmBuffIsJudgedOnMembersOnly()
    {
        // Benefit of the Archangel (75011): no range, a 1,000 radius around the caster.
        var baseline = new Spell(new DbSpell { Type = "StrengthBuff", Target = "Realm", Range = 0, Radius = 1000, Duration = 1200 }, 50);
        Assert.That(BotBrain.AreaBuffSkipsPets(baseline), Is.True);
    }

    [Test]
    public void ARangedRealmBuffStillCountsPets()
    {
        var single = new Spell(new DbSpell { Type = "StrengthBuff", Target = "Realm", Range = 1000, Radius = 0, Duration = 1200 }, 50);
        var group = new Spell(new DbSpell { Type = "StrengthBuff", Target = "Group", Range = 0, Radius = 1000, Duration = 1200 }, 50);
        Assert.That(BotBrain.AreaBuffSkipsPets(single), Is.False);
        Assert.That(BotBrain.AreaBuffSkipsPets(group), Is.False);
        Assert.That(BotBrain.AreaBuffSkipsPets(null), Is.False);
    }
}
