using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DOL.Events;
using DOL.GS.ServerProperties;
using DOL.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DOL.GS
{
    /// <summary>
    /// A read-only browser map of who is where: autonomous gamebots, real players and the companions in their groups,
    /// region by region, with groups drawn together. It listens on 127.0.0.1 only, at the login port plus
    /// PORT_OFFSET, and changes nothing in the world. The idea comes from daoc-sdk's gateway viewer; no code is taken.
    /// </summary>
    public static class BotWatch
    {
        private static readonly Logger log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        public const int PORT_OFFSET = 20; // The Passport Desk has +10.
        private const string PAGE_RESOURCE = "DOL.GS.BotWatch.html";
        private static readonly ConditionalWeakTable<Group, object> GroupIds = new();
        private static int _nextGroupId;
        private static WebApplication _app;
        private static readonly JsonSerializerOptions Json = new(); // Property names as in the records, which the page reads.

        public sealed record RegionRow(ushort Id, string Name, bool Dungeon, int Bots, int Players);
        public sealed record ZoneRow(ushort Id, string Name, int X, int Y, int Width, int Height);
        public sealed record UnitRow(long Id, string Kind, string Name, int Realm, string Class, int Level, int X, int Y,
            int Heading, int Health, bool Alive, bool InCombat, int Group, bool Leader, string Target, string Zone,
            string Activity, string Goal, string Camp, string Travel);
        public sealed record RegionView(ushort Id, string Name, DateTime UpdatedUtc, ZoneRow[] Zones, UnitRow[] Units);

        public static void Start(GameServerConfiguration config)
        {
            if (!Properties.BOT_WATCH)
                return;

            try
            {
                WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
                builder.Logging.ClearProviders();
                builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, config.Port + PORT_OFFSET));

                _app = builder.Build();
                _app.Use(RefuseForeignHosts);
                _app.MapGet("/", Page);
                _app.MapGet("/api/regions", () => Results.Json(Regions(), Json));
                _app.MapGet("/api/region/{id}", (ushort id) => Region(id) is { } view ? Results.Json(view, Json) : Results.NotFound());
                BotWatchAdmin.Map(_app);
                _app.StartAsync().GetAwaiter().GetResult();

                if (log.IsInfoEnabled)
                    log.Info($"Bot Watch is open at http://127.0.0.1:{config.Port + PORT_OFFSET}/");
            }
            catch (Exception e)
            {
                if (log.IsErrorEnabled)
                    log.Error("Bot Watch could not start", e);
            }
        }

        [GameServerStoppedEvent]
        public static void Stop(DOLEvent e, object sender, EventArgs args)
        {
            BotTimeline.Stop();
            WebApplication app = Interlocked.Exchange(ref _app, null);
            app?.StopAsync().GetAwaiter().GetResult();
        }

        // A page on another site could otherwise read the map through a DNS name pointed at 127.0.0.1.
        private static async Task RefuseForeignHosts(HttpContext context, Func<Task> next)
        {
            string host = context.Request.Host.Host;

            if (host is not ("127.0.0.1" or "localhost"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next();
        }

        private static async Task Page(HttpContext context)
        {
            using Stream page = typeof(BotWatch).Assembly.GetManifestResourceStream(PAGE_RESOURCE);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";
            await page.CopyToAsync(context.Response.Body);
        }

        public static RegionRow[] Regions()
        {
            var bots = new Dictionary<ushort, int>();
            var players = new Dictionary<ushort, int>();

            foreach (GameLiving unit in Units())
            {
                Dictionary<ushort, int> counts = unit is GamePlayer ? players : bots;
                counts[unit.CurrentRegionID] = counts.GetValueOrDefault(unit.CurrentRegionID) + 1;
            }

            return WorldMgr.GetAllRegions()
                .Where(region => region.Zones.Count > 0)
                .Select(region => new RegionRow(region.ID, region.Description, region.IsDungeon,
                    bots.GetValueOrDefault(region.ID), players.GetValueOrDefault(region.ID)))
                .OrderByDescending(row => row.Bots + row.Players)
                .ThenBy(row => row.Id)
                .ToArray();
        }

        public static RegionView Region(ushort id)
        {
            Region region = WorldMgr.GetRegion(id);

            if (region == null)
                return null;

            ZoneRow[] zones = region.Zones
                .Select(zone => new ZoneRow(zone.ID, zone.Description, zone.XOffset, zone.YOffset, zone.Width, zone.Height))
                .ToArray();
            UnitRow[] units = Units()
                .Where(unit => unit.CurrentRegionID == id)
                .Select(Row)
                .Where(row => row != null)
                .ToArray();

            return new RegionView(region.ID, region.Description, DateTime.UtcNow, zones, units);
        }

        /// <summary>Every live gamebot, every player, and the companion bots in the players' groups, once each.</summary>
        private static IEnumerable<GameLiving> Units()
        {
            var seen = new HashSet<GameLiving>();

            foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
            {
                if (bot?.ObjectState == GameObject.eObjectState.Active && seen.Add(bot))
                    yield return bot;
            }

            foreach (GamePlayer player in ClientService.Instance.GetPlayers())
            {
                if (player?.ObjectState != GameObject.eObjectState.Active || !seen.Add(player))
                    continue;

                yield return player;

                foreach (GameBot companion in player.Group?.GetMembersInTheGroup().OfType<GameBot>() ?? [])
                {
                    if (companion.ObjectState == GameObject.eObjectState.Active && seen.Add(companion))
                        yield return companion;
                }
            }
        }

        // Runs off the game loop, so a unit can change under it; a unit that throws is left out of this refresh.
        private static UnitRow Row(GameLiving unit)
        {
            try
            {
                Group group = unit.Group;
                OfflineWorldBotRecord record = (unit as GameBot)?.PersistentRecord;
                string kind = unit switch
                {
                    GamePlayer => "player",
                    GameBot { PersistentRecord: not null } => "bot",
                    _ => "companion"
                };
                string className = unit switch
                {
                    GamePlayer player => player.CharacterClass?.Name,
                    GameBot bot => bot.CharacterClass?.Name,
                    _ => null
                };

                return new UnitRow(
                    record?.BotId ?? unit.ObjectID,
                    kind,
                    unit.Name,
                    (int) unit.Realm,
                    className ?? string.Empty,
                    unit.Level,
                    unit.X,
                    unit.Y,
                    unit.Heading,
                    unit.HealthPercent,
                    unit.IsAlive,
                    unit.InCombat,
                    group == null ? 0 : (int) GroupIds.GetValue(group, _ => Interlocked.Increment(ref _nextGroupId)),
                    group != null && group.LivingLeader == unit,
                    unit.TargetObject?.Name ?? string.Empty,
                    unit.CurrentZone?.Description ?? string.Empty,
                    record?.Activity ?? string.Empty,
                    record?.CurrentGoal ?? string.Empty,
                    record?.CurrentCampId ?? string.Empty,
                    record?.TravelDestination ?? string.Empty);
            }
            catch (Exception e) when (e is NullReferenceException or InvalidOperationException or ArgumentException)
            {
                return null;
            }
        }
    }
}
