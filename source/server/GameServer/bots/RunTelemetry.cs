using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS
{
    public static class RunTelemetryProperties
    {
        [ServerProperty("run_telemetry", "run_telemetry", "Tuning telemetry for bot runs: loot drops, fight summaries, bot progress snapshots, mob resets and path failures, bot vendor trades and sampled pull targets, written to logs/run-telemetry. Diagnostic only; never changes game decisions.", true)]
        public static bool ENABLED = true;

        [ServerProperty("run_telemetry", "run_telemetry_loot", "One record per monster kill that rolls loot: every item and coin dropped, with the monster and killer.", true)]
        public static bool LOOT = true;

        [ServerProperty("run_telemetry", "run_telemetry_fights", "One record per monster fight with players or bots: won, lost or reset, its length, damage dealt and taken per participant.", true)]
        public static bool FIGHTS = true;

        [ServerProperty("run_telemetry", "run_telemetry_snapshot_minutes", "Minutes between progress snapshots of each autonomous gamebot (level, XP, money, gear value, activity). 0 turns them off.", 10)]
        public static int SNAPSHOT_MINUTES = 10;

        [ServerProperty("run_telemetry", "run_telemetry_mob_ai", "Count monster path failures while chasing, written as a summary every snapshot period. Monster resets are fight records.", true)]
        public static bool MOB_AI = true;

        [ServerProperty("run_telemetry", "run_telemetry_economy", "One record per autonomous gamebot vendor sale, purchase or forced backpack clear.", true)]
        public static bool ECONOMY = true;

        [ServerProperty("run_telemetry", "run_telemetry_target_sample_percent", "Percent of new autonomous gamebot camp pull targets recorded with the reason they won (0 turns it off).", 10)]
        public static int TARGET_SAMPLE_PERCENT = 10;

        [ServerProperty("run_telemetry", "run_telemetry_keep_days", "Days of run-telemetry files to keep; older run-YYYYMMDD.jsonl files are deleted when a new day's file starts (0 keeps them all).", 7)]
        public static int KEEP_DAYS = 7;
    }

    /// <summary>
    /// JSONL records for tuning the game, the AI and loot from a bot run (logs/run-telemetry/run-YYYYMMDD.jsonl,
    /// summarized by source/tools/run_telemetry_report.py). Every hook is wrapped so that a logging fault can never
    /// interrupt combat, loot or movement, and nothing here feeds back into a decision.
    /// </summary>
    public static class RunTelemetry
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(RunTelemetry));
        private const long FileLimitBytes = 2048L * 1024 * 1024;
        // A monster left idle this long without dying or resetting starts a fresh fight on its next hit.
        private const long StaleFightMilliseconds = 60_000;
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
        private static readonly ConcurrentQueue<string> Pending = new();
        private static readonly ConditionalWeakTable<GameNPC, Fight> Fights = new();
        private static readonly ConcurrentDictionary<long, BotProgress> Progress = new();
        private static readonly ConcurrentDictionary<long, ushort> LastPullTarget = new();
        private static readonly ConcurrentDictionary<(string Mob, ushort Region, int SpawnX, int SpawnY, string Status, bool Jumped), long> PathFailures = new();
        private static readonly ConcurrentDictionary<(string Side, string Who, int Band, string Weapon, bool OffHand, bool Styled, string Result), SwingTally> Swings = new();
        private static long _nextPathFailureFlushTick;
        private static Timer _flushTimer;
        private static int _writing;
        // The UTC day whose file passed the size limit; records resume with the next day's file.
        private static string _fullDay;

        private static bool On => RunTelemetryProperties.ENABLED;

        public static string Folder => Path.Combine(
            GameServer.Instance?.Configuration?.RootDirectory ?? AppContext.BaseDirectory, "logs", "run-telemetry");

        private sealed class Fight
        {
            public long FirstTick, LastTick;
            // The monster's own melee and ranged swings at players and bots, and how many landed.
            public int MobSwings, MobHits;
            public readonly Dictionary<GameLiving, Side> Sides = new();
        }

        private sealed class Side
        {
            public double Dealt, Taken;
            public long FirstTick;
        }

        private sealed class BotProgress
        {
            public long NextTick, Experience, Money, Kills, Deaths, Fights, Resets;
            public int Level;
            public DateTime AtUtc;
        }

        private sealed class SwingTally
        {
            public long Count, Damage;
            public double Miss, Evade, Parry, Block;
        }

        #region Hooks

        /// <summary>GameLiving.TakeDamage, after pet damage is folded into its owner.</summary>
        public static void Damage(GameLiving victim, GameObject source, double amount)
        {
            if (!On || !RunTelemetryProperties.FIGHTS || amount <= 0 || source is not GameLiving attacker)
                return;
            try
            {
                if (IsMonster(victim) && IsParticipant(attacker))
                    Add(victim as GameNPC, attacker, amount, 0);
                else if (IsParticipant(victim) && Root(attacker) is GameNPC mob && IsMonster(mob))
                    Add(mob, victim, 0, amount);
            }
            catch { }
        }

        /// <summary>AbstractServerRules.OnNpcKilled: the monster died, every participant won.</summary>
        public static void MobKilled(GameNPC mob, GameObject killer)
        {
            if (!On || !RunTelemetryProperties.FIGHTS || mob == null || !IsMonster(mob))
                return;
            try
            {
                Fight fight = Take(mob);
                if (fight == null)
                    return;
                lock (fight)
                {
                    WriteFight("win", mob, fight, null, killer);
                    foreach (GameLiving living in fight.Sides.Keys)
                        if (living is GameBot bot && Progress.TryGetValue(bot.DatabaseID, out BotProgress progress))
                            Interlocked.Increment(ref progress.Kills);
                }
            }
            catch { }
        }

        /// <summary>StandardMobState_RETURN_TO_SPAWN.Enter: the monster gave up and is leashing home.</summary>
        public static void MobReset(GameNPC mob)
        {
            if (!On || !RunTelemetryProperties.FIGHTS || mob == null || !IsMonster(mob))
                return;
            try
            {
                Fight fight = Take(mob);
                if (fight == null)
                    return;
                lock (fight)
                {
                    WriteFight("reset", mob, fight, null, null);
                    foreach (GameLiving living in fight.Sides.Keys)
                        if (living is GameBot bot && Progress.TryGetValue(bot.DatabaseID, out BotProgress progress))
                            Interlocked.Increment(ref progress.Resets);
                }
            }
            catch { }
        }

        /// <summary>AutonomousDeathDiagnostics.Record: a gamebot lost its fight; the monster's fight carries on.</summary>
        public static void BotDied(GameBot bot, GameObject killer)
        {
            if (!On || bot == null)
                return;
            try
            {
                if (Progress.TryGetValue(bot.DatabaseID, out BotProgress progress))
                    Interlocked.Increment(ref progress.Deaths);
                if (!RunTelemetryProperties.FIGHTS || Root(killer as GameLiving) is not GameNPC mob || !IsMonster(mob) ||
                    !Fights.TryGetValue(mob, out Fight fight))
                    return;
                lock (fight)
                    WriteFight("loss", mob, fight, bot, killer);
            }
            catch { }
        }

        /// <summary>AbstractServerRules.DropLoot, with the full roll for one kill (an empty roll is recorded too).</summary>
        public static void Loot(GameNPC mob, GameObject killer, IReadOnlyList<DbItemTemplate> drops)
        {
            if (!On || !RunTelemetryProperties.LOOT || mob == null)
                return;
            try
            {
                GameLiving owner = Root(killer as GameLiving);
                long coin = 0;
                var items = new List<object>();
                foreach (DbItemTemplate item in drops)
                {
                    if (GameMoney.IsItemMoney(item.Name))
                    {
                        coin += item.Price;
                        continue;
                    }
                    int bonusCount = BonusTypes(item).Count(type => type != 0);
                    int utility = Math.Abs(item.Bonus1) + Math.Abs(item.Bonus2) + Math.Abs(item.Bonus3) + Math.Abs(item.Bonus4) +
                                  Math.Abs(item.Bonus5) + Math.Abs(item.Bonus6) + Math.Abs(item.Bonus7) + Math.Abs(item.Bonus8) +
                                  Math.Abs(item.Bonus9) + Math.Abs(item.Bonus10) + Math.Abs(item.ExtraBonus);
                    items.Add(new
                    {
                        id = item.Id_nb, name = item.Name, source = item switch
                        {
                            GeneratedUniqueItem => "rog",
                            DbItemUnique => "unique",
                            _ => "template"
                        },
                        level = item.Level, quality = item.Quality, objectType = item.Object_Type, slot = item.Item_Type,
                        dpsAf = item.DPS_AF, speedAbs = item.SPD_ABS, bonusLevel = item.Bonus, bonusCount, utility,
                        price = item.Price, realm = item.Realm
                    });
                }
                Write("loot", new
                {
                    mob = mob.Name, mobLevel = mob.Level, mobId = mob.InternalID, region = mob.CurrentRegionID,
                    zone = mob.CurrentZone?.Description, killer = owner?.Name, killerKind = AutonomousDeathDiagnostics.KillerKind(owner),
                    killerClass = ClassName(owner), killerLevel = owner?.Level ?? 0, coin, items
                });
            }
            catch { }
        }

        /// <summary>NpcMovementComponent: a monster in combat could not path to its target.</summary>
        public static void MobPathFailure(GameNPC mob, string status, bool jumped)
        {
            if (!On || !RunTelemetryProperties.MOB_AI || mob == null || !mob.InCombat || !IsMonster(mob))
                return;
            try
            {
                // The spawn point (to 100 units) finds the exact spawn to fix; a respawn reuses the same one.
                int spawnX = (mob.SpawnPoint?.X ?? mob.X) / 100 * 100, spawnY = (mob.SpawnPoint?.Y ?? mob.Y) / 100 * 100;
                PathFailures.AddOrUpdate((mob.Name, mob.CurrentRegionID, spawnX, spawnY, status, jumped), 1, (_, n) => n + 1);
            }
            catch { }
        }

        /// <summary>
        /// AttackComponent.MakeAttack (NPC and bot attackers): one melee or ranged swing. Monster swings at players and
        /// bots, and bot swings at monsters, are counted by level band, weapon and result, with the defence chances rolled.
        /// </summary>
        public static void Swing(AttackData ad)
        {
            if (!On || !RunTelemetryProperties.MOB_AI || ad?.Attacker == null || ad.Target == null)
                return;
            try
            {
                string side, who;
                if (IsMonster(ad.Attacker) && IsParticipant(ad.Target))
                {
                    (side, who) = ("mob", ClassName(ad.Target));
                    if (Fights.TryGetValue((GameNPC)ad.Attacker, out Fight fight))
                        lock (fight)
                        {
                            fight.MobSwings++;
                            if (ad.AttackResult is eAttackResult.HitUnstyled or eAttackResult.HitStyle)
                                fight.MobHits++;
                        }
                }
                else if (ad.Attacker is GameBot bot && IsMonster(ad.Target))
                    (side, who) = ("bot", bot.ClassName);
                else
                    return;
                int band = ad.Attacker.Level / 10 * 10;
                string weapon = ad.Weapon == null ? "none" : ((eObjectType)ad.Weapon.Object_Type).ToString();
                SwingTally tally = Swings.GetOrAdd((side, who ?? "?", band, weapon, ad.IsOffHand, ad.Style != null, ad.AttackResult.ToString()),
                    _ => new SwingTally());
                lock (tally)
                {
                    tally.Count++;
                    tally.Damage += Math.Max(0, ad.Damage + ad.CriticalDamage);
                    tally.Miss += ad.MissChance;
                    tally.Evade += ad.EvadeChance;
                    tally.Parry += ad.ParryChance;
                    tally.Block += ad.BlockChance;
                }
            }
            catch { }
        }

        /// <summary>AutonomousWorldBotController.Tick: a progress snapshot every run_telemetry_snapshot_minutes.</summary>
        public static void BotTick(GameBot bot)
        {
            int minutes = RunTelemetryProperties.SNAPSHOT_MINUTES;
            if (!On || minutes <= 0 || bot == null)
                return;
            long now = GameLoop.GameLoopTime;
            try
            {
                if (RunTelemetryProperties.MOB_AI && now >= Interlocked.Read(ref _nextPathFailureFlushTick))
                {
                    long next = now + minutes * 60_000L;
                    if (Interlocked.Exchange(ref _nextPathFailureFlushTick, next) <= now)
                    {
                        FlushPathFailures();
                        FlushSwings();
                    }
                }

                // Snapshots are staggered by bot id so a full roster does not all write in one tick.
                BotProgress progress = Progress.GetOrAdd(bot.DatabaseID, id => new BotProgress
                {
                    NextTick = now + id % 60 * 1_000, Experience = bot.Experience, Level = bot.Level,
                    Money = AutonomousBotEconomy.GetMoney(id), AtUtc = DateTime.UtcNow
                });
                if (now < progress.NextTick)
                    return;
                progress.NextTick = now + minutes * 60_000L;

                DateTime utcNow = DateTime.UtcNow;
                long money = AutonomousBotEconomy.GetMoney(bot.DatabaseID);
                long levelStart = bot.Level > 1 ? GamePlayer.GetExperienceAmountForLevel(bot.Level - 1) : 0;
                long levelEnd = GamePlayer.GetExperienceAmountForLevel(bot.Level);
                double levelPercent = levelEnd > levelStart ? (bot.Experience - levelStart) * 100.0 / (levelEnd - levelStart) : 0;
                var equipped = bot.Inventory?.EquippedItems?.Where(item => item != null).ToArray() ?? Array.Empty<DbInventoryItem>();
                Write("snapshot", new
                {
                    bot = bot.Name, botId = bot.DatabaseID, cls = bot.ClassName, realm = bot.Realm.ToString(),
                    level = bot.Level, levelPercent = Math.Round(levelPercent, 1), experience = bot.Experience,
                    minutes = Math.Round((utcNow - progress.AtUtc).TotalMinutes, 1),
                    xpGained = bot.Experience - progress.Experience, levelsGained = bot.Level - progress.Level,
                    money, moneyGained = money - progress.Money,
                    kills = Interlocked.Exchange(ref progress.Kills, 0), deaths = Interlocked.Exchange(ref progress.Deaths, 0),
                    resets = Interlocked.Exchange(ref progress.Resets, 0),
                    gearValue = equipped.Sum(AutonomousBotEconomy.EquipmentValue), equippedPieces = equipped.Length,
                    averageItemLevel = equipped.Length == 0 ? 0 : Math.Round(equipped.Average(item => item.Level), 1),
                    group = bot.Group?.MemberCount ?? 1, activity = bot.PersistentRecord?.Activity,
                    goal = bot.PersistentRecord?.CurrentGoal, region = bot.CurrentRegionID, zone = bot.CurrentZone?.Description,
                    alive = bot.IsAlive
                });
                progress.Experience = bot.Experience;
                progress.Level = bot.Level;
                progress.Money = money;
                progress.AtUtc = utcNow;
            }
            catch { }
        }

        /// <summary>AutonomousBotEconomy: a vendor sale, a purchase or a forced backpack clear.</summary>
        public static void Trade(GameBot bot, string kind, GameNPC merchant, DbInventoryItem item, long copper, int count = 1)
        {
            if (!On || !RunTelemetryProperties.ECONOMY || bot == null)
                return;
            try
            {
                Write("trade", new
                {
                    bot = bot.Name, botId = bot.DatabaseID, cls = bot.ClassName, level = bot.Level, kind,
                    merchant = merchant?.Name, item = item?.Name, itemId = item?.Id_nb, itemLevel = item?.Level ?? 0,
                    count, copper, balance = AutonomousBotEconomy.GetMoney(bot.DatabaseID),
                    region = bot.CurrentRegionID, zone = bot.CurrentZone?.Description
                });
            }
            catch { }
        }

        /// <summary>AutonomousWorldBotController camp search: a sample of new pull targets and why they won.</summary>
        public static void PullTarget(GameBot bot, GameNPC target, string campId, string campMonster, bool groupDirected)
        {
            int percent = RunTelemetryProperties.TARGET_SAMPLE_PERCENT;
            if (!On || percent <= 0 || bot == null || target == null)
                return;
            try
            {
                // The search reruns every think; only a change of target is a decision.
                if (LastPullTarget.TryGetValue(bot.DatabaseID, out ushort last) && last == target.ObjectID)
                    return;
                LastPullTarget[bot.DatabaseID] = target.ObjectID;
                if (Random.Shared.Next(100) >= percent)
                    return;
                Write("pull-target", new
                {
                    bot = bot.Name, botId = bot.DatabaseID, cls = bot.ClassName, level = bot.Level,
                    group = bot.Group?.MemberCount ?? 1, groupDirected, camp = campId, campMonster,
                    target = target.Name, targetLevel = target.Level, con = ConLevels.GetConColor(bot.GetConLevel(target)).ToString(),
                    distance = bot.GetDistanceTo(target), targetInCombat = target.InCombat,
                    nearbyMonsters = target.GetNPCsInRadius(500).Count(npc => npc != target && npc.IsAlive && IsMonster(npc)),
                    hp = bot.HealthPercent, mana = bot.ManaPercent, region = bot.CurrentRegionID, zone = bot.CurrentZone?.Description
                });
            }
            catch { }
        }

        #endregion

        #region Fights

        private static void Add(GameNPC mob, GameLiving participant, double dealt, double taken)
        {
            if (mob == null)
                return;
            long now = GameLoop.GameLoopTime;
            Fight fight = Fights.GetValue(mob, _ => new Fight { FirstTick = now, LastTick = now });
            lock (fight)
            {
                if (now - fight.LastTick > StaleFightMilliseconds)
                {
                    fight.Sides.Clear();
                    fight.FirstTick = now;
                }
                fight.LastTick = now;
                if (!fight.Sides.TryGetValue(participant, out Side side))
                    fight.Sides[participant] = side = new Side { FirstTick = now };
                side.Dealt += dealt;
                side.Taken += taken;
            }
        }

        /// <summary>Ends a monster's fight (death or reset). Respawns reuse the GameNPC, so the record must go. Callers lock it.</summary>
        private static Fight Take(GameNPC mob)
        {
            if (!Fights.TryGetValue(mob, out Fight fight))
                return null;
            Fights.Remove(mob);
            lock (fight)
                return fight.Sides.Count == 0 ? null : fight;
        }

        private static void WriteFight(string outcome, GameNPC mob, Fight fight, GameBot loser, GameObject killer)
        {
            long now = GameLoop.GameLoopTime;
            int baf = mob.Brain is StandardMobBrain brain ? brain.BafAddCount : 0;
            var who = fight.Sides
                .Where(pair => loser == null || pair.Key == loser)
                .Select(pair => new
                {
                    name = pair.Key.Name, kind = AutonomousDeathDiagnostics.KillerKind(pair.Key), cls = ClassName(pair.Key),
                    level = pair.Key.Level, con = ConLevels.GetConColor(pair.Key.GetConLevel(mob)).ToString(),
                    dealt = (long)pair.Value.Dealt, taken = (long)pair.Value.Taken,
                    joinedAfterMs = pair.Value.FirstTick - fight.FirstTick, group = pair.Key.Group?.MemberCount ?? 1,
                    hp = pair.Key.HealthPercent, mana = pair.Key.ManaPercent, alive = pair.Key.IsAlive,
                    distance = pair.Key.ObjectState == GameObject.eObjectState.Active && pair.Key.CurrentRegionID == mob.CurrentRegionID
                        ? mob.GetDistanceTo(pair.Key) : -1
                })
                .ToArray();
            if (who.Length == 0)
                return;
            Write("fight", new
            {
                outcome, mob = mob.Name, mobLevel = mob.Level, mobId = mob.InternalID, mobMaxHealth = mob.MaxHealth,
                mobHp = mob.HealthPercent, durationMs = now - fight.FirstTick, participants = fight.Sides.Count, baf,
                killer = killer?.Name, region = mob.CurrentRegionID, zone = mob.CurrentZone?.Description,
                mobSwings = fight.MobSwings, mobHits = fight.MobHits,
                // Why a reset happened: the monster's own last swing and last hit taken, and how far it had chased.
                mobLastSwingAgoMs = mob.LastAttackTickPvE > 0 ? now - mob.LastAttackTickPvE : -1,
                mobLastHitAgoMs = mob.LastAttackedByEnemyTickPvE > 0 ? now - mob.LastAttackedByEnemyTickPvE : -1,
                fromSpawn = mob.SpawnPoint != null ? mob.GetDistanceTo(mob.SpawnPoint) : -1, who
            });
        }

        private static void FlushPathFailures()
        {
            if (PathFailures.IsEmpty)
                return;
            var counts = new List<object>();
            foreach (var key in PathFailures.Keys.ToArray())
                if (PathFailures.TryRemove(key, out long count))
                    counts.Add(new { mob = key.Mob, region = key.Region, spawnX = key.SpawnX, spawnY = key.SpawnY, status = key.Status, jumped = key.Jumped, count });
            Write("mob-path-failures", new { counts });
        }

        private static void FlushSwings()
        {
            if (Swings.IsEmpty)
                return;
            var counts = new List<object>();
            foreach (var key in Swings.Keys.ToArray())
                if (Swings.TryRemove(key, out SwingTally t))
                    lock (t)
                        counts.Add(new
                        {
                            side = key.Side, who = key.Who, band = key.Band, weapon = key.Weapon, offHand = key.OffHand,
                            styled = key.Styled, result = key.Result, count = t.Count, damage = t.Damage,
                            miss = t.Miss / t.Count, evade = t.Evade / t.Count, parry = t.Parry / t.Count, block = t.Block / t.Count
                        });
            Write("swings", new { counts });
        }

        #endregion

        #region Helpers

        /// <summary>A real monster: not a gamebot, pet, guard or other realm-controlled NPC.</summary>
        private static bool IsMonster(GameLiving living) =>
            living is GameNPC npc && npc is not GameBot && npc.Brain is not IControlledBrain && npc.Brain is StandardMobBrain &&
            npc is not Keeps.GameKeepGuard && npc.Realm == eRealm.None;

        private static bool IsParticipant(GameLiving living) => living is GamePlayer or GameBot;

        private static GameLiving Root(GameLiving living) =>
            living is GameNPC { Brain: IControlledBrain brain } && brain.GetLivingOwner() is GameLiving owner ? owner : living;

        private static string ClassName(GameLiving living) => living switch
        {
            GamePlayer player => player.CharacterClass?.Name,
            GameBot bot => bot.ClassName,
            _ => null
        };

        private static IEnumerable<int> BonusTypes(DbItemTemplate item)
        {
            yield return item.Bonus1Type; yield return item.Bonus2Type; yield return item.Bonus3Type; yield return item.Bonus4Type;
            yield return item.Bonus5Type; yield return item.Bonus6Type; yield return item.Bonus7Type; yield return item.Bonus8Type;
            yield return item.Bonus9Type; yield return item.Bonus10Type; yield return item.ExtraBonusType;
        }

        private static void Write(string ev, object detail)
        {
            if (_fullDay == DateTime.UtcNow.ToString("yyyyMMdd"))
                return;
            Pending.Enqueue(JsonSerializer.Serialize(new { t = DateTime.UtcNow, ev, detail }, Json));
        }

        #endregion

        #region File

        [GameServerStartedEvent]
        public static void Start(DOLEvent e, object sender, EventArgs args)
        {
            _flushTimer?.Dispose();
            _flushTimer = new Timer(_ => Flush(), null, 2_000, 2_000);
        }

        [GameServerStoppedEvent]
        public static void Stop(DOLEvent e, object sender, EventArgs args)
        {
            _flushTimer?.Dispose();
            _flushTimer = null;
            if (On && RunTelemetryProperties.MOB_AI)
            {
                FlushPathFailures();
                FlushSwings();
            }
            Flush();
        }

        private static string _prunedDay;

        /// <summary>
        /// Deletes run-YYYYMMDD.jsonl files older than KEEP_DAYS. A busy bot run writes about 1 GB a day and nothing
        /// cleaned them (setup review 2026-11-06).
        /// </summary>
        private static void PruneOldFiles(DateTime nowUtc)
        {
            int keep = RunTelemetryProperties.KEEP_DAYS;
            if (keep <= 0)
                return;
            foreach (string file in Directory.EnumerateFiles(Folder, "run-*.jsonl"))
            {
                if (IsExpired(Path.GetFileName(file), nowUtc, keep))
                {
                    try { File.Delete(file); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        /// <summary>Whether a run-YYYYMMDD.jsonl file name is from more than <paramref name="keepDays"/> days ago. Pure.</summary>
        public static bool IsExpired(string fileName, DateTime nowUtc, int keepDays)
        {
            if (keepDays <= 0 || fileName is null || fileName.Length != "run-yyyyMMdd.jsonl".Length || !fileName.StartsWith("run-") || !fileName.EndsWith(".jsonl"))
                return false;
            return DateTime.TryParseExact(fileName.Substring(4, 8), "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
                       System.Globalization.DateTimeStyles.None, out DateTime day)
                   && day < nowUtc.Date.AddDays(-keepDays);
        }

        private static void Flush()
        {
            if (Pending.IsEmpty || Interlocked.Exchange(ref _writing, 1) == 1)
                return;
            try
            {
                Directory.CreateDirectory(Folder);
                string day = DateTime.UtcNow.ToString("yyyyMMdd");
                string path = Path.Combine(Folder, $"run-{day}.jsonl");
                if (day != _prunedDay)
                {
                    _prunedDay = day;
                    PruneOldFiles(DateTime.UtcNow);
                }
                if (File.Exists(path) && new FileInfo(path).Length > FileLimitBytes)
                {
                    _fullDay = day;
                    Pending.Clear();
                    Log.Warn($"RUN_TELEMETRY {path} passed {FileLimitBytes / 1024 / 1024} MB; records stop until the next UTC day.");
                    return;
                }
                var builder = new StringBuilder();
                while (Pending.TryDequeue(out string line))
                    builder.Append(line).Append('\n');
                File.AppendAllText(path, builder.ToString());
            }
            catch (Exception exception)
            {
                Log.Warn("RUN_TELEMETRY could not write its log: " + exception.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _writing, 0);
            }
        }

        #endregion
    }
}
