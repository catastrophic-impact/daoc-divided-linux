using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using DOL.Database;
using DOL.Events;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Battleground bracket bots (daoc-divided-linux addition). Roster bots tagged in divided_bracket_bots stay out of the world until a
    /// player is inside the battleground they belong to; then enough of them log in (inside the battleground, at their realm's arrival point)
    /// to fill each realm, and they log out some time after the last player leaves. The normal population controller skips them.
    /// Keep this file free of DividedRealms code changes: the only hooks elsewhere are IsManaged, IsAtCap, TryChooseGoal and QueueLogin callers.
    /// </summary>
    public static class BracketBotService
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly ConcurrentDictionary<long, BracketBotRecord> Tags = new();
        private static readonly Dictionary<int, DateTime> LastPlayerSeen = new(); // timer thread only
        private static readonly Dictionary<int, DateTime> LastAutoCreateAttempt = new(); // timer thread only
        private static readonly object CreateLock = new();
        private const int LoginsPerRealmPerTick = 4;
        private const int LogoutsPerTick = 6;
        private static Timer _timer;
        private static int _busy;

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            Reload();
            _timer = new Timer(Tick, null, 5000, 5000);
        }

        [GameServerStoppedEvent]
        public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
        {
            _timer?.Dispose();
            _timer = null;
        }

        public static void Reload()
        {
            Tags.Clear();
            foreach (BracketBotRecord tag in DOLDB<BracketBotRecord>.SelectAllObjects())
                Tags[tag.BotId] = tag;
        }

        // ---- hooks used by DividedRealms' bot code -------------------------------------------------

        /// <summary>True for a bracket bot: the normal population controller must not log it in.</summary>
        public static bool IsManaged(long botId) => Tags.ContainsKey(botId);

        /// <summary>True when a bracket bot has reached its bracket's top level and must not gain more experience.</summary>
        public static bool IsAtCap(GameBot bot) => bot != null && Tags.TryGetValue(bot.DatabaseID, out BracketBotRecord tag) && bot.Level >= tag.LevelCap;

        /// <summary>A bracket bot only ever works on the battleground goal.</summary>
        public static bool TryChooseGoal(GameBot bot, out eAutonomousObjectiveKind kind)
        {
            kind = eAutonomousObjectiveKind.Battleground;
            return bot != null && Tags.ContainsKey(bot.DatabaseID) && BattlegroundBrackets.LevelHasBracket(bot.Level);
        }

        public static int Count => Tags.Count;

        // ---- status and creation (used by the admin layer) -------------------------------------------

        public sealed record BracketStatus(string Name, ushort Region, int PlayersInside, Dictionary<eRealm, int> Roster, Dictionary<eRealm, int> Online);

        public static List<BracketStatus> Status()
        {
            var result = new List<BracketStatus>();
            Dictionary<ushort, Dictionary<eRealm, int>> players = PlayersByRegion();
            for (int i = 0; i < BattlegroundBrackets.Count; i++)
            {
                BattlegroundBrackets.Bracket bracket = BattlegroundBrackets.Get(i);
                var roster = BracketBotPlanner.Realms.ToDictionary(r => r, r => Tags.Values.Count(t => t.BattlegroundRegion == bracket.RegionId && t.Realm == (int)r));
                var online = BracketBotPlanner.Realms.ToDictionary(r => r, r => OnlineBracketBots(bracket.RegionId, r).Count);
                int inside = players.TryGetValue(bracket.RegionId, out var byRealm) ? byRealm.Values.Sum() : 0;
                result.Add(new BracketStatus(bracket.Name, bracket.RegionId, inside, roster, online));
            }
            return result;
        }

        /// <summary>Creates count roster bots per realm for one battleground (by name or region id) and tags them. Returns the new names.</summary>
        public static List<string> Create(string battleground, int countPerRealm)
        {
            BattlegroundBrackets.Bracket bracket = null;
            for (int i = 0; i < BattlegroundBrackets.Count; i++)
            {
                BattlegroundBrackets.Bracket candidate = BattlegroundBrackets.Get(i);
                if (candidate.Name.Equals(battleground, StringComparison.OrdinalIgnoreCase) || candidate.RegionId.ToString() == battleground)
                    bracket = candidate;
            }
            if (bracket == null)
                throw new ArgumentException("Unknown battleground. Use Abermenai, Thidranki, Murdaigean or Caledonia.");
            if (countPerRealm is < 1 or > 100)
                throw new ArgumentException("Create 1 to 100 bracket bots per realm at a time.");

            return CreateBots(bracket, BracketBotPlanner.Realms.ToDictionary(r => r, r => countPerRealm));
        }

        private static List<string> CreateBots(BattlegroundBrackets.Bracket bracket, IReadOnlyDictionary<eRealm, int> countByRealm)
        {
            var names = new List<string>();
            lock (CreateLock)
            {
                var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (OfflineWorldBotRecord record in DOLDB<OfflineWorldBotRecord>.SelectAllObjects())
                    reserved.Add(record.Name);
                foreach (DbCoreCharacter character in DOLDB<DbCoreCharacter>.SelectAllObjects())
                    reserved.Add(character.Name);

                lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
                {
                    foreach (eRealm realm in BracketBotPlanner.Realms)
                    {
                        int count = countByRealm.TryGetValue(realm, out int n) ? n : 0;
                        for (int i = 0; i < count; i++)
                            names.Add(CreateOne(bracket, realm, reserved));
                    }
                }
            }
            Reload();
            AutonomousPopulationController.RequestRefresh();
            Log.Info($"Created {names.Count} bracket bot(s) for {bracket.Name}.");
            return names;
        }

        /// <summary>A new server has no bracket bots. When a player is inside a battleground, create the ones its roster lacks.</summary>
        private static void EnsureRoster(BattlegroundBrackets.Bracket bracket, DateTime now)
        {
            if (!BracketBotProperties.AUTO_CREATE)
                return;
            if (LastAutoCreateAttempt.TryGetValue(bracket.RegionId, out DateTime last) && (now - last).TotalSeconds < 60)
                return; // a failed attempt is not retried every tick
            Dictionary<eRealm, int> roster = BracketBotPlanner.Realms.ToDictionary(r => r, r => Tags.Values.Count(t => t.BattlegroundRegion == bracket.RegionId && t.Realm == (int)r));
            Dictionary<eRealm, int> missing = BracketBotPlanner.MissingRoster(roster, BracketBotProperties.EffectivePerRealm);
            if (missing.Count == 0)
                return;
            LastAutoCreateAttempt[bracket.RegionId] = now;
            try
            {
                Log.Info($"BRACKET_BOTS auto-create battleground={bracket.Name} missing={string.Join(",", missing.Select(kv => $"{GlobalConstants.RealmToName(kv.Key)}:{kv.Value}"))}");
                CreateBots(bracket, missing);
            }
            catch (Exception exception)
            {
                Log.Error($"Bracket bots could not be created for {bracket.Name}", exception);
            }
        }

        private static string CreateOne(BattlegroundBrackets.Bracket bracket, eRealm realm, ISet<string> reserved)
        {
            AutonomousBotIdentityGenerator.Identity identity = null;
            for (int attempt = 0; attempt < 20 && identity == null; attempt++)
            {
                eGender gender = Random.Shared.Next(2) == 0 ? eGender.Male : eGender.Female;
                AutonomousBotIdentityGenerator.Identity candidate = AutonomousBotIdentityGenerator.Generate(realm, gender, reserved);
                if (!IsInventedClass(candidate.CharacterClass))
                    identity = candidate;
            }
            if (identity == null)
                throw new InvalidOperationException("Could not pick a class for a bracket bot.");

            int level = Random.Shared.Next(bracket.MinLevel, bracket.MaxLevel + 1);
            string now = DateTime.UtcNow.ToString("O");
            GameLocation arrival = BattlegroundBrackets.BotArrival(bracket, realm);
            var record = new OfflineWorldBotRecord
            {
                Name = identity.Name,
                Realm = (int)identity.Realm,
                ClassId = (int)identity.CharacterClass,
                ClassName = identity.CharacterClass.ToString(),
                RaceId = (int)identity.Race,
                RaceName = identity.Race.ToString(),
                Gender = (int)identity.Gender,
                Level = level,
                Experience = GamePlayer.GetExperienceAmountForLevel(level - 1),
                MoneyCopper = 500_000L, // 50 gold pocket money; the gear comes from GameBot.EquipBracketKit at first login
                IsAlive = true,
                Health = 1,
                Activity = "Bracket bot waiting for a player in " + bracket.Name,
                CurrentGoal = "Awaiting a player in the battleground",
                ObjectiveProgress = "Created as a bracket bot",
                LastUpdateUtc = now,
                LastMeaningfulProgressUtc = now,
                RegionId = arrival.RegionID,
                BindRegionId = arrival.RegionID,
                X = arrival.X,
                Y = arrival.Y,
                Z = arrival.Z,
                BindX = arrival.X,
                BindY = arrival.Y,
                BindZ = arrival.Z,
                ZoneName = bracket.Name,
                ObjectiveKind = eAutonomousObjectiveKind.Battleground.ToString(),
            };
            if (!GameServer.Database.AddObject(record) || record.BotId <= 0)
                throw new InvalidOperationException($"Could not save bracket bot {identity.Name}.");
            var tag = new BracketBotRecord { BotId = record.BotId, BattlegroundRegion = bracket.RegionId, Realm = (int)realm, LevelCap = bracket.MaxLevel };
            if (!GameServer.Database.AddObject(tag))
                throw new InvalidOperationException($"Could not tag bracket bot {identity.Name}.");
            return identity.Name;
        }

        // The invented classes are not wanted on bots (owner, 2026-10-30).
        private static bool IsInventedClass(eCharacterClass characterClass) =>
            characterClass is eCharacterClass.Stormforged or eCharacterClass.PendragonSovereign or eCharacterClass.DeathKnight or eCharacterClass.TuathaSovereign;

        // ---- the tick -------------------------------------------------------------------------------

        private static Dictionary<ushort, Dictionary<eRealm, int>> PlayersByRegion()
        {
            var result = new Dictionary<ushort, Dictionary<eRealm, int>>();
            foreach (GamePlayer player in ClientService.Instance.GetPlayers())
            {
                ushort region = player.CurrentRegionID;
                if (!BattlegroundBrackets.IsBattlegroundRegion(region) || player.ObjectState != GameObject.eObjectState.Active)
                    continue;
                if (!result.TryGetValue(region, out var byRealm))
                    result[region] = byRealm = new Dictionary<eRealm, int>();
                byRealm[player.Realm] = byRealm.TryGetValue(player.Realm, out int count) ? count + 1 : 1;
            }
            int debug = BracketBotProperties.DEBUG_PRESENCE_REGION;
            if (BattlegroundBrackets.IsBattlegroundRegion((ushort)debug))
            {
                if (!result.TryGetValue((ushort)debug, out var byRealm))
                    result[(ushort)debug] = byRealm = new Dictionary<eRealm, int>();
                byRealm[eRealm.Albion] = byRealm.TryGetValue(eRealm.Albion, out int count) ? count + 1 : 1;
            }
            return result;
        }

        private static List<GameBot> OnlineBracketBots(int region, eRealm realm) =>
            AutonomousBotRegistry.Snapshot()
                .Where(bot => Tags.TryGetValue(bot.DatabaseID, out BracketBotRecord tag) && tag.BattlegroundRegion == region && tag.Realm == (int)realm)
                .ToList();

        private static void Tick(object state)
        {
            if (Interlocked.Exchange(ref _busy, 1) != 0)
                return;
            try
            {
                DateTime now = DateTime.UtcNow;
                Dictionary<ushort, Dictionary<eRealm, int>> players = PlayersByRegion();
                for (int i = 0; i < BattlegroundBrackets.Count; i++)
                {
                    BattlegroundBrackets.Bracket bracket = BattlegroundBrackets.Get(i);
                    int region = bracket.RegionId;
                    bool playersInside = players.TryGetValue(bracket.RegionId, out var humansByRealm) && humansByRealm.Values.Sum() > 0;
                    if (playersInside)
                        LastPlayerSeen[region] = now;
                    bool inGrace = LastPlayerSeen.TryGetValue(region, out DateTime seen) && (now - seen).TotalSeconds < Math.Max(0, BracketBotProperties.GRACE_SECONDS);

                    if (!BracketBotProperties.ENABLED || (!playersInside && !inGrace))
                    {
                        LogOutAll(region);
                        continue;
                    }
                    if (!playersInside)
                        continue; // grace period: leave the bots as they are
                    EnsureRoster(bracket, now);

                    // Players and ordinary bots of each realm already inside; bracket bots are not counted.
                    var present = BracketBotPlanner.Realms.ToDictionary(r => r, r => humansByRealm.TryGetValue(r, out int n) ? n : 0);
                    foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
                    {
                        if (bot.CurrentRegionID == region && !Tags.ContainsKey(bot.DatabaseID) && present.ContainsKey(bot.Realm))
                            present[bot.Realm]++;
                    }
                    Dictionary<eRealm, int> target = BracketBotPlanner.Plan(present, BracketBotProperties.EffectivePerRealm, BracketBotProperties.FillIn, BracketBotProperties.KEEP_EVEN);

                    foreach (eRealm realm in BracketBotPlanner.Realms)
                    {
                        List<GameBot> online = OnlineBracketBots(region, realm);
                        int want = target[realm];
                        if (online.Count < want)
                            LogIn(bracket, realm, want - online.Count);
                        else if (online.Count > want)
                            LogOut(online.Take(Math.Min(LogoutsPerTick, online.Count - want)));
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Error("Bracket bot tick failed", exception);
            }
            finally
            {
                Volatile.Write(ref _busy, 0);
            }
        }

        private static void LogIn(BattlegroundBrackets.Bracket bracket, eRealm realm, int needed)
        {
            GameLocation arrival = BattlegroundBrackets.BotArrival(bracket, realm);
            IEnumerable<long> ids = Tags.Values
                .Where(t => t.BattlegroundRegion == bracket.RegionId && t.Realm == (int)realm && !AutonomousBotRegistry.Contains(t.BotId))
                .Select(t => t.BotId)
                .ToList();
            int queued = 0;
            foreach (long id in ids)
            {
                if (queued >= Math.Min(needed, LoginsPerRealmPerTick))
                    break;
                // The bot arrives on open ground outside its realm's portal keep, a little apart from the others.
                int jitterX = Random.Shared.Next(-400, 401), jitterY = Random.Shared.Next(-400, 401);
                if (AutonomousPopulationController.QueueLogin(id, record =>
                    {
                        record.RegionId = arrival.RegionID;
                        record.X = arrival.X + jitterX;
                        record.Y = arrival.Y + jitterY;
                        record.Z = arrival.Z;
                        // Start fresh on the battleground goal: a saved solo or group task from an earlier session would walk it out of the battleground.
                        record.ObjectiveKind = eAutonomousObjectiveKind.Battleground.ToString();
                        record.ObjectiveAssignmentId = string.Empty;
                        record.ObjectiveExpiresUtc = string.Empty;
                        record.ObjectivePhase = "Awaiting objective";
                        record.CurrentGoal = "Battleground";
                    }))
                    queued++;
            }
            if (queued > 0)
                Log.Info($"BRACKET_BOTS login battleground={bracket.Name} realm={GlobalConstants.RealmToName(realm)} queued={queued} wanted={needed}");
        }

        private static void LogOutAll(int region)
        {
            List<GameBot> online = AutonomousBotRegistry.Snapshot()
                .Where(bot => Tags.TryGetValue(bot.DatabaseID, out BracketBotRecord tag) && tag.BattlegroundRegion == region)
                .Take(LogoutsPerTick)
                .ToList();
            if (online.Count > 0)
                LogOut(online);
        }

        private static void LogOut(IEnumerable<GameBot> bots)
        {
            foreach (GameBot bot in bots)
            {
                if (bot.InCombat || bot.Group != null && bot.Group.GetPlayersInTheGroup().Count > 0)
                    continue; // never pull a bot out of a fight or from a player's group; it goes on the next tick
                GameBot leaving = bot;
                // Same as the Living Server schedule: leave on the game loop, shortly after.
                _ = new ECSGameTimer(leaving, timer =>
                {
                    try
                    {
                        if (!leaving.InCombat)
                            leaving.Delete();
                    }
                    catch (Exception exception)
                    {
                        Log.Warn($"Bracket bot {leaving.Name} could not log out", exception);
                    }
                    return 0;
                }, 1_500 + Random.Shared.Next(2_000));
            }
        }
    }
}
