using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Night playtest 2026-11-04: only bosses read their tether, so an oaken treant (tether 4500) chased a tester
    /// 8,300 units and Atlantis mobs with no template chased forever.
    /// </summary>
    [TestFixture]
    public class UT_MobLeash
    {
        private sealed class TestBossBrain : StandardMobBrain { }

        [Test]
        public void TemplateTetherIsTheLeash()
        {
            Assert.That(StandardMobBrain.ResolveLeashRange(true, 4500, 0), Is.EqualTo(4500));
            Assert.That(StandardMobBrain.ResolveLeashRange(true, 8000, 0), Is.EqualTo(8000));
        }

        [Test]
        public void MobsWithoutATetherGetTheDefault()
        {
            Assert.That(StandardMobBrain.ResolveLeashRange(true, 0, 0), Is.EqualTo(StandardMobBrain.DEFAULT_LEASH_RANGE));
            Assert.That(StandardMobBrain.ResolveLeashRange(true, 2, 0), Is.EqualTo(StandardMobBrain.DEFAULT_LEASH_RANGE),
                "a Bonedancer-style rank number in the tether column is not a leash");
        }

        [Test]
        public void RoamersGetTheirRoamingRangeOnTop()
        {
            Assert.That(StandardMobBrain.ResolveLeashRange(true, 4500, 1500), Is.EqualTo(6000));
        }

        [Test]
        public void OnlyOrdinaryMobsLeashHere()
        {
            Assert.That(StandardMobBrain.ResolveLeashRange(false, 4500, 0), Is.Zero);
        }

        [Test]
        public void LeashTripsOnlyPastTheRange()
        {
            Point3D spawn = new(100000, 100000, 3000);
            Assert.That(StandardMobBrain.IsBeyondLeash(spawn, new Point3D(104000, 100000, 3000), 4500), Is.False);
            Assert.That(StandardMobBrain.IsBeyondLeash(spawn, new Point3D(108300, 100000, 3000), 4500), Is.True, "the treant at 8,300 units");
            Assert.That(StandardMobBrain.IsBeyondLeash(null, new Point3D(108300, 100000, 3000), 4500), Is.False, "no anchor, no leash");
            Assert.That(StandardMobBrain.IsBeyondLeash(spawn, new Point3D(108300, 100000, 3000), 0), Is.False, "no range, no leash");
        }

        [Test]
        public void OtherBrainsKeepTheirOwnRules()
        {
            Assert.That(new StandardMobBrain().LeashRange, Is.Zero, "no body yet");
            Assert.That(new TestBossBrain().LeashRange, Is.Zero, "a scripted brain");
        }
    }
}
