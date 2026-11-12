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
using DOL.Events;
using DOL.GS.ServerProperties;
using DOL.GS.Spells;
using DOL.GS.Styles;
using DOL.Logging;

namespace DOL.GS
{
    public static class BotTesterProperties
    {
        [ServerProperty("bot_tester", "bot_tester_log", "Bots act as testers: record failed and refused spells, styles, spell resists, exceptions and deaths, with a periodic report, in logs/bot-tester. Diagnostic only; never changes bot decisions.", true)]
        public static bool ENABLED = true;

        [ServerProperty("bot_tester", "bot_tester_log_verbose", "Also write every bot spell cast, spell hit and style execution to the bot tester event log (large).", false)]
        public static bool VERBOSE = false;

        [ServerProperty("bot_tester", "bot_tester_report_minutes", "Minutes between bot tester reports (REPORT.txt and summary JSON).", 10)]
        public static int REPORT_MINUTES = 10;
    }

    /// <summary>
    /// Turns every bot (companions, /spawn helpers and autonomous gamebots) into a tester. It counts what each
    /// class's spells and styles actually do in play, and records what goes wrong: casts refused with the
    /// game's own message, interrupts, resists and immunities, damage spells that never deal damage, styles
    /// whose requirements fail, spells and styles that are never used, handler exceptions, and deaths with
    /// the bot's last actions.
    /// Output (server folder): logs/bot-tester/events-YYYYMMDD.jsonl, summary-latest.json and REPORT.txt.
    /// Measurements never feed back into bot decisions, and a logging failure never reaches the game.
    /// </summary>
    public static class BotTesterLog
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(BotTesterLog));
        private const long EventFileLimitBytes = 256L * 1024 * 1024;
        private const int RecentActions = 8;
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
        private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

        private static readonly ConcurrentQueue<string> Pending = new();
        // Keyed by (kind, class, id) value tuples: the skip hooks run on every bot think and must not allocate keys.
        private static readonly ConcurrentDictionary<(string Kind, int ClassId, int Id), SkillStats> Skills = new();
        private static readonly ConcurrentDictionary<int, ClassStats> Classes = new();
        private static readonly ConcurrentDictionary<string, ProblemStats> Exceptions = new();
        private static readonly ConcurrentDictionary<string, long> Throttle = new();
        private static readonly ConditionalWeakTable<GameLiving, BotState> States = new();
        private static readonly DateTime StartedUtc = DateTime.UtcNow;
        private static Timer _flushTimer;
        private static Timer _reportTimer;
        private static int _writing;
        private static bool _eventFileFull;

        public static bool Enabled => BotTesterProperties.ENABLED;
        public static bool Verbose => BotTesterProperties.ENABLED && BotTesterProperties.VERBOSE;

        public static string Folder => Path.Combine(
            GameServer.Instance?.Configuration?.RootDirectory ?? AppContext.BaseDirectory, "logs", "bot-tester");

        #region Statistics

        public sealed class SkillStats
        {
            public string Kind;
            public int ClassId;
            public int Id;
            public string Name;
            public string Type;
            public int Level;
            public bool Harmful;
            public double ListedDamage;
            public readonly ConcurrentDictionary<string, byte> KnownBy = new();
            public long Attempts, Casts, Refused, Interrupted, Negated, Landed, Hits, ZeroDamageHits, Damage, Failed, Skipped;
            public readonly ConcurrentDictionary<string, long> Reasons = new();
            /// <summary>Why the bot AI passed over this spell before trying it (no cast was requested).</summary>
            public readonly ConcurrentDictionary<string, long> SkipReasons = new();

            public void Reason(string reason) => Reasons.AddOrUpdate(Trim(reason, 160), 1, (_, n) => n + 1);
            public void Skip(string reason) => SkipReasons.AddOrUpdate(reason, 1, (_, n) => n + 1);
        }

        internal sealed class ClassStats
        {
            public int ClassId;
            public string Name;
            public readonly ConcurrentDictionary<string, byte> Bots = new();
            public long Deaths, Kills;
            public readonly ConcurrentDictionary<string, long> Killers = new();
        }

        internal sealed class ProblemStats
        {
            public string Signature;
            public string Example;
            public long Count;
            public readonly ConcurrentDictionary<string, byte> Classes = new();
        }

        private sealed class BotState
        {
            public string LastMessage;
            public long LastMessageTick;
            public readonly Queue<string> Recent = new();
        }

        private static ClassStats ClassOf(GameBot bot)
        {
            int id = bot.CharacterClass?.ID ?? 0;
            // The three Maulers share one class name; the realm tells them apart in the report.
            string name = Spells.ModernMauler.IsMauler(id) ? $"Mauler ({GlobalConstants.RealmToName(bot.Realm)})" : bot.ClassName ?? bot.CharacterClass?.Name;
            ClassStats stats = Classes.GetOrAdd(id, _ => new ClassStats { ClassId = id, Name = name });
            stats.Bots.TryAdd(BotKey(bot), 0);
            return stats;
        }

        private static SkillStats SpellStats(GameBot bot, Spell spell, bool pet)
        {
            int classId = bot.CharacterClass?.ID ?? 0;
            string kind = pet ? "pet-spell" : "spell";
            return Skills.GetOrAdd((kind, classId, spell.ID), _ => new SkillStats
            {
                Kind = kind, ClassId = classId, Id = spell.ID, Name = spell.Name, Type = spell.SpellType.ToString(),
                Level = spell.Level, Harmful = spell.IsHarmful, ListedDamage = spell.Damage
            });
        }

        private static SkillStats StyleStats(GameBot bot, Style style)
        {
            int classId = bot.CharacterClass?.ID ?? 0;
            return Skills.GetOrAdd(("style", classId, style.ID), _ => new SkillStats
            {
                Kind = "style", ClassId = classId, Id = style.ID, Name = style.Name, Type = style.Spec, Level = style.Level, Harmful = true
            });
        }

        #endregion

        #region Hooks

        /// <summary>The bot a spell belongs to: the caster itself, or the bot owning the casting pet.</summary>
        public static GameBot Resolve(GameLiving caster, out bool pet)
        {
            pet = false;
            if (caster is GameBot bot)
                return bot;
            if (caster is GameNPC { Brain: IControlledBrain brain } && brain.Owner is GameBot owner)
            {
                pet = true;
                return owner;
            }
            return null;
        }

        /// <summary>The spells and styles a bot knows, so the report can list the ones it never uses.</summary>
        public static void RegisterKit(GameBot bot)
        {
            if (!Enabled || bot?.CharacterClass == null) return;
            try
            {
                ClassOf(bot);
                string id = BotKey(bot);
                foreach (Spell spell in bot.Spells?.ToArray() ?? [])
                    if (spell != null) SpellStats(bot, spell, false).KnownBy.TryAdd(id, 0);
                foreach (Style style in bot.Styles?.ToArray() ?? [])
                    if (style != null) StyleStats(bot, style).KnownBy.TryAdd(id, 0);
            }
            catch { }
        }

        /// <summary>
        /// The bot AI considered a known attack spell and passed over it, with the deciding check. Counted only
        /// (no event per call): these run on every think. Reasons should be short constant strings.
        /// </summary>
        public static void Skipped(GameLiving caster, Spell spell, string reason)
        {
            if (!Enabled || spell == null || caster is not GameBot bot) return;
            try
            {
                SkillStats stats = SpellStats(bot, spell, false);
                Interlocked.Increment(ref stats.Skipped);
                stats.Skip(reason);
            }
            catch { }
        }

        /// <summary>Every message the game sends a bot's spell (refusals, resists, "you can't do that").</summary>
        public static void SpellMessage(GameLiving caster, Spell spell, string message)
        {
            if (!Enabled || string.IsNullOrEmpty(message)) return;
            try
            {
                GameBot bot = Resolve(caster, out bool pet);
                if (bot == null) return;
                BotState state = States.GetOrCreateValue(caster);
                state.LastMessage = message;
                state.LastMessageTick = GameLoop.GameLoopTime;
                if (Verbose)
                    Write("spell-message", bot, new { pet, spell = SpellInfo(spell), message });
            }
            catch { }
        }

        public static void CastStarted(SpellHandler handler) => Cast(handler, "attempt");
        public static void CastRefused(SpellHandler handler) => Cast(handler, "refused");
        public static void CastInterrupted(SpellHandler handler) => Cast(handler, "interrupted");
        public static void CastFinished(SpellHandler handler) => Cast(handler, "cast");

        private static void Cast(SpellHandler handler, string outcome)
        {
            if (!Enabled || handler?.Spell == null) return;
            try
            {
                GameBot bot = Resolve(handler.Caster, out bool pet);
                if (bot == null) return;
                SkillStats stats = SpellStats(bot, handler.Spell, pet);
                switch (outcome)
                {
                    case "attempt":
                        Interlocked.Increment(ref stats.Attempts);
                        return;
                    case "cast":
                        Interlocked.Increment(ref stats.Casts);
                        Remember(bot, $"cast {handler.Spell.Name}");
                        if (Verbose)
                            Write("cast", bot, new { pet, spell = SpellInfo(handler.Spell), target = TargetInfo(handler.Target) });
                        return;
                    case "interrupted":
                        Interlocked.Increment(ref stats.Interrupted);
                        stats.Reason("interrupted" + (handler.Caster.IsMoving ? " while moving" : handler.Caster.IsBeingInterrupted ? " by an attack" : ""));
                        if (Verbose)
                            Write("interrupted", bot, new { pet, spell = SpellInfo(handler.Spell), moving = handler.Caster.IsMoving });
                        return;
                }

                // Refused by CheckBeginCast. Use the message the game sent for this cast, if any.
                Interlocked.Increment(ref stats.Refused);
                BotState state = States.GetOrCreateValue(handler.Caster);
                string reason = state.LastMessageTick == GameLoop.GameLoopTime && !string.IsNullOrEmpty(state.LastMessage)
                    ? state.LastMessage
                    : !string.IsNullOrEmpty(handler.RefuseReason) ? "refused: " + handler.RefuseReason
                    : handler.Caster.IsBeingInterrupted ? "interrupted before casting"
                    : $"refused without a message ({handler.GetType().Name})";
                stats.Reason(reason);
                Remember(bot, $"refused {handler.Spell.Name}: {reason}");
                if (Verbose || Once($"refused:{bot.ObjectID}:{handler.Spell.ID}:{reason}", 300_000))
                    Write("cast-refused", bot, new { pet, spell = SpellInfo(handler.Spell), target = TargetInfo(handler.Target), reason });
            }
            catch { }
        }

        public static void SpellNegated(SpellHandler handler, GameLiving target, string reason)
        {
            if (!Enabled || handler?.Spell == null) return;
            try
            {
                GameBot bot = Resolve(handler.Caster, out bool pet);
                if (bot == null) return;
                SkillStats stats = SpellStats(bot, handler.Spell, pet);
                Interlocked.Increment(ref stats.Negated);
                stats.Reason(reason);
                if (Verbose)
                    Write("spell-negated", bot, new { pet, spell = SpellInfo(handler.Spell), target = TargetInfo(target), reason });
            }
            catch { }
        }

        /// <summary>The spell passed its resist and immunity checks on a target (counts crowd control, debuffs and procs, not only damage).</summary>
        public static void SpellLanded(SpellHandler handler, GameLiving target)
        {
            if (!Enabled || handler?.Spell == null) return;
            try
            {
                GameBot bot = Resolve(handler.Caster, out bool pet);
                if (bot == null) return;
                Interlocked.Increment(ref SpellStats(bot, handler.Spell, pet).Landed);
            }
            catch { }
        }

        public static void SpellDamage(SpellHandler handler, AttackData ad)
        {
            if (!Enabled || handler?.Spell == null || ad == null) return;
            try
            {
                GameBot bot = Resolve(handler.Caster, out bool pet);
                if (bot == null) return;
                SkillStats stats = SpellStats(bot, handler.Spell, pet);
                int damage = Math.Max(0, ad.Damage) + Math.Max(0, ad.CriticalDamage);
                Interlocked.Increment(ref stats.Hits);
                Interlocked.Add(ref stats.Damage, damage);
                if (damage == 0)
                    Interlocked.Increment(ref stats.ZeroDamageHits);
                if (Verbose)
                    Write("spell-hit", bot, new { pet, spell = SpellInfo(handler.Spell), target = TargetInfo(ad.Target), damage, critical = ad.CriticalDamage });
            }
            catch { }
        }

        public static void StyleExecuted(GameLiving attacker, Style style, bool perfect, double styleDamage)
        {
            if (!Enabled || style == null) return;
            try
            {
                if (attacker is not GameBot bot) return;
                SkillStats stats = StyleStats(bot, style);
                if (perfect)
                {
                    Interlocked.Increment(ref stats.Casts);
                    Interlocked.Add(ref stats.Damage, (long)Math.Max(0, styleDamage));
                    Remember(bot, $"style {style.Name}");
                }
                else
                {
                    Interlocked.Increment(ref stats.Failed);
                    stats.Reason(StyleFailure(bot, style));
                    if (Once($"style-failed:{bot.ObjectID}:{style.ID}", 300_000))
                        Write("style-failed", bot, new { style = StyleInfo(style), reason = StyleFailure(bot, style),
                            weapon = bot.ActiveWeapon?.Name, target = TargetInfo(bot.TargetObject as GameLiving) });
                }
                if (Verbose)
                    Write("style", bot, new { style = StyleInfo(style), perfect, damage = (int)styleDamage, target = TargetInfo(bot.TargetObject as GameLiving) });
            }
            catch { }
        }

        public static void ServiceException(string service, GameObject owner, Exception exception)
        {
            if (!Enabled || exception == null) return;
            try
            {
                GameBot bot = owner as GameLiving is { } living ? Resolve(living, out _) : null;
                if (bot == null) return;
                string frame = exception.StackTrace?.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? string.Empty;
                string signature = $"{service}: {exception.GetType().Name}: {Trim(exception.Message, 120)} @ {Trim(frame, 200)}";
                ProblemStats stats = Exceptions.GetOrAdd(signature, _ => new ProblemStats { Signature = signature, Example = exception.ToString() });
                Interlocked.Increment(ref stats.Count);
                stats.Classes.TryAdd(bot.ClassName ?? "?", 0);
                if (Once("exception:" + signature, 60_000))
                    Write("exception", bot, new { service, pet = owner != bot, owner = owner.Name,
                        spell = SpellInfo(bot.castingComponent?.SpellHandler?.Spell), recent = Recent(bot), exception = exception.ToString() });
            }
            catch { }
        }

        public static void Died(GameBot bot, GameObject killer)
        {
            if (!Enabled || bot == null) return;
            try
            {
                ClassStats stats = ClassOf(bot);
                Interlocked.Increment(ref stats.Deaths);
                string killerName = killer is GameLiving living ? $"{living.Name} ({living.Level})" : killer?.Name ?? "unknown";
                stats.Killers.AddOrUpdate(killer?.Name ?? "unknown", 1, (_, n) => n + 1);
                GameLiving target = bot.TargetObject as GameLiving;
                Write("death", bot, new
                {
                    killer = killerName, killerIsPlayer = killer is GamePlayer, grouped = bot.Group?.MemberCount ?? 1,
                    // Was the killer the monster the bot chose to fight, or an add or a roamer that joined in?
                    killerWasTarget = killer != null && ReferenceEquals(killer, target), target = target?.Name,
                    killerCon = killer is GameLiving con ? bot.GetConLevel(con) : (double?)null,
                    attackers = bot.attackComponent?.AttackerTracker?.Count ?? 0, goal = bot.PersistentRecord?.CurrentGoal,
                    mana = bot.ManaPercent, endurance = bot.EndurancePercent, activity = bot.PersistentRecord?.Activity,
                    stance = bot.Stance.ToString(), pet = bot.ControlledBrain?.Body?.Name, recent = Recent(bot)
                });
            }
            catch { }
        }

        /// <summary>What a gamebot's PvP death paid one player (realm points, bounty points and experience), including nothing.</summary>
        public static void PvpReward(GameBot victim, GamePlayer player, int realmPoints, int bountyPoints, long experience, bool worth, double damagePercent)
        {
            if (!Enabled || victim == null || player == null) return;
            try
            {
                Write("pvp-reward", victim, new
                {
                    player = player.Name, playerLevel = player.Level, realmPoints, bountyPoints, experience,
                    worthPoints = worth, damagePercent = Math.Round(damagePercent * 100, 1),
                    reason = !worth ? "killed recently (realm point worth timer)" : realmPoints == 0 && bountyPoints == 0 && experience == 0 ? "zero share" : null
                });
            }
            catch { }
        }

        public static void Killed(GameBot bot)
        {
            if (!Enabled || bot == null) return;
            try { Interlocked.Increment(ref ClassOf(bot).Kills); } catch { }
        }

        #endregion

        #region Helpers

        private static string BotKey(GameBot bot) => bot.InternalID ?? bot.Name;

        private static string StyleFailure(GameBot bot, Style style)
        {
            if (bot.TargetObject is not GameLiving)
                return "no target";
            return style.OpeningRequirementType switch
            {
                Style.eOpening.Positional => $"needs {(Style.eOpeningPosition)style.OpeningRequirementValue} position",
                Style.eOpening.Defensive => $"reactive: needs the target's attack to end in {style.AttackResultRequirement}",
                _ when style.OpeningRequirementValue != 0 => $"needs opener style {style.OpeningRequirementValue} first",
                _ when style.AttackResultRequirement != Style.eAttackResultRequirement.Any =>
                    $"needs the bot's previous attack to end in {style.AttackResultRequirement}",
                _ => $"requirements not met (weapon {bot.ActiveWeapon?.Name ?? "none"})"
            };
        }

        private static void Remember(GameBot bot, string action)
        {
            BotState state = States.GetOrCreateValue(bot);
            lock (state.Recent)
            {
                state.Recent.Enqueue($"{DateTime.UtcNow:HH:mm:ss} {action}");
                while (state.Recent.Count > RecentActions)
                    state.Recent.Dequeue();
            }
        }

        private static string[] Recent(GameBot bot)
        {
            BotState state = States.GetOrCreateValue(bot);
            lock (state.Recent)
                return state.Recent.ToArray();
        }

        private static bool Once(string key, long intervalMs)
        {
            long now = GameLoop.GameLoopTime;
            bool fresh = true;
            Throttle.AddOrUpdate(key, now, (_, last) =>
            {
                fresh = now - last >= intervalMs;
                return fresh ? now : last;
            });
            if (Throttle.Count > 50_000)
                Throttle.Clear();
            return fresh;
        }

        private static object SpellInfo(Spell spell) => spell == null ? null :
            new { id = spell.ID, name = spell.Name, type = spell.SpellType.ToString(), level = spell.Level, target = spell.Target.ToString() };

        private static object StyleInfo(Style style) =>
            new { id = style.ID, name = style.Name, spec = style.Spec, level = style.Level,
                opening = style.OpeningRequirementType.ToString(), openingValue = style.OpeningRequirementValue };

        private static object TargetInfo(GameLiving target) => target == null ? null :
            new { name = target.Name, level = target.Level, player = target is GamePlayer, bot = target is GameBot, health = target.HealthPercent };

        private static string Trim(string text, int length) =>
            string.IsNullOrEmpty(text) ? string.Empty : text.Length <= length ? text : text[..length];

        private static string BotKind(GameBot bot) =>
            bot.IsAutonomousWorldBot ? "gamebot" : bot.IsTemporaryGroupHelper ? "spawn-helper" : bot.IsAccountCompanion ? "account-companion" : "companion";

        private static void Write(string ev, GameBot bot, object detail)
        {
            if (_eventFileFull) return;
            var record = new
            {
                t = DateTime.UtcNow, ev, bot = bot.Name, kind = BotKind(bot), cls = bot.ClassName,
                classId = bot.CharacterClass?.ID, level = bot.Level, realm = bot.Realm.ToString(),
                region = bot.CurrentRegionID, zone = bot.CurrentZone?.Description, hp = bot.HealthPercent, detail
            };
            Pending.Enqueue(JsonSerializer.Serialize(record, Json));
        }

        #endregion

        #region Files and report

        [GameServerStartedEvent]
        public static void Start(DOLEvent e, object sender, EventArgs args)
        {
            _flushTimer?.Dispose();
            _reportTimer?.Dispose();
            _flushTimer = new Timer(_ => Flush(), null, 2_000, 2_000);
            int minutes = Math.Max(1, BotTesterProperties.REPORT_MINUTES);
            _reportTimer = new Timer(_ => WriteReport(), null, TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(minutes));
        }

        [GameServerStoppedEvent]
        public static void Stop(DOLEvent e, object sender, EventArgs args)
        {
            _flushTimer?.Dispose();
            _reportTimer?.Dispose();
            _flushTimer = null;
            _reportTimer = null;
            Flush();
            WriteReport();
        }

        private static void Flush()
        {
            if (Pending.IsEmpty || Interlocked.Exchange(ref _writing, 1) == 1) return;
            try
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, $"events-{DateTime.UtcNow:yyyyMMdd}.jsonl");
                if (File.Exists(path) && new FileInfo(path).Length > EventFileLimitBytes)
                {
                    _eventFileFull = true;
                    Pending.Clear();
                    Log.Warn($"BOT_TESTER event log {path} passed {EventFileLimitBytes / 1024 / 1024} MB; events stop until tomorrow, reports continue.");
                    return;
                }
                var builder = new StringBuilder();
                while (Pending.TryDequeue(out string line))
                    builder.Append(line).Append('\n');
                File.AppendAllText(path, builder.ToString());
            }
            catch (Exception exception)
            {
                Log.Warn("BOT_TESTER could not write its event log: " + exception.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _writing, 0);
            }
        }

        /// <summary>The report: findings first, then per-class spell and style statistics.</summary>
        public static void WriteReport()
        {
            if (!Enabled) return;
            try
            {
                _eventFileFull = false;
                Directory.CreateDirectory(Folder);
                List<object> findings = Findings().ToList();
                var classes = Classes.Values.OrderBy(c => c.ClassId).Select(c => new
                {
                    classId = c.ClassId, name = c.Name, bots = c.Bots.Count, deaths = c.Deaths, kills = c.Kills,
                    topKillers = c.Killers.OrderByDescending(k => k.Value).Take(5).Select(k => $"{k.Key} x{k.Value}").ToArray(),
                    spells = SkillsFor(c.ClassId, "spell"), petSpells = SkillsFor(c.ClassId, "pet-spell"), styles = SkillsFor(c.ClassId, "style")
                }).ToArray();
                var summary = new
                {
                    schema = 1, generatedUtc = DateTime.UtcNow, sinceUtc = StartedUtc, findings,
                    exceptions = Exceptions.Values.OrderByDescending(x => x.Count).Select(x => new
                        { x.Signature, x.Count, classes = x.Classes.Keys.ToArray(), x.Example }).ToArray(),
                    classes
                };
                File.WriteAllText(Path.Combine(Folder, "summary-latest.json"), JsonSerializer.Serialize(summary, PrettyJson));
                File.WriteAllText(Path.Combine(Folder, "REPORT.txt"), TextReport(findings.Count));
            }
            catch (Exception exception)
            {
                Log.Warn("BOT_TESTER could not write its report: " + exception.Message);
            }
        }

        private static object[] SkillsFor(int classId, string kind) => Skills.Values
            .Where(s => s.ClassId == classId && s.Kind == kind)
            .OrderBy(s => s.Level).ThenBy(s => s.Id)
            .Select(s => new
            {
                s.Id, s.Name, s.Type, s.Level, knownBy = s.KnownBy.Count, s.Attempts, used = s.Casts, s.Refused, s.Interrupted, s.Skipped,
                skipReasons = s.SkipReasons.OrderByDescending(r => r.Value).Take(5).Select(r => $"{r.Key} x{r.Value}").ToArray(),
                negated = s.Negated, s.Landed, failed = s.Failed, s.Hits, zeroDamageHits = s.ZeroDamageHits, s.Damage,
                averageDamage = s.Hits > 0 ? s.Damage / s.Hits : s.Kind == "style" && s.Casts > 0 ? s.Damage / s.Casts : 0,
                reasons = s.Reasons.OrderByDescending(r => r.Value).Take(5).Select(r => $"{r.Key} x{r.Value}").ToArray()
            }).ToArray<object>();

        public sealed record Finding(string Severity, string Class, string Skill, string Problem, string Detail);

        public static IEnumerable<Finding> FindingsFor(IEnumerable<SkillStats> skills, Func<int, string> className)
        {
            foreach (SkillStats s in skills)
            {
                string cls = className(s.ClassId);
                string skill = $"{s.Kind} {s.Name} ({s.Id}, {s.Type}, level {s.Level})";
                long tries = s.Casts + s.Refused + s.Failed;
                string reasons = string.Join("; ", s.Reasons.OrderByDescending(r => r.Value).Take(3).Select(r => $"{r.Key} x{r.Value}"));
                if (s.Refused >= 5 && s.Casts == 0)
                    yield return new Finding("high", cls, skill, "always refused: never cast successfully", reasons);
                else if (s.Kind == "style" && s.Failed >= 10 && s.Casts == 0)
                    yield return new Finding("high", cls, skill, "style never executed: requirements always failed", reasons);
                else if (tries >= 20 && s.Refused + s.Failed > tries / 2)
                    yield return new Finding("medium", cls, skill, $"fails more than half the time ({s.Refused + s.Failed} of {tries})", reasons);
                if (s.Kind != "style" && s.ListedDamage > 0 && s.Hits >= 5 && s.Damage == 0)
                    yield return new Finding("high", cls, skill, $"damage spell hit {s.Hits} times for 0 damage", reasons);
                // A pulsing or area spell can be negated several times per cast, so compare against every landing attempt.
                long landed = Math.Max(s.Hits, s.Landed);
                long landings = Math.Max(s.Casts, landed + s.Negated);
                if (s.Kind != "style" && s.Negated >= 10 && s.Negated * 10 >= landings * 9)
                    yield return new Finding(landed == 0 && s.ListedDamage > 0 ? "high" : "medium", cls, skill,
                        $"resisted or negated {s.Negated} of {landings} attempts to land ({landed} landed, {s.Hits} hits)", reasons);
                if (s.Interrupted >= 20 && s.Interrupted > s.Casts)
                    yield return new Finding("low", cls, skill, $"interrupted more often than cast ({s.Interrupted} vs {s.Casts})", reasons);
                if (s.KnownBy.Count > 0 && tries == 0 && s.Attempts == 0)
                {
                    string skips = string.Join("; ", s.SkipReasons.OrderByDescending(r => r.Value).Take(3).Select(r => $"{r.Key} x{r.Value}"));
                    yield return new Finding("info", cls, skill, $"known by {s.KnownBy.Count} bot(s) but never tried",
                        skips.Length > 0 ? "skipped: " + skips : "never considered by the bot AI (no skip recorded)");
                }
            }
        }

        private static IEnumerable<object> Findings()
        {
            string Name(int id) => Classes.TryGetValue(id, out ClassStats c) ? c.Name : ((eCharacterClass)id).ToString();
            foreach (ProblemStats x in Exceptions.Values.OrderByDescending(x => x.Count))
                yield return new Finding("critical", string.Join(",", x.Classes.Keys), "-", $"exception x{x.Count}", x.Signature);
            foreach (Finding f in FindingsFor(Skills.Values, Name)
                         .OrderBy(f => f.Severity switch { "critical" => 0, "high" => 1, "medium" => 2, "low" => 3, _ => 4 })
                         .ThenBy(f => f.Class))
                yield return f;
        }

        private static string TextReport(int findingCount)
        {
            var text = new StringBuilder();
            text.AppendLine($"Bot tester report  {DateTime.Now:yyyy-MM-dd HH:mm}  (since server start {StartedUtc.ToLocalTime():HH:mm})");
            text.AppendLine($"Classes seen: {Classes.Count}   Bots seen: {Classes.Values.Sum(c => c.Bots.Count)}   " +
                $"Deaths: {Classes.Values.Sum(c => c.Deaths)}   Kills: {Classes.Values.Sum(c => c.Kills)}   Findings: {findingCount}");
            text.AppendLine("Details: summary-latest.json (all statistics) and events-*.jsonl (individual events).");
            text.AppendLine();
            foreach (var group in Findings().OfType<Finding>().GroupBy(f => f.Severity))
            {
                text.AppendLine($"== {group.Key.ToUpperInvariant()} ({group.Count()}) ==");
                foreach (Finding f in group.Take(group.Key == "info" ? 60 : 200))
                {
                    text.AppendLine($"[{f.Class}] {f.Skill}: {f.Problem}");
                    if (!string.IsNullOrEmpty(f.Detail))
                        text.AppendLine($"      {f.Detail}");
                }
                text.AppendLine();
            }
            text.AppendLine("== CLASSES ==");
            foreach (ClassStats c in Classes.Values.OrderBy(c => c.ClassId))
            {
                var used = Skills.Values.Where(s => s.ClassId == c.ClassId).ToArray();
                text.AppendLine($"{c.Name,-22} bots {c.Bots.Count,3}  deaths {c.Deaths,4}  kills {c.Kills,5}  " +
                    $"spells used {used.Count(s => s.Kind == "spell" && s.Casts > 0)}/{used.Count(s => s.Kind == "spell")}  " +
                    $"styles used {used.Count(s => s.Kind == "style" && s.Casts > 0)}/{used.Count(s => s.Kind == "style")}");
            }
            return text.ToString();
        }

        /// <summary>The most severe findings, one line each, for the in-game /bottester command.</summary>
        public static IReadOnlyList<string> TopFindings(int count) => Findings().OfType<Finding>()
            .Where(f => f.Severity != "info").Take(count)
            .Select(f => $"[{f.Severity}] {f.Class}: {f.Skill}: {f.Problem}").ToArray();

        public static string Totals() =>
            $"classes {Classes.Count}, bots {Classes.Values.Sum(c => c.Bots.Count)}, deaths {Classes.Values.Sum(c => c.Deaths)}, " +
            $"kills {Classes.Values.Sum(c => c.Kills)}, spells/styles tracked {Skills.Count}, exception kinds {Exceptions.Count}";

        /// <summary>Starts a fresh test run: clears every statistic (the event file is kept).</summary>
        public static void Reset() => ResetForTests();

        /// <summary>Clears all statistics (tests).</summary>
        internal static void ResetForTests()
        {
            Skills.Clear();
            Classes.Clear();
            Exceptions.Clear();
            Throttle.Clear();
            Pending.Clear();
        }

        #endregion
    }
}
