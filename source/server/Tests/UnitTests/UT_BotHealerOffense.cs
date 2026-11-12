using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Night playtest 2026-11-04: Healer bots spent their power on Hinder Spirit (964 casts) and stuns, not heals.</summary>
    [TestFixture]
    public class UT_BotHealerOffense
    {
        private static Spell Make(string type, int level, double cast = 0, int damage = 0, int damageType = 0, int id = 0) =>
            new(new DbSpell { SpellID = id != 0 ? id : 810000 + level * 10 + damageType, Name = "test", Type = type, Target = "Enemy",
                CastTime = cast, Damage = damage, DamageType = damageType, Range = 1500, Duration = 20 }, level);

        [Test]
        public void LowerRanksOfADebuffAreOutclassed()
        {
            Spell rank1 = Make("CombatSpeedDebuff", 2);
            Spell rank2 = Make("CombatSpeedDebuff", 12);
            Spell rank3 = Make("CombatSpeedDebuff", 22);
            Spell[] known = { rank1, rank2, rank3 };
            Assert.Multiple(() =>
            {
                Assert.That(BotBrain.IsOutclassedSpell(rank1, known, 50), Is.True);
                Assert.That(BotBrain.IsOutclassedSpell(rank2, known, 50), Is.True);
                Assert.That(BotBrain.IsOutclassedSpell(rank3, known, 50), Is.False);
                // A rank above the bot's level does not count.
                Assert.That(BotBrain.IsOutclassedSpell(rank2, known, 15), Is.False);
            });
        }

        [Test]
        public void LowerStunRanksAreOutclassedButOtherKindsAreNot()
        {
            Spell stun = Make("Stun", 5);
            Spell betterStun = Make("Stun", 25);
            Spell debuff = Make("StrengthDebuff", 4, damageType: 10);
            Spell otherDebuff = Make("StrengthDebuff", 30, damageType: 11);
            Spell[] known = { stun, betterStun, debuff, otherDebuff };
            Assert.Multiple(() =>
            {
                Assert.That(BotBrain.IsOutclassedSpell(stun, known, 50), Is.True);
                Assert.That(BotBrain.IsOutclassedSpell(debuff, known, 50), Is.False, "a different damage type is a different spell");
            });
        }

        [Test]
        public void DamageRanksKeepTheirOldRules()
        {
            Spell top = Make("DirectDamage", 43, cast: 2.6, damage: 50, id: 820430);
            Spell lower = Make("DirectDamage", 33, cast: 2.6, damage: 50, id: 820330);
            Spell[] known = { top, lower };
            Assert.That(BotBrain.IsOutclassedSpell(lower, known, 50), Is.EqualTo(BotBrain.IsOutclassedDamageSpell(lower, known, 50)));
            Assert.That(BotBrain.IsOutclassedSpell(lower, known, 50), Is.True);
        }

        [Test]
        public void AGroupedHealerCastsNothingHarmfulWhileSomeoneIsHurt()
        {
            foreach (BotHarmfulSpellKind kind in new[] { BotHarmfulSpellKind.Damage, BotHarmfulSpellKind.Debuff, BotHarmfulSpellKind.CrowdControl })
                Assert.That(BotHealerOffensePolicy.Allows(kind, true, grouped: true, 100, memberHurt: true, targetOnHealer: true), Is.False);
        }

        [Test]
        public void AGroupedHealerDebuffsOnlyWithPowerToSpare()
        {
            Assert.That(BotHealerOffensePolicy.Allows(BotHarmfulSpellKind.Debuff, true, true, 60, false, false), Is.False);
            Assert.That(BotHealerOffensePolicy.Allows(BotHarmfulSpellKind.Debuff, true, true, 80, false, false), Is.True);
            Assert.That(BotHealerOffensePolicy.Allows(BotHarmfulSpellKind.Damage, true, true, 40, false, false), Is.False);
            Assert.That(BotHealerOffensePolicy.Allows(BotHarmfulSpellKind.Damage, true, true, 60, false, false), Is.True);
        }

        [Test]
        public void AGroupedHealerStunsOnlyWhatIsOnItself()
        {
            Assert.That(BotHealerOffensePolicy.Allows(BotHarmfulSpellKind.CrowdControl, true, true, 100, false, targetOnHealer: false), Is.False);
            Assert.That(BotHealerOffensePolicy.Allows(BotHarmfulSpellKind.CrowdControl, true, true, 30, false, targetOnHealer: true), Is.True);
        }

        [Test]
        public void ASoloHealerStillFights()
        {
            Assert.That(BotHealerOffensePolicy.Allows(BotHarmfulSpellKind.Damage, true, false, 10, false, true), Is.True);
            Assert.That(BotHealerOffensePolicy.Allows(BotHarmfulSpellKind.Debuff, true, false, 30, false, true), Is.False);
            Assert.That(BotHealerOffensePolicy.Allows(BotHarmfulSpellKind.Debuff, true, false, 50, false, true), Is.True);
        }

        [Test]
        public void OtherClassesAreUntouched()
        {
            Assert.That(BotHealerOffensePolicy.IsPrimaryHealer(eCharacterClass.Healer), Is.True);
            Assert.That(BotHealerOffensePolicy.IsPrimaryHealer(eCharacterClass.Wizard), Is.False);
            Assert.That(BotHealerOffensePolicy.Allows(BotHarmfulSpellKind.Debuff, false, true, 5, true, false), Is.True);
        }

        [Test]
        public void SpellKinds()
        {
            Assert.That(BotHealerOffensePolicy.Kind(Make("CombatSpeedDebuff", 2)), Is.EqualTo(BotHarmfulSpellKind.Debuff));
            Assert.That(BotHealerOffensePolicy.Kind(Make("Stun", 3)), Is.EqualTo(BotHarmfulSpellKind.CrowdControl));
            Assert.That(BotHealerOffensePolicy.Kind(Make("DirectDamage", 4, damage: 30)), Is.EqualTo(BotHarmfulSpellKind.Damage));
        }
    }
}
