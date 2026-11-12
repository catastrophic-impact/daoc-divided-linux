using System;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_BotWatchAdmin
    {
        private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void ItIsOffUnlessTheWorldTurnsItOn()
        {
            var field = typeof(Properties).GetField(nameof(Properties.BOT_WATCH_ADMIN));
            var attr = (ServerPropertyAttribute) Attribute.GetCustomAttribute(field, typeof(ServerPropertyAttribute));

            Assert.That(attr.Key, Is.EqualTo("bot_watch_admin"));
            Assert.That(attr.DefaultValue, Is.EqualTo(false));
        }

        [TestCase(1, "1-9")]
        [TestCase(9, "1-9")]
        [TestCase(10, "10-19")]
        [TestCase(34, "20-34")]
        [TestCase(49, "35-49")]
        [TestCase(50, "50")]
        public void LevelBands(int level, string band) => Assert.That(BotWatchAdmin.LevelBand(level), Is.EqualTo(band));

        [Test]
        public void AlbionIsNotNamedByItsAlias()
        {
            Assert.That(BotWatchAdmin.RealmName(eRealm.Albion), Is.EqualTo("Albion"));
            Assert.That(BotWatchAdmin.RealmName(eRealm.Hibernia), Is.EqualTo("Hibernia"));
        }

        [Test]
        public void MinutesSinceProgressReadsTheRecord()
        {
            var record = new OfflineWorldBotRecord { LastMeaningfulProgressUtc = "2026-11-08T11:40:00.0000000Z" };

            Assert.That(BotWatchAdmin.MinutesSinceProgress(record, Now), Is.EqualTo(20).Within(0.01));
            Assert.That(BotWatchAdmin.MinutesSinceProgress(new OfflineWorldBotRecord(), Now), Is.Null);
            Assert.That(BotWatchAdmin.MinutesSinceProgress(new OfflineWorldBotRecord { LastMeaningfulProgressUtc = "never" }, Now), Is.Null);
            Assert.That(BotWatchAdmin.MinutesSinceProgress(null, Now), Is.Null);
        }

        [Test]
        public void OnlyLiveBotsPastTheThresholdAreStalled()
        {
            var record = new OfflineWorldBotRecord { LastMeaningfulProgressUtc = "2026-11-08T11:40:00Z" };

            Assert.That(BotWatchAdmin.IsStalled(record, true, Now, TimeSpan.FromMinutes(15)), Is.True);
            Assert.That(BotWatchAdmin.IsStalled(record, true, Now, TimeSpan.FromMinutes(30)), Is.False);
            Assert.That(BotWatchAdmin.IsStalled(record, false, Now, TimeSpan.FromMinutes(15)), Is.False);
            Assert.That(BotWatchAdmin.IsStalled(new OfflineWorldBotRecord(), true, Now, TimeSpan.FromMinutes(15)), Is.False);
        }
    }
}
