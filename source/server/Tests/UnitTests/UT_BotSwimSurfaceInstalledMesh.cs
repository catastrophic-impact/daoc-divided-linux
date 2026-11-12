using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bots on top of the water are drawn swimming (Discord 2026-11-04): the installed meshes mark the water surface with
    /// swim polygons in places where the zone water level says dry land. Points read from the installed meshes.
    /// </summary>
    [TestFixture, NonParallelizable, Explicit("Reads the installed navmeshes (OFFLINE_DAOC_NAV_ROOT)")]
    public class UT_BotSwimSurfaceInstalledMesh
    {
        private static bool _resolverSet;

        [TestCase(252, 16384, 36864, 2960, true, TestName = "Thidranki river surface swims (zone water level 0)")]
        [TestCase(250, 16384, 36864, 2960, true, TestName = "Caledon river surface swims (zone water level 0)")]
        [TestCase(73, 264192, 526336, 7994, true, TestName = "Atlantis sea surface swims (region water level 7970)")]
        [TestCase(73, 264192, 526336, 8100, false, TestName = "Atlantis well above the sea does not swim")]
        public void WaterSurface(int regionId, float x, float y, float z, bool expected)
        {
            string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            Assert.That(root, Is.Not.Null.And.Not.Empty);
            string native = Environment.GetEnvironmentVariable("OFFLINE_DAOC_TEST_DETOUR");
            if (!string.IsNullOrEmpty(native) && !_resolverSet && (_resolverSet = true))
                NativeLibrary.SetDllImportResolver(typeof(LocalPathfindingMgr).Assembly,
                    (name, assembly, search) => name == "lib/Detour" ? NativeLibrary.Load(native) : IntPtr.Zero);
            string previous = Environment.CurrentDirectory;
            Environment.CurrentDirectory = root;
            try
            {
                using var db = new SQLiteConnection($"Data Source={Path.GetFullPath(Path.Combine(root, "..", "data", "opendaoc.sqlite3.db"))};Read Only=True;Pooling=False;");
                db.Open();
                Region region = UT_AuditedDungeonInstalledMesh.BuildRegion(db, regionId, new List<Zone>());
                Zone zone = region.Zones.First(z => x >= z.XOffset && x < z.XOffset + z.Width && y >= z.YOffset && y < z.YOffset + z.Height);
                Assert.That(BotPlayerPresentation.OnWaterSurface(PathfindingProvider.LocalPathfindingMgr, zone, new Vector3(x, y, z)), Is.EqualTo(expected));

                // Dry ground in the same zone does not swim, apart from the last couple of units at a shore.
                IPathfindingMgr nav = PathfindingProvider.LocalPathfindingMgr;
                EDtPolyFlags[] walkOnly = [EDtPolyFlags.Walk, EDtPolyFlags.Swim | EDtPolyFlags.Disabled];
                Vector3[] ground = Enumerable.Range(1, 31).SelectMany(i => Enumerable.Range(1, 31).Select(j =>
                        nav.GetClosestPoint(zone, new Vector3(zone.XOffset + zone.Width * i / 32f, zone.YOffset + zone.Height * j / 32f, 10000), 4, 4, 20000, walkOnly)))
                    .Where(p => p.HasValue).Select(p => p.Value).ToArray();
                int wet = ground.Count(p => BotPlayerPresentation.OnWaterSurface(nav, zone, p));
                TestContext.Progress.WriteLine($"zone {zone.ID}: {wet} of {ground.Length} ground points read as water");
                Assert.That(ground, Is.Not.Empty);
                Assert.That(wet, Is.LessThanOrEqualTo(ground.Length / 20), "ground points read as water");
            }
            finally
            {
                Environment.CurrentDirectory = previous;
            }
        }
    }
}
