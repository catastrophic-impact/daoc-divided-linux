using System;
using System.IO;
using OfflineDaoc.Configuration;

namespace DOL.GS;

public static class AutonomousBotGoalPolicy
{
    public static BotGoalSettings Settings { get; private set; } = BotGoalSettings.Defaults;
    public const string OverrideVariable = "OFFLINE_DAOC_BOT_GOALS";
    public static bool IsConfigured { get; private set; }

    public static string FilePath { get; private set; } // daoc-divided-linux: lets the admin screen edit the goals file

    // One startup read only. Never poll settings or query the database on AI turns.
    public static void Initialize(string serverDirectory)
    {
        string path = Path.Combine(serverDirectory, BotGoalSettings.FileName);
        // Test runs may point at their own goals file so the owner's saved launcher setting
        // (bot-goals.json) is never edited for a test.
        string overridePath = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            path = overridePath;
            DOL.Logging.LoggerManager.Create(typeof(AutonomousBotGoalPolicy)).Warn($"BOT_GOALS_OVERRIDE using {overridePath} instead of {BotGoalSettings.FileName}");
        }
        FilePath = path;
        Settings = BotGoalSettings.Load(path); // Reject corruption rather than silently ignoring 0% exclusions.
        IsConfigured = File.Exists(path);
    }

    // The admin screen saves new goal percentages here. Every goal choice reads Settings, so new choices use the change;
    // saved tasks are re-checked by ReconcileSavedAssignment at each bot's next login.
    // daoc-divided-linux: Apply() is ours (saves the goals the admin screen edits).
    public static void Apply(BotGoalSettings settings)
    {
        string path = FilePath ?? throw new InvalidOperationException("Bot goals are not initialized.");
        settings.Save(path, () => true);
        Settings = settings;
        IsConfigured = true;
    }

    // Night playtest 2026-11-04: the 20-49 row's 20% frontier RvR sent level 22-30 bots roaming enemy frontiers, where they
    // died to purple PvE monsters and level 50 enemy bots (Midgard: 129 deaths on "Active frontier RvR" below 50, only 14 to
    // enemy bots). The 20-49 row's RvR share now starts at level 40, at half strength, and reaches its full value at 50; the
    // rest goes back to solo and group PvE in their ratio. Battlegrounds (15-35) are untouched.
    public const int FrontierRvrMinimumLevel = 40;

    /// <summary>The part of the configured RvR share a bot of this level gets: 0 below 40, 50% at 40 rising to 95% at 49, all at 50. Pure.</summary>
    public static double FrontierRvrScale(int level) =>
        level >= 50 ? 1.0 : level < FrontierRvrMinimumLevel ? 0.0 : 0.5 + 0.5 * (level - FrontierRvrMinimumLevel) / 10.0;

    public static bool LevelAllowsFrontierRvr(int level) => FrontierRvrScale(level) > 0;

    /// <summary>A row with its RvR share scaled for the level and the remainder handed back to solo and group PvE. Pure.</summary>
    public static BotGoalWeights ScaleForLevel(BotGoalWeights row, int level)
    {
        double scale = FrontierRvrScale(level);
        if (row == null || row.RvR == 0 || scale >= 1.0)
            return row;
        int rvr = (int)Math.Floor(row.RvR * scale);
        int freed = row.RvR - rvr;
        int pve = row.SoloPve + row.GroupPve;
        int toSolo = pve == 0 ? freed : (int)Math.Round(freed * (double)row.SoloPve / pve);
        return row with { SoloPve = row.SoloPve + toSolo, GroupPve = row.GroupPve + freed - toSolo, RvR = rvr };
    }

    /// <summary>The goal weights a bot of this level actually draws from.</summary>
    public static BotGoalWeights WeightsFor(int level) => ScaleForLevel(Settings.ForLevel(level), level);

    public static eAutonomousObjectiveKind Choose(int level, Random random = null, bool excludeGroup = false,
        bool excludeBattlegrounds = false) =>
        (eAutonomousObjectiveKind)WeightsFor(level).Choose((random ?? Random.Shared).NextDouble(), excludeGroup,
            excludeBattlegrounds || !BattlegroundBrackets.LevelHasBracket(level));

    // A battleground goal also needs a bracket for the level (15-35); level is the only battleground limit.
    public static bool Allows(int level, eAutonomousObjectiveKind kind) =>
        WeightsFor(level).Allows((int)kind) &&
        (kind != eAutonomousObjectiveKind.Battleground || BattlegroundBrackets.LevelHasBracket(level));

    public static eAutonomousObjectiveKind EnsureAllowed(int level, eAutonomousObjectiveKind kind) =>
        Allows(level, kind) ? kind : Choose(level);

    // Called before an autonomous actor can enter the world. Keep inventory,
    // position, XP and valid saved task clocks; discard only a now-disabled task.
    public static bool ReconcileSavedAssignment(OfflineWorldBotRecord record)
    {
        if (record == null) return false;
        if (!IsConfigured)
        {
            // Without a goals file only the level gate applies: a saved frontier tour below level 40 ends.
            if (AutonomousObjectiveAssignments.Parse(record.ObjectiveKind) != eAutonomousObjectiveKind.RvR ||
                LevelAllowsFrontierRvr(record.Level))
                return false;
            ClearSavedAssignment(record, eAutonomousObjectiveKind.SoloPve);
            return true;
        }
        bool changed = false;
        if (!string.IsNullOrEmpty(record.ObjectiveRvrEligibleUtc))
        {
            record.ObjectiveRvrEligibleUtc = string.Empty;
            changed = true;
        }
        if (!AutonomousObjectiveAssignments.IsBetweenPveTasks(record) &&
            !Allows(record.Level, AutonomousObjectiveAssignments.Parse(record.ObjectiveKind)))
        {
            ClearSavedAssignment(record, Choose(record.Level));
            changed = true;
        }
        if (changed) record.Dirty = true;
        return changed;
    }

    private static void ClearSavedAssignment(OfflineWorldBotRecord record, eAutonomousObjectiveKind kind)
    {
        record.ObjectiveKind = kind.ToString();
        record.ObjectiveAssignmentId = string.Empty;
        record.ObjectiveAssignedUtc = record.ObjectiveExpiresUtc = string.Empty;
        record.CurrentCampId = record.TargetName = record.TravelDestination = string.Empty;
        record.ObjectivePhase = "Choosing a goal using Bot Goals Setting";
        record.Dirty = true;
    }
}
