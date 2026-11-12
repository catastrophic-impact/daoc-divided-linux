using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// DR-122 (2026-11-08): a Paladin in a player's party "led the pull" and then only cast heals while three giants beat
    /// the player for 65 s. BOT-AI-2026-11-07 item 5: a hybrid tank holds the monsters first and heals only below half.
    /// </summary>
    [TestFixture]
    public class UT_HybridTankFirst
    {
        [Test]
        public void OnlyThePaladinIsAHybridTank()
        {
            Assert.That(BotPartyRoles.IsHybridTank(eCharacterClass.Paladin), Is.True);
            foreach (eCharacterClass other in new[] { eCharacterClass.Armsman, eCharacterClass.Warrior, eCharacterClass.Hero,
                         eCharacterClass.Cleric, eCharacterClass.Warden, eCharacterClass.Friar, eCharacterClass.Valkyrie })
                Assert.That(BotPartyRoles.IsHybridTank(other), Is.False, other.ToString());
        }

        [TestCase(79, false)]
        [TestCase(50, false)]
        [TestCase(49, true)]
        [TestCase(10, true)]
        public void APaladinInAPlayersPartyFightsUntilSomeoneIsBelowHalf(int lowest, bool healsFirst)
        {
            Assert.That(BotPartyRoles.HealsBeforeFighting(eCharacterClass.Paladin, true, 2, lowest), Is.EqualTo(healsFirst));
        }

        [Test]
        public void OtherHealersAndOtherPartiesKeepHealingFirst()
        {
            Assert.That(BotPartyRoles.HealsBeforeFighting(eCharacterClass.Cleric, true, 2, 79), Is.True);
            Assert.That(BotPartyRoles.HealsBeforeFighting(eCharacterClass.Friar, true, 3, 79), Is.True);
            // Alone it is its own healer; in a bot party the coordinator may have given it the healer's job.
            Assert.That(BotPartyRoles.HealsBeforeFighting(eCharacterClass.Paladin, true, 1, 79), Is.True);
            Assert.That(BotPartyRoles.HealsBeforeFighting(eCharacterClass.Paladin, false, 4, 79), Is.True);
        }
    }
}
