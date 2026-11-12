using DOL.Database;
using DOL.GS;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>DR-216: the Cloak of the Loyal abilities' effect types and the stats they raise.</summary>
    [TestFixture]
    public class UT_LoyaltyCloakAbilities
    {
        private static Spell Of(eSpellType type, string target = "Self", int radius = 0, int range = 0) =>
            new(new DbSpell { SpellID = 1, Type = type.ToString(), Target = target, Value = 10, Duration = 30, Radius = radius, Range = range }, 1);

        [Test]
        public void EachBuffRaisesItsStats()
        {
            Assert.That(EffectHelper.GetEffectFromSpell(Of(eSpellType.ArtifactSpellDamage)), Is.EqualTo(eEffect.SpellDamageBuff));
            Assert.That(EffectHelper.GetPropertiesFromEffect(eEffect.SpellDamageBuff), Is.EqualTo(new[] { eProperty.SpellDamage }));
            Assert.That(EffectHelper.GetPropertiesFromEffect(eEffect.MeleeCritBuff), Is.EqualTo(new[] { eProperty.CriticalMeleeHitChance }));
            Assert.That(EffectHelper.GetPropertiesFromEffect(eEffect.ParryBlockBuff), Is.EqualTo(new[] { eProperty.ParryChance, eProperty.BlockChance }));
            Assert.That(EffectHelper.GetPropertiesFromEffect(eEffect.ArcaneLeadership), Is.EqualTo(new[] { eProperty.SpellRange, eProperty.ResistPierce }));
            Assert.That(EffectHelper.GetPropertiesFromEffect(eEffect.ControlShrug), Has.Count.EqualTo(6));
        }

        [Test]
        public void NewSpellTypesHaveTheirOwnEffects()
        {
            Assert.That(EffectHelper.GetEffectFromSpell(Of(eSpellType.MeleeCritBuff)), Is.EqualTo(eEffect.MeleeCritBuff));
            Assert.That(EffectHelper.GetEffectFromSpell(Of(eSpellType.ParryBlockBuff)), Is.EqualTo(eEffect.ParryBlockBuff));
            Assert.That(EffectHelper.GetEffectFromSpell(Of(eSpellType.LoyalArcaneLeadership)), Is.EqualTo(eEffect.ArcaneLeadership));
            Assert.That(EffectHelper.GetEffectFromSpell(Of(eSpellType.IgnoreBladeturn)), Is.EqualTo(eEffect.IgnoreBladeturn));
            Assert.That(EffectHelper.GetEffectFromSpell(Of(eSpellType.GroupDDRadius)), Is.EqualTo(eEffect.GroupDDRadius));
            Assert.That(EffectHelper.GetEffectFromSpell(Of(eSpellType.ControlShrug)), Is.EqualTo(eEffect.ControlShrug));
            Assert.That(EffectHelper.GetEffectFromSpell(Of(eSpellType.DiseaseWard)), Is.EqualTo(eEffect.DiseaseWard));
            Assert.That(EffectHelper.GetEffectFromSpell(Of(eSpellType.InCombatSpeed)), Is.EqualTo(eEffect.MovementSpeedBuff));
        }

        [Test]
        public void OnlySingleTargetDirectDamageGetsTheEnchanterRadius()
        {
            // No caster effect: the radius is never changed.
            Assert.That(LoyaltyCloaks.AdjustDirectDamageRadius(null, Of(eSpellType.DirectDamage, "Enemy", 0, 1500), 0), Is.EqualTo(0));
            // Already an area spell, a bolt or a self spell: left alone before the caster is even looked at.
            Assert.That(LoyaltyCloaks.AdjustDirectDamageRadius(null, Of(eSpellType.DirectDamage, "Enemy", 350, 1500), 350), Is.EqualTo(350));
            Assert.That(LoyaltyCloaks.AdjustDirectDamageRadius(null, Of(eSpellType.Bolt, "Enemy", 0, 1500), 0), Is.EqualTo(0));
            Assert.That(LoyaltyCloaks.AdjustDirectDamageRadius(null, Of(eSpellType.DirectDamage, "Self"), 0), Is.EqualTo(0));
        }
    }
}
