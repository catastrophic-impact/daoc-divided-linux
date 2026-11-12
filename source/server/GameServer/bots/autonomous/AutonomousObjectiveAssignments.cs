using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DOL.GS;

/// <summary>
/// Owns durable population work allocation.  A bot's displayed activity is not
/// authority: this record is the source of truth across reloads and is consumed
/// by both PvE and RvR matchmaking.
/// </summary>
public enum eAutonomousObjectiveKind
{
    SoloPve,
    GroupPve,
    RvR,
    // Roam and siege inside the battleground of the bot's level bracket (levels 15-35).
    Battleground,
}

public enum eAutonomousPveCompletionMode
{
    Time,
    Kills,
}

public static class AutonomousObjectiveAssignments
{
    private const long RebalanceIntervalMilliseconds = 60_000;
    public static readonly TimeSpan MinimumRvrTenure = TimeSpan.FromMinutes(45);
    public static readonly TimeSpan MaximumRvrTenure = TimeSpan.FromMinutes(120);
    public static readonly TimeSpan MinimumPveTenure = TimeSpan.FromMinutes(45);
    public static readonly TimeSpan MaximumPveTenure = TimeSpan.FromMinutes(120);
    public static readonly TimeSpan GroupMatchmakingTimeout = TimeSpan.FromMinutes(20);
    private const string BetweenTasksPrefix = "between-pve-services-";
    public const string NoCampGoal = "Find a reachable level-appropriate XP camp";
    public const string RvrGoal = "Active frontier RvR";
    public const string BattlegroundGoal = "Battleground tour";
    public const string PveCompletionRequired = "awaiting-completed-pve-task";
    public static readonly TimeSpan MaximumBetweenTaskDuration = TimeSpan.FromMinutes(30);
    private static readonly object Sync = new();
    private static readonly ConcurrentDictionary<long, (string Assignment, long StartedTick)> GroupWaits = new();
    private static long _nextRebalanceTick;
    private static long _epoch;

    public readonly record struct Allocation(int SoloPve, int GroupPve, int RvR, int Battlegrounds = 0);
    public readonly record struct BetweenTaskPlan(bool Train, bool Unload);

    public static BetweenTaskPlan RollBetweenTaskPlan(bool canSpendPoints, bool backpackFull, double trainRoll, double unloadRoll,
        bool trainingOverdue = false) =>
        new(canSpendPoints && (trainingOverdue || trainRoll >= 0 && trainRoll < 0.90),
            backpackFull && unloadRoll >= 0 && unloadRoll < 0.95);

    public static eAutonomousObjectiveKind KindFor(GameBot bot) =>
        Parse(bot?.PersistentRecord?.ObjectiveKind);

    public static eAutonomousObjectiveKind Parse(string value) =>
        Enum.TryParse(value, true, out eAutonomousObjectiveKind result) ? result : eAutonomousObjectiveKind.SoloPve;

    public static bool Is(GameBot bot, eAutonomousObjectiveKind kind) => KindFor(bot) == kind;

    /// <summary>
    /// The status code shared with PvE (combat, route threats, replans) names a camp goal, and a frontier bot has no
    /// camp: of 1,276 RvR deaths in the last log of the night of 2026-10-31, 1,246 reported "Find a reachable
    /// level-appropriate XP camp" and 25 an old "Grind ..." goal, for bots whose objective was RvR. Any other goal text
    /// is kept as it is.
    /// </summary>
    public static string GoalFor(eAutonomousObjectiveKind kind, string goal)
    {
        bool campGoal = goal == NoCampGoal || goal?.StartsWith("Grind ", StringComparison.Ordinal) == true;
        return !campGoal ? goal : kind switch
        {
            eAutonomousObjectiveKind.RvR => RvrGoal,
            eAutonomousObjectiveKind.Battleground => BattlegroundGoal,
            _ => goal,
        };
    }

    public static TimeSpan RollRvrTenure(Random random = null)
    {
        random ??= Random.Shared;
        return TimeSpan.FromMinutes(random.Next((int)MinimumRvrTenure.TotalMinutes, (int)MaximumRvrTenure.TotalMinutes + 1));
    }

    public static TimeSpan RollPveTenure(Random random = null)
    {
        random ??= Random.Shared;
        return TimeSpan.FromMinutes(random.Next((int)MinimumPveTenure.TotalMinutes, (int)MaximumPveTenure.TotalMinutes + 1));
    }

    public static eAutonomousPveCompletionMode RollSoloPveCompletionMode(Random random = null)
    {
        return eAutonomousPveCompletionMode.Time;
    }

    public static bool HasActivePveAssignment(OfflineWorldBotRecord record, DateTime utcNow)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.ObjectiveAssignmentId))
            return false;
        eAutonomousObjectiveKind kind = Parse(record.ObjectiveKind);
        if (kind == eAutonomousObjectiveKind.RvR)
            return false;
        // Old persisted "Kills" records are also deadline-only. Real kill/XP
        // accounting remains in the normal server rules, never a task quota.
        return !IsBetweenPveTasks(record) &&
               DateTime.TryParse(record.ObjectiveExpiresUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime expiresUtc) &&
               expiresUtc.ToUniversalTime() > utcNow.ToUniversalTime();
    }

    public static void RecordPveKill(GameBot bot)
    {
        OfflineWorldBotRecord record = bot?.PersistentRecord;
        if (record == null || Parse(record.ObjectiveKind) == eAutonomousObjectiveKind.RvR)
            return;

        // Diagnostic count only; cannot finish or shorten a task.
        record.ObjectivePveKills++;
        bot.MarkAutonomousStateDirty();
        AutonomousCampDangerBench.RecordKill(record.CurrentCampId);
        // TargetName is the camp's monster while the bot works a camp (a kill elsewhere has no camp and no effect).
        if (!string.IsNullOrWhiteSpace(record.CurrentCampId))
            AutonomousCampDangerBench.RecordKill(AutonomousCampDangerBench.SpeciesKey(bot.CurrentRegionID, record.TargetName));
    }

    public static bool HasActiveRvrTenure(OfflineWorldBotRecord record, DateTime utcNow)
    {
        return record != null && Parse(record.ObjectiveKind) == eAutonomousObjectiveKind.RvR &&
               DateTime.TryParse(record.ObjectiveExpiresUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime expiresUtc) &&
               expiresUtc.ToUniversalTime() > utcNow.ToUniversalTime();
    }

    public static bool NormalizeLegacyTimedAssignment(OfflineWorldBotRecord record, DateTime utcNow)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.ObjectiveAssignmentId) || IsBetweenPveTasks(record))
            return false;
        bool changed = false;
        if (Parse(record.ObjectiveKind) != eAutonomousObjectiveKind.RvR && record.ObjectivePveMode != "Time")
        {
            record.ObjectivePveMode = "Time";
            changed = true;
        }
        if (record.ObjectivePveKillTarget != 0) { record.ObjectivePveKillTarget = 0; changed = true; }
        if (!DateTime.TryParse(record.ObjectiveAssignedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime assigned))
        {
            assigned = utcNow;
            record.ObjectiveAssignedUtc = assigned.ToString("O");
            changed = true;
        }
        bool validExpiry = DateTime.TryParse(record.ObjectiveExpiresUtc, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out DateTime expiry);
        double duration = validExpiry ? (expiry.ToUniversalTime() - assigned.ToUniversalTime()).TotalMinutes : 45;
        if (!validExpiry || duration < 45 || duration > 120)
        {
            record.ObjectiveExpiresUtc = assigned.ToUniversalTime().AddMinutes(Math.Clamp(duration, 45, 120)).ToString("O");
            changed = true;
        }
        return changed;
    }

    public static bool IsRvrEligible(OfflineWorldBotRecord record, DateTime utcNow)
    {
        // Frontier RvR starts at level 40 whatever the settings say (AutonomousBotGoalPolicy.FrontierRvrMinimumLevel).
        if (record != null && !AutonomousBotGoalPolicy.LevelAllowsFrontierRvr(record.Level))
            return false;
        if (AutonomousBotGoalPolicy.IsConfigured)
            return record == null || AutonomousBotGoalPolicy.WeightsFor(record.Level).RvR > 0;
        if (record?.ObjectiveRvrEligibleUtc == PveCompletionRequired) return false;
        return record == null || string.IsNullOrWhiteSpace(record.ObjectiveRvrEligibleUtc) ||
               !DateTime.TryParse(record.ObjectiveRvrEligibleUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime eligibleUtc) ||
               eligibleUtc.ToUniversalTime() <= utcNow.ToUniversalTime();
    }

    /// <summary>Selects only PvE work, retaining the current bucket's solo/group balance.</summary>
    public static eAutonomousObjectiveKind ChoosePveObjective(Allocation target, int assignedSolo, int assignedGroup, Random random = null)
    {
        random ??= Random.Shared;
        int soloNeeded = Math.Max(0, target.SoloPve - assignedSolo);
        int groupNeeded = Math.Max(0, target.GroupPve - assignedGroup);
        if (soloNeeded + groupNeeded == 0)
        {
            if (target.SoloPve + target.GroupPve == 0) return eAutonomousObjectiveKind.RvR;
            return random.Next(target.SoloPve + target.GroupPve) < target.SoloPve
                ? eAutonomousObjectiveKind.SoloPve : eAutonomousObjectiveKind.GroupPve;
        }
        return random.Next(soloNeeded + groupNeeded) < soloNeeded ? eAutonomousObjectiveKind.SoloPve : eAutonomousObjectiveKind.GroupPve;
    }

    /// <summary>Returns exact integer targets from the startup goal policy.</summary>
    public static Allocation TargetForPopulation(int count, bool levelFifty)
    {
        var weights = AutonomousBotGoalPolicy.Settings.ForLevel(levelFifty ? 50 : 20);
        return Allocate(count, weights.SoloPve / 100d, weights.GroupPve / 100d, weights.RvR / 100d, weights.Battlegrounds / 100d);
    }

    /// <summary>
    /// The 20-49 band's targets from its members' own levels: each bot counts with its level's weights
    /// (AutonomousBotGoalPolicy.WeightsFor), so the band's RvR share only comes from bots of level 40 and up.
    /// </summary>
    public static Allocation TargetForMidLevelPopulation(IReadOnlyList<int> levels)
    {
        if (levels == null || levels.Count == 0)
            return new Allocation(0, 0, 0, 0);
        double solo = 0, group = 0, rvr = 0, battlegrounds = 0;
        foreach (int level in levels)
        {
            var weights = AutonomousBotGoalPolicy.WeightsFor(level);
            solo += weights.SoloPve; group += weights.GroupPve; rvr += weights.RvR; battlegrounds += weights.Battlegrounds;
        }
        double total = levels.Count * 100d;
        return Allocate(levels.Count, solo / total, group / total, rvr / total, battlegrounds / total);
    }

    /// <summary>Below level 20 only the configured solo/group weights are used.</summary>
    public static Allocation TargetForLowLevelPopulation(int count)
    {
        var weights = AutonomousBotGoalPolicy.Settings.Levels1To19;
        return Allocate(count, weights.SoloPve / 100d, weights.GroupPve / 100d, 0, weights.Battlegrounds / 100d);
    }

    /// <summary>
    /// Battleground slots can only go to bots whose level fits a bracket (levels 15-35);
    /// the share the rest of the band can't use is handed back to solo/group PvE in their ratio.
    /// </summary>
    public static Allocation LimitBattlegrounds(Allocation target, int eligible)
    {
        int surplus = target.Battlegrounds - Math.Clamp(eligible, 0, target.Battlegrounds);
        if (surplus <= 0) return target;
        int pve = target.SoloPve + target.GroupPve;
        int toSolo = pve == 0 ? surplus : (int)Math.Round(surplus * (double)target.SoloPve / pve);
        return target with { SoloPve = target.SoloPve + toSolo, GroupPve = target.GroupPve + surplus - toSolo,
            Battlegrounds = target.Battlegrounds - surplus };
    }

    public static bool CanTakeBattleground(GameBot bot) =>
        bot != null && AutonomousBotGoalPolicy.Allows(bot.Level, eAutonomousObjectiveKind.Battleground);

    private static Allocation Allocate(int count, double soloWeight, double groupWeight, double rvrWeight, double battlegroundWeight = 0)
    {
        count = Math.Max(0, count);
        double[] desired = { count * soloWeight, count * groupWeight, count * rvrWeight, count * battlegroundWeight };
        int[] target = desired.Select(value => (int)Math.Floor(value)).ToArray();
        foreach (int index in Enumerable.Range(0, target.Length)
                     .OrderByDescending(index => desired[index] - target[index])
                     .ThenBy(index => index)
                     .Take(count - target.Sum()))
            target[index]++;
        return new Allocation(target[0], target[1], target[2], target[3]);
    }

    /// <summary>
    /// Rebalances by realm and level band. A stable allocation lives for one
    /// minute (and is persisted), avoiding per-think thrash while distributing
    /// all valid members without travel, density, or class preference.
    /// </summary>
    public static void ReconcileIfDue()
    {
        // This method is called by every active bot's group pulse. The old
        // implementation took the global lock and rebuilt the full roster
        // before checking its seven-minute timer, turning population growth
        // into O(N^2) work. The lock-free gate makes non-due calls constant
        // time; the inner gate prevents two due callers from duplicating work.
        long nowTick = GameLoop.GameLoopTime;
        if (nowTick < Interlocked.Read(ref _nextRebalanceTick))
            return;

        lock (Sync)
        {
            nowTick = GameLoop.GameLoopTime;
            if (nowTick < _nextRebalanceTick)
                return;

            DateTime utcNow = DateTime.UtcNow;
            GameBot[] roster = AutonomousBotRegistry.Snapshot()
                .Where(bot => bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper && !BotDutyLeases.IsSpokenForByPlayer(bot) &&
                              bot.PersistentRecord != null && !AutonomousBeggars.Is(bot))
                .ToArray();
            // The existing one-minute pass is also a backstop for a service bot
            // whose own movement/maintenance branch is delayed. No new timer.
            foreach (GameBot bot in roster.Where(BetweenTaskServiceExpired))
                CompleteBetweenTaskServices(bot);
            roster = roster.Where(bot => !IsBetweenPveTasks(bot)).ToArray();
            // Group/warband deadlines belong to the coordinator, not individual
            // members' old allocation timestamps. Snapshot once per allocation pass.
            var groupOwned = roster.Where(bot => bot.Group != null &&
                (!bot.Group.GetMembersInTheGroup().Any(member => member is GamePlayer) || BotLedGroups.IsHostedGroup(bot.Group))).ToHashSet();
            foreach (GameBot bot in roster.Where(bot => !groupOwned.Contains(bot)))
                if (NormalizeLegacyTimedAssignment(bot.PersistentRecord, utcNow))
                {
                    bot.MarkAutonomousStateDirty();
                    AutonomousBotStatusPersistence.Queue(bot);
                }
            GameBot[] expiredRvr = roster.Where(bot => Parse(bot.PersistentRecord.ObjectiveKind) == eAutonomousObjectiveKind.RvR &&
                                                       !string.IsNullOrWhiteSpace(bot.PersistentRecord.ObjectiveAssignmentId) &&
                                                       !groupOwned.Contains(bot) &&
                                                       !AutonomousRvrEventLayer.IsForceCommitted($"rvr-{bot.DatabaseID}", GameLoop.GameLoopTime) &&
                                                       !HasActiveRvrTenure(bot.PersistentRecord, utcNow))
                .ToArray();
            foreach (GameBot bot in expiredRvr)
            {
                // Solo roaming retains its own clock. Warbands expire together.
                BeginPveIntermission(bot);
            }
            GameBot[] completedPve = roster.Where(bot => Parse(bot.PersistentRecord.ObjectiveKind) != eAutonomousObjectiveKind.RvR &&
                                                          !IsBetweenPveTasks(bot) &&
                                                          !groupOwned.Contains(bot) &&
                                                          !string.IsNullOrWhiteSpace(bot.PersistentRecord.ObjectiveAssignmentId) &&
                                                          !HasActivePveAssignment(bot.PersistentRecord, utcNow))
                .ToArray();
            foreach (GameBot bot in completedPve)
            {
                MarkPveTaskCompleted(bot);
                if (TryBeginBetweenTaskServices(bot))
                    continue;
                bot.PersistentRecord.ObjectiveAssignmentId = string.Empty;
                bot.PersistentRecord.CurrentCampId = string.Empty;
                bot.PersistentRecord.TargetName = string.Empty;
                bot.PersistentRecord.TravelDestination = string.Empty;
                bot.MarkAutonomousStateDirty();
            }
            _epoch++;
            Interlocked.Exchange(ref _nextRebalanceTick, nowTick + RebalanceIntervalMilliseconds);
            foreach (IGrouping<(eRealm Realm, int Band), GameBot> bucket in roster.GroupBy(bot =>
                         (bot.Realm, bot.Level >= 50 ? 50 : bot.Level >= 20 ? 20 : 0)))
            {
                GameBot[] members = bucket.Where(bot => !IsBetweenPveTasks(bot)).ToArray();
                // Fisher-Yates, rather than a scored ordering: among the legal
                // realm/level bucket every bot has equal selection probability.
                for (int index = members.Length - 1; index > 0; index--)
                {
                    int other = Random.Shared.Next(index + 1);
                    (members[index], members[other]) = (members[other], members[index]);
                }

                Allocation target = bucket.Key.Band == 0
                    ? TargetForLowLevelPopulation(members.Length)
                    : bucket.Key.Band == 20
                        ? TargetForMidLevelPopulation(members.Select(member => (int)member.Level).ToArray())
                        : TargetForPopulation(members.Length, true);
                target = LimitBattlegrounds(target, members.Count(CanTakeBattleground));
                int currentRvr = members.Count(bot => Is(bot, eAutonomousObjectiveKind.RvR));
                foreach (GameBot bot in members.Where(bot => bot.Group == null &&
                             Is(bot, eAutonomousObjectiveKind.GroupPve) &&
                             GroupMatchmakingTimedOutOnline(bot)).ToArray())
                {
                    bool useRvr = IsRvrEligible(bot.PersistentRecord, utcNow) && currentRvr < target.RvR;
                    eAutonomousObjectiveKind fallback = useRvr
                        ? eAutonomousObjectiveKind.RvR : eAutonomousObjectiveKind.SoloPve;
                    if (AutonomousBotGoalPolicy.IsConfigured)
                    {
                        var weights = AutonomousBotGoalPolicy.WeightsFor(bot.Level);
                        if (!weights.Allows((int)fallback))
                            fallback = AutonomousBotGoalPolicy.Choose(bot.Level, excludeGroup: true);
                    }
                    // No frontier tour below 40 (Assign enforces it too): the log and the RvR count say what is assigned.
                    if (fallback == eAutonomousObjectiveKind.RvR && !AutonomousBotGoalPolicy.LevelAllowsFrontierRvr(bot.Level))
                    {
                        fallback = eAutonomousObjectiveKind.SoloPve;
                        useRvr = false;
                    }
                    if (useRvr) currentRvr++;
                    DOL.Logging.LoggerManager.Create(typeof(AutonomousObjectiveAssignments)).Warn(
                        $"AUTONOMOUS_GROUP_MATCHMAKING_TIMEOUT bot=\"{bot.Name}\" id={bot.DatabaseID} " +
                        $"realm={GlobalConstants.RealmToName(bot.Realm)} level={bot.Level} waitedMinutes=20 fallback={fallback}");
                    Assign(bot, fallback, bucket.Key, ++_epoch);
                    bot.PersistentRecord.CurrentCampId = string.Empty;
                    bot.PersistentRecord.TargetName = string.Empty;
                    bot.PersistentRecord.TravelDestination = string.Empty;
                    bot.PersistentRecord.ObjectivePhase =
                        fallback == eAutonomousObjectiveKind.GroupPve
                            ? "Group-only setting: no complete role roster yet; restarting the 20-minute queue"
                            : $"No complete eight-member role roster formed within 20 minutes; assigned {fallback}";
                    bot.MarkAutonomousStateDirty();
                    AutonomousBotStatusPersistence.Queue(bot);
                }
                // A live RvR tour is per bot and is never disturbed by the
                // shared seven-minute allocator. It continues through reloads
                // because its expiry is held by the persistent record.
                GameBot[] lockedRvr = members.Where(bot => HasActiveRvrTenure(bot.PersistentRecord, utcNow) ||
                    groupOwned.Contains(bot) && Is(bot, eAutonomousObjectiveKind.RvR)).ToArray();
                GameBot[] lockedPve = members.Where(bot => HasActivePveAssignment(bot.PersistentRecord, utcNow) ||
                    groupOwned.Contains(bot) && !Is(bot, eAutonomousObjectiveKind.RvR)).ToArray();
                int assignedSolo = lockedPve.Count(bot => Parse(bot.PersistentRecord.ObjectiveKind) == eAutonomousObjectiveKind.SoloPve);
                int assignedGroup = lockedPve.Count(bot => Parse(bot.PersistentRecord.ObjectiveKind) == eAutonomousObjectiveKind.GroupPve);
                int assignedBattleground = lockedPve.Count(bot => Parse(bot.PersistentRecord.ObjectiveKind) == eAutonomousObjectiveKind.Battleground);
                int assignedRvr = lockedRvr.Length;
                // A battleground slot is drawn with the same odds as the remaining solo/group need.
                bool TakesBattleground(GameBot bot, int otherNeeded)
                {
                    int needed = Math.Max(0, target.Battlegrounds - assignedBattleground);
                    return needed > 0 && CanTakeBattleground(bot) && Random.Shared.Next(needed + Math.Max(0, otherNeeded)) < needed;
                }
                // With personalities on, a pool's open slots are planned together so the type decides WHO takes
                // each goal (No-lifers and Daredevils to RvR, Socialites to groups...); the mix still decides how
                // many (AutonomousBotPersonality.PlanGoals, review of goals 2026-10-17). Off: the old draw, unchanged.
                eAutonomousObjectiveKind?[] Plan(GameBot[] pool, bool withRvr) =>
                    AutonomousBotPersonality.Enabled
                        ? AutonomousBotPersonality.PlanGoals(pool.Select(AutonomousBotPersonality.PlannerType).ToArray(),
                            pool.Select(CanTakeBattleground).ToArray(), target.SoloPve - assignedSolo, target.GroupPve - assignedGroup,
                            withRvr ? target.RvR - assignedRvr : 0, target.Battlegrounds - assignedBattleground, Random.Shared)
                        : new eAutonomousObjectiveKind?[pool.Length];

                GameBot[] pvePool = members.Where(bot => !groupOwned.Contains(bot) && !HasActiveRvrTenure(bot.PersistentRecord, utcNow) &&
                                                         !HasActivePveAssignment(bot.PersistentRecord, utcNow) &&
                                                         !IsRvrEligible(bot.PersistentRecord, utcNow)).ToArray();
                eAutonomousObjectiveKind?[] pvePlan = Plan(pvePool, false);
                foreach (int i in AutonomousBotPersonality.PlannedFirst(pvePlan))
                {
                    GameBot bot = pvePool[i];
                    eAutonomousObjectiveKind kind = pvePlan[i] ?? (TakesBattleground(bot,
                        Math.Max(0, target.SoloPve - assignedSolo) + Math.Max(0, target.GroupPve - assignedGroup))
                        ? eAutonomousObjectiveKind.Battleground
                        : ChoosePveObjective(target, assignedSolo, assignedGroup));
                    if (kind == eAutonomousObjectiveKind.Battleground)
                        assignedBattleground++;
                    else if (kind == eAutonomousObjectiveKind.SoloPve)
                        assignedSolo++;
                    else
                        assignedGroup++;
                    Assign(bot, kind, bucket.Key, _epoch);
                }

                GameBot[] rvrPool = members.Where(bot => !groupOwned.Contains(bot) && !HasActiveRvrTenure(bot.PersistentRecord, utcNow) &&
                                                         !HasActivePveAssignment(bot.PersistentRecord, utcNow) &&
                                                         IsRvrEligible(bot.PersistentRecord, utcNow)).ToArray();
                eAutonomousObjectiveKind?[] rvrPlan = Plan(rvrPool, true);
                foreach (int i in AutonomousBotPersonality.PlannedFirst(rvrPlan))
                {
                    GameBot bot = rvrPool[i];
                    eAutonomousObjectiveKind kind;
                    int rvrNeeded = Math.Max(0, target.RvR - assignedRvr);
                    int soloNeeded = Math.Max(0, target.SoloPve - assignedSolo);
                    int groupNeeded = Math.Max(0, target.GroupPve - assignedGroup);
                    if (rvrPlan[i] is { } planned)
                        kind = planned;
                    else if (rvrNeeded > 0 && Random.Shared.Next(rvrNeeded + soloNeeded + groupNeeded) < rvrNeeded)
                        kind = eAutonomousObjectiveKind.RvR;
                    else if (TakesBattleground(bot, soloNeeded + groupNeeded))
                        kind = eAutonomousObjectiveKind.Battleground;
                    else if (AutonomousBotGoalPolicy.IsConfigured && rvrNeeded + soloNeeded + groupNeeded == 0)
                        kind = AutonomousBotPersonality.ChooseGoal(bot, () => AutonomousBotGoalPolicy.Choose(bot.Level));
                    else
                        kind = ChoosePveObjective(target, assignedSolo, assignedGroup);

                    switch (kind)
                    {
                        case eAutonomousObjectiveKind.SoloPve: assignedSolo++; break;
                        case eAutonomousObjectiveKind.GroupPve: assignedGroup++; break;
                        case eAutonomousObjectiveKind.RvR: assignedRvr++; break;
                        case eAutonomousObjectiveKind.Battleground: assignedBattleground++; break;
                    }
                    Assign(bot, kind, bucket.Key, _epoch);
                }
            }
        }
    }

    private static void BeginPveIntermission(GameBot bot)
    {
        OfflineWorldBotRecord record = bot.PersistentRecord;
        record.ObjectiveExpiresUtc = string.Empty;
        record.ObjectivePveMode = string.Empty;
        record.ObjectivePveKillTarget = 0;
        record.ObjectivePveKills = 0;
        record.ObjectiveRvrEligibleUtc = AutonomousBotGoalPolicy.IsConfigured ? string.Empty : PveCompletionRequired;
        record.ObjectiveAssignmentId = string.Empty;
        record.ObjectivePhase = "Returning to PvE after frontier tour";
        record.CurrentCampId = string.Empty;
        record.TargetName = string.Empty;
        record.TravelDestination = string.Empty;
        record.ObjectiveProgress = "Frontier tenure complete; choosing a PvE objective";
        TryBeginBetweenTaskServices(bot);
        bot.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(bot);
    }

    public static void BeginSoloAfterGroupTask(GameBot bot, string reason)
    {
        if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.Group != null || bot.PersistentRecord == null)
            return;
        // Do not acquire the allocation lock from the group coordinator: the
        // allocation pass itself can remove group members. A fresh independent
        // task resets only work, never progress/items. Every level must finish
        // one PvE assignment after an RvR tour before becoming eligible again.
        bool leavingRvr = Is(bot, eAutonomousObjectiveKind.RvR);
        if (leavingRvr)
            bot.PersistentRecord.ObjectiveRvrEligibleUtc = AutonomousBotGoalPolicy.IsConfigured ? string.Empty : PveCompletionRequired;
        if (TryBeginBetweenTaskServices(bot))
            return;
        Assign(bot, AutonomousBotGoalPolicy.IsConfigured ? AutonomousBotPersonality.ChooseGoal(bot, () => AutonomousBotGoalPolicy.Choose(bot.Level)) :
            bot.Level >= 50 && !leavingRvr ? RollLevelFiftyObjective() : eAutonomousObjectiveKind.SoloPve,
            (bot.Realm, bot.Level >= 50 ? 50 : bot.Level >= 20 ? 20 : 0), GameLoop.GameLoopTime);
        bot.PersistentRecord.CurrentCampId = string.Empty;
        bot.PersistentRecord.TargetName = string.Empty;
        bot.PersistentRecord.TravelDestination = string.Empty;
        bot.PersistentRecord.ObjectivePhase = reason;
        bot.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(bot);
    }

    /// <summary>Ends a battleground tour early (out-levelled, or no teleporter reachable) with a new non-battleground goal.</summary>
    public static void EndBattlegroundTour(GameBot bot, string reason)
    {
        if (bot?.PersistentRecord == null || !Is(bot, eAutonomousObjectiveKind.Battleground)) return;
        eAutonomousObjectiveKind next = AutonomousBotGoalPolicy.IsConfigured
            ? AutonomousBotGoalPolicy.Choose(bot.Level, excludeBattlegrounds: true)
            : eAutonomousObjectiveKind.SoloPve;
        Assign(bot, next, (bot.Realm, bot.Level >= 50 ? 50 : bot.Level >= 20 ? 20 : 0), GameLoop.GameLoopTime);
        bot.PersistentRecord.CurrentCampId = bot.PersistentRecord.TargetName = bot.PersistentRecord.TravelDestination = string.Empty;
        bot.PersistentRecord.ObjectivePhase = reason;
        bot.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(bot);
    }

    public static bool GroupMatchmakingTimedOut(OfflineWorldBotRecord record, DateTime utcNow) =>
        record != null && Parse(record.ObjectiveKind) == eAutonomousObjectiveKind.GroupPve &&
        DateTime.TryParse(record.ObjectiveAssignedUtc, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out DateTime assignedUtc) &&
        utcNow.ToUniversalTime() - assignedUtc.ToUniversalTime() >= GroupMatchmakingTimeout;

    private static bool GroupMatchmakingTimedOutOnline(GameBot bot)
    {
        long key = bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID;
        string assignment = bot.PersistentRecord?.ObjectiveAssignmentId ?? string.Empty;
        if (!GroupWaits.TryGetValue(key, out var wait) ||
            !string.Equals(wait.Assignment, assignment, StringComparison.Ordinal))
        {
            GroupWaits[key] = (assignment, GameLoop.GameLoopTime);
            return false;
        }
        return GameLoop.GameLoopTime - wait.StartedTick >= GroupMatchmakingTimeout.TotalMilliseconds;
    }

    /// <summary>How long an ungrouped bot has waited on its current GroupPve assignment (0 when it isn't waiting).</summary>
    public static long GroupWaitMilliseconds(GameBot bot)
    {
        if (bot?.PersistentRecord == null || bot.Group != null || !Is(bot, eAutonomousObjectiveKind.GroupPve))
            return 0;
        long key = bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID;
        return GroupWaits.TryGetValue(key, out var wait) &&
               string.Equals(wait.Assignment, bot.PersistentRecord.ObjectiveAssignmentId ?? string.Empty, StringComparison.Ordinal)
            ? Math.Max(0, GameLoop.GameLoopTime - wait.StartedTick)
            : 0;
    }

    /// <summary>
    /// Matchmaking owns an ungrouped GroupPvE bot until it assigns the complete
    /// eight-member roster or performs the twenty-minute fallback.  The generic
    /// fifteen-minute no-movement watchdog must not race that authoritative
    /// deadline and turn a valid queue wait into a capital recovery.
    /// </summary>
    public static bool IsAwaitingGroupMatchmaking(GameBot bot) =>
        bot?.IsAutonomousWorldBot == true && !bot.IsTemporaryGroupHelper && bot.Group == null &&
        bot.PersistentRecord != null && Is(bot, eAutonomousObjectiveKind.GroupPve);

    public static bool IsProtectedGroupMatchmakingState(eAutonomousObjectiveKind kind, bool hasGroup) =>
        kind == eAutonomousObjectiveKind.GroupPve && !hasGroup;

    /// <summary>
    /// A capital relocation invalidates the controller's in-memory route and
    /// therefore must also receive a genuinely new durable assignment id. This
    /// changes no inventory, equipment, coin, level, or character data.
    /// </summary>
    public static void BeginSoloAfterCapitalRecovery(GameBot bot, string reason)
    {
        if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.PersistentRecord == null)
            return;
        Assign(bot, eAutonomousObjectiveKind.SoloPve,
            (bot.Realm, bot.Level >= 50 ? 50 : bot.Level >= 20 ? 20 : 0),
            GameLoop.GameLoopTime + Math.Max(1, bot.PersistentRecord.RecoveryCount));
        bot.PersistentRecord.CurrentCampId = string.Empty;
        bot.PersistentRecord.TargetName = string.Empty;
        bot.PersistentRecord.TravelDestination = string.Empty;
        bot.PersistentRecord.ObjectivePhase = reason;
        bot.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(bot);
    }

    public static void AssignForcedRaid(GameBot bot, string eventId, bool forced = true)
    {
        if (!AutonomousRealmRaid.IsEligible(bot) || AutonomousRealmRaid.GetView(bot.Group)?.EventId != eventId) return;
        Assign(bot, eAutonomousObjectiveKind.GroupPve, (bot.Realm, 50), GameLoop.GameLoopTime, true);
        bot.PersistentRecord.ObjectiveAssignmentId = $"{(forced ? "forced" : "automatic")}-raid-{eventId}-{bot.DatabaseID}-{GameLoop.GameLoopTime}";
        bot.PersistentRecord.CurrentCampId = bot.PersistentRecord.TargetName = bot.PersistentRecord.TravelDestination = string.Empty;
        bot.PersistentRecord.ObjectivePhase = $"{(forced ? "Forced" : "Automatic")} realm event — heading to staging";
        bot.MarkAutonomousStateDirty();
    }

    private static void Assign(GameBot bot, eAutonomousObjectiveKind kind, (eRealm Realm, int Band) bucket, long epoch, bool forcedRaid = false)
    {
        // daoc-divided-linux: a battleground bracket bot only ever works on the battleground goal.
        if (BracketBotService.TryChooseGoal(bot, out eAutonomousObjectiveKind bracketGoal))
            kind = bracketGoal;
        // Recovery and task-boundary callers cannot reintroduce a disabled goal.
        if (AutonomousBotGoalPolicy.IsConfigured && !forcedRaid)
            kind = AutonomousBotGoalPolicy.EnsureAllowed(bot.Level, kind);
        // No frontier tour below level 40, configured or not (night playtest 2026-11-04).
        if (kind == eAutonomousObjectiveKind.RvR && !forcedRaid && !AutonomousBotGoalPolicy.LevelAllowsFrontierRvr(bot.Level))
            kind = eAutonomousObjectiveKind.SoloPve;
        // No healer free for the queue at this level: solo instead of a 20-minute wait that ends solo anyway (AutonomousGroupSupply).
        if (kind == eAutonomousObjectiveKind.GroupPve && !forcedRaid && !AutonomousGroupSupply.ShouldQueue(bot))
        {
            eAutonomousObjectiveKind solo = AutonomousBotGoalPolicy.IsConfigured
                ? AutonomousBotGoalPolicy.EnsureAllowed(bot.Level, eAutonomousObjectiveKind.SoloPve) : eAutonomousObjectiveKind.SoloPve;
            if (solo != eAutonomousObjectiveKind.GroupPve)
            {
                kind = solo;
                DOL.Logging.LoggerManager.Create(typeof(AutonomousObjectiveAssignments)).Debug(
                    $"AUTONOMOUS_GROUP_HEALER_SHORT bot=\"{bot.Name}\" realm={GlobalConstants.RealmToName(bot.Realm)} level={bot.Level} assigned={kind}");
            }
        }
        OfflineWorldBotRecord record = bot.PersistentRecord;
        // eRealm.Albion and _FirstPlayerRealm share a value; ToString() labelled Albion assignments "_firstplayerrealm-...".
        string assignment = $"{GlobalConstants.RealmToName(bucket.Realm).ToLowerInvariant()}-{bucket.Band}-{epoch}-{kind}";
        if (string.Equals(record.ObjectiveKind, kind.ToString(), StringComparison.Ordinal) &&
            string.Equals(record.ObjectiveAssignmentId, assignment, StringComparison.Ordinal))
            return;

        record.ObjectiveKind = kind.ToString();
        record.ObjectiveAssignmentId = assignment;
        record.ObjectiveAssignedUtc = DateTime.UtcNow.ToString("O");
        DateTime now = DateTime.UtcNow;
        record.ObjectivePveKills = 0;
        record.ObjectivePveKillTarget = 0;
        if (kind == eAutonomousObjectiveKind.RvR)
        {
            record.ObjectiveExpiresUtc = now.Add(RollRvrTenure()).ToString("O");
            record.ObjectivePveMode = string.Empty;
        }
        else
        {
            // This is only the ungrouped matchmaking allocation lifetime.
            // A formed party's common clock replaces it once assembled.
            record.ObjectivePveMode = eAutonomousPveCompletionMode.Time.ToString();
            record.ObjectiveExpiresUtc = now.Add(RollPveTenure()).ToString("O");
        }
        if (kind == eAutonomousObjectiveKind.RvR)
            record.ObjectiveRvrEligibleUtc = string.Empty;
        record.ObjectivePhase = kind == eAutonomousObjectiveKind.RvR ? "Awaiting realm warband" : "Awaiting objective";
        record.ObjectiveProgress = kind switch
        {
            eAutonomousObjectiveKind.GroupPve => "Awaiting a party; the formed group will share one task timer",
            eAutonomousObjectiveKind.SoloPve => $"Solo timed grind ends {record.ObjectiveExpiresUtc}",
            eAutonomousObjectiveKind.Battleground => $"Battleground tour ends {record.ObjectiveExpiresUtc}",
            _ => record.ObjectiveProgress,
        };
        long waitKey = bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID;
        if (kind == eAutonomousObjectiveKind.GroupPve)
            GroupWaits[waitKey] = (assignment, GameLoop.GameLoopTime);
        else
            GroupWaits.TryRemove(waitKey, out _);
        bot.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(bot);
    }

    /// <summary>
    /// The allocator runs once a minute, so a bot that logs in (or finishes a task) first picks and walks
    /// to a camp with no assignment, then loses that walk when its first assignment arrives (2026-10-18:
    /// 111 of 148 goal attempts by 120 new bots ended "Objective assignment changed" after 7-47 seconds).
    /// A first PvE assignment for an ungrouped bot keeps that camp; RvR, battleground and between-task
    /// work still replan, and a bot awaiting group matchmaking never keeps a dungeon camp.
    /// <paramref name="hasCamp"/> means the controller's camp is still the record's saved camp (a capital
    /// recovery clears the saved camp first, so its stale route is always dropped).
    /// A PvE assignment that follows another ordinary PvE assignment keeps the camp too while the bot is still
    /// walking there: the allocator renews an expired solo tenure, or moves a solo bot to the party queue, without
    /// touching its camp, and dropping the walk was the commonest end of a goal (2026-10-28: 4,189 goals ended
    /// "Objective assignment changed", 2,389 of them solo to solo and 1,036 solo to the party queue; 3,457 had not
    /// arrived). A bot already grinding at its camp still moves on at a renewal, as before.
    /// Callers that mean a new route (matchmaking timeouts, capital recovery, the end of a group or frontier task)
    /// clear the saved camp, so <paramref name="hasCamp"/> is false for them.
    /// </summary>
    public static bool KeepsCampOnFirstAssignment(string previousAssignmentId, eAutonomousObjectiveKind kind,
        bool betweenTasks, bool grouped, bool hasCamp, bool campIsDungeon, bool arrivedAtCamp = false) =>
        (string.IsNullOrEmpty(previousAssignmentId) || !arrivedAtCamp && IsOrdinaryPveAssignmentId(previousAssignmentId)) &&
        hasCamp && !grouped && !betweenTasks &&
        (kind == eAutonomousObjectiveKind.SoloPve || kind == eAutonomousObjectiveKind.GroupPve && !campIsDungeon);

    /// <summary>An allocator-made solo or party-queue id (<c>realm-band-epoch-Kind</c>); never a raid, RvR or between-task id.</summary>
    public static bool IsOrdinaryPveAssignmentId(string assignmentId) =>
        !string.IsNullOrEmpty(assignmentId) && !assignmentId.StartsWith(BetweenTasksPrefix, StringComparison.Ordinal) &&
        !assignmentId.Contains("-raid-", StringComparison.Ordinal) &&
        (assignmentId.EndsWith("-" + eAutonomousObjectiveKind.SoloPve, StringComparison.Ordinal) ||
         assignmentId.EndsWith("-" + eAutonomousObjectiveKind.GroupPve, StringComparison.Ordinal));

    public static bool IsBetweenPveTasks(OfflineWorldBotRecord record) =>
        record?.ObjectiveAssignmentId?.StartsWith(BetweenTasksPrefix, StringComparison.Ordinal) == true;

    public static bool IsBetweenPveTasks(GameBot bot) => IsBetweenPveTasks(bot?.PersistentRecord);

    public static bool WantsBetweenTaskTraining(GameBot bot) =>
        HasBetweenTaskFlag(bot?.PersistentRecord, 'T');

    public static bool WantsBetweenTaskInventory(GameBot bot) =>
        HasBetweenTaskFlag(bot?.PersistentRecord, 'I');

    public static bool WantsBetweenTaskDowntime(GameBot bot) =>
        HasBetweenTaskFlag(bot?.PersistentRecord, 'D');

    public static bool WantsBetweenTaskCraft(GameBot bot) =>
        HasBetweenTaskFlag(bot?.PersistentRecord, 'C');

    /// <summary>
    /// Closes a maintenance assignment as soon as the work it actually
    /// requested is complete.  The old path waited for the next global
    /// rebalance (or the thirty-minute lease) even after the sale/listing,
    /// purchase, or training action had succeeded, leaving stale service
    /// routes and repeated timeout messages.  Optional town downtime keeps
    /// its own controller-owned clock and is intentionally left alone here.
    /// </summary>
    public static bool TryCompleteBetweenTaskServicesIfSatisfied(GameBot bot)
    {
        if (!IsBetweenPveTasks(bot) || BetweenTaskServiceExpired(bot) || WantsBetweenTaskDowntime(bot))
            return false;

        bool trainingComplete = !WantsBetweenTaskTraining(bot) ||
            (!bot.HasSpendableAutonomousTrainingPoints && !bot.HasPendingAutonomousTraining);
        bool inventoryComplete = !WantsBetweenTaskInventory(bot) || AutonomousBotEconomy.GetNeededService(bot) == null;
        bool craftComplete = !WantsBetweenTaskCraft(bot) || AutonomousBotCrafting.TripDone(bot);
        if (!trainingComplete || !inventoryComplete || !craftComplete)
            return false;

        CompleteBetweenTaskServices(bot);
        return true;
    }

    public static bool HasBetweenTaskFlag(OfflineWorldBotRecord record, char flag)
    {
        if (!IsBetweenPveTasks(record)) return false;
        string assignment = record.ObjectiveAssignmentId;
        int end = assignment.IndexOf('-', BetweenTasksPrefix.Length);
        return end > BetweenTasksPrefix.Length &&
            assignment.AsSpan(BetweenTasksPrefix.Length, end - BetweenTasksPrefix.Length).Contains(flag);
    }

    public static bool IsIntentionalTownIdle(OfflineWorldBotRecord record, DateTime now)
    {
        return HasBetweenTaskFlag(record, 'D') && record.ObjectivePhase == AutonomousWorldBotController.TownIdlePhase &&
            record.Activity == AutonomousWorldBotController.TownIdlePhase &&
            DateTime.TryParse(record.ObjectiveExpiresUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime end) &&
            now < end.ToUniversalTime();
    }

    public static eAutonomousObjectiveKind RollLevelFiftyObjective(Random random = null)
    {
        return AutonomousBotGoalPolicy.Choose(50, random);
    }

    public static void MarkPveTaskCompleted(GameBot bot)
    {
        if (bot?.PersistentRecord == null || Is(bot, eAutonomousObjectiveKind.RvR) || IsBetweenPveTasks(bot)) return;
        bot.PersistentRecord.ObjectiveRvrEligibleUtc = string.Empty;
        bot.MarkAutonomousStateDirty();
    }

    // Called only at a task boundary, never from loot handling. Entering this
    // durable phase commits to unloading even after the first slot is freed.
    private static bool TryBeginBetweenTaskServices(GameBot bot)
    {
        if (bot?.PersistentRecord == null)
            return false;
        // A natural task break: deliver pieces made for other bots and perhaps commission one (bot_craft_orders).
        AutonomousBotCraftOrders.AtTaskBreak(bot);
        if (bot.Group != null)
            return false;
        // Unload at this task boundary when the bag is nearly full, not only
        // when completely full: otherwise the next task leaves loot on the ground.
        BetweenTaskPlan plan = RollBetweenTaskPlan(bot.HasSpendableAutonomousTrainingPoints,
            AutonomousBotEconomy.IsBackpackNearlyFull(bot), Random.Shared.NextDouble(), Random.Shared.NextDouble(),
            bot.IsAutonomousTrainingOverdue);
        bool downtime = AutonomousBotPersonality.RollTownBreak(bot, Random.Shared.NextDouble());
        // A crafter with a paid order to make always goes.
        bool craft = AutonomousBotCrafting.WantsTrip(AutonomousBotCrafting.IsTrained(bot), bot.Level,
            AutonomousBotCrafting.MaterialStacks(bot), Random.Shared.NextDouble()) ||
            AutonomousBotCrafting.WantsTrip(true, bot.Level, 0, 0) && AutonomousBotCraftOrders.HasOpenWork(bot);
        if (!plan.Train && !plan.Unload && !downtime && !craft) return false;
        OfflineWorldBotRecord record = bot.PersistentRecord;
        record.ObjectiveKind = eAutonomousObjectiveKind.SoloPve.ToString();
        // A craft trip ends at the usual inventory stop: what it made goes to the Realm Exchange there.
        record.ObjectiveAssignmentId = BetweenTasksPrefix + (plan.Train ? "T" : "") + (craft ? "C" : "") + (plan.Unload || craft ? "I" : "") + (downtime ? "D" : "") +
            "-" + bot.DatabaseID + "-" + GameLoop.GameLoopTime;
        record.ObjectiveAssignedUtc = DateTime.UtcNow.ToString("O");
        // A failed service route cannot trap the bot forever either. Existing
        // movement/progress watchdogs still operate during the trip.
        record.ObjectiveExpiresUtc = DateTime.UtcNow.Add(MaximumBetweenTaskDuration).ToString("O");
        record.ObjectivePveMode = eAutonomousPveCompletionMode.Time.ToString();
        record.ObjectivePveKillTarget = 0;
        record.ObjectivePhase = "Between tasks";
        record.CurrentCampId = string.Empty;
        record.TargetName = string.Empty;
        record.TravelDestination = string.Empty;
        record.ObjectiveProgress = "Task complete; " + (plan.Train ? "train earned specialization points; " : "") +
            (craft ? "make a trip to craft; " : "") + (plan.Unload ? "unload backpack; " : "") + (downtime ? "15-30 minute town break if reachable within the maintenance budget; " : "") + "then choose the next task";
        AutonomousBotEconomy.MarkInventoryChanged(bot);
        bot.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(bot);
        return true;
    }

    public static bool BetweenTaskServiceExpired(GameBot bot) => IsBetweenPveTasks(bot) &&
        (!DateTime.TryParse(bot.PersistentRecord.ObjectiveExpiresUtc, null,
             System.Globalization.DateTimeStyles.RoundtripKind, out DateTime due) || due.ToUniversalTime() <= DateTime.UtcNow);

    public static void CompleteBetweenTaskServices(GameBot bot)
    {
        if (!IsBetweenPveTasks(bot)) return;
        // Hand over what this break's craft trip made for other bots, not one task later.
        AutonomousBotCraftOrders.AtTaskBreak(bot, commission: false);
        if (BetweenTaskServiceExpired(bot))
            DOL.Logging.LoggerManager.Create(typeof(AutonomousObjectiveAssignments)).Warn(
                $"AUTONOMOUS_BETWEEN_TASK_TIMEOUT bot=\"{bot.Name}\" id={bot.DatabaseID} level={bot.Level} " +
                $"region={bot.CurrentRegionID} x={bot.X} y={bot.Y} z={bot.Z} " +
                $"goal=\"{bot.PersistentRecord.CurrentGoal}\" activity=\"{bot.PersistentRecord.Activity}\" " +
                $"assignment=\"{bot.PersistentRecord.ObjectiveAssignmentId}\"");
        bool pveRequired = bot.PersistentRecord.ObjectiveRvrEligibleUtc == PveCompletionRequired;
        Assign(bot, AutonomousBotGoalPolicy.IsConfigured ? AutonomousBotPersonality.ChooseGoal(bot, () => AutonomousBotGoalPolicy.Choose(bot.Level)) :
            bot.Level >= 50 && !pveRequired ? RollLevelFiftyObjective() : eAutonomousObjectiveKind.SoloPve,
            (bot.Realm, bot.Level >= 50 ? 50 : bot.Level >= 20 ? 20 : 0), GameLoop.GameLoopTime);
    }
}
