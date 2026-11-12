using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_MinstrelHybridCombat
    {
        [Test]
        public void AutonomousMinstrelsAreHybridWithoutChangingCompanionsOrCasters()
        {
            Assert.That(BotBrain.PrefersSpellRange(new ClassMinstrel(), false, true), Is.False);
            Assert.That(BotBrain.PrefersSpellRange(new ClassMinstrel(), true, true), Is.False);
            Assert.That(BotBrain.PrefersSpellRange(new ClassMinstrel(), true, false), Is.False);
            Assert.That(BotBrain.PrefersSpellRange(new ClassMinstrel(), false, false), Is.True);
            Assert.That(BotBrain.PrefersSpellRange(new ClassSorcerer(), false, true), Is.True);
            Assert.That(BotBrain.PrefersSpellRange(new ClassWizard(), true, true), Is.True);
        }

        /// <summary>DR-271: a player's grouped Shaman companion casts from range; autonomous and solo Shamans keep meleeing.</summary>
        [Test]
        public void GroupedShamanCompanionsStayAtSpellRange()
        {
            Assert.That(BotBrain.PrefersSpellRange(new ClassShaman(), true, false), Is.True);
            Assert.That(BotBrain.PrefersSpellRange(new ClassShaman(), false, false), Is.False);
            Assert.That(BotBrain.PrefersSpellRange(new ClassShaman(), true, true), Is.False);
            Assert.That(BotBrain.PrefersSpellRange(new ClassShaman(), false, true), Is.False);
            // Until it knows an attack spell it still melees, as a Druid does.
            Assert.That(BotBrain.FightsAtSpellRange(true, true, () => false), Is.False);
            Assert.That(BotBrain.FightsAtSpellRange(true, true, () => true), Is.True);
        }

        [TestCase("DirectDamage", 0, true)]
        [TestCase("DirectDamage", 2, false)]
        [TestCase("Charm", 0, false)]
        [TestCase("HealthRegenBuff", 0, false)]
        public void OnlyInstantDamageCanContinueMeleeWhileQueued(string type, double castTime, bool expected)
        {
            var spell = new Spell(new DbSpell { Type = type, Target = "Enemy", CastTime = castTime }, 1);
            Assert.That(MinstrelBotCombatPolicy.ContinueAfterInstantDamage(true, true, false, null, true, spell), Is.EqualTo(expected));
            Assert.That(MinstrelBotCombatPolicy.ContinueAfterInstantDamage(true, true, true, spell, false, null), Is.EqualTo(expected));
            Assert.That(MinstrelBotCombatPolicy.ContinueAfterInstantDamage(false, true, false, null, true, spell), Is.False);
        }

        [Test]
        public void UnknownQueuedRequestsDoNotBypassCastingSafety()
        {
            Assert.That(MinstrelBotCombatPolicy.ContinueAfterInstantDamage(true, true, false, null, true, null), Is.False);
            Assert.That(MinstrelBotCombatPolicy.ContinueAfterInstantDamage(true, true, true, null, false, null), Is.False);
            Assert.That(MinstrelBotCombatPolicy.ContinueAfterInstantDamage(true, true, false, null, false, null), Is.True);
        }
    }
}
