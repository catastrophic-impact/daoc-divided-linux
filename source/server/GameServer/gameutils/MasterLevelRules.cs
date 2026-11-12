using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.GS.Spells;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Offline Master Level progression. Experience earned at level 50 fills the ML bar and each full bar
    /// raises the Master Level (1-10); finishing all of a level's Trials of Atlantis raises it too (see
    /// Trials/TrialManager). Every class may follow any of the eight paths (class 0 careers); /ml chooses
    /// or changes the path.
    /// </summary>
    public static class MasterLevelRules
    {
        /// <summary>The level-50 experience one Master Level costs at ml_xp_rate 1: one level 49 to 50.</summary>
        public static long LevelSpan => GamePlayer.GetExperienceAmountForLevel(GamePlayer.MAX_LEVEL - 1) - GamePlayer.GetExperienceAmountForLevel(GamePlayer.MAX_LEVEL - 2);

        public static long ToMasterExperience(long experience, long perMasterLevel, long levelSpan, double rate)
        {
            if (experience <= 0 || levelSpan <= 0 || rate <= 0)
                return 0;
            return (long)Math.Min(long.MaxValue / 2, experience * (double)perMasterLevel / levelSpan * rate);
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, string[]> _pathNames = new();

        /// <summary>The player's chosen path, or null. Cached per class because calculators ask often.</summary>
        public static string CurrentPath(GamePlayer player)
        {
            string[] names = _pathNames.GetOrAdd(player.CharacterClass.ID, _ => Paths(player).Select(p => p.KeyName).ToArray());
            return player.MLLine < names.Length ? names[player.MLLine] : null;
        }

        /// <summary>True when a player follows this path and has reached the given Master Level (passives).</summary>
        public static bool HasPassive(GameLiving living, string path, int masterLevel) =>
            living is GamePlayer player && player.MLLevel >= masterLevel && CurrentPath(player) == path;

        /// <summary>The paths in career order; a character's MLLine is an index into this list.</summary>
        public static List<Specialization> Paths(GamePlayer player) =>
            SkillBase.GetSpecializationCareer(player.CharacterClass.ID).Keys.Where(spec => spec is IMasterLevelsSpecialization).ToList();

        /// <summary>Activate both endgame bars at 50, including existing characters on login.
        /// This does not reset progress or require choosing a Master Level path.</summary>
        public static void EnsureTraining(GamePlayer player)
        {
            if (player.Level < GamePlayer.MAX_LEVEL || ChampionRules.PathFor(player.Realm) == null)
                return;
            if (!player.MLGranted)
            {
                player.MLGranted = true;
                player.Out.SendMessage("Master Level training is active. Experience now advances your Master Levels; /ml lets you change your path.", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            }
            // Champion training is no longer switched on here: the player starts it at the King in the throne room
            // (ChampionRules.BecomeChampion). Characters who already have it keep it.
        }

        public static void GainExperience(GamePlayer player, long experience)
        {
            if (player.Level < GamePlayer.MAX_LEVEL || player.MLLevel >= GamePlayer.ML_MAX_LEVEL)
                return;
            long gained = ToMasterExperience(experience, player.GetMLExperienceForLevel(player.MLLevel + 1), LevelSpan, Properties.ML_XP_RATE);
            if (gained <= 0)
                return;

            EnsureTraining(player);

            player.MLExperience += gained;
            int before = player.MLLevel;
            while (player.MLLevel < GamePlayer.ML_MAX_LEVEL && player.MLExperience >= player.GetMLExperienceForLevel(player.MLLevel + 1))
            {
                player.MLExperience -= player.GetMLExperienceForLevel(player.MLLevel + 1);
                player.MLLevel++;
                player.Out.SendMessage($"You have reached Master Level {player.MLLevel}!", eChatType.CT_ScreenCenter, eChatLoc.CL_SystemWindow);
            }
            if (player.MLLevel >= GamePlayer.ML_MAX_LEVEL)
                player.MLExperience = 0;

            if (player.MLLevel != before)
                Refresh(player);
        }

        /// <summary>Records a finished trial for the player's next Master Level and raises it when all are done.</summary>
        public static bool CompleteTrialStep(GamePlayer player, int masterLevel, int step)
        {
            if (player.MLLevel + 1 != masterLevel || player.HasFinishedMLStep(masterLevel, step))
                return false;

            EnsureTraining(player);
            player.SetFinishedMLStep(masterLevel, step);
            player.Out.SendMessage($"You have finished trial {masterLevel}.{step}!", eChatType.CT_Important, eChatLoc.CL_SystemWindow);

            if (player.GetCountMLStepsCompleted((byte)masterLevel) >= player.GetStepCountForML((byte)masterLevel))
            {
                player.MLLevel = masterLevel;
                player.MLExperience = 0;
                player.Out.SendMessage($"You have reached Master Level {player.MLLevel}!", eChatType.CT_ScreenCenter, eChatLoc.CL_SystemWindow);
                Refresh(player);
            }
            else
                player.Out.SendMasterLevelWindow((byte)masterLevel);
            return true;
        }

        public static bool ChoosePath(GamePlayer player, string name, out string message)
        {
            List<Specialization> paths = Paths(player);
            int index = paths.FindIndex(path => path.KeyName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                                                path.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                message = $"Unknown path. Choose one of: {string.Join(", ", paths.Select(path => path.KeyName))}.";
                return false;
            }
            if (player.InCombat)
            {
                message = "You can't change your Master Level path in combat.";
                return false;
            }
            player.MLLine = (byte)index;
            // Leaving a path drops its spec on refresh, but its spell line would otherwise stay registered.
            foreach (Specialization other in paths.Where((_, i) => i != index))
                player.RemoveSpellLine(other.KeyName);
            Refresh(player);
            message = $"Your Master Level path is now {paths[index].KeyName}.";
            return true;
        }

        public static string Describe(GamePlayer player)
        {
            List<Specialization> paths = Paths(player);
            string path = player.MLLine < paths.Count ? paths[player.MLLine].KeyName : "none";
            string progress = player.MLLevel >= GamePlayer.ML_MAX_LEVEL ? "complete"
                : $"{player.MLExperience * 100 / Math.Max(1, player.GetMLExperienceForLevel(player.MLLevel + 1))}% toward ML{player.MLLevel + 1}";
            return player.Level < GamePlayer.MAX_LEVEL
                ? $"Master Levels start at level {GamePlayer.MAX_LEVEL}. Paths: {string.Join(", ", paths.Select(p => p.KeyName))}."
                : $"Master Level {player.MLLevel} {path} ({progress}). Paths: {string.Join(", ", paths.Select(p => p.KeyName))}. Use /ml path <name> to change.";
        }

        public static void Refresh(GamePlayer player)
        {
            player.RefreshSpecDependantSkills(true);
            player.Out.SendUpdatePlayerSkills(true);
            SyncPassives(player);
            player.Out.SendUpdatePlayer();
            player.Out.SendMasterLevelWindow((byte)player.MLLevel);
            player.SaveIntoDatabase();
        }

        /// <summary>
        /// Shows each unlocked passive of the current path as a permanent effect icon and removes those of
        /// other paths. The bonuses themselves come from <see cref="HasPassive"/> checks in the calculators.
        /// </summary>
        public static void SyncPassives(GamePlayer player)
        {
            if (player?.ObjectState != GameObject.eObjectState.Active)
                return;
            List<Specialization> paths = Paths(player);
            string path = player.MLLevel > 0 && player.MLLine < paths.Count ? paths[player.MLLine].KeyName : null;
            string championPath = player.ChampionLevel > 0 ? ChampionRules.PathFor(player.Realm) : null;
            var shown = new HashSet<int>();
            foreach (ECSGameSpellEffect effect in player.effectListComponent.GetSpellEffects(eEffect.MasterLevel))
            {
                if (effect.IsEnding || effect.SpellHandler is not MasterLevelAbilityHandler { Ability: { Passive: true } ability })
                    continue;
                bool keep = ability.Path == championPath ? ability.Level <= player.ChampionLevel
                    : ability.Path == path && ability.Level <= player.MLLevel;
                if (!keep)
                    effect.End();
                else
                    shown.Add(effect.SpellHandler.Spell.ID);
            }
            if (path != null)
            {
                int pathIndex = Array.IndexOf(MasterLevelAbilities.Paths, path);
                for (int level = 1; level <= player.MLLevel && pathIndex >= 0; level++)
                {
                    int id = MasterLevelAbilities.FirstId + pathIndex * 10 + level - 1;
                    if (MasterLevelAbilities.For(id) is not { Passive: true } || shown.Contains(id) || SkillBase.GetSpellByID(id) is not Spell spell)
                        continue;
                    var handler = new MasterLevelAbilityHandler(player, spell, player.GetSpellLine(path) ?? SkillBase.GetSpellLine(path));
                    ECSGameEffectFactory.Create(new(player, 0, 1, handler), static (in i) => new MasterLevelEffect(i, permanent: true));
                }
            }
            if (championPath != null)
            {
                for (int level = 1; level <= player.ChampionLevel; level++)
                {
                    int id = ChampionAbilities.IdFor(player.Realm, level);
                    if (MasterLevelAbilities.For(id) is not { Passive: true } || shown.Contains(id) || SkillBase.GetSpellByID(id) is not Spell spell)
                        continue;
                    var handler = new MasterLevelAbilityHandler(player, spell, player.GetSpellLine(championPath) ?? SkillBase.GetSpellLine(championPath));
                    ECSGameEffectFactory.Create(new(player, 0, 1, handler), static (in i) => new MasterLevelEffect(i, permanent: true));
                }
            }
            MLMod.Refresh(player);
        }

        [GameServerStartedEvent]
        public static void OnServerStart(DOLEvent e, object sender, EventArgs arguments) =>
            GameEventMgr.AddHandler(GamePlayerEvent.GameEntered, new DOLEventHandler(PlayerEntered));

        [GameServerStoppedEvent]
        public static void OnServerStop(DOLEvent e, object sender, EventArgs arguments) =>
            GameEventMgr.RemoveHandler(GamePlayerEvent.GameEntered, new DOLEventHandler(PlayerEntered));

        private static void PlayerEntered(DOLEvent e, object sender, EventArgs arguments)
        {
            if (sender is GamePlayer player)
            {
                EnsureTraining(player);
                ChampionRules.RemindToVisitKing(player);
                if (player.MLLevel > 0 || player.ChampionLevel > 0)
                    SyncPassives(player);
            }
        }
    }
}
