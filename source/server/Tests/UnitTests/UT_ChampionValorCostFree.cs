using DOL.Database;
using DOL.GS;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// DR-101 (2026-11-08): 1.129 made the Champion Valor auras (Champion's Ambience 2% ... Champion's Aura 10%) a chance
    /// for spells and styles to cost no power or endurance; they were still siege-damage songs.
    /// </summary>
    [TestFixture]
    public class UT_ChampionValorCostFree
    {
        private static Spell Of(eSpellType type, int value) =>
            new(new DbSpell { SpellID = 74107, Type = type.ToString(), Target = "Group", Value = value, Duration = 8 }, 6);

        [Test]
        public void TheAuraRunsAsItsOwnEffect()
        {
            Assert.That(EffectHelper.GetEffectFromSpell(Of(eSpellType.CostFreeChance, 2)), Is.EqualTo(eEffect.CostFreeChance));
            Assert.That(EffectHelper.GetEffectFromSpell(Of(eSpellType.SiegeProtection, 10)), Is.EqualTo(eEffect.SiegeProtection));
        }

        [TestCase(2, 1, true)]
        [TestCase(2, 2, true)]
        [TestCase(2, 3, false)]
        [TestCase(10, 10, true)]
        [TestCase(10, 11, false)]
        [TestCase(0, 1, false)]
        public void OneRollAgainstThePercent(int percent, int roll, bool free)
        {
            Assert.That(CostFreeChance.Rolls(percent, roll), Is.EqualTo(free));
        }

        [Test]
        public void NoLivingNoAuraNoFreeCost()
        {
            Assert.That(CostFreeChance.Percent(null), Is.EqualTo(0));
            Assert.That(CostFreeChance.Rolls((GameLiving) null), Is.False);
        }
    }
}
