using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner 2026-11-09: account companions earned no realm points beside their player. They now earn them like a
    /// gamebot in the group, keep them on the character, and /spawn helpers still earn nothing.
    /// </summary>
    [TestFixture]
    public class UT_AccountCompanionRealmPoints
    {
        private static GameBot Bot(bool autonomous, bool helper, bool accountCompanion)
        {
            var bot = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, new ArrayList(1));
            Set(bot, nameof(GameBot.IsAutonomousWorldBot), autonomous);
            Set(bot, nameof(GameBot.IsTemporaryGroupHelper), helper);
            Set(bot, nameof(GameBot.IsAccountCompanion), accountCompanion);
            return bot;
        }

        private static void Set(GameBot bot, string property, object value) =>
            typeof(GameBot).GetProperty(property).SetValue(bot, value);

        [Test]
        public void AccountCompanionsAndGamebotsEarn_SpawnHelpersDoNot()
        {
            Assert.That(AutonomousBotRealmPointRewards.EarnsRealmPoints(Bot(true, false, false)), Is.True, "gamebot");
            Assert.That(AutonomousBotRealmPointRewards.EarnsRealmPoints(Bot(false, true, true)), Is.True, "account companion");
            Assert.That(AutonomousBotRealmPointRewards.EarnsRealmPoints(Bot(false, true, false)), Is.False, "/spawn helper");
        }

        [Test]
        public void CompanionRealmPoints_AddUpAndRaiseTheRealmRank()
        {
            GameBot companion = Bot(false, true, true);
            Set(companion, nameof(GameBot.CompanionRealmPoints), 0L);

            companion.GainRealmPoints(20, true);
            Assert.That(companion.CompanionRealmPoints, Is.EqualTo(20));
            Assert.That(companion.RealmLevel, Is.EqualTo(1), "RR1L1 below 25 realm points");

            companion.GainRealmPoints(GamePlayer.REALMPOINTS_FOR_LEVEL[3], true);
            Assert.That(companion.RealmLevel, Is.EqualTo(3), "RR1L3 at 145 realm points");
            Assert.That(companion.AutonomousRealmPoints, Is.EqualTo(0), "a companion's points are the character's, not a gamebot's");
        }

        [Test]
        public void SpawnHelper_GainsNothing()
        {
            GameBot helper = Bot(false, true, false);
            helper.GainRealmPoints(500, true);
            Assert.That(helper.CompanionRealmPoints, Is.EqualTo(0));
            Assert.That(helper.RealmLevel, Is.EqualTo(0));
        }

        [Test]
        public void CompanionInAPlayerGroup_GetsThePlayersShare()
        {
            // Player and companion together did all the damage: both use the group's count and damage, so the
            // companion's share equals the player's when they are the same level and rank.
            int victim = AutonomousBotRealmPointRewards.GetPlayerEquivalentRealmPointValue(50, 0);
            int awarder = AutonomousBotRealmPointRewards.GetPlayerEquivalentRealmPointValue(50, 0);
            int share = AutonomousBotRealmPointRewards.CalculateRealmPointReward(victim, 0, awarder, 0, 2, 2, 1.0, true);
            Assert.That(share, Is.GreaterThan(0));
            Assert.That(share, Is.EqualTo(AutonomousBotRealmPointRewards.ApplyKillBonus(victim / 2) * 9 / 8).Within(1));
        }
    }
}
