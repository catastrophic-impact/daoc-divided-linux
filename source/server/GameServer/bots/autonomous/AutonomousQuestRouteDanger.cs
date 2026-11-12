using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// Where a gamebot's quest step may send it (DR-076, 2026-11-08). Quest 20081 sent level 30 bots for luch catchers past
/// a level 52-54 pooka camp in Lough Gur, and they died there again and again (one bot 28 times) while a level 37 luch
/// catcher stood on the bind side. A step's place is now chosen among the spots the bot can fight whose straight road
/// does not pass within aggro reach of a monster far over its level; when there is none the step is set aside.
///
/// Built once with the quest catalog from the spawn spots it already holds: a grid of aggressive monster spots per
/// region. A check walks the road's cells, so it costs a few hundred look-ups and no world scan. Pure.
/// </summary>
public sealed class AutonomousQuestRouteDanger
{
    public const int CellSize = 1024;
    /// <summary>Added to a monster's aggro range: a road that close is a road into its fight.</summary>
    public const int Margin = 250;
    /// <summary>A monster with no aggro range of its own still bites this close.</summary>
    public const int DefaultAggroRange = 500;
    /// <summary>How many of a step's nearest spots are tried before the step is set aside.</summary>
    public const int MaximumCandidates = 12;

    public readonly record struct Danger(string Name, int X, int Y, int Level, int Reach);

    private readonly Dictionary<(ushort Region, int Cx, int Cy), List<Danger>> _cells = new();

    public int Count { get; private set; }

    public static AutonomousQuestRouteDanger Build(IEnumerable<AutonomousBotQuests.NpcSpot> spots)
    {
        var grid = new AutonomousQuestRouteDanger();
        foreach (AutonomousBotQuests.NpcSpot spot in spots ?? [])
        {
            if (spot == null || !spot.Hostile || !spot.Aggressive || spot.Level <= 0)
                continue;
            int reach = (spot.AggroRange > 0 ? spot.AggroRange : DefaultAggroRange) + Margin;
            var key = (spot.Region, Cell(spot.X), Cell(spot.Y));
            if (!grid._cells.TryGetValue(key, out List<Danger> list))
                grid._cells[key] = list = new List<Danger>(2);
            list.Add(new Danger(spot.Name, spot.X, spot.Y, spot.Level, reach));
            grid.Count++;
        }
        return grid;
    }

    /// <summary>A monster too far above a lone bot to walk past (red or purple, as the route detour rules).</summary>
    public static bool FarAbove(int botLevel, int monsterLevel) => AutonomousPveDangerBypass.IsFarAbove(botLevel, monsterLevel);

    /// <summary>
    /// The worst far-over-level aggressive monster within reach of the straight road from (fromX, fromY) to (toX, toY),
    /// or null when the road is clear. Monsters named <paramref name="ignoreName"/> (the step's own target, whose level
    /// the caller has already judged) are left out.
    /// </summary>
    public Danger? Blocker(ushort region, int fromX, int fromY, int toX, int toY, int botLevel, string ignoreName = null)
    {
        if (_cells.Count == 0)
            return null;
        Danger? worst = null;
        var seen = new HashSet<(int, int)>();
        double length = Math.Sqrt((double)(toX - fromX) * (toX - fromX) + (double)(toY - fromY) * (toY - fromY));
        int samples = Math.Max(1, (int)Math.Ceiling(length / (CellSize / 2.0)));
        for (int i = 0; i <= samples; i++)
        {
            double t = (double)i / samples;
            int cx = Cell((int)(fromX + (toX - fromX) * t)), cy = Cell((int)(fromY + (toY - fromY) * t));
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (!seen.Add((cx + dx, cy + dy)) || !_cells.TryGetValue((region, cx + dx, cy + dy), out List<Danger> list))
                    continue;
                foreach (Danger danger in list)
                {
                    if (!FarAbove(botLevel, danger.Level) ||
                        ignoreName != null && string.Equals(danger.Name, ignoreName, StringComparison.OrdinalIgnoreCase) ||
                        DistanceToSegment(fromX, fromY, toX, toY, danger.X, danger.Y) > danger.Reach)
                        continue;
                    if (worst == null || danger.Level > worst.Value.Level)
                        worst = danger;
                }
            }
        }
        return worst;
    }

    /// <summary>
    /// The spot to send the bot to: of the candidates (nearest first), the first it can fight whose road from
    /// (fromX, fromY) is clear. A spot in another region is judged by its own surroundings only (the road there is the
    /// travel planner's). Null, with what stood in the way, when none qualifies.
    /// </summary>
    public AutonomousBotQuests.NpcSpot Choose(IEnumerable<AutonomousBotQuests.NpcSpot> nearestFirst, ushort fromRegion, int fromX, int fromY,
        int botLevel, bool fights, out string blocked)
    {
        blocked = null;
        string firstReason = null;
        foreach (AutonomousBotQuests.NpcSpot spot in nearestFirst.Take(MaximumCandidates))
        {
            if (fights && spot.Hostile && spot.Level > 0 && !AutonomousBotQuests.CanFight(botLevel, spot.Level))
            {
                firstReason ??= $"Route blocked: every {spot.Name} is far over level {botLevel} (level {spot.Level})";
                continue;
            }
            bool sameRegion = spot.Region == fromRegion;
            Danger? danger = Blocker(spot.Region, sameRegion ? fromX : spot.X, sameRegion ? fromY : spot.Y, spot.X, spot.Y, botLevel, spot.Name);
            if (danger is Danger d)
            {
                firstReason ??= $"Route blocked by {d.Name} (level {d.Level}) on the way to {spot.Name}";
                continue;
            }
            return spot;
        }
        blocked = firstReason;
        return null;
    }

    private static int Cell(int coordinate) => (int)Math.Floor(coordinate / (double)CellSize);

    public static double DistanceToSegment(int ax, int ay, int bx, int by, int px, int py)
    {
        double vx = bx - ax, vy = by - ay;
        double lengthSquared = vx * vx + vy * vy;
        double t = lengthSquared <= 0 ? 0 : Math.Clamp(((px - ax) * vx + (py - ay) * vy) / lengthSquared, 0, 1);
        double cx = ax + t * vx - px, cy = ay + t * vy - py;
        return Math.Sqrt(cx * cx + cy * cy);
    }
}
