using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// The client draws water only inside each zone's river shapes (sector.dat [riverNN]), not everywhere below a level.
    /// For the zones listed in world/data/zone_water.json (the three Atlantis copies, made by
    /// source/tools/zone_water_from_client.py) the server uses those shapes, so land below the region's water level,
    /// such as the Stygian Delta and the Land of Atum, is dry. Other zones keep the flat zone/region water level.
    /// </summary>
    public static class ZoneWater
    {
        public const string ResourceName = "DOL.GS.ZoneWater.json";

        private static readonly Logger log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly Dictionary<ushort, Shape[]> _zones = Load();

        public static int ZoneCount => _zones.Count;
        public static string LoadError { get; private set; }

        /// <summary>Called once at server start (the data loads on first use; tests have no logger).</summary>
        public static void LogStatus()
        {
            if (LoadError != null)
                log.Error($"ZONE_WATER not loaded, zones use their flat water level: {LoadError}");
            else
                log.Info($"ZONE_WATER loaded {_zones.Count} zones, {_zones.Values.Sum(shapes => shapes.Length)} water shapes");
        }

        /// <summary>
        /// Answers for zones that have client water shapes. x and y are zone coordinates (0-65535).
        /// Returns false when the zone has no shape data, so the caller falls back to its water level.
        /// </summary>
        public static bool TryIsUnderwater(ushort zoneId, int x, int y, int z, out bool underwater)
        {
            underwater = false;

            if (!_zones.TryGetValue(zoneId, out Shape[] shapes))
                return false;

            foreach (Shape shape in shapes)
            {
                if (z < shape.Height && shape.Contains(x, y))
                {
                    underwater = true;
                    break;
                }
            }

            return true;
        }

        /// <summary>The water surface height at a zone point, if the zone has shape data and the point is in water.</summary>
        public static bool TryGetSurface(ushort zoneId, int x, int y, out int height)
        {
            height = 0;

            if (!_zones.TryGetValue(zoneId, out Shape[] shapes))
                return false;

            bool found = false;

            foreach (Shape shape in shapes)
            {
                if (shape.Height > height && shape.Contains(x, y))
                {
                    height = shape.Height;
                    found = true;
                }
            }

            return found;
        }

        private static Dictionary<ushort, Shape[]> Load()
        {
            Dictionary<ushort, Shape[]> zones = new();

            try
            {
                using Stream stream = typeof(ZoneWater).Assembly.GetManifestResourceStream(ResourceName);

                if (stream == null)
                {
                    LoadError = $"resource {ResourceName} missing";
                    return zones;
                }

                Dictionary<string, ShapeRow[]> rows = JsonSerializer.Deserialize<Dictionary<string, ShapeRow[]>>(stream);

                foreach ((string zone, ShapeRow[] shapes) in rows)
                    zones[ushort.Parse(zone)] = shapes.Select(row => new Shape(row.height, row.quads)).ToArray();

            }
            catch (Exception e)
            {
                LoadError = e.ToString();
                zones.Clear();
            }

            return zones;
        }

        private sealed class ShapeRow
        {
            public int height { get; set; }
            public int[][] quads { get; set; }
        }

        private sealed class Shape
        {
            public int Height { get; }
            private readonly int[][] _quads;
            private readonly int _minX, _minY, _maxX, _maxY;

            public Shape(int height, int[][] quads)
            {
                Height = height;
                _quads = quads;
                _minX = quads.Min(q => Math.Min(Math.Min(q[0], q[2]), Math.Min(q[4], q[6])));
                _maxX = quads.Max(q => Math.Max(Math.Max(q[0], q[2]), Math.Max(q[4], q[6])));
                _minY = quads.Min(q => Math.Min(Math.Min(q[1], q[3]), Math.Min(q[5], q[7])));
                _maxY = quads.Max(q => Math.Max(Math.Max(q[1], q[3]), Math.Max(q[5], q[7])));
            }

            public bool Contains(int x, int y)
            {
                if (x < _minX || x > _maxX || y < _minY || y > _maxY)
                    return false;

                foreach (int[] quad in _quads)
                {
                    if (InQuad(quad, x, y))
                        return true;
                }

                return false;
            }

            // Points on an edge count as inside, so the four quarter squares of an ocean zone leave no gaps.
            private static bool InQuad(int[] q, int x, int y)
            {
                bool inside = false;

                for (int i = 0, j = 3; i < 4; j = i++)
                {
                    int xi = q[i * 2], yi = q[i * 2 + 1];
                    int xj = q[j * 2], yj = q[j * 2 + 1];

                    if (OnSegment(xi, yi, xj, yj, x, y))
                        return true;

                    if ((yi > y) != (yj > y) && x < (long) (xj - xi) * (y - yi) / (double) (yj - yi) + xi)
                        inside = !inside;
                }

                return inside;
            }

            private static bool OnSegment(int x1, int y1, int x2, int y2, int x, int y)
            {
                if (x < Math.Min(x1, x2) || x > Math.Max(x1, x2) || y < Math.Min(y1, y2) || y > Math.Max(y1, y2))
                    return false;

                return (long) (x2 - x1) * (y - y1) == (long) (y2 - y1) * (x - x1);
            }
        }
    }
}
