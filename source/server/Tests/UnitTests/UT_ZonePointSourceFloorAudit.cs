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
    /// Read-only audit: every zone point source against the installed navmeshes. A bot walks to a crossing's source and
    /// AutonomousZonePointApproach accepts floor only within 256 units of its height, so a source buried under (or floating
    /// over) the installed floor is quarantined by every bot that tries it. 2026-11-04 test install: zone point 90 (Hall of
    /// the Corrupt to Summoner's Hall) has the classic SourceZ 13476, 1,302 under the 1.130 floor at the same X,Y; the
    /// Albion Summoner's Hall raid logged 547 "No legal region route" and 558 "No tested zone connection" retries at the
    /// Hall of the Corrupt arrival. Lists each source with no floor in reach and the floor straight above or below it.
    /// The Atlantis underwater portals (regions 30, 73, 130) are expected: their sea meshes are surface-only.
    /// Run: OFFLINE_DAOC_NAV_ROOT=&lt;install&gt;/runtime/server OFFLINE_DAOC_TEST_DETOUR=&lt;install&gt;/runtime/server/lib/Detour.dll
    /// </summary>
    [TestFixture, NonParallelizable, Explicit("Read-only installed zone point audit")]
    public class UT_ZonePointSourceFloorAudit
    {
        [Test]
        public void EveryZonePointSourceHasFloorWithinTheBotApproachSearch()
        {
            string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            Assert.That(root, Is.Not.Null.And.Not.Empty);
            string previous = Environment.CurrentDirectory;
            var lines = new List<string>();
            try
            {
                string native = Environment.GetEnvironmentVariable("OFFLINE_DAOC_TEST_DETOUR");
                if (!string.IsNullOrEmpty(native))
                    NativeLibrary.SetDllImportResolver(typeof(LocalPathfindingMgr).Assembly,
                        (name, assembly, search) => name == "lib/Detour" ? NativeLibrary.Load(native) : IntPtr.Zero);
                Environment.CurrentDirectory = root;
                string databasePath = Environment.GetEnvironmentVariable("OFFLINE_DAOC_DB_PATH") ??
                    Path.GetFullPath(Path.Combine(root, "..", "data", "opendaoc.sqlite3.db"));
                using var db = new SQLiteConnection($"Data Source={databasePath};Read Only=True;Pooling=False;");
                db.Open();
                var rows = new List<(int Id, int Realm, int Region, Vector3 Source, int Target)>();
                using (var command = db.CreateCommand())
                {
                    command.CommandText = "select Id,Realm,SourceRegion,SourceX,SourceY,SourceZ,TargetRegion from ZonePoint where SourceRegion>0 and (SourceX<>0 or SourceY<>0)";
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                        rows.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                            new Vector3(reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5)), reader.GetInt32(6)));
                }
                IPathfindingMgr nav = PathfindingProvider.LocalPathfindingMgr;
                int checkedRows = 0, noMesh = 0;
                foreach (var group in rows.GroupBy(r => r.Region))
                {
                    var loaded = new List<Zone>();
                    try
                    {
                        Region region = UT_AuditedDungeonInstalledMesh.BuildRegion(db, group.Key, loaded);
                        foreach (var row in group)
                        {
                            Zone zone = region.GetZone((int)row.Source.X, (int)row.Source.Y);
                            if (zone == null || !nav.HasNavmesh(zone)) { noMesh++; continue; }
                            checkedRows++;
                            Vector3? near = nav.GetClosestPoint(zone, row.Source, 174, 174, 256, nav.DefaultFilters);
                            if (near.HasValue) continue;
                            Vector3? far = nav.GetClosestPoint(zone, row.Source, 174, 174, 4096, nav.DefaultFilters);
                            lines.Add($"BAD zp={row.Id} realm={row.Realm} {row.Region}->{row.Target} source={Fmt(row.Source)} " +
                                (far.HasValue ? $"floor={Fmt(far.Value)} dz={(int)(far.Value.Z - row.Source.Z)}" : "no floor within 4096"));
                        }
                    }
                    finally { foreach (Zone z in loaded) LocalPathfindingMgr.UnloadNavMesh(z); }
                }
                lines.Insert(0, $"rows={rows.Count} checked={checkedRows} noMesh={noMesh} bad={lines.Count}");
            }
            finally
            {
                Environment.CurrentDirectory = previous;
                TestContext.Out.WriteLine(string.Join(Environment.NewLine, lines));
            }
        }

        private static string Fmt(Vector3 v) => $"{(int)v.X},{(int)v.Y},{(int)v.Z}";
    }
}
