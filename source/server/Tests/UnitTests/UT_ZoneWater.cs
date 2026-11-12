using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Atlantis water follows the client's river shapes (night playtest 2026-11-04): players drowned standing on dry land
    /// in the Stygian Delta and the Land of Atum, whose ground is below the region water level 7970.
    /// </summary>
    [TestFixture]
    public class UT_ZoneWater
    {
        [Test]
        public void AllThreeAtlantisCopiesHaveShapes()
        {
            Assert.That(ZoneWater.LoadError, Is.Null);
            Assert.That(ZoneWater.ZoneCount, Is.EqualTo(33));
        }

        [TestCase((ushort) 38, 35088, 51284, 7852, false, TestName = "Albion Stygian Delta land below 7970 is dry (tester drowned here)")]
        [TestCase((ushort) 81, 35088, 51284, 7852, false, TestName = "Midgard Stygian Delta land below 7970 is dry")]
        [TestCase((ushort) 138, 35088, 51284, 7852, false, TestName = "Hibernia Stygian Delta land below 7970 is dry")]
        [TestCase((ushort) 82, 25790, 61629, 7469, false, TestName = "Land of Atum below 7970 is dry (Amessis spawn)")]
        [TestCase((ushort) 82, 25000, 30000, 7600, true, TestName = "Land of Atum lake is water")]
        [TestCase((ushort) 81, 57216, 64128, 7600, true, TestName = "Stygian Delta coast water is still water")]
        [TestCase((ushort) 81, 57216, 64128, 8020, false, TestName = "Above the Delta coast surface is dry")]
        [TestCase((ushort) 74, 30000, 30000, 6000, true, TestName = "Open ocean is water")]
        [TestCase((ushort) 74, 0, 0, 6000, true, TestName = "Ocean zone corner is water")]
        [TestCase((ushort) 74, 32768, 32768, 8009, true, TestName = "Ocean quarter seam is water just below the surface")]
        [TestCase((ushort) 74, 30000, 30000, 8010, false, TestName = "Ocean surface height is not underwater")]
        [TestCase((ushort) 84, 30000, 30000, 6000, false, TestName = "Typhon's Reach lava is not water")]
        public void Underwater(ushort zone, int x, int y, int z, bool expected)
        {
            Assert.That(ZoneWater.TryIsUnderwater(zone, x, y, z, out bool underwater), Is.True);
            Assert.That(underwater, Is.EqualTo(expected));
        }

        [TestCase((ushort) 1)]
        [TestCase((ushort) 70)]
        [TestCase((ushort) 80)]
        public void OtherZonesKeepTheirWaterLevel(ushort zone)
        {
            Assert.That(ZoneWater.TryIsUnderwater(zone, 30000, 30000, 0, out _), Is.False);
        }

        [Test]
        public void DeltaCoastSurfaceIsTheClientHeight()
        {
            Assert.That(ZoneWater.TryGetSurface(81, 57216, 64128, out int height), Is.True);
            Assert.That(height, Is.EqualTo(8010));
        }
    }
}
