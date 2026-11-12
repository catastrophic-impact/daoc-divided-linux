using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Raid route rules from the Living Server 1.2 play test (2026-11-07; 7eb241f44, 5324734c6, 8ab2279ee).</summary>
    [TestFixture]
    public class UT_RaidRouteRules
    {
        // Easmarach: at 30% he climbs the waterfall and takes back an eighth of his health, so his health ran 30% -> 42%
        // -> 41, 40, 39, 37, 36, 35% with the raid shooting him, never below the earlier 30%. Only a new lowest counted as
        // damage, so he was skipped "no damage for five minutes".
        [Test]
        public void ASelfHealingBossWornDownAgainIsProgress()
        {
            int lowest = 30_000, last = 42_000;
            foreach (int health in new[] { 41_000, 40_000, 39_000, 37_000, 36_000, 35_000 })
            {
                Assert.That(RealmRaidDungeonRoute.TargetTookDamage(health, lowest, last), Is.True, $"{health}");
                last = health;
            }
        }

        [Test]
        public void ANewLowestIsProgress() =>
            Assert.That(RealmRaidDungeonRoute.TargetTookDamage(29_000, 30_000, 31_000), Is.True);

        [Test]
        public void NoDamageIsNoProgress()
        {
            Assert.That(RealmRaidDungeonRoute.TargetTookDamage(100_000, 100_000, 100_000), Is.False, "untouched");
            Assert.That(RealmRaidDungeonRoute.TargetTookDamage(45_000, 30_000, 44_000), Is.False, "healing back up");
        }

        [Test]
        public void AFullHealThenDamageIsProgress()
        {
            // Grand Summoner Govannon heals to full at 50%: the raid's damage from 100% down is progress at once.
            Assert.That(RealmRaidDungeonRoute.TargetTookDamage(99_000, 50_000, 100_000), Is.True);
            Assert.That(RealmRaidDungeonRoute.TargetTookDamage(100_000, 50_000, 99_000), Is.False, "the heal itself is not damage");
        }

        [Test]
        public void TheFirstLookIsNoProgressByItself() =>
            Assert.That(RealmRaidDungeonRoute.TargetTookDamage(50_000, 50_000, 0), Is.False);

        [Test]
        public void ARaidWithNoTargetTurnsOnTheNamedAttackingIt()
        {
            var attacker = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
            var target = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
            Assert.That(RealmRaidDungeonRoute.TurnsOnAttacker(null, attacker), Is.SameAs(attacker));
            Assert.That(RealmRaidDungeonRoute.TurnsOnAttacker(null, null), Is.Null);
            Assert.That(RealmRaidDungeonRoute.TurnsOnAttacker(target, attacker), Is.Null, "with a target the old rule decides");
        }
    }
}
