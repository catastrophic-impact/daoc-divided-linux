using System;
using System.Collections.Generic;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Fixes from the 600-per-realm overnight run of 2026-10-19/20.</summary>
    [TestFixture]
    public class UT_OvernightRunFixes
    {
        // Contested raids stay; only the realm that landed the final kill wins (Summoner's Hall, 2026-10-20).
        [TestCase(true, eRealm.None, "Boss defeated")]
        [TestCase(false, eRealm.Hibernia, "Beaten to it")]
        [TestCase(true, eRealm.Hibernia, "Boss defeated")]   // credited first; a later rival kill changes nothing
        [TestCase(false, eRealm.None, "Boss defeated")]      // an uncontested kill with no damage record still counts
        public void OnlyTheRealmThatKilledTheFinalBossWins(bool credited, eRealm beatenBy, string outcome) =>
            Assert.That(AutonomousRealmRaid.CompletionOutcome(credited, beatenBy), Is.EqualTo(outcome));

        // Tuscaran Glacier, 2026-10-20: the Birghir muster stayed at 0 for an hour while Suttung killed the raid.
        [TestCase(0L, 60 * 60_000L, false)]                  // never empty
        [TestCase(1L, 19 * 60_000L, false)]
        [TestCase(1L, 20 * 60_000L + 1, true)]
        [TestCase(10 * 60_000L, 25 * 60_000L, false)]
        public void AMusterEmptyForTwentyMinutesIsSetAside(long emptySince, long now, bool unreachable) =>
            Assert.That(RealmRaidDungeonRoute.MusterUnreachable(emptySince, now), Is.EqualTo(unreachable));

        // Marfach Cavern, 2026-10-20: eight-bot parties pulled Black Lady as a corridor blocker.
        [TestCase(false, false, false, true)]   // an ordinary corridor monster is still cleared
        [TestCase(true, false, false, false)]   // an epic monster nobody came for is gone around or passed
        [TestCase(true, true, false, true)]     // the bots' own target
        [TestCase(true, false, true, true)]     // a raid inside its own dungeon fights its encounters
        public void APartyPullsAnEpicOnlyWhenItCameForIt(bool epic, bool ownTarget, bool inOwnRaidDungeon, bool pulls) =>
            Assert.That(AutonomousDungeonPolicy.PullsOnRoute(epic, ownTarget, inOwnRaidDungeon), Is.EqualTo(pulls));

        // Overnight run 2026-11-02: essence shredder packs killed 206 solo level 50s approaching Darkness Falls camps.
        [TestCase(false, true, false, false, ConColor.RED, 0, true)]      // a red alone
        [TestCase(false, true, false, false, ConColor.ORANGE, 2, true)]   // an orange pack
        [TestCase(false, true, false, false, ConColor.ORANGE, 1, false)]  // an orange pair is fought
        [TestCase(false, true, false, false, ConColor.YELLOW, 5, false)]
        [TestCase(true, true, false, false, ConColor.RED, 3, false)]      // a party clears it as before
        [TestCase(false, false, false, false, ConColor.RED, 3, false)]    // on the way out there is no other way
        [TestCase(false, true, true, false, ConColor.RED, 3, false)]      // rejoining the leader
        [TestCase(false, true, false, true, ConColor.RED, 0, false)]      // the camp's own monster
        public void ASoloBotTurnsBackFromAHardDungeonBlocker(bool grouped, bool hasCamp, bool rejoining, bool ownTarget,
            ConColor con, int adds, bool turnsBack) =>
            Assert.That(AutonomousDungeonPolicy.SoloTurnsBackFrom(grouped, hasCamp, rejoining, ownTarget, con, adds), Is.EqualTo(turnsBack));

        /// <summary>
        /// DR-277: a bot inside a blocker's reach had every camp of the region skipped. Only the ways that lead nearer to
        /// the blocker are blocked from there; from outside nothing changes.
        /// </summary>
        [Test]
        public void ABotInsideABlockersReachKeepsTheWaysOut()
        {
            System.Numerics.Vector2 blocker = new(350_000, 590_000);
            const float reach = 1_500;
            System.Numerics.Vector2 inside = new(350_600, 590_000); // 600 from the blocker
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRouteBlockMemory.Blocks(inside, new(360_000, 590_000), blocker, reach), Is.False, "straight away from it");
                Assert.That(AutonomousRouteBlockMemory.Blocks(inside, new(350_600, 600_000), blocker, reach), Is.False, "sideways: never nearer than now");
                Assert.That(AutonomousRouteBlockMemory.Blocks(inside, new(340_000, 590_000), blocker, reach), Is.True, "through the blocker");
                Assert.That(AutonomousRouteBlockMemory.Blocks(inside, new(350_100, 590_000), blocker, reach), Is.True, "a camp nearer to the blocker");
                Assert.That(AutonomousRouteBlockMemory.Blocks(inside, new(350_500, 590_000), blocker, reach), Is.False, "a step within the tolerance");

                System.Numerics.Vector2 outside = new(355_000, 590_000);
                Assert.That(AutonomousRouteBlockMemory.Blocks(outside, new(345_000, 590_000), blocker, reach), Is.True, "from outside: through the reach");
                Assert.That(AutonomousRouteBlockMemory.Blocks(outside, new(351_000, 590_000), blocker, reach), Is.True, "from outside: a camp inside the reach");
                Assert.That(AutonomousRouteBlockMemory.Blocks(outside, new(355_000, 600_000), blocker, reach), Is.False, "from outside: past it");
            });

            AutonomousRouteBlockMemory.Clear();
            long now = 1_000_000;
            long band5 = AutonomousRouteBlockMemory.WordOfMouthKey(eRealm.Hibernia, 8, 200);
            AutonomousRouteBlockMemory.Note(band5, 200, blocker, reach, now, AutonomousRouteBlockMemory.WordOfMouthSlots);
            int open = 0;
            for (int degrees = 0; degrees < 360; degrees += 10)
            {
                System.Numerics.Vector2 camp = inside + 8_000 * new System.Numerics.Vector2(
                    System.MathF.Cos(degrees * System.MathF.PI / 180), System.MathF.Sin(degrees * System.MathF.PI / 180));
                if (!AutonomousRouteBlockMemory.Crosses(band5, 200, inside, camp, now + 1))
                    open++;
            }
            Assert.That(open, Is.InRange(25, 29), "all but the quarter of the compass that leads at the blocker stays open");
            Assert.That(AutonomousRouteBlockMemory.TryNearest(band5, 200, inside, now + 1, out System.Numerics.Vector2 at, out float radius), Is.True);
            Assert.That((at, radius), Is.EqualTo((blocker, reach)));
            Assert.That(AutonomousRouteBlockMemory.TryNearest(band5, 201, inside, now + 1, out _, out _), Is.False, "another region");
            Assert.That(AutonomousRouteBlockMemory.Within(band5, 200, new(351_000, 590_000), now + 1), Is.True, "a camp inside the reach never comes back");
            Assert.That(AutonomousRouteBlockMemory.Within(band5, 200, new(352_000, 590_000), now + 1), Is.False);
            Assert.That(AutonomousRouteBlockMemory.Within(band5, 200, new(351_000, 590_000), now + AutonomousRouteBlockMemory.CooldownMilliseconds + 1), Is.False, "forgotten");
            AutonomousRouteBlockMemory.Clear();
        }

        [Test]
        public void ARedOrPurpleBlockerIsPassedOnToBotsOfTheSameLevel()
        {
            AutonomousRouteBlockMemory.Clear();
            long now = 1_000_000;
            long band10 = AutonomousRouteBlockMemory.WordOfMouthKey(eRealm.Albion, 11, 1);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRouteBlockMemory.PassedOn(ConColor.PURPLE), Is.True);
                Assert.That(AutonomousRouteBlockMemory.PassedOn(ConColor.ORANGE), Is.False);
                Assert.That(band10, Is.LessThan(0), "never a bot or party key");
                Assert.That(AutonomousRouteBlockMemory.WordOfMouthKey(eRealm.Albion, 14, 1), Is.EqualTo(band10));
                Assert.That(AutonomousRouteBlockMemory.WordOfMouthKey(eRealm.Albion, 15, 1), Is.Not.EqualTo(band10));
                Assert.That(AutonomousRouteBlockMemory.WordOfMouthKey(eRealm.Hibernia, 11, 1), Is.Not.EqualTo(band10));
                Assert.That(AutonomousRouteBlockMemory.WordOfMouthKey(eRealm.Albion, 11, 2), Is.Not.EqualTo(band10));
            });
            for (int i = 0; i < 10; i++)   // ten fields of maidens: a shared key holds more than four
                AutonomousRouteBlockMemory.Note(band10, 1, new(500_000 + i * 5_000, 380_000), 830, now, AutonomousRouteBlockMemory.WordOfMouthSlots);
            Assert.That(AutonomousRouteBlockMemory.Crosses(band10, 1, new(500_000, 370_000), new(500_000, 390_000), now), Is.True);
            Assert.That(AutonomousRouteBlockMemory.Crosses(band10, 1, new(545_000, 370_000), new(545_000, 390_000), now), Is.True);
            AutonomousRouteBlockMemory.Clear();
        }

        [Test]
        public void ThreeDeathsInOnePlaceMakeItTheBotsOwnDangerSpot()
        {
            DateTime now = new(2026, 10, 20, 4, 0, 0, DateTimeKind.Utc);
            var deaths = new List<AutonomousRepeatDeathSpots.Death>
            {
                new(200, 368211, 652323, now.AddMinutes(-50)),
                new(200, 367881, 653097, now.AddMinutes(-30)),
            };
            Assert.That(AutonomousRepeatDeathSpots.Spots(deaths, 200, now), Is.Empty, "two deaths are not a pattern yet");

            deaths.Add(new(200, 368177, 652326, now.AddMinutes(-5)));
            var spots = AutonomousRepeatDeathSpots.Spots(deaths, 200, now);
            Assert.That(spots, Has.Count.EqualTo(1));
            Assert.That(spots[0], Is.EqualTo((368177, 652326)), "the latest death marks the spot");
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRepeatDeathSpots.Spots(deaths, 100, now), Is.Empty, "another region");
                Assert.That(AutonomousRepeatDeathSpots.Spots(deaths, 200, now.AddHours(3)), Is.Empty, "deaths older than three hours are forgotten");
            });

            deaths.Add(new(200, 300000, 600000, now.AddMinutes(-1)));   // far away: not part of the cluster
            Assert.That(AutonomousRepeatDeathSpots.Spots(deaths, 200, now), Has.Count.EqualTo(1));
        }

        [Test]
        public void ABotsOwnSpotsKeepItsCampsAwayAndForgetOtherBots()
        {
            AutonomousRepeatDeathSpots.Clear();
            try
            {
                DateTime now = DateTime.UtcNow;
                for (int i = 0; i < 3; i++)
                    AutonomousRepeatDeathSpots.Record(42, 200, 368200 + i * 50, 652300, now.AddMinutes(-i));
                var spots = AutonomousRepeatDeathSpots.AllSpots(42, now);
                Assert.Multiple(() =>
                {
                    Assert.That(AutonomousRepeatDeathSpots.IsNear(spots, 200, 369000, 652800), Is.True, "a camp beside the spot");
                    Assert.That(AutonomousRepeatDeathSpots.IsNear(spots, 200, 380000, 652300), Is.False, "a camp well away");
                    Assert.That(AutonomousRepeatDeathSpots.AllSpots(43, now), Is.Empty, "another bot's deaths are its own");
                });
            }
            finally { AutonomousRepeatDeathSpots.Clear(); }
        }

        // Blendrake Faste, 2026-10-20: Midgard kept ~80 attacking Caer Sursbrooke while its own keep fell undefended.
        [TestCase(0, 24, 80, 80, 8, true)]      // nobody defending, the attack keeps 72 of 80
        [TestCase(11, 24, 80, 80, 8, true)]     // still under half the defense cap
        [TestCase(12, 24, 80, 80, 8, false)]    // half the cap defends: the attack keeps its army
        [TestCase(0, 24, 44, 80, 8, false)]     // the attack is already down to about half: it stays
        [TestCase(0, 24, 48, 80, 8, true)]      // 40 left of the 80 that marched
        public void AnAttackingWarbandBreaksOffToDefendItsOwnKeep(int defenders, int cap, int attackers, int marched, int size, bool breaks) =>
            Assert.That(RealmEventPolicy.BreaksOffToDefend(defenders, cap, attackers, marched, size), Is.EqualTo(breaks));

        [TestCase(true, 30, true)]
        [TestCase(true, 0, false)]    // a battle opened before its army arrived
        [TestCase(false, 30, false)]  // attackers still marching: the defense musters as before
        public void DefendersGoStraightInOnceTheAttackersAreAtTheKeep(bool battleStarted, int attackersPresent, bool skip) =>
            Assert.That(RealmEventPolicy.DefendersSkipMuster(battleStarted, attackersPresent), Is.EqualTo(skip));

        // Gjalpinulva, 2026-10-20: immune beyond 2,000 from her lair, but her tether is 2,500.
        [TestCase(1237, -2291, true)]    // where Midgard raiders fought and died
        [TestCase(1400, 1400, false)]    // about 1,980: still in the lair
        [TestCase(0, 2001, true)]
        public void ADragonOutsideItsLairIsTreatedAsOutOfTether(int dx, int dy, bool outside) =>
            Assert.That(DragonLairPlacement.IsOutOfLair(dx, dy), Is.EqualTo(outside));

        [Test]
        public void EveryDragonIsImmuneOutsideTheSameLairRadius() => Assert.Multiple(() =>
        {
            Assert.That(MidGjalpinulva.LairRadius, Is.EqualTo(DragonLairPlacement.LairRadius));
            Assert.That(AlbGolestandt.LairRadius, Is.EqualTo(DragonLairPlacement.LairRadius));
            Assert.That(HibCuuldurach.LairRadius, Is.EqualTo(DragonLairPlacement.LairRadius));
        });

        // Sheeroe Hills, 2026-10-20: the release bind was about 2,300 units from the glimmer pack that killed the bot.
        [TestCase(369726, 651032, false)]   // the bind they kept releasing to
        [TestCase(380000, 651032, true)]    // a bind about 12,000 away
        public void ABotThatKeepsDyingReleasesToABindWellAway(int bindX, int bindY, bool clear) =>
            Assert.That(BotReleaseBindPoints.ClearOf(bindX, bindY, new List<(int X, int Y)> { (368010, 652514) },
                GameBot.ReleaseClearanceFromDeathSpot), Is.EqualTo(clear));

        // Aegir's Landing, 2026-10-19: level-1 parties were sent to the Shrouded Isles and died to level 18-21 cats.
        [TestCase(eRealm.Midgard, (ushort)151, 1, false)]
        [TestCase(eRealm.Midgard, (ushort)151, 9, false)]
        [TestCase(eRealm.Midgard, (ushort)151, 10, true)]
        [TestCase(eRealm.Albion, (ushort)51, 5, false)]
        [TestCase(eRealm.Hibernia, (ushort)181, 5, false)]
        [TestCase(eRealm.Midgard, (ushort)100, 1, true)]     // classic Midgard is where new bots level
        public void NewBotsLevelInTheClassicZonesFirst(eRealm realm, ushort region, int level, bool allowed) =>
            Assert.That(AutonomousOrdinaryPveRealmPolicy.CanAssignCamp(realm, region, 0, level), Is.EqualTo(allowed));
    }
}
