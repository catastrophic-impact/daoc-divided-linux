using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DOL.GS
{
    /// <summary>
    /// A camp whose route defeats several different bots is benched for a
    /// while so the planner stops sending more bots into the same blocked
    /// terrain (the overnight run had 37 bots in turn give up on one Lough Derg
    /// camp). One bot's failure never benches a camp: that bot already avoids
    /// it through its own recent-failure list. Nothing is changed in the world;
    /// the camp simply becomes selectable again once the bench expires.
    /// </summary>
    public static class AutonomousCampRouteQuarantine
    {
        public const int DistinctBotsToQuarantine = 3;
        public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan QuarantineDuration = TimeSpan.FromHours(2);

        private sealed class Record
        {
            public readonly object Gate = new();
            public readonly Dictionary<long, DateTime> FailedBots = new();
            public DateTime QuarantinedUntilUtc;
        }

        private static readonly ConcurrentDictionary<string, Record> Records = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Returns true when this report put the camp on the bench.</summary>
        public static bool ReportFailure(string campId, long botId, DateTime? nowUtc = null)
        {
            if (string.IsNullOrWhiteSpace(campId) || botId <= 0 || IsRealmRaidCamp(campId))
                return false;
            DateTime now = nowUtc ?? DateTime.UtcNow;
            Record record = Records.GetOrAdd(campId, _ => new Record());
            lock (record.Gate)
            {
                if (record.QuarantinedUntilUtc > now)
                    return false;
                record.FailedBots[botId] = now;
                List<long> stale = null;
                foreach (KeyValuePair<long, DateTime> pair in record.FailedBots)
                    if (now - pair.Value > FailureWindow)
                        (stale ??= new()).Add(pair.Key);
                if (stale != null)
                    foreach (long id in stale)
                        record.FailedBots.Remove(id);
                if (record.FailedBots.Count < DistinctBotsToQuarantine)
                    return false;
                record.FailedBots.Clear();
                record.QuarantinedUntilUtc = now + QuarantineDuration;
                if (campId.StartsWith("family:", StringComparison.Ordinal))
                    System.Threading.Interlocked.Increment(ref _familyQuarantines);
                return true;
            }
        }

        /// <summary>
        /// One monster's camps in one region, benched as a whole by watchdog rescues. The Sheeroe Hills "pale horse"
        /// is four nearby camp spots: overnight 2026-10-20 three solo bots stalled within 17 minutes on different
        /// spots, so no single spot reached three bots and seven stalls followed.
        /// </summary>
        public static string FamilyKey(ushort regionId, string monsterName) =>
            string.IsNullOrWhiteSpace(monsterName) || regionId == 0
                ? string.Empty
                : $"family:{regionId}:{monsterName.Trim().ToLowerInvariant()}";

        private static int _familyQuarantines;

        /// <summary>Family check for the camp planner; free until any family has ever been benched.</summary>
        public static bool IsFamilyQuarantined(ushort regionId, string monsterName, DateTime? nowUtc = null) =>
            System.Threading.Volatile.Read(ref _familyQuarantines) > 0 &&
            IsQuarantined(FamilyKey(regionId, monsterName), nowUtc);

        /// <summary>A realm raid's own event camp (realm-event-&lt;event&gt;): never benched by this generic rule. 1.1.2 test,
        /// 2026-11-07: three Hibernian raiders whose path to Princess Nahemah failed benched "realm-event-darkness-hibernia"
        /// for two hours; the raid owns its route and its retries (RealmRaidDungeonRoute).</summary>
        public static bool IsRealmRaidCamp(string campId) =>
            campId != null && campId.StartsWith(RealmRaidCampPrefix, StringComparison.OrdinalIgnoreCase);

        public const string RealmRaidCampPrefix = "realm-event-";

        public static bool IsQuarantined(string campId, DateTime? nowUtc = null) =>
            !string.IsNullOrWhiteSpace(campId) && !IsRealmRaidCamp(campId) && Records.TryGetValue(campId, out Record record) &&
            record.QuarantinedUntilUtc > (nowUtc ?? DateTime.UtcNow);

        public static void ResetForTests()
        {
            Records.Clear();
            _familyQuarantines = 0;
        }
    }
}
