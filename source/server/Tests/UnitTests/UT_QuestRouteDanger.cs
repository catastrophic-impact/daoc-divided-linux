using DOL.GS;
using NUnit.Framework;
using Spot = DOL.GS.AutonomousBotQuests.NpcSpot;

namespace DOL.UnitTests
{
    /// <summary>
    /// DR-076 (2026-11-08): quest 20081 step 1 sent level 30 bots for luch catchers past a level 52-54 pooka camp in
    /// Lough Gur (one bot died 28 times). A quest step now goes to a spot the bot can fight whose road is clear.
    /// </summary>
    [TestFixture]
    public class UT_QuestRouteDanger
    {
        private const ushort Lough = 200;
        private static Spot Monster(string name, int x, int y, int level, bool aggressive = true, ushort region = Lough) =>
            new(name, region, x, y, 0, eRealm.None, level, true, aggressive, 500);

        [Test]
        public void ARoadPastAPurpleCampIsBlocked()
        {
            var grid = AutonomousQuestRouteDanger.Build([Monster("pooka", 305_000, 700_200, 53)]);
            AutonomousQuestRouteDanger.Danger? blocker = grid.Blocker(Lough, 300_000, 700_000, 310_000, 700_000, 30);
            Assert.That(blocker, Is.Not.Null);
            Assert.That(blocker.Value.Name, Is.EqualTo("pooka"));
            // The same camp far off the road, a peaceful one, or one a level 50 walks past, blocks nothing.
            Assert.That(grid.Blocker(Lough, 300_000, 690_000, 310_000, 690_000, 30), Is.Null);
            Assert.That(AutonomousQuestRouteDanger.Build([Monster("pooka", 305_000, 700_200, 53, aggressive: false)])
                .Blocker(Lough, 300_000, 700_000, 310_000, 700_000, 30), Is.Null);
            Assert.That(grid.Blocker(Lough, 300_000, 700_000, 310_000, 700_000, 50), Is.Null);
            Assert.That(grid.Blocker(201, 300_000, 700_000, 310_000, 700_000, 30), Is.Null);
        }

        [Test]
        public void TheStepGoesToTheSpotWithAClearRoad()
        {
            var behindTheCamp = Monster("luch catcher", 310_000, 700_000, 32);
            var bindSide = Monster("luch catcher", 280_000, 700_000, 32);
            var grid = AutonomousQuestRouteDanger.Build([Monster("pooka", 305_000, 700_200, 53), behindTheCamp, bindSide]);
            Spot chosen = grid.Choose([behindTheCamp, bindSide], Lough, 300_000, 700_000, 30, true, out string blocked);
            Assert.That(chosen, Is.SameAs(bindSide));
            Assert.That(blocked, Is.Null);
        }

        [Test]
        public void WithNoSafeSpotTheStepIsSetAsideAsARouteBlock()
        {
            var behindTheCamp = Monster("luch catcher", 310_000, 700_000, 32);
            var tooStrong = Monster("luch catcher", 280_000, 700_000, 43);
            var grid = AutonomousQuestRouteDanger.Build([Monster("pooka", 305_000, 700_200, 53), behindTheCamp, tooStrong]);
            Spot chosen = grid.Choose([behindTheCamp, tooStrong], Lough, 300_000, 700_000, 30, true, out string blocked);
            Assert.That(chosen, Is.Null);
            Assert.That(blocked, Does.StartWith("Route blocked by pooka (level 53)"));
            Assert.That(AutonomousBotQuests.Classify(blocked), Is.EqualTo(eBotQuestPark.Route));
        }

        [Test]
        public void TheTargetsOwnLevelIsJudgedByCanFightNotAsARouteThreat()
        {
            // An orange luch catcher is a fair quest fight; the target's own spot is not a "monster in the way".
            var orange = Monster("luch catcher", 302_000, 700_000, 33);
            var grid = AutonomousQuestRouteDanger.Build([orange]);
            Assert.That(grid.Choose([orange], Lough, 300_000, 700_000, 30, true, out _), Is.SameAs(orange));
            // A talk step to a character never refuses on the character's own level.
            var giver = new Spot("Larylle", Lough, 302_000, 700_000, 0, eRealm.Hibernia, 60, false);
            Assert.That(grid.Choose([giver], Lough, 300_000, 700_000, 30, false, out _), Is.SameAs(giver));
        }

        [Test]
        public void ASpotInAnotherRegionIsJudgedByItsSurroundings()
        {
            var camped = Monster("water badger", 50_000, 50_000, 30, region: 201);
            var grid = AutonomousQuestRouteDanger.Build([Monster("pooka", 50_300, 50_000, 55, region: 201), camped]);
            Assert.That(grid.Choose([camped], Lough, 300_000, 700_000, 30, true, out string blocked), Is.Null);
            Assert.That(blocked, Does.Contain("pooka"));
        }
    }
}
