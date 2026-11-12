using DOL.Database;
using DOL.GS;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// ToA named drops (2026-11-08): official item spell 12900 restores 3% health, endurance and power every 5 seconds
    /// for 30 seconds (Robes of the Magus, Timeless Indigo Mail, Xanxicar Remnant Cloak). No spell type did that.
    /// </summary>
    [TestFixture]
    public class UT_HealAllOverTime
    {
        [Test]
        public void ItRunsAsAHealOverTimeEffect()
        {
            var spell = new Spell(new DbSpell { SpellID = 812900, Type = eSpellType.HealAllOverTime.ToString(), Target = "Self", Value = 3, Duration = 30, Frequency = 50 }, 50);
            Assert.That(spell.SpellType, Is.EqualTo(eSpellType.HealAllOverTime));
            Assert.That(EffectHelper.GetEffectFromSpell(spell), Is.EqualTo(eEffect.HealOverTime));
        }

        [TestCase(3, 2000, 60)]
        [TestCase(3, 100, 3)]
        [TestCase(-3, 1000, 30)]
        [TestCase(3, 10, 1)]
        [TestCase(3, 0, 0)]
        public void APulseRestoresAPercentOfThePool(double percent, int max, int expected)
        {
            Assert.That(HealAllOverTimeSpellHandler.PulseAmount(percent, max), Is.EqualTo(expected));
        }
    }
}
