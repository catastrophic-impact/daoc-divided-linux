using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Night playtest 2026-11-04: Hunters drew shots with a monster on them and cast Wolfen Frenzy with no pet.</summary>
    [TestFixture]
    public class UT_HunterBotFixes
    {
        [Test]
        public void AnArcherHitInMeleeHoldsItsShot()
        {
            Assert.That(BotArcheryShots.HoldShotUnderMeleePressure(beingInterruptedByOther: true, attackerInMeleeRange: true), Is.True);
            // A caster or archer hit from range keeps shooting back; nobody beside it, it draws as before.
            Assert.That(BotArcheryShots.HoldShotUnderMeleePressure(true, false), Is.False);
            Assert.That(BotArcheryShots.HoldShotUnderMeleePressure(false, true), Is.False);
        }

        [Test]
        public void APetSpellNeedsALivingPetInReach()
        {
            Assert.That(BotBrain.IsPetTargetSpell(eSpellTarget.PET), Is.True);
            Assert.That(BotBrain.IsPetTargetSpell(eSpellTarget.CONTROLLED), Is.True);
            Assert.That(BotBrain.IsPetTargetSpell(eSpellTarget.SELF), Is.False);
            Assert.That(BotBrain.PetSpellHasHolder(petAlive: false, petInReach: false), Is.False);
            Assert.That(BotBrain.PetSpellHasHolder(true, false), Is.False);
            Assert.That(BotBrain.PetSpellHasHolder(true, true), Is.True);
        }
    }
}
