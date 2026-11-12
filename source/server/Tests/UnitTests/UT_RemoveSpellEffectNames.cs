using DOL.GS;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Cure spells list effects by name. An unknown name parsed to 0 (Bladeturn) and stripped Bladeturn.</summary>
    [TestFixture]
    public sealed class UT_RemoveSpellEffectNames
    {
        [Test]
        public void UnknownNamesRemoveNothing() => Assert.Multiple(() =>
        {
            Assert.That(RemoveSpellEffectHandler.EffectsFor("Silence"), Is.Empty);
            Assert.That(RemoveSpellEffectHandler.EffectsFor("MagicAblativeArmor"), Is.Empty);
            Assert.That(RemoveSpellEffectHandler.EffectsFor("0"), Is.Empty);
            Assert.That(RemoveSpellEffectHandler.EffectsFor(null), Is.Empty);
        });

        [Test]
        public void RealNamesAndAliasesMapToTheirEffects() => Assert.Multiple(() =>
        {
            Assert.That(RemoveSpellEffectHandler.EffectsFor("Nearsight"), Is.EqualTo(new[] { eEffect.Nearsight }));
            Assert.That(RemoveSpellEffectHandler.EffectsFor("Mesmerize"), Is.EqualTo(new[] { eEffect.Mez }));
            Assert.That(RemoveSpellEffectHandler.EffectsFor("StyleBleeding"), Is.EqualTo(new[] { eEffect.Bleed }));
            Assert.That(RemoveSpellEffectHandler.EffectsFor("WarlockSpeedDecrease"), Does.Contain(eEffect.Snare));
            Assert.That(RemoveSpellEffectHandler.EffectsFor("PveResurrectionIllness"), Is.EqualTo(new[] { eEffect.ResurrectionIllness }));
        });
    }
}
