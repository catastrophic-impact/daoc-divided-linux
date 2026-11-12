using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Events;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS.Trials
{
    /// <summary>
    /// The Trials of Atlantis as a second road to Master Levels, beside the experience bar. Trials are
    /// code, not world rows: their NPCs exist only while the server runs and leave no database changes.
    /// ml_trials_enabled turns them off; ml_xp_rate 0 makes them the only road.
    /// </summary>
    public static class TrialManager
    {
        public static readonly Logger Log = LoggerManager.Create(typeof(TrialManager));

        private static readonly Dictionary<TrialZone, ushort[]> Regions = new()
        {
            [TrialZone.Oceanus] = new ushort[] { 30, 73, 130 },
            [TrialZone.CetusPit] = new ushort[] { 35, 78, 135 },
            [TrialZone.Sobekite] = new ushort[] { 36, 79, 136 },
            [TrialZone.Twilight] = new ushort[] { 37, 80, 137 },
            [TrialZone.Maati] = new ushort[] { 40, 83, 140 },
            [TrialZone.Pyramid] = new ushort[] { 45, 88, 145 },
            [TrialZone.Volcanus] = new ushort[] { 46, 89, 146 },
            [TrialZone.Aerus] = new ushort[] { 47, 90, 147 },
        };

        private static readonly List<TrialStarter> Starters = new();

        public static IReadOnlyList<TrialStarter> AllStarters => Starters;

        public static IReadOnlyList<ushort> RegionsOf(TrialZone zone) => Regions[zone];

        public static string ScopeName(TrialScope scope) => scope switch
        {
            TrialScope.Solo => "solo",
            TrialScope.Group => "group",
            _ => "battlegroup",
        };

        [GameServerStartedEvent]
        public static void OnServerStart(DOLEvent e, object sender, EventArgs arguments)
        {
            if (Properties.ML_TRIALS_ENABLED)
                Populate();
        }

        public const int DefaultClearRadius = 450;

        /// <summary>
        /// Whether a world NPC near a trial anchor is a hostile monster to remove: realm 0, aggressive, on its own brain,
        /// not at peace, and not a trial, encounter, merchant or keep NPC. No trial counts world kills.
        /// </summary>
        public static bool ShouldClear(GameNPC npc)
        {
            if (npc is not { IsAlive: true, Realm: eRealm.None } || (npc.Flags & GameNPC.eFlags.PEACE) != 0 || !IsWorldMonster(npc))
                return false;
            return npc.Brain is DOL.AI.Brain.StandardMobBrain { AggroLevel: > 0 } and not DOL.AI.Brain.IControlledBrain;
        }

        // World monsters are plain GameNPCs or the few monster classes the world rows use (exact types, so trial mobs,
        // encounter bosses and anchors, merchants, guards and scripted NPCs all stay).
        public static bool IsWorldMonster(GameNPC npc) => IsWorldMonsterType(npc.GetType());

        public static bool IsWorldMonsterType(Type type) =>
            type == typeof(GameNPC) || type == typeof(GameEpicNPC) || type == typeof(GameEpicBoss) || type.Name == "NightSpawn";

        /// <summary>A bad trial must never stop the server from starting.</summary>
        public static void Populate()
        {
            foreach (string error in TrialCatalog.Validate())
                Log.Warn($"Master Level trial: {error}");

            foreach (TrialDefinition definition in TrialCatalog.All)
            {
                foreach (ushort regionId in Regions[definition.Zone])
                {
                    try
                    {
                        Region region = WorldMgr.GetRegion(regionId);
                        if (region == null)
                            continue;
                        foreach (string name in definition.Replaces)
                        {
                            foreach (GameNPC imported in WorldMgr.GetNPCsByNameFromRegion(name, regionId, eRealm.None))
                            {
                                if (definition.ReplaceRadius <= 0 || Math.Abs(imported.X - definition.Anchor.X) <= definition.ReplaceRadius && Math.Abs(imported.Y - definition.Anchor.Y) <= definition.ReplaceRadius)
                                    imported.Delete();
                            }
                        }
                        if (definition.ClearRadius > 0)
                        {
                            foreach (GameNPC nearby in WorldMgr.GetNPCsCloseToSpot(regionId, definition.Anchor.X, definition.Anchor.Y, definition.Anchor.Z, (ushort)definition.ClearRadius))
                            {
                                if (ShouldClear(nearby))
                                    nearby.Delete();
                            }
                        }
                        TrialStarter starter = new(definition)
                        {
                            CurrentRegion = region,
                            X = definition.Anchor.X,
                            Y = definition.Anchor.Y,
                            Z = definition.Anchor.Z,
                            Heading = definition.Anchor.Heading,
                        };
                        if (starter.AddToWorld())
                            Starters.Add(starter);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"Trial {definition.Key} could not be placed in region {regionId}", e);
                    }
                }
            }
            Log.Info($"Master Level trials: {TrialCatalog.All.Count} defined, {Starters.Count} starters placed.");
        }

        /// <summary>The copy in the player's current region, else the one for their realm.</summary>
        public static TrialStarter StarterFor(GamePlayer player, TrialDefinition definition)
        {
            int realm = player.Realm switch
            {
                eRealm.Midgard => 0,
                eRealm.Albion => 1,
                _ => 2,
            };
            List<TrialStarter> matches = Starters.Where(starter => starter.Definition == definition).ToList();
            return matches.FirstOrDefault(starter => starter.CurrentRegionID == player.CurrentRegionID) ??
                matches.FirstOrDefault(starter => Regions[definition.Zone][realm] == starter.CurrentRegionID);
        }

        /// <summary>Steps of the trial's Master Level this player still needs before it. Empty once past that level.</summary>
        public static int[] MissingSteps(GamePlayer player, TrialDefinition definition)
        {
            if (player.MLLevel >= definition.ML)
                return Array.Empty<int>();
            return Required(definition).Where(step => !player.HasFinishedMLStep(definition.ML, step)).ToArray();
        }

        public static IEnumerable<int> Required(TrialDefinition definition) =>
            definition.RequiresSteps ?? (definition.Step == 10 ? Enumerable.Range(1, 9) : Enumerable.Empty<int>());

        public static bool TryStart(TrialStarter starter, GamePlayer player, out string reason)
        {
            lock (starter)
            {
                reason = Refusal(starter, player);
                if (reason != null)
                    return false;
                starter.Run = new TrialRun(starter, player);
            }
            starter.Run.Start();
            return true;
        }

        private static string Refusal(TrialStarter starter, GamePlayer player)
        {
            if (starter.Run != null)
                return "This trial is already under way.";
            if (GameLoop.GameLoopTime < starter.AvailableAt)
                return "This trial is still settling. Come back in a moment.";
            if (player.Level < GamePlayer.MAX_LEVEL)
                return $"The trials are for those of level {GamePlayer.MAX_LEVEL}.";
            if (MissingSteps(player, starter.Definition) is { Length: > 0 } missing)
                return $"You must first finish step{(missing.Length > 1 ? "s" : "")} {string.Join(", ", missing)} of Master Level {starter.Definition.ML}.";
            return starter.Definition.Refuse?.Invoke(player);
        }

        public static void Award(TrialRun run)
        {
            TrialDefinition definition = run.Definition;
            IEnumerable<GamePlayer> players = definition.Scope == TrialScope.Solo ? new[] { run.Leader }
                : run.Players.Concat(run.Leader.Group?.GetPlayersInTheGroup() ?? Enumerable.Empty<GamePlayer>()).Distinct();

            foreach (GamePlayer player in players.Where(player => player.CurrentRegion == run.Starter.CurrentRegion && player.IsWithinRadius(run.Starter, definition.LeashRange)))
                MasterLevelRules.CompleteTrialStep(player, definition.ML, definition.Step);
        }

        public static void Finished(TrialRun run)
        {
            run.Starter.AvailableAt = GameLoop.GameLoopTime + run.Definition.ResetSeconds * 1000L;
            run.Starter.Run = null;
        }

        /// <summary>Text for /ml trials and the Arbiter: what is left of the given Master Level.</summary>
        public static string Describe(GamePlayer player, int ml)
        {
            List<TrialDefinition> trials = TrialCatalog.All.Where(trial => trial.ML == ml).OrderBy(trial => trial.Step).ToList();
            if (trials.Count == 0)
                return $"No trials are known for Master Level {ml} yet.";
            return $"Master Level {ml} trials:\n" + string.Join("\n", trials.Select(trial =>
                $"{(player.HasFinishedMLStep(ml, trial.Step) ? "[done]" : "[open]")} {trial.Key} {trial.Name} - {trial.Zone}, {trial.StarterName} ({ScopeName(trial.Scope)})"));
        }
    }
}
