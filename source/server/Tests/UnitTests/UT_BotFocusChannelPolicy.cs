using DOL.Database;
using DOL.GS;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Discord 2026-11-09: a Heretic companion never left its damage focus to heal (BotFocusChannelPolicy).</summary>
    [TestFixture]
    public class UT_BotFocusChannelPolicy
    {
        private static Spell Singe() => new FocusSpell(new Spell(new DbSpell { SpellID = 70001, Name = "Arawn's Singe",
            Type = "RampingDamageFocus", Target = "Enemy", Range = 1650, Duration = 16, Frequency = 15, Pulse = 1 }, 1));

        private static Spell Heal() => new(new DbSpell { SpellID = 70002, Name = "Major Restoration", Type = "Heal",
            Target = "Realm" }, 1);

        [Test]
        public void ChannelledDamageFocusLetsHealsIn() =>
            Assert.That(BotFocusChannelPolicy.ChecksHealsDuringChannel(true, Singe()), Is.True);

        [Test]
        public void FocusStillBeingCastIsLeftAlone() =>
            Assert.That(BotFocusChannelPolicy.ChecksHealsDuringChannel(false, Singe()), Is.False);

        [Test]
        public void OrdinaryCastsAreLeftAlone()
        {
            Assert.That(BotFocusChannelPolicy.ChecksHealsDuringChannel(true, Heal()), Is.False);
            Assert.That(BotFocusChannelPolicy.ChecksHealsDuringChannel(true, null), Is.False);
            Assert.That(BotFocusChannelPolicy.ChecksHealsDuringChannel((CastingComponent)null), Is.False);
        }
    }
}
