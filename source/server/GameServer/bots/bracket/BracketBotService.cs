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
    /// Battleground bracket bots (daoc-divided-linux addition). While a player is inside a battleground, brand-new bots are created for it
    /// (one per realm every couple of seconds, random name, class and level in the bracket, at most 2 of a class per realm), logged in at their
    /// realm's arrival point, and deleted some time after the last player leaves, so every visit meets different characters. They are
    /// tagged in divided_bracket_bots; the normal population controller skips them. Deleting uses the same queue as the admin's "delete bot".
    /// Keep this file free of DividedRealms code changes: the only hooks elsewhere are IsManaged, IsAtCap, TryChooseGoal and QueueLogin callers.
    /// </summary>
    public static class BracketBotService
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly ConcurrentDictionary<long, BracketBotRecord> Tags = new();
        private static readonly Dictionary<int, DateTime> LastPlayerSeen = new(); // timer thread only
        private static readonly ConcurrentDictionary<long, int> ClassOf = new(); // bot id -> class id, for the 2-per-class limit
        private static readonly Dictionary<int, DateTime> CreateBackoff = new(); // timer thread only
        private static readonly object CreateLock = new();
        private const int LoginsPerRealmPerTick = 4;
        private const int DeletesPerTick = 6;
        private static Timer _timer;
        private static int _busy;

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            Reload();
            // Bracket bots are made fresh for every visit: any left in the database from before the server stopped are not wanted.
            List<BracketBotRecord> stale = Tags.Values.ToList();
            foreach (BracketBotRecord leftover in stale)
                RetireAndQueueDelete(leftover);
            if (stale.Count > 0)
                Log.Info($"BRACKET_BOTS startup cleanup: queued deletion of {stale.Count} leftover bracket bot(s)");
            _timer = new Timer(Tick, null, 5000, 2000);
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

        /// <summary>Creates the given number of bots per realm for one battleground and tags them. Returns the new names.</summary>
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
            // The population controller logs bots in from its cached roster: have it re-read the roster now, or new bots wait for its slow poll.
            AutonomousPopulationController.RequestRefresh();
            Log.Debug($"Created {names.Count} bracket bot(s) for {bracket.Name}.");
            return names;
        }

        /// <summary>
        /// Deletes a bracket bot the way the admin's "delete bot" does: mark it retired and queue a Delete command, which the population
        /// controller runs on the game loop (guild, market lots, inventory, record, and the live bot if it is in the world). Then forgets the tag.
        /// </summary>
        private static void RetireAndQueueDelete(BracketBotRecord tag)
        {
            string now = DateTime.UtcNow.ToString("O");
            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
            {
                OfflineWorldBotRecord record = DOLDB<OfflineWorldBotRecord>.SelectObject(DB.Column("BotId").IsEqualTo(tag.BotId));
                if (record != null)
                {
                    // Mark both copies: a live bot saves its in-memory record and must not un-retire itself.
                    if (AutonomousBotRegistry.TryGet(tag.BotId, out GameBot live) && live.PersistentRecord != null)
                        Retire(live.PersistentRecord, now);
                    Retire(record, now);
                    GameServer.Database.SaveObject(record);
                }

                bool alreadyQueued = DOLDB<OfflineBotCommandRecord>
                    .SelectObjects(DB.Column("BotId").IsEqualTo(tag.BotId))
                    .Any(c => c.CommandType == "Delete" && (c.State == "Pending" || c.State == "Processing"));
                if (!alreadyQueued)
                {
                    GameServer.Database.AddObject(new OfflineBotCommandRecord
                    {
                        BotId = tag.BotId,
                        CommandType = "Delete",
                        RequestedUtc = now,
                        State = "Pending",
                        RequestedByAccount = "bracket-bots",
                    });
                }

                tag.AllowDelete = true;
                GameServer.Database.DeleteObject(tag);
            }
            Tags.TryRemove(tag.BotId, out _);
            ClassOf.TryRemove(tag.BotId, out _);
        }

        private static void Retire(OfflineWorldBotRecord record, string now)
        {
            record.IsRetired = true;
            record.Activity = "Deletion requested";
            record.CurrentGoal = string.Empty;
            record.LastUpdateUtc = now;
            record.Dirty = true;
        }

        private static string CreateOne(BattlegroundBrackets.Bracket bracket, eRealm realm, ISet<string> reserved)
        {
            AutonomousBotIdentityGenerator.Identity identity = null;
            for (int attempt = 0; attempt < 30 && identity == null; attempt++)
            {
                eGender gender = Random.Shared.Next(2) == 0 ? eGender.Male : eGender.Female;
                AutonomousBotIdentityGenerator.Identity candidate = AutonomousBotIdentityGenerator.Generate(realm, gender, reserved);
                if (IsInventedClass(candidate.CharacterClass))
                    continue;
                // Variety: at most 2 of a class per realm here. After 25 tries take what comes (a realm with few classes still fills).
                int sameClass = Tags.Values.Count(t => t.BattlegroundRegion == bracket.RegionId && t.Realm == (int)realm
                                                       && ClassOf.TryGetValue(t.BotId, out int classId) && classId == (int)candidate.CharacterClass);
                if (BracketBotPlanner.ClassAllowed(sameClass) || attempt >= 25)
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
            Tags[tag.BotId] = tag;
            ClassOf[tag.BotId] = (int)identity.CharacterClass;
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
                        DeleteAll(region);
                        continue;
                    }
                    if (!playersInside)
                        continue; // grace period: leave the bots as they are

                    // Players and ordinary bots of each realm already inside; bracket bots are not counted.
                    var present = BracketBotPlanner.Realms.ToDictionary(r => r, r => humansByRealm.TryGetValue(r, out int n) ? n : 0);
                    foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
                    {
                        if (bot.CurrentRegionID == region && !Tags.ContainsKey(bot.DatabaseID) && present.ContainsKey(bot.Realm))
                            present[bot.Realm]++;
                    }
                    Dictionary<eRealm, int> target = BracketBotPlanner.Plan(present, BracketBotProperties.EffectivePerRealm, BracketBotProperties.FillIn, BracketBotProperties.KEEP_EVEN);

                    // Make one new bot per realm that still lacks some (created or waiting to log in), so a wave builds up over a few seconds.
                    var create = new Dictionary<eRealm, int>();
                    foreach (eRealm realm in BracketBotPlanner.Realms)
                    {
                        int existing = Tags.Values.Count(t => t.BattlegroundRegion == region && t.Realm == (int)realm);
                        if (BracketBotPlanner.NeedsAnother(target[realm], existing))
                            create[realm] = 1;
                    }
                    if (create.Count > 0 && !(CreateBackoff.TryGetValue(region, out DateTime until) && now < until))
                    {
                        try
                        {
                            CreateBots(bracket, create);
                        }
                        catch (Exception exception)
                        {
                            CreateBackoff[region] = now.AddSeconds(60); // do not retry a failing insert every tick
                            Log.Error($"Bracket bots could not be created for {bracket.Name}", exception);
                        }
                    }

                    // Log in the ones that are made. Realms with more bots than wanted keep them until the visit ends (no churn).
                    foreach (eRealm realm in BracketBotPlanner.Realms)
                    {
                        int online = OnlineBracketBots(region, realm).Count;
                        if (online < target[realm])
                            LogIn(bracket, realm, target[realm] - online);
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

        /// <summary>Deletes the bracket bots of a battleground (the visit is over, or the feature is off), a few per tick.</summary>
        private static void DeleteAll(int region)
        {
            int removed = 0;
            foreach (BracketBotRecord tag in Tags.Values.Where(t => t.BattlegroundRegion == region).ToList())
            {
                if (removed >= DeletesPerTick)
                    break;
                // Never pull a bot out of a player's group; it goes on a later tick. Combat does not hold it back: the visit is over (no player
                // is inside), and bots of different realms left alone in a battleground fight each other for a long time.
                if (AutonomousBotRegistry.TryGet(tag.BotId, out GameBot bot) && bot.Group != null && bot.Group.GetPlayersInTheGroup().Count > 0)
                    continue;
                try
                {
                    RetireAndQueueDelete(tag);
                    removed++;
                }
                catch (Exception exception)
                {
                    Log.Warn($"Bracket bot {tag.BotId} could not be queued for deletion", exception);
                }
            }
            if (removed > 0)
                Log.Info($"BRACKET_BOTS delete region={region} queued={removed}");
        }
    }
}
