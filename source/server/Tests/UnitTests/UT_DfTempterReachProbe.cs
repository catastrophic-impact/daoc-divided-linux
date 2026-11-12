using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Tool (DR-246): for each point, where a follow chase snaps on the navmesh (128-unit box, as
    /// NpcMovementComponent.TrySnapToMesh), the floor beneath it, and whether a walking path joins them.
    /// Env: OFFLINE_DAOC_NAV_ROOT, OFFLINE_DAOC_PROOF_OUT, OFFLINE_DAOC_PROBE_POINTS = "zoneId:offX:offY:x,y,z;...".
    /// </summary>
    [TestFixture, Explicit("Tempter reach probe tool"), NonParallelizable]
    public class UT_DfTempterReachProbe
    {
        [Test]
        public void ProbeTempterReach()
        {
            string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            string output = Environment.GetEnvironmentVariable("OFFLINE_DAOC_PROOF_OUT");
            string spec = Environment.GetEnvironmentVariable("OFFLINE_DAOC_PROBE_POINTS");
            Assume.That(root != null && output != null && spec != null);
            string previous = Environment.CurrentDirectory;
            Environment.CurrentDirectory = root;
            var nav = PathfindingProvider.LocalPathfindingMgr;
            var lines = new List<string>();
            var zones = new Dictionary<ushort, Zone>();
            var nodes = new WrappedPathfindingNode[512];
            string startSpec = Environment.GetEnvironmentVariable("OFFLINE_DAOC_PROBE_START");
            Vector3? start = startSpec == null ? null : new Vector3(startSpec.Split(',').Select(float.Parse).ToArray());
            try
            {
                foreach (string item in spec.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = item.Split(':');
                    ushort id = ushort.Parse(parts[0]);
                    if (!zones.TryGetValue(id, out Zone zone))
                    {
                        zone = new Zone(null, id, $"zone{id}", int.Parse(parts[1]), int.Parse(parts[2]), 65536, 65536, id, false, 0, false, 0, 0, 0, 0, 0);
                        LocalPathfindingMgr.LoadNavMesh(zone);
                        zones[id] = zone;
                    }
                    float[] v = parts[3].Split(',').Select(float.Parse).ToArray();
                    var p = new Vector3(v[0], v[1], v[2]);
                    Vector3? snap = nav.GetClosestPoint(zone, p, 128, 128, 128, nav.DefaultFilters);
                    Vector3? floor = nav.GetFloorBeneath(zone, p, 3000, nav.DefaultFilters);
                    string path = "-";
                    if (snap.HasValue && floor.HasValue)
                    {
                        PathfindingResult r = nav.GetPathStraight(zone, start ?? floor.Value, snap.Value, nav.DefaultFilters, nodes);
                        float endGap = r.NodeCount > 0 ? Vector3.Distance(nodes[r.NodeCount - 1].Position, snap.Value) : -1;
                        path = $"{r.Status} nodes {r.NodeCount} endgap {endGap:0}";
                    }
                    lines.Add($"{p.X:0},{p.Y:0},{p.Z:0} snap {(snap.HasValue ? $"{snap.Value.X:0},{snap.Value.Y:0},{snap.Value.Z:0}" : "none")} " +
                              $"floor {(floor.HasValue ? $"{floor.Value.Z:0}" : "none")} path {path}");
                }
            }
            finally
            {
                foreach (Zone zone in zones.Values) LocalPathfindingMgr.UnloadNavMesh(zone);
                Environment.CurrentDirectory = previous;
            }
            File.WriteAllLines(output, lines);
        }
    }
}
