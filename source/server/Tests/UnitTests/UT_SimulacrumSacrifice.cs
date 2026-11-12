using DOL.Database;
using DOL.GS;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>DR-094: the Cabalist pet sacrifice line reads its bonuses from the spell's custom values.</summary>
    [TestFixture]
    public class UT_SimulacrumSacrifice
    {
        private static Spell Rank(params (string Key, string Value)[] values)
        {
            DbSpell row = new() { SpellID = 77560, Type = eSpellType.SimulacrumSacrifice.ToString(), Target = "Self", Duration = 600 };
            row.CustomValues = new DbSpellXCustomValues[values.Length];

            for (int i = 0; i < values.Length; i++)
                row.CustomValues[i] = new DbSpellXCustomValues { SpellID = 77560, KeyName = values[i].Key, Value = values[i].Value };

            return new Spell(row, 43);
        }

        [Test]
        public void TheSpellTypeHasItsOwnEffect()
        {
            Assert.That(EffectHelper.GetEffectFromSpell(Rank()), Is.EqualTo(eEffect.SimulacrumSacrifice));
        }

        [Test]
        public void ARankGivesOnlyWhatItsRowNames()
        {
            Spell soul = Rank((SimulacrumSacrificeSpellHandler.MeleeReductionKey, "10"));
            Assert.That(SimulacrumSacrificeSpellHandler.Param(soul, SimulacrumSacrificeSpellHandler.MeleeReductionKey), Is.EqualTo(10));
            Assert.That(SimulacrumSacrificeSpellHandler.Param(soul, SimulacrumSacrificeSpellHandler.HealthArmorKey), Is.EqualTo(0));
            Assert.That(SimulacrumSacrificeSpellHandler.Param(soul, SimulacrumSacrificeSpellHandler.ProcSpellKey), Is.EqualTo(0));

            Spell brilliance = Rank(
                (SimulacrumSacrificeSpellHandler.MeleeReductionKey, "10"),
                (SimulacrumSacrificeSpellHandler.HealthArmorKey, "10"),
                (SimulacrumSacrificeSpellHandler.SnareReductionKey, "25"),
                (SimulacrumSacrificeSpellHandler.MagicDamageKey, "15"),
                (SimulacrumSacrificeSpellHandler.ProcSpellKey, "77551"));
            Assert.That(SimulacrumSacrificeSpellHandler.Param(brilliance, SimulacrumSacrificeSpellHandler.SnareReductionKey), Is.EqualTo(25));
            Assert.That(SimulacrumSacrificeSpellHandler.Param(brilliance, SimulacrumSacrificeSpellHandler.MagicDamageKey), Is.EqualTo(15));
            Assert.That(SimulacrumSacrificeSpellHandler.Param(brilliance, SimulacrumSacrificeSpellHandler.ProcSpellKey), Is.EqualTo(77551));
        }

        [Test]
        public void ANegativeOrMissingValueCountsAsNothing()
        {
            Assert.That(SimulacrumSacrificeSpellHandler.Param(Rank((SimulacrumSacrificeSpellHandler.MeleeReductionKey, "-5")), SimulacrumSacrificeSpellHandler.MeleeReductionKey), Is.EqualTo(0));
            Assert.That(SimulacrumSacrificeSpellHandler.Param(null, SimulacrumSacrificeSpellHandler.MeleeReductionKey), Is.EqualTo(0));
        }

        [Test]
        public void TenPercentComesOffMeleeDamage()
        {
            Assert.That(SimulacrumSacrificeSpellHandler.Reduced(200, 10), Is.EqualTo(180));
            Assert.That(SimulacrumSacrificeSpellHandler.Reduced(0, 10), Is.EqualTo(0));
            Assert.That(SimulacrumSacrificeSpellHandler.Reduced(200, 250), Is.EqualTo(0));
        }

        [Test]
        public void ALivingWithoutTheEffectIsUntouched()
        {
            Assert.That(SimulacrumSacrificeSpellHandler.ActiveOn(null), Is.Null);
            Assert.That(SimulacrumSacrificeSpellHandler.HealthArmorPercent(null), Is.EqualTo(0));
            Assert.That(HealthArmorChantSpellHandler.Scale(null), Is.EqualTo(1));
            Assert.DoesNotThrow(() => SimulacrumSacrificeSpellHandler.OnAttacked(null, null));
        }
    }
}
