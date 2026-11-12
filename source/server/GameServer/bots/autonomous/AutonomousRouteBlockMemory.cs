using System.Collections.Concurrent;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// The monsters a bot or its party gave up a camp for, and how far around them to stay: for
    /// <see cref="CooldownMilliseconds"/> the camp picker skips any camp whose straight line from the bot passes inside
    /// that reach (<see cref="Blocks"/>). The give-up memory (AutonomousFailedGoalMemory) only blocks 200 units around the old camp, and a
    /// party that gave up a giant spider camp for a goblin pack picked the next giant spider spawn 1,000 units on and
    /// walked back into the same goblins: on three 14 minute private copy runs (2026-11-02) 13, 57 and 45 low level
    /// party members died within 2,500 units of a camp their party had given up in the last five minutes.
    /// </summary>
    public static class AutonomousRouteBlockMemory
    {
        public const long CooldownMilliseconds = 15 * 60_000;
        /// <summary>The reach kept around a blocker at least: about one aggro range plus the retreat clearance.</summary>
        public const float MinimumRadius = 800f;
        private const int SlotsPerOwner = 4;
        private const int SoftOwnerCap = 2_048;

        /// <summary>
        /// Blockers a whole realm's bots of about one level share, per region. On the 2026-11-02 overnight run nine roaming
        /// howling maidens (24-26) in Black Mtns. North killed about 200 Albion bots of level 10-19, each finding the field
        /// for itself on the way to Lieutenant Brude or a quest step and giving its camp up only once inside it.
        /// </summary>
        public const int WordOfMouthSlots = 32;
        public const int WordOfMouthLevelBand = 5;

        /// <summary>Only a red or purple blocker is passed on: a yellow or orange one is a fair fight for some bots of the band. Pure.</summary>
        public static bool PassedOn(ConColor con) => con >= ConColor.RED;

        /// <summary>The shared owner key for a realm's bots of one level band in one region (negative: bot and party keys are positive). Pure.</summary>
        public static long WordOfMouthKey(eRealm realm, int level, ushort region) =>
            -(((long)realm << 32) | ((long)(level / WordOfMouthLevelBand) << 16) | region) - 1;

        private struct Entry
        {
            public ushort Region;
            public Vector2 At;
            public float Radius;
            public long Until;
        }

        private static readonly ConcurrentDictionary<long, Entry[]> Owners = new();

        public static void Note(long ownerKey, ushort region, Vector2 at, float radius, long now, int slots = SlotsPerOwner)
        {
            if (ownerKey == 0 || region == 0)
                return;
            if (Owners.Count > SoftOwnerCap)
                Prune(now);
            Entry[] entries = Owners.GetOrAdd(ownerKey, _ => new Entry[slots]);
            radius = System.Math.Max(radius, MinimumRadius);
            lock (entries)
            {
                int slot = 0;
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].Until > now && entries[i].Region == region &&
                        Vector2.Distance(entries[i].At, at) <= entries[i].Radius)
                    {
                        slot = i; // the same blocker again: refresh it
                        break;
                    }
                    if (entries[i].Until < entries[slot].Until)
                        slot = i; // else replace the oldest
                }
                entries[slot] = new Entry { Region = region, At = at, Radius = radius, Until = now + CooldownMilliseconds };
            }
        }

        /// <summary>Whether the straight way from <paramref name="from"/> to <paramref name="to"/> passes a remembered blocker.</summary>
        public static bool Crosses(long ownerKey, ushort region, Vector2 from, Vector2 to, long now)
        {
            if (ownerKey == 0 || !Owners.TryGetValue(ownerKey, out Entry[] entries))
                return false;
            lock (entries)
            {
                foreach (Entry entry in entries)
                    if (entry.Until > now && entry.Region == region && Blocks(from, to, entry.At, entry.Radius))
                        return true;
            }
            return false;
        }

        /// <summary>How much nearer to a blocker a way must lead a bot already inside its reach before it counts as blocked.</summary>
        public const float InsideTolerance = 150f;

        /// <summary>
        /// Whether the straight way from <paramref name="from"/> to <paramref name="to"/> is blocked by a blocker at
        /// <paramref name="at"/>. From outside its reach: the way passes inside. From inside it, every way starts inside,
        /// so only a way that leads nearer to the blocker is blocked and the ways out stay open. On the 2026-11-11 run
        /// 347 Hibernian bots of level 5-9 stood inside a shared blocker's reach and had all their camps skipped
        /// 16,808 times (DR-277). Pure.
        /// </summary>
        public static bool Blocks(Vector2 from, Vector2 to, Vector2 at, float radius)
        {
            float nearest = AutonomousRouteThreatPolicy.DistanceToSegment(from, to, at);
            float start = Vector2.Distance(from, at);
            return start < radius ? nearest < start - InsideTolerance : nearest < radius;
        }

        /// <summary>Whether a point lies inside the reach of one of this owner's live blockers.</summary>
        public static bool Within(long ownerKey, ushort region, Vector2 point, long now)
        {
            if (ownerKey == 0 || !Owners.TryGetValue(ownerKey, out Entry[] entries))
                return false;
            lock (entries)
            {
                foreach (Entry entry in entries)
                    if (entry.Until > now && entry.Region == region && Vector2.Distance(entry.At, point) < entry.Radius)
                        return true;
            }
            return false;
        }

        /// <summary>The live blocker of this owner nearest to a point, for the log; false when there is none.</summary>
        public static bool TryNearest(long ownerKey, ushort region, Vector2 from, long now, out Vector2 at, out float radius)
        {
            at = default;
            radius = 0;
            if (ownerKey == 0 || !Owners.TryGetValue(ownerKey, out Entry[] entries))
                return false;
            float best = float.MaxValue;
            lock (entries)
            {
                foreach (Entry entry in entries)
                {
                    float distance = Vector2.Distance(entry.At, from);
                    if (entry.Until > now && entry.Region == region && distance < best)
                    {
                        best = distance;
                        at = entry.At;
                        radius = entry.Radius;
                    }
                }
            }
            return best < float.MaxValue;
        }

        public static bool HasAny(long ownerKey, long now)
        {
            if (ownerKey == 0 || !Owners.TryGetValue(ownerKey, out Entry[] entries))
                return false;
            lock (entries)
            {
                foreach (Entry entry in entries)
                    if (entry.Until > now)
                        return true;
            }
            return false;
        }

        public static void Clear() => Owners.Clear();

        private static void Prune(long now)
        {
            foreach (var pair in Owners)
            {
                bool live = false;
                lock (pair.Value)
                    foreach (Entry entry in pair.Value)
                        live |= entry.Until > now;
                if (!live)
                    Owners.TryRemove(pair.Key, out _);
            }
        }
    }
}
