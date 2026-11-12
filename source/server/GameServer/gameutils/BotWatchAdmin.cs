using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using DOL.GS.ServerProperties;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace DOL.GS
{
    /// <summary>
    /// Read-only developer endpoints on the Bot Watch port (/api/admin/...): server summary, bot search, one bot in
    /// detail, stalled bots and players online. Off unless the server property bots/bot_watch_admin is True (release
    /// worlds never set it). Same loopback-only listener and Host check as Bot Watch; nothing here changes the world.
    /// </summary>
    public static class BotWatchAdmin
    {
        public sealed record Summary(DateTime ServerStartedUtc, double UptimeMinutes, int Players, int BotsActive,
            int BotsInCombat, int BotsDead, Dictionary<string, int> BotsByRealm, Dictionary<string, int> BotsByLevelBand,
            int StalledOver15Minutes, RegionCount[] TopRegions, long ManagedMemoryMb);
        public sealed record RegionCount(ushort Region, string Name, int Bots, int Players);
        public sealed record BotRow(long Id, string Name, string Realm, string Class, int Level, ushort Region, string Zone,
            bool Alive, bool InCombat, string Activity, string Goal, double? MinutesSinceProgress);
        public sealed record BotDetail(long Id, string Name, string Realm, string Class, string Race, int Level, long Experience,
            long RealmPoints, long MoneyCopper, ushort Region, string RegionName, string Zone, int X, int Y, int Z,
            bool Alive, int HealthPercent, int ManaPercent, int EndurancePercent, bool InCombat, string Target,
            string Brain, string BrainState, string Activity, string Goal, string ObjectiveKind, string ObjectivePhase,
            string ObjectiveAssignment, string ObjectiveProgress, string Camp, string Travel, int Deaths, int Recoveries,
            string LastMeaningfulProgressUtc, double? MinutesSinceProgress, string[] Group, string GroupLeader);
        public sealed record PlayerRow(string Name, string Account, string Class, int Level, string Realm, ushort Region,
            string Zone, bool Alive, bool InCombat, string Target, string[] Group);

        public static void Map(WebApplication app)
        {
            if (!Properties.BOT_WATCH_ADMIN)
                return;

            app.MapGet("/api/admin/summary", () => Results.Json(GetSummary()));
            app.MapGet("/api/admin/bots", (string name, ushort? region, string realm, int? limit) =>
                Results.Json(FindBots(name, region, realm, limit ?? 50)));
            app.MapGet("/api/admin/bot/{key}", (string key) => GetBot(key) is { } d ? Results.Json(d) : Results.NotFound());
            app.MapGet("/api/admin/stalled", (int? minutes, int? limit) =>
                Results.Json(Stalled(TimeSpan.FromMinutes(minutes ?? 15), limit ?? 100)));
            app.MapGet("/api/admin/players", () => Results.Json(Players()));
            app.MapGet("/api/admin/bot/{key}/timeline", (string key) =>
                FindBotId(key) is { } id ? Results.Json(new { botId = id, events = BotTimeline.For(id) }) : Results.NotFound());
            BotTimeline.Start();
        }

        // ------------------------------------------------------------ pure helpers (unit tested)

        public static string LevelBand(int level) => level switch
        {
            < 10 => "1-9",
            < 20 => "10-19",
            < 35 => "20-34",
            < 50 => "35-49",
            _ => "50"
        };

        /// <summary>Minutes since the bot record's last meaningful progress, or null when it has never recorded any.</summary>
        public static double? MinutesSinceProgress(OfflineWorldBotRecord record, DateTime nowUtc)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.LastMeaningfulProgressUtc))
                return null;

            if (!DateTime.TryParse(record.LastMeaningfulProgressUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime last))
                return null;

            return Math.Max(0, (nowUtc - last).TotalMinutes);
        }

        public static bool IsStalled(OfflineWorldBotRecord record, bool alive, DateTime nowUtc, TimeSpan threshold) =>
            alive && MinutesSinceProgress(record, nowUtc) is { } minutes && minutes >= threshold.TotalMinutes;

        // eRealm has alias names on the same values (_FirstPlayerRealm = Albion), so ToString() is not reliable.
        public static string RealmName(eRealm realm) => realm switch
        {
            eRealm.Albion => "Albion",
            eRealm.Midgard => "Midgard",
            eRealm.Hibernia => "Hibernia",
            eRealm.None => "None",
            _ => ((int) realm).ToString()
        };

        // ------------------------------------------------------------ live views (run off the game loop: guard each unit)

        private static IEnumerable<GameBot> Bots() =>
            AutonomousBotRegistry.Snapshot().Where(b => b?.ObjectState == GameObject.eObjectState.Active);

        public static Summary GetSummary()
        {
            DateTime now = DateTime.UtcNow;
            var byRealm = new Dictionary<string, int>();
            var byBand = new Dictionary<string, int>();
            var regions = new Dictionary<ushort, (int Bots, int Players)>();
            int active = 0, combat = 0, dead = 0, stalled = 0;

            foreach (GameBot bot in Bots())
            {
                try
                {
                    active++;
                    if (bot.InCombat) combat++;
                    if (!bot.IsAlive) dead++;
                    string realm = RealmName(bot.Realm);
                    byRealm[realm] = byRealm.GetValueOrDefault(realm) + 1;
                    string band = LevelBand(bot.Level);
                    byBand[band] = byBand.GetValueOrDefault(band) + 1;
                    var r = regions.GetValueOrDefault(bot.CurrentRegionID);
                    regions[bot.CurrentRegionID] = (r.Bots + 1, r.Players);
                    if (IsStalled(bot.PersistentRecord, bot.IsAlive, now, TimeSpan.FromMinutes(15))) stalled++;
                }
                catch (Exception e) when (e is NullReferenceException or InvalidOperationException) { }
            }

            int players = 0;
            foreach (GamePlayer p in ClientService.Instance.GetPlayers())
            {
                if (p?.ObjectState != GameObject.eObjectState.Active) continue;
                players++;
                var r = regions.GetValueOrDefault(p.CurrentRegionID);
                regions[p.CurrentRegionID] = (r.Bots, r.Players + 1);
            }

            DateTime started = Process.GetCurrentProcess().StartTime.ToUniversalTime();
            RegionCount[] top = regions.OrderByDescending(kv => kv.Value.Bots + kv.Value.Players).Take(15)
                .Select(kv => new RegionCount(kv.Key, WorldMgr.GetRegion(kv.Key)?.Description ?? "", kv.Value.Bots, kv.Value.Players))
                .ToArray();
            return new Summary(started, Math.Round((now - started).TotalMinutes, 1), players, active, combat, dead, byRealm,
                byBand, stalled, top, GC.GetTotalMemory(false) / (1024 * 1024));
        }

        public static BotRow[] FindBots(string name, ushort? region, string realm, int limit)
        {
            DateTime now = DateTime.UtcNow;
            var rows = new List<BotRow>();
            foreach (GameBot bot in Bots())
            {
                try
                {
                    if (!string.IsNullOrEmpty(name) && bot.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (region.HasValue && bot.CurrentRegionID != region.Value) continue;
                    if (!string.IsNullOrEmpty(realm) && !RealmName(bot.Realm).Equals(realm, StringComparison.OrdinalIgnoreCase)) continue;
                    rows.Add(Row(bot, now));
                    if (rows.Count >= Math.Clamp(limit, 1, 500)) break;
                }
                catch (Exception e) when (e is NullReferenceException or InvalidOperationException) { }
            }
            return rows.ToArray();
        }

        private static BotRow Row(GameBot bot, DateTime now)
        {
            OfflineWorldBotRecord rec = bot.PersistentRecord;
            return new BotRow(rec?.BotId ?? bot.ObjectID, bot.Name, RealmName(bot.Realm), bot.CharacterClass?.Name ?? "",
                bot.Level, bot.CurrentRegionID, bot.CurrentZone?.Description ?? "", bot.IsAlive, bot.InCombat,
                rec?.Activity ?? "", rec?.CurrentGoal ?? "", MinutesSinceProgress(rec, now) is { } m ? Math.Round(m, 1) : null);
        }

        public static long? FindBotId(string key)
        {
            bool byId = long.TryParse(key, out long id);
            GameBot bot = Bots().FirstOrDefault(b => byId ? b.PersistentRecord?.BotId == id
                : b.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
            return bot?.PersistentRecord?.BotId ?? (byId ? id : null);
        }

        public static BotDetail GetBot(string key)
        {
            DateTime now = DateTime.UtcNow;
            bool byId = long.TryParse(key, out long id);
            GameBot bot = Bots().FirstOrDefault(b => byId ? b.PersistentRecord?.BotId == id
                : b.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (bot == null)
                return null;

            try
            {
                OfflineWorldBotRecord rec = bot.PersistentRecord;
                Group group = bot.Group;
                string state = bot.Brain?.FSM?.GetCurrentState()?.StateType.ToString() ?? "";
                return new BotDetail(rec?.BotId ?? bot.ObjectID, bot.Name, RealmName(bot.Realm), bot.CharacterClass?.Name ?? "",
                    rec?.RaceName ?? "", bot.Level, bot.Experience, rec?.RealmPoints ?? 0, rec?.MoneyCopper ?? 0,
                    bot.CurrentRegionID, bot.CurrentRegion?.Description ?? "", bot.CurrentZone?.Description ?? "",
                    bot.X, bot.Y, bot.Z, bot.IsAlive, bot.HealthPercent, bot.ManaPercent, bot.EndurancePercent, bot.InCombat,
                    bot.TargetObject?.Name ?? "", bot.Brain?.GetType().Name ?? "", state, rec?.Activity ?? "",
                    rec?.CurrentGoal ?? "", rec?.ObjectiveKind ?? "", rec?.ObjectivePhase ?? "", rec?.ObjectiveAssignmentId ?? "",
                    rec?.ObjectiveProgress ?? "", rec?.CurrentCampId ?? "", rec?.TravelDestination ?? "", rec?.DeathCount ?? 0,
                    rec?.RecoveryCount ?? 0, rec?.LastMeaningfulProgressUtc ?? "",
                    MinutesSinceProgress(rec, now) is { } m ? Math.Round(m, 1) : null,
                    group?.GetMembersInTheGroup().Select(g => g.Name).ToArray() ?? [], group?.LivingLeader?.Name ?? "");
            }
            catch (Exception e) when (e is NullReferenceException or InvalidOperationException)
            {
                return null;
            }
        }

        public static BotRow[] Stalled(TimeSpan threshold, int limit)
        {
            DateTime now = DateTime.UtcNow;
            var rows = new List<BotRow>();
            foreach (GameBot bot in Bots())
            {
                try
                {
                    if (IsStalled(bot.PersistentRecord, bot.IsAlive, now, threshold))
                        rows.Add(Row(bot, now));
                }
                catch (Exception e) when (e is NullReferenceException or InvalidOperationException) { }
            }
            return rows.OrderByDescending(r => r.MinutesSinceProgress).Take(Math.Clamp(limit, 1, 1000)).ToArray();
        }

        public static PlayerRow[] Players()
        {
            var rows = new List<PlayerRow>();
            foreach (GamePlayer p in ClientService.Instance.GetPlayers())
            {
                try
                {
                    if (p?.ObjectState != GameObject.eObjectState.Active) continue;
                    rows.Add(new PlayerRow(p.Name, p.Client?.Account?.Name ?? "", p.CharacterClass?.Name ?? "", p.Level,
                        RealmName(p.Realm), p.CurrentRegionID, p.CurrentZone?.Description ?? "", p.IsAlive, p.InCombat,
                        p.TargetObject?.Name ?? "", p.Group?.GetMembersInTheGroup().Select(g => g.Name).ToArray() ?? []));
                }
                catch (Exception e) when (e is NullReferenceException or InvalidOperationException) { }
            }
            return rows.ToArray();
        }
    }
}
