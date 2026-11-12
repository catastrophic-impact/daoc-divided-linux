using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.Keeps;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;
using DOL.GS.ServerRules;
using static DOL.GS.ServerRules.IServerRules;

namespace DOL.GS;

/// <summary>
/// Treats a persistent autonomous gamebot as a player only for RvR realm-point
/// credit. Ordinary NPCs, controlled pets, and temporary /spawn companions
/// remain ineligible victims.
/// </summary>
public static class AutonomousBotRealmPointRewards
{
    internal const string LastRealmPointDeathTickProperty = "autonomous.rvr.last.realm.point.death.tick";

    public static bool IsEligibleVictim(GameNPC npc) =>
        npc is GameBot { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false };

    internal const string ReleasedAfterDeathProperty = "autonomous.rvr.released.after.death";

    /// <summary>
    /// 1.124 (DR-175): realm points from killing a player are raised by 25% before any other bonus.
    /// kill_rp_bonus_percent, 0 turns it off.
    /// </summary>
    public static int ApplyKillBonus(int realmPoints) =>
        realmPoints <= 0 ? realmPoints : (int)(realmPoints * (100L + Math.Max(0, Properties.KILL_RP_BONUS_PERCENT)) / 100);

    /// <summary>
    /// 1.123b (DR-180): seconds after a death until the victim is worth realm points again. 2 minutes when it
    /// released, 5 when it was resurrected.
    /// </summary>
    public static int RealmPointWorthSeconds(bool released)
    {
        int resurrected = Math.Max(0, Properties.RP_WORTH_SECONDS);
        return released ? Math.Min(resurrected, Math.Max(0, Properties.RP_WORTH_SECONDS_RELEASED)) : resurrected;
    }

    /// <summary>A gamebot released to its bind point: the shorter wait applies to its next death.</summary>
    public static void NoteReleased(GameBot bot) => bot?.TempProperties?.SetProperty(ReleasedAfterDeathProperty, true);

    /// <summary>Realm points each realm level adds to what a player or gamebot is worth (1 before patch 1.81).</summary>
    public const int RealmPointsPerRealmLevel = 10;

    public static int GetPlayerEquivalentRealmPointValue(byte level, int realmLevel)
    {
        // The player formula since 1.81 (DR-174), shared with GamePlayer.RealmPointsValue: (level - 20)^2 for the
        // level, plus 10 points per realm level. 900 at level 50 RR1, 1,800 at level 50 RR10.
        int modifiedLevel = level - 20;
        return Math.Max(1, modifiedLevel * modifiedLevel) + Math.Max(0, realmLevel) * RealmPointsPerRealmLevel;
    }

    public static int CalculateRealmPointReward(int victimRealmPointValue, int victimRealmLevel,
        int awarderRealmPointValue, int awarderRealmLevel, int participantCount,
        int groupContributorCount, double damagePercent, bool applyRealmRankAdjustment)
    {
        if (victimRealmPointValue <= 0 || awarderRealmPointValue <= 0 ||
            participantCount <= 0 || damagePercent <= 0)
        {
            return 0;
        }

        int baseRealmPoints = victimRealmPointValue / participantCount;
        baseRealmPoints = Math.Min(baseRealmPoints, awarderRealmPointValue * 2);
        int realmPoints = ApplyKillBonus((int)(baseRealmPoints * Math.Min(1.0, damagePercent)));

        if (applyRealmRankAdjustment)
        {
            realmPoints = (int)(realmPoints *
                (1.0 + 2.0 * (victimRealmLevel - awarderRealmLevel) / 900.0));
        }

        if (groupContributorCount > 1)
            realmPoints += (int)(realmPoints * (groupContributorCount - 1) * 0.125);

        return Math.Max(0, realmPoints);
    }

    /// <summary>Bounty point value of a gamebot, from the same formula as GamePlayer.BountyPointsValue.</summary>
    public static int GetPlayerEquivalentBountyPointValue(byte level) => (int)(1 + level * 0.6);

    /// <summary>
    /// Bounty points for a share of a kill, as AbstractServerRules.AwardPlayerOnPlayerKill pays them:
    /// the victim's value split by participants, capped at twice the awarder's own value, then scaled
    /// by damage. Outpost (keep bonus) and battleground rules are applied by the caller.
    /// </summary>
    public static int CalculateBountyPointReward(int victimBountyPointValue, int awarderBountyPointValue,
        int participantCount, double damagePercent)
    {
        if (victimBountyPointValue <= 0 || awarderBountyPointValue <= 0 ||
            participantCount <= 0 || damagePercent <= 0)
        {
            return 0;
        }

        int baseBountyPoints = victimBountyPointValue / participantCount;
        baseBountyPoints = Math.Min(baseBountyPoints, awarderBountyPointValue * 2);
        return Math.Max(0, (int)(baseBountyPoints * Math.Min(1.0, damagePercent)));
    }

    /// <summary>Experience value of a gamebot: a level's living value times four, as GamePlayer.ExperienceValue.</summary>
    public static long GetPlayerEquivalentExperienceValue(long livingExperienceValue) => livingExperienceValue * 4;

    /// <summary>
    /// The share of a kill's experience before the outpost bonus, as AbstractServerRules.AwardPlayerOnPlayerKill
    /// pays it: the victim's value split by participants, capped at xp_pvp_cap_percent of the awarder's own value.
    /// The caller adds the bonus to <paramref name="damageShare"/>, which is the capped share scaled by damage.
    /// </summary>
    public static long CalculateExperienceReward(long victimExperienceValue, long awarderExperienceValue,
        int capPercent, int participantCount, double damagePercent, out long damageShare)
    {
        damageShare = 0;
        if (victimExperienceValue <= 0 || awarderExperienceValue <= 0 || participantCount <= 0 || damagePercent <= 0)
            return 0;

        long baseExperience = Math.Min(victimExperienceValue / participantCount, awarderExperienceValue * capPercent / 100);
        damageShare = (long)(baseExperience * Math.Min(1.0, damagePercent));
        return baseExperience;
    }

    private static int CalculateOutpostBountyBonus(GamePlayer player, int bountyPoints)
    {
        if (KeepBonusMgr.RealmHasBonus(eKeepBonusType.Bounty_Points_5, player.Realm))
            return (int)(bountyPoints / 100.0 * 5);
        if (KeepBonusMgr.RealmHasBonus(eKeepBonusType.Bounty_Points_3, player.Realm))
            return (int)(bountyPoints / 100.0 * 3);
        return 0;
    }

    private static readonly DOL.Logging.Logger RvrLog = DOL.Logging.LoggerManager.Create(typeof(AutonomousBotRealmPointRewards));

    /// <summary>
    /// RVR_BOT_KILLED: every gamebot killed by another realm (bot, player, pet or guard), with its siege event
    /// force and where it fell. Siege armies were losing half their strength on the march with no record of it
    /// (run 17, Caer Sursbrooke).
    /// </summary>
    private static void LogRvrDeath(GameBot victim, GameObject killer)
    {
        if (victim?.IsAutonomousWorldBot != true || killer is not GameLiving living || !RvrLog.IsInfoEnabled) return;
        GameLiving owner = ResolveRootRewardOwner(living) ?? living;
        if (owner.Realm == eRealm.None || (DOL.GS.ServerRules.HybridCoopRules.Enabled ? !DOL.GS.ServerRules.HybridCoopRules.RewardEnemies(owner, victim) : owner.Realm == victim.Realm)) return;
        string force = victim.TempProperties.GetProperty<string>("RvrEventForce") ?? "";
        RvrLog.Info($"RVR_BOT_KILLED victim={victim.Name} id={victim.DatabaseID} realm={GlobalConstants.RealmToName(victim.Realm)} " +
                    $"goal={AutonomousObjectiveAssignments.KindFor(victim)} force={force} killer=\"{owner.Name}\" killerRealm={GlobalConstants.RealmToName(owner.Realm)} " +
                    $"killerType={owner.GetType().Name} region={victim.CurrentRegionID} zone=\"{victim.CurrentZone?.Description}\" " +
                    $"at={victim.X},{victim.Y},{victim.Z}");
    }

    /// <summary>
    /// RVR_PLAYER_REWARD: what a real player got for a gamebot kill, and why it was nothing when it was. Discord report
    /// 2026-11-05 (Abermenai): "no RPs or XP from any kill", and no log could say which rule stopped it.
    /// </summary>
    private static void LogPlayerReward(GameBot victim, GamePlayer player, string outcome, int realmPoints, int bountyPoints,
        long experience, bool worth, double damagePercent, string reason)
    {
        if (!RvrLog.IsInfoEnabled) return;
        RvrLog.Info($"RVR_PLAYER_REWARD player={player.Name} level={player.Level} victim={victim.Name} victimLevel={victim.Level} " +
                    $"outcome={outcome} rp={realmPoints} bp={bountyPoints} xp={experience} worth={worth} " +
                    $"damage={damagePercent * 100:0.#}% region={victim.CurrentRegionID}" + (reason == null ? "" : $" reason=\"{reason}\""));
    }

    public static void Award(GameBot killedBot, GameObject killer)
    {
        LogRvrDeath(killedBot, killer);
        if (!IsEligibleVictim(killedBot))
            return;

        long now = GameLoop.GameLoopTime;
        long previousDeath = killedBot.TempProperties.GetProperty<long>(
            LastRealmPointDeathTickProperty, -1);
        bool releasedSincePreviousDeath = killedBot.TempProperties.GetProperty(ReleasedAfterDeathProperty, false);
        long worthInterval = RealmPointWorthSeconds(releasedSincePreviousDeath) * 1000L;
        bool isWorthRealmPoints = previousDeath < 0 || now - previousDeath >= worthInterval;
        killedBot.TempProperties.SetProperty(ReleasedAfterDeathProperty, false);

        // Every death resets the same repeat-kill window used for a real player,
        // including a death caused by an NPC or by somebody who receives no RP.
        killedBot.TempProperties.SetProperty(LastRealmPointDeathTickProperty, now);

        KeyValuePair<GameLiving, double>[] rawContributors;
        lock (killedBot.XpGainersLock)
            rawContributors = killedBot.XPGainers.ToArray();

        Dictionary<GameLiving, double> hostileContributors = new();
        foreach (KeyValuePair<GameLiving, double> pair in rawContributors)
        {
            GameLiving credited = ResolveRootRewardOwner(pair.Key);
            if (credited == null || credited.Realm == eRealm.None || (DOL.GS.ServerRules.HybridCoopRules.Enabled ? !DOL.GS.ServerRules.HybridCoopRules.RewardEnemies(credited, killedBot) : credited.Realm == killedBot.Realm))
                continue;

            hostileContributors[credited] = hostileContributors.TryGetValue(credited, out double existing)
                ? existing + pair.Value
                : pair.Value;
        }

        // A hostile player fighting this bot with no damage on record (a full heal clears the list) gets nothing at all.
        if (RvrLog.IsInfoEnabled)
            foreach (GamePlayer nearby in killedBot.GetPlayersInRadius(WorldMgr.MAX_EXPFORKILL_DISTANCE))
                if (nearby.Realm != killedBot.Realm && nearby.TargetObject == killedBot && !hostileContributors.ContainsKey(nearby))
                    LogPlayerReward(killedBot, nearby, "uncredited", 0, 0, 0, isWorthRealmPoints, 0, "no damage on record");

        double totalDamage = hostileContributors.Sum(pair => pair.Value);
        if (totalDamage <= 0)
            return;

        int victimRealmPointValue = GetPlayerEquivalentRealmPointValue(killedBot.Level, killedBot.RealmLevel);
        if (isWorthRealmPoints)
            PayGamebots(killedBot, victimRealmPointValue, killedBot.RealmLevel, hostileContributors, totalDamage);

        Dictionary<GamePlayer, EntityCountTotalDamagePair> playerContributions = new();
        Dictionary<Group, EntityCountTotalDamagePair> groupContributions = new();

        foreach (KeyValuePair<GameLiving, double> pair in hostileContributors)
        {
            // Persistent gamebots remain part of the damage denominator, just
            // like another real participant, but this path only pays connected
            // players. Temporary companions have already resolved to the owner.
            // A gamebot in a player's group adds its damage and itself to that group's share, as for a monster kill
            // (ProcessXpGainers). Discord report 2026-11-05 (Abermenai): a player grouped with gamebots got nothing for
            // the kills the bots made, because the group's share counted players only.
            if (pair.Key is GameBot { Group: { } botGroup } groupBot && botGroup.GetPlayersInTheGroup().Count > 0)
            {
                if (groupBot.ObjectState is GameObject.eObjectState.Active &&
                    groupBot.IsWithinRadius(killedBot, WorldMgr.MAX_EXPFORKILL_DISTANCE))
                {
                    AddContribution(groupBot, pair.Value, botGroup, groupContributions);
                }
                continue;
            }
            if (pair.Key is not GamePlayer player)
                continue;
            if (player.ObjectState is not GameObject.eObjectState.Active ||
                !player.IsWithinRadius(killedBot, WorldMgr.MAX_EXPFORKILL_DISTANCE))
            {
                LogPlayerReward(killedBot, player, "skipped", 0, 0, 0, isWorthRealmPoints, pair.Value / totalDamage,
                    player.ObjectState is not GameObject.eObjectState.Active ? "not in the world" : "out of reward range");
                continue;
            }

            AddContribution(player, pair.Value, player, playerContributions);
            if (player.Group != null)
                AddContribution(player, pair.Value, player.Group, groupContributions);
        }

        if (playerContributions.Count == 0)
            return;

        GameLiving creditedKiller = ResolveRootRewardOwner(killer as GameLiving);
        int victimValue = GetPlayerEquivalentRealmPointValue(killedBot.Level, killedBot.RealmLevel);

        foreach (KeyValuePair<GamePlayer, EntityCountTotalDamagePair> pair in playerContributions)
        {
            GamePlayer player = pair.Key;
            lock (player.AwardLock)
            {
                EntityCountTotalDamagePair contribution = pair.Value;
                if (player.Group != null && groupContributions.TryGetValue(player.Group, out EntityCountTotalDamagePair group))
                    contribution = group;

                double damagePercent = Math.Min(1.0, contribution.Damage / totalDamage);
                int contributorCount = Math.Max(1, contribution.Count);
                int groupContributorCount = player.Group == null ? 1 : contributorCount;
                int realmPointsEarned = 0;
                int bountyPointsPaid = 0;
                long experiencePaid = 0;

                if (isWorthRealmPoints)
                {
                    DbBattleground battleground = GameServer.KeepManager.GetBattleground(player.CurrentRegionID);
                    bool applyRankAdjustment = battleground == null || player.RealmLevel < battleground.MaxRealmLevel;
                    realmPointsEarned = CalculateRealmPointReward(victimValue, killedBot.RealmLevel,
                        player.RealmPointsValue, player.RealmLevel, contributorCount,
                        groupContributorCount, damagePercent, applyRankAdjustment);

                    realmPointsEarned = RvrRealmPointRules.OnKillRealmPoints(player, realmPointsEarned); // DR-181, DR-182

                    if (realmPointsEarned > 0)
                        player.GainRealmPoints(realmPointsEarned, true);

                    // Bounty points, as for killing a real player (same split, cap, outpost bonus and battleground rule).
                    bool bountyAllowedHere = Properties.ALLOW_BPS_IN_BGS || killedBot.CurrentZone?.IsBG != true;
                    if (bountyAllowedHere)
                    {
                        int bountyPointsEarned = CalculateBountyPointReward(
                            GetPlayerEquivalentBountyPointValue(killedBot.Level), player.BountyPointsValue,
                            contributorCount, damagePercent);
                        bountyPointsEarned += CalculateOutpostBountyBonus(player, bountyPointsEarned);

                        if (bountyPointsEarned > 0)
                            player.GainBountyPoints(bountyPointsEarned);
                        bountyPointsPaid = bountyPointsEarned;
                    }

                    // Experience, as for killing a real player: at level 50 this feeds Master Levels, below it levels.
                    long baseExperience = CalculateExperienceReward(
                        GetPlayerEquivalentExperienceValue(killedBot.ExperienceValue), player.ExperienceValue,
                        Properties.XP_PVP_CAP_PERCENT, contributorCount, damagePercent, out long experience);
                    experience += AbstractServerRules.CalculateOutpostExperienceBonus(player, baseExperience);

                    if (experience > 0)
                        player.GainExperience(eXPSource.Player, experience);
                    experiencePaid = experience;
                }
                else
                {
                    string name = AutonomousNameMask.NameFor(player, killedBot, 0, true);
                    player.Out.SendMessage($"{name} has been killed recently and is worth no realm points!",
                        eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                    player.Out.SendMessage($"{name} has been killed recently and is worth no experience!",
                        eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                }

                bool deathBlow = ReferenceEquals(player, creditedKiller);
                bool soloKill = damagePercent >= 1.0 && contributorCount == 1;
                player.UpdateKillStatsOnPlayerKill(killedBot.Realm, deathBlow, soloKill, realmPointsEarned);
                BotTesterLog.PvpReward(killedBot, player, realmPointsEarned, bountyPointsPaid, experiencePaid, isWorthRealmPoints, damagePercent);
                LogPlayerReward(killedBot, player, "paid", realmPointsEarned, bountyPointsPaid, experiencePaid, isWorthRealmPoints,
                    damagePercent, !isWorthRealmPoints ? "killed recently" : realmPointsEarned == 0 ? "realm point share rounds to 0" : null);
            }
        }
    }

    /// <summary>
    /// Gamebots earn realm points for PvP kills like players: a share of the victim's value by
    /// their part of the damage, with the same group bonus. Only persistent gamebots and account
    /// companions of a hostile realm within reward range (pets resolve to their bot). /spawn helpers earn nothing.
    /// </summary>
    public static void PayGamebots(GameLiving victim, int victimRealmPointValue, int victimRealmLevel,
        IReadOnlyDictionary<GameLiving, double> hostileContributors, double totalDamage)
    {
        if (victim == null || victimRealmPointValue <= 0 || totalDamage <= 0) return;
        var inRange = hostileContributors
            .Where(pair => pair.Key != null && pair.Key.ObjectState == GameObject.eObjectState.Active &&
                pair.Key.IsWithinRadius(victim, WorldMgr.MAX_EXPFORKILL_DISTANCE))
            .ToArray();
        var bots = inRange
            .Where(pair => pair.Key is GameBot bot && EarnsRealmPoints(bot) &&
                bot.Realm != eRealm.None && (DOL.GS.ServerRules.HybridCoopRules.Enabled ? DOL.GS.ServerRules.HybridCoopRules.RewardEnemies(bot, victim) : bot.Realm != victim.Realm))
            .Select(pair => (Bot: (GameBot)pair.Key, Damage: pair.Value)).ToArray();
        foreach (var (bot, damage) in bots)
        {
            int groupCount;
            double groupDamage;
            if (bot.Group == null)
            {
                groupCount = 1;
                groupDamage = damage;
            }
            else if (bot.IsAccountCompanion)
            {
                // An account companion takes its group's split like a player in that group: every contributor in the
                // group (players, gamebots, companions) counts, as Award pays the players. Owner 2026-11-09:
                // companions earned no realm points beside their player.
                var group = inRange.Where(other => other.Key.Group == bot.Group).ToArray();
                groupCount = Math.Max(1, group.Length);
                groupDamage = group.Sum(other => other.Value);
            }
            else
            {
                groupCount = Math.Max(1, bots.Count(other => other.Bot.Group == bot.Group));
                groupDamage = bots.Where(other => other.Bot.Group == bot.Group).Sum(other => other.Damage);
            }
            double damagePercent = Math.Min(1.0, groupDamage / totalDamage);
            int points = CalculateRealmPointReward(victimRealmPointValue, victimRealmLevel,
                GetPlayerEquivalentRealmPointValue(bot.Level, bot.RealmLevel), bot.RealmLevel,
                groupCount, groupCount, damagePercent, true);
            points = RvrRealmPointRules.OnKillRealmPoints(bot, points); // DR-181, DR-182
            if (points > 0) bot.GainRealmPoints(points, true);

            // Bounty points as a player gets them (same split, cap and battleground rule); gamebots keep none.
            if (bot.IsAccountCompanion && (Properties.ALLOW_BPS_IN_BGS || victim.CurrentZone?.IsBG != true))
            {
                int bountyPoints = CalculateBountyPointReward(GetPlayerEquivalentBountyPointValue(victim.Level),
                    GetPlayerEquivalentBountyPointValue(bot.Level), groupCount, damagePercent);
                if (bountyPoints > 0) bot.GainBountyPoints(bountyPoints);
            }
        }
    }

    /// <summary>Persistent gamebots and account companions earn realm points; /spawn helpers do not.</summary>
    public static bool EarnsRealmPoints(GameBot bot) =>
        bot is { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false } or { IsAccountCompanion: true };

    /// <summary>A player killed in PvP pays the gamebots that fought them (players are paid by the rules).</summary>
    public static void PayGamebotsForPlayerKill(GamePlayer killedPlayer)
    {
        if (killedPlayer == null) return;
        KeyValuePair<GameLiving, double>[] raw;
        lock (killedPlayer.XpGainersLock)
            raw = killedPlayer.XPGainers.ToArray();
        var hostile = new Dictionary<GameLiving, double>();
        foreach (var pair in raw)
        {
            GameLiving credited = ResolveRootRewardOwner(pair.Key);
            if (credited == null || credited.Realm == eRealm.None || (DOL.GS.ServerRules.HybridCoopRules.Enabled ? !DOL.GS.ServerRules.HybridCoopRules.RewardEnemies(credited, killedPlayer) : credited.Realm == killedPlayer.Realm)) continue;
            hostile[credited] = hostile.GetValueOrDefault(credited) + pair.Value;
        }
        PayGamebots(killedPlayer, killedPlayer.RealmPointsValue, killedPlayer.RealmLevel, hostile, hostile.Sum(pair => pair.Value));
    }

    /// <summary>Realm level for a realm point total, from the player table (RR1L0 = 0 ... RR5L3 = 43).</summary>
    public static int RealmLevelFor(long realmPoints)
    {
        if (realmPoints <= 0) return 0;
        long[] table = GamePlayer.REALMPOINTS_FOR_LEVEL;
        for (int level = table.Length - 1; level > 0; level--)
            if (table[level] <= realmPoints) return level;
        return 0;
    }

    public static GameLiving ResolveRootRewardOwner(GameLiving source)
    {
        GameLiving current = source;
        for (int depth = 0; depth < 16 && current is GameNPC npc &&
             npc.Brain is IControlledBrain controlled &&
             controlled.GetLivingOwner() is GameLiving owner; depth++)
        {
            current = owner;
        }

        return current;
    }

    private static void AddContribution<T>(GameLiving participant, double damage, T entity,
        Dictionary<T, EntityCountTotalDamagePair> contributions) where T : class, IGameStaticItemOwner
    {
        if (contributions.TryGetValue(entity, out EntityCountTotalDamagePair value))
        {
            value.Count++;
            value.Damage += damage;
            if (value.HighestLevelPlayer.Level < participant.Level)
                value.HighestLevelPlayer = participant;
        }
        else
        {
            contributions[entity] = new EntityCountTotalDamagePair(1, damage, participant);
        }
    }
}
