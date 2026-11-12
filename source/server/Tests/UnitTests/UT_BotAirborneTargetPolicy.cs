using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// DR-246: companions climbed into the air after the Darkness Falls door tempters. Heights are from the test world:
    /// hall floor 21,572, tempters hovering at 22,177 to 22,728 (flag FLYING), ledge tempters standing on the mesh.
    /// </summary>
    [TestFixture]
    public class UT_BotAirborneTargetPolicy
    {
        private static readonly Vector3 HallFloor = new(18271, 29735, 21574);

        [Test]
        public void WalkEndUnderAHoveringTempterStaysOnTheFloor()
        {
            Vector3 tempter = new(18271, 29735, 22177);
            Assert.That(BotAirborneTargetPolicy.GroundedWalkEnd(tempter, HallFloor), Is.EqualTo(HallFloor));
        }

        [Test]
        public void WalkEndUnderALowFlyerIsKept()
        {
            Vector3 lowFlyer = new(17682, 29711, 21940);
            Assert.That(BotAirborneTargetPolicy.GroundedWalkEnd(lowFlyer, HallFloor with { Z = 21603 }), Is.EqualTo(lowFlyer));
        }

        [Test]
        public void WalkEndOnALedgeOrSlopeIsKept()
        {
            Vector3 ledge = new(18966, 28801, 21869);
            Assert.That(BotAirborneTargetPolicy.GroundedWalkEnd(ledge, ledge with { Z = 21872 - 10 }), Is.EqualTo(ledge));
            Assert.That(BotAirborneTargetPolicy.GroundedWalkEnd(ledge, null), Is.EqualTo(ledge));
        }

        [Test]
        public void MeleeCompanionDropsAHighFlyer() =>
            Assert.That(BotAirborneTargetPolicy.CompanionDropsFlyer(true, false, false, GameNPC.eFlags.FLYING, 22177, 21574), Is.True);

        [TestCase(true, false, false, (GameNPC.eFlags)0, 22177, TestName = "Ground mob at the same height is fought")]
        [TestCase(true, false, false, GameNPC.eFlags.FLYING, 21900, TestName = "Low flyer within reach is fought")]
        [TestCase(true, true, false, GameNPC.eFlags.FLYING, 22177, TestName = "Autonomous bots keep their own flyer watch")]
        [TestCase(true, false, true, GameNPC.eFlags.FLYING, 22177, TestName = "A companion with a bow shoots it")]
        [TestCase(false, false, false, GameNPC.eFlags.FLYING, 22177, TestName = "Not a companion")]
        public void OtherCasesKeepTheTarget(bool companion, bool autonomous, bool ranged, GameNPC.eFlags flags, int targetZ) =>
            Assert.That(BotAirborneTargetPolicy.CompanionDropsFlyer(companion, autonomous, ranged, flags, targetZ, 21574), Is.False);
    }
}
