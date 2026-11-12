using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    public sealed record ArtifactSpawnDef(string Name, int Model, int Size, int Level, int Count, bool IsBoss);

    public sealed record ArtifactEscortDef(string Name, int Model, int Size, int Level, int X, int Y, int Z);

    /// <summary>
    /// A stage starts when the one before is dead, or at AtHealth percent on that stage's boss. A
    /// Gate stage's bosses cannot be hurt while its adds live. An Escort walks to its point while
    /// the stage's mobs attack it: arriving ends the stage and its death ends the encounter.
    /// </summary>
    public sealed record ArtifactStageDef(string Text, ArtifactSpawnDef[] Spawns, int AtHealth = 0, bool Gate = false, ArtifactEscortDef Escort = null);

    /// <summary>One artifact's encounter. The rows come from ArtifactEncounters.Data.cs.</summary>
    public sealed record ArtifactEncounterDef(string ArtifactId, string Name, string Zone, string Hint, int HealthFactor, string[] Mechanics, string RequiredItem, string AnchorText, ArtifactStageDef[] Stages, string Trigger = ArtifactEncounters.TriggerTouch, bool Underwater = false, string[] RequiredKills = null);

    public static partial class ArtifactEncounters
    {
        public const string PackagePrefix = "artifact:";
        public const int RepeatGlassReward = 25;
        public const int CreditRadius = 7000;
        public const int SoloHealthFactor = 3;
        public const double SoloSpellEffectiveness = 0.5;
        public const string TriggerTouch = "touch";
        public const string TriggerNight = "night";
        public const string TriggerDay = "day";
        public const string TriggerNear = "near";

        private const long KillWindowMs = 15 * 60 * 1000;

        private const int SpellIdBase = 826200;

        private static readonly Dictionary<(string Artifact, ushort Region, string Name), (long Time, HashSet<GamePlayer> Players)> _kills = new();
        private static readonly Lock _killsLock = new();
        private static readonly Lazy<Dictionary<string, ArtifactEncounterDef>> _byArtifact = new(() => All.ToDictionary(encounter => encounter.ArtifactId));

        /// <summary>Boss spells live in the world patch at SpellIdBase and up, in this order.</summary>
        private static readonly string[] MechanicSpells = ["aoe_nuke", "dd_nuke", "root", "mez", "stun", "snare", "disease", "lifedrain", "fire_breath"];

        public static ArtifactEncounterDef Find(string artifactId)
        {
            return artifactId != null && _byArtifact.Value.TryGetValue(artifactId, out ArtifactEncounterDef encounter) ? encounter : null;
        }

        public static string ArtifactIdOf(GameNPC npc)
        {
            string package = npc.PackageID;
            return package != null && package.StartsWith(PackagePrefix, StringComparison.Ordinal) ? package[PackagePrefix.Length..] : null;
        }

        public static List<Spell> SpellsFor(ArtifactEncounterDef encounter)
        {
            List<Spell> spells = [];

            foreach (string mechanic in encounter.Mechanics)
            {
                int index = Array.IndexOf(MechanicSpells, mechanic);
                Spell spell = index < 0 ? null : SkillBase.GetSpellByID(SpellIdBase + index);

                if (spell != null)
                    spells.Add(spell);
            }

            return spells;
        }

        /// <summary>
        /// Everyone who fought, with their group and battlegroup, if they are alive and near the boss
        /// when it dies (live gave the dead nothing). Bots earn nothing but do bring in the group they
        /// fought with.
        /// </summary>
        public static HashSet<GamePlayer> EligiblePlayers(GameNPC boss, GameObject killer)
        {
            HashSet<GamePlayer> players = [];

            void Add(GameObject source)
            {
                GameLiving living = source as GameLiving;

                if (living is GameNPC { Brain: IControlledBrain controlled } && controlled.GetLivingOwner() != null)
                    living = controlled.GetLivingOwner();

                if (living == null)
                    return;

                if (living is GamePlayer player)
                {
                    players.Add(player);

                    BattleGroup battleGroup = player.TempProperties.GetProperty<BattleGroup>(BattleGroup.BATTLEGROUP_PROPERTY);

                    if (battleGroup != null)
                    {
                        foreach (GamePlayer member in battleGroup.Members.Keys.OfType<GamePlayer>())
                            players.Add(member);
                    }
                }

                if (living.Group != null)
                {
                    foreach (GamePlayer member in living.Group.GetPlayersInTheGroup())
                        players.Add(member);
                }
            }

            foreach (GameLiving gainer in boss.XPGainers.Keys.ToList())
                Add(gainer);

            Add(killer);

            players.RemoveWhere(player => !CanEarnReward(player) || player.CurrentRegionID != boss.CurrentRegionID || !player.IsWithinRadius(boss, CreditRadius));
            return players;
        }

        /// <summary>Only a living player in the world earns encounter credit or glass.</summary>
        public static bool CanEarnReward(GamePlayer player) => player != null && player.ObjectState == GameObject.eObjectState.Active && player.IsAlive;

        /// <summary>A boss out in the world. Some encounters need several of them dead close together.</summary>
        public static void WorldBossDied(GameNPC boss, GameObject killer, string artifactId)
        {
            HashSet<GamePlayer> players = EligiblePlayers(boss, killer);
            string[] required = Find(artifactId)?.RequiredKills;

            if (required is { Length: > 0 })
            {
                lock (_killsLock)
                {
                    long now = GameLoop.GameLoopTime;
                    _kills[(artifactId, boss.CurrentRegionID, boss.Name)] = (now, players);

                    if (!AllKilled(required, artifactId, boss.CurrentRegionID, now, out HashSet<GamePlayer> everyone))
                        return;

                    players = everyone;

                    foreach (string name in required)
                        _kills.Remove((artifactId, boss.CurrentRegionID, name));
                }
            }

            AwardCredit(players, artifactId);
        }

        private static bool AllKilled(string[] required, string artifactId, ushort region, long now, out HashSet<GamePlayer> everyone)
        {
            everyone = [];

            foreach (string name in required)
            {
                if (!_kills.TryGetValue((artifactId, region, name), out var kill) || now - kill.Time > KillWindowMs)
                    return false;

                everyone.UnionWith(kill.Players);
            }

            return true;
        }

        public static void AwardCredit(IEnumerable<GamePlayer> players, string artifactId)
        {
            string name = ArtifactCatalog.NameOf(artifactId);

            foreach (GamePlayer player in players)
            {
                // Multi-kill and staged encounters gather players over several kills: someone who
                // died after an earlier kill is still on the list.
                if (!CanEarnReward(player))
                    continue;

                if (ArtifactProgress.Get(player, artifactId)?.EncounterDone == true)
                {
                    GiveGlass(player, RepeatGlassReward);
                    continue;
                }

                ArtifactProgress.Update(player, artifactId, row => row.EncounterDone = true);
                player.Out.SendMessage($"You have completed the encounter for {name}! Speak to an Artifact Scholar to receive it.", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            }
        }

        private static void GiveGlass(GamePlayer player, int count)
        {
            DbItemTemplate glass = GameServer.Database.FindObjectByKey<DbItemTemplate>(LootGeneratorAtlanteanGlass.GlassItemId);

            if (glass == null)
                return;

            GameInventoryItem item = GameInventoryItem.Create(glass);
            item.Count = count;

            if (player.Inventory.AddTemplate(item, count, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                player.Out.SendMessage($"You receive {count} Atlantean Glass.", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            else
                player.Out.SendMessage("You have no room for your Atlantean Glass.", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
        }
    }

    /// <summary>
    /// A mob in an artifact encounter. As a Mob row with PackageID artifact:id it is a boss that is
    /// simply out in the world. The anchor spawns the others for its stages.
    /// </summary>
    public class ArtifactBoss : GameNPC
    {
        public ArtifactEncounterAnchor Anchor { get; set; }
        public string ArtifactId { get; set; }
        public bool GivesCredit { get; set; } = true;
        public bool IsStageBoss { get; set; }

        /// <summary>While any of these live, this mob cannot be hurt.</summary>
        public List<ArtifactBoss> GuardedBy { get; set; }

        /// <summary>The boss of the fight or of a stage, as opposed to one of a crowd of adds.</summary>
        public bool CastsSpells { get; set; } = true;

        /// <summary>
        /// Whether killing this mob pays XP and loot. Only the last stage does: every earlier form or split paid a full
        /// kill (each Peridas form, each Gem stage), on top of the encounter's own reward.
        /// </summary>
        public bool Rewards { get; set; } = true;

        public override long ExperienceValue => Rewards ? base.ExperienceValue : 0;

        public override long GetExperienceValueForLevel(int level) => Rewards ? base.GetExperienceValueForLevel(level) : 0;

        public override bool CanDropLoot => Rewards && base.CanDropLoot;

        private const int AddHealthDivisor = 5;

        private bool _calledForHelp;
        private long _nextGuardMessage;

        public override int MaxHealth
        {
            get
            {
                int factor = ArtifactEncounters.Find(ArtifactId)?.HealthFactor ?? 1;
                return base.MaxHealth * Math.Max(1, CastsSpells ? factor : factor / AddHealthDivisor);
            }
        }

        public override bool AddToWorld()
        {
            ArtifactId ??= ArtifactEncounters.ArtifactIdOf(this);
            ArtifactEncounterDef encounter = ArtifactEncounters.Find(ArtifactId);

            if (encounter != null && CastsSpells)
                Spells = ArtifactEncounters.SpellsFor(encounter);

            // Boss spells are fixed numbers made for a group, so solo fights get a lighter version.
            if (encounter != null && encounter.HealthFactor <= ArtifactEncounters.SoloHealthFactor)
                Effectiveness = ArtifactEncounters.SoloSpellEffectiveness;

            if (encounter?.Underwater == true)
                Flags |= eFlags.SWIMMING;

            return base.AddToWorld();
        }

        private bool Guarded(GameObject source)
        {
            if (GuardedBy == null || !GuardedBy.Any(guard => guard.IsAlive && guard.ObjectState == eObjectState.Active))
                return false;

            if (source is GamePlayer player && GameLoop.GameLoopTime >= _nextGuardMessage)
            {
                _nextGuardMessage = GameLoop.GameLoopTime + 10000;
                player.Out.SendMessage($"{GetName(0, true)} cannot be hurt while its servants stand!", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            }

            return true;
        }

        public override void TakeDamage(GameObject source, eDamageType damageType, int damageAmount, int criticalAmount)
        {
            if (Guarded(source))
                return;

            base.TakeDamage(source, damageType, damageAmount, criticalAmount);

            if (!_calledForHelp && GivesCredit && IsAlive && HealthPercent <= 50
                && ArtifactEncounters.Find(ArtifactId)?.Mechanics.Contains("summon_adds") == true)
            {
                _calledForHelp = true;
                CallForHelp(source as GameLiving);
            }
        }

        private void CallForHelp(GameLiving target)
        {
            for (int i = 0; i < 2; i++)
            {
                ArtifactBoss add = new()
                {
                    ArtifactId = ArtifactId,
                    GivesCredit = false,
                    CastsSpells = false,
                    Name = $"{Name}'s servant",
                    Model = Model,
                    Size = (byte) Math.Max(20, Size * 3 / 4),
                    Level = (byte) Math.Max(1, Level - 5),
                    Realm = Realm,
                    CurrentRegion = CurrentRegion,
                    Heading = Heading,
                    X = X + Util.Random(-200, 200),
                    Y = Y + Util.Random(-200, 200),
                    Z = Z,
                    RespawnInterval = -1,
                    LoadedFromScript = true
                };

                add.SetStats();
                add.SetOwnBrain(new StandardMobBrain { AggroLevel = 100, AggroRange = 1500 });
                add.AddToWorld();

                if (target != null && add.Brain is StandardMobBrain brain)
                    brain.AddToAggroList(target, 1);
            }
        }

        public override void Die(GameObject killer)
        {
            _calledForHelp = false;

            if (GivesCredit && ArtifactId != null)
            {
                if (Anchor != null)
                    Anchor.BossDied(this, killer);
                else
                    ArtifactEncounters.WorldBossDied(this, killer, ArtifactId);
            }

            base.Die(killer);
        }
    }

    /// <summary>The one the players walk to safety in an escort stage.</summary>
    public class ArtifactEscort : GameNPC
    {
        public override int MaxHealth => base.MaxHealth * 8;
    }

    /// <summary>
    /// Where an encounter starts: a statue, altar or the like that a player touches, or an unseen
    /// marker that wakes when someone comes near at the right time of day.
    /// </summary>
    public class ArtifactEncounterAnchor : GameNPC
    {
        private const int TickMs = 2000;
        private const int CooldownMs = 5 * 60 * 1000;
        private const int RareSpawnCooldownMs = 30 * 60 * 1000;
        private const int AbandonMs = 90 * 1000;
        private const int LeashRadius = 4000;
        private const int WatchMs = 5000;
        private const int WatchRadius = 1500;

        private readonly List<ArtifactBoss> _alive = [];
        private readonly HashSet<GamePlayer> _credited = [];
        private ArtifactEncounterDef _encounter;
        private GamePlayer _starter;
        private ECSGameTimer _timer;
        private ECSGameTimer _watch;
        private ArtifactEscort _escort;
        private ArtifactEscortDef _escortDef;
        private int _stage = -1;
        private int _originX;
        private int _originY;
        private int _originZ;
        private long _lastPlayerNear;
        private long _readyAt;

        public bool Running => _timer != null;

        public override bool IsAttackable => false;

        public override void TakeDamage(GameObject source, eDamageType damageType, int damageAmount, int criticalAmount) { }

        public override bool AddToWorld()
        {
            if (!base.AddToWorld())
                return false;

            ArtifactEncounterDef encounter = ArtifactEncounters.Find(ArtifactEncounters.ArtifactIdOf(this));

            if (encounter != null && encounter.Trigger != ArtifactEncounters.TriggerTouch)
                _watch = new ECSGameTimer(this, Watch, WatchMs);

            return true;
        }

        private int Watch(ECSGameTimer timer)
        {
            ArtifactEncounterDef encounter = ArtifactEncounters.Find(ArtifactEncounters.ArtifactIdOf(this));
            bool night = CurrentRegion.IsNightTime;
            bool rightTime = encounter.Trigger switch
            {
                ArtifactEncounters.TriggerNight => night,
                ArtifactEncounters.TriggerDay => !night,
                _ => true
            };

            if (rightTime && !Running && GameLoop.GameLoopTime >= _readyAt)
            {
                GamePlayer player = GetPlayersInRadius((ushort) WatchRadius).Cast<GamePlayer>().FirstOrDefault(nearby => nearby.IsAlive);

                if (player != null)
                    Start(player, encounter);
            }

            return WatchMs;
        }

        public override bool Interact(GamePlayer player)
        {
            if (!base.Interact(player))
                return false;

            ArtifactEncounterDef encounter = ArtifactEncounters.Find(ArtifactEncounters.ArtifactIdOf(this));

            if (encounter == null || encounter.Trigger != ArtifactEncounters.TriggerTouch)
                return false;

            if (!string.IsNullOrEmpty(encounter.AnchorText))
                player.Out.SendMessage(encounter.AnchorText, eChatType.CT_System, eChatLoc.CL_SystemWindow);

            if (Running)
            {
                player.Out.SendMessage("It is already stirring.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return true;
            }

            if (GameLoop.GameLoopTime < _readyAt)
            {
                player.Out.SendMessage("Nothing happens. It needs time to gather its strength.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return true;
            }

            if (!string.IsNullOrEmpty(encounter.RequiredItem))
            {
                DbInventoryItem key = player.Inventory.GetFirstItemByID(encounter.RequiredItem, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);

                if (key == null)
                {
                    player.Out.SendMessage("Something is missing. You do not have what it needs.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    return true;
                }

                player.Inventory.RemoveCountFromStack(key, 1);
            }

            Start(player, encounter);
            return true;
        }

        private void Start(GamePlayer player, ArtifactEncounterDef encounter)
        {
            _encounter = encounter;
            _starter = player;
            _stage = -1;
            _originX = X;
            _originY = Y;
            _originZ = Z;
            _lastPlayerNear = GameLoop.GameLoopTime;
            _timer = new ECSGameTimer(this, Tick, TickMs);
            NextStage();
        }

        private void NextStage()
        {
            _stage++;
            _alive.RemoveAll(mob => !mob.IsAlive || mob.ObjectState != eObjectState.Active);

            ArtifactStageDef stage = _encounter.Stages[_stage];

            if (!string.IsNullOrEmpty(stage.Text))
            {
                foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                    player.Out.SendMessage(stage.Text, eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            }

            bool lastStage = _stage == _encounter.Stages.Length - 1;
            List<ArtifactBoss> spawned = [];

            foreach (ArtifactSpawnDef spawn in stage.Spawns)
            {
                for (int i = 0; i < spawn.Count; i++)
                {
                    ArtifactBoss mob = SpawnMob(spawn, lastStage && spawn.IsBoss);
                    mob.Rewards = lastStage;
                    spawned.Add(mob);
                }
            }

            if (stage.Gate)
            {
                List<ArtifactBoss> adds = spawned.Where(mob => !mob.IsStageBoss).ToList();

                // Gates are things to break: columns, chests, links. They hold their ground.
                foreach (ArtifactBoss add in adds)
                    add.MaxSpeedBase = 0;

                foreach (ArtifactBoss boss in spawned.Where(mob => mob.IsStageBoss))
                    boss.GuardedBy = adds;
            }

            if (stage.Escort != null)
                StartEscort(stage.Escort, spawned);
        }

        private void StartEscort(ArtifactEscortDef escort, List<ArtifactBoss> attackers)
        {
            _escortDef = escort;
            _escort = new ArtifactEscort
            {
                Name = escort.Name,
                Model = (ushort) escort.Model,
                Size = (byte) escort.Size,
                Level = (byte) escort.Level,
                Realm = _starter?.Realm ?? eRealm.None,
                CurrentRegion = CurrentRegion,
                Heading = Heading,
                X = X,
                Y = Y,
                Z = Z,
                RespawnInterval = -1,
                LoadedFromScript = true
            };

            if (_encounter.Underwater)
                _escort.Flags |= eFlags.SWIMMING;

            // A mob brain would keep walking it back to where it spawned.
            _escort.SetStats();
            _escort.SetOwnBrain(new BlankBrain());
            _escort.AddToWorld();
            _escort.Health = _escort.MaxHealth;

            foreach (ArtifactBoss mob in attackers)
            {
                if (mob.Brain is StandardMobBrain brain)
                    brain.AddToAggroList(_escort, 100);
            }

            _escort.PathTo(new Point3D(escort.X, escort.Y, escort.Z), 150);
        }

        /// <summary>True while an escort walks. Ends the stage on arrival and the encounter on its death.</summary>
        private bool TickEscort()
        {
            if (_escort == null)
                return false;

            if (!_escort.IsAlive)
            {
                foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                    player.Out.SendMessage($"{_escort.Name} has fallen!", eChatType.CT_Important, eChatLoc.CL_SystemWindow);

                _escort = null;
                Finish();
                return true;
            }

            Point3D target = new(_escortDef.X, _escortDef.Y, _escortDef.Z);

            if (_escort.IsWithinRadius(target, 200))
            {
                _escort.Delete();
                _escort = null;
                _originX = _escortDef.X;
                _originY = _escortDef.Y;
                _originZ = _escortDef.Z;

                foreach (ArtifactBoss mob in _alive)
                {
                    if (mob.ObjectState == eObjectState.Active)
                        mob.Delete();
                }

                _alive.Clear();
                return false;
            }

            if (!_escort.IsMoving)
                _escort.PathTo(target, 150);

            return true;
        }

        public void BossDied(ArtifactBoss boss, GameObject killer)
        {
            _credited.UnionWith(ArtifactEncounters.EligiblePlayers(boss, killer));

            if (_alive.Any(mob => mob != boss && mob.GivesCredit && mob.IsAlive && mob.ObjectState == eObjectState.Active))
                return;

            ArtifactEncounters.AwardCredit(_credited.ToList(), _encounter.ArtifactId);
            _credited.Clear();
        }

        public static bool IsCaster(ArtifactSpawnDef spawn) => spawn.IsBoss;

        private ArtifactBoss SpawnMob(ArtifactSpawnDef spawn, bool givesCredit)
        {
            double angle = Util.RandomDouble() * Math.PI * 2;
            int radius = spawn.IsBoss ? 150 : 300;

            ArtifactBoss mob = new()
            {
                Anchor = this,
                ArtifactId = _encounter.ArtifactId,
                GivesCredit = givesCredit,
                IsStageBoss = spawn.IsBoss,
                // Only bosses cast and take the encounter's health factor. "|| Count == 1" made every lone form of a
                // solo encounter a 3x caster (Orion's Belt form 1 killed a plate Armsman; Night Terror stunned at stage 1).
                CastsSpells = IsCaster(spawn),
                Name = spawn.Name,
                Model = (ushort) spawn.Model,
                Size = (byte) spawn.Size,
                Level = (byte) spawn.Level,
                Realm = eRealm.None,
                CurrentRegion = CurrentRegion,
                Heading = Heading,
                X = _originX + (int) (Math.Cos(angle) * radius),
                Y = _originY + (int) (Math.Sin(angle) * radius),
                Z = _originZ,
                RespawnInterval = -1,
                LoadedFromScript = true
            };

            mob.SetStats();
            mob.SetOwnBrain(new StandardMobBrain { AggroLevel = 100, AggroRange = 1500 });
            mob.AddToWorld();

            if (_starter != null && _starter.IsAlive && mob.Brain is StandardMobBrain brain)
                brain.AddToAggroList(_starter, 1);

            _alive.Add(mob);
            return mob;
        }

        private int Tick(ECSGameTimer timer)
        {
            bool playerNear = PlayerNear(this) || _alive.Any(PlayerNear) || (_escort != null && PlayerNear(_escort));

            if (playerNear)
                _lastPlayerNear = GameLoop.GameLoopTime;
            else if (GameLoop.GameLoopTime - _lastPlayerNear > AbandonMs)
            {
                Finish();
                return 0;
            }

            if (TickEscort())
                return Running ? TickMs : 0;

            int nextHealth = _stage < _encounter.Stages.Length - 1 ? _encounter.Stages[_stage + 1].AtHealth : 0;

            if (nextHealth > 0 && _alive.Any(mob => mob.IsStageBoss && mob.IsAlive && mob.HealthPercent <= nextHealth))
            {
                NextStage();
                return TickMs;
            }

            if (_alive.Any(mob => mob.IsAlive && mob.ObjectState == eObjectState.Active))
                return TickMs;

            if (_stage >= _encounter.Stages.Length - 1)
            {
                Finish();
                return 0;
            }

            NextStage();
            return TickMs;
        }

        private static bool PlayerNear(GameObject place)
        {
            return place.ObjectState == eObjectState.Active && place.GetPlayersInRadius((ushort) LeashRadius).Cast<GamePlayer>().Any(player => player.IsAlive);
        }

        private void Finish()
        {
            if (_escort != null)
            {
                _escort.Delete();
                _escort = null;
            }

            foreach (ArtifactBoss mob in _alive)
            {
                if (mob.ObjectState == eObjectState.Active)
                    mob.Delete();
            }

            _alive.Clear();
            _credited.Clear();
            _timer = null;
            _starter = null;
            _readyAt = GameLoop.GameLoopTime + (_watch != null ? RareSpawnCooldownMs : CooldownMs);
        }

        public override bool RemoveFromWorld()
        {
            _watch?.Stop();

            if (Running)
            {
                _timer.Stop();
                Finish();
            }

            return base.RemoveFromWorld();
        }
    }
}
