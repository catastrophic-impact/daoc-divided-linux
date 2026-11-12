using System;
using System.Numerics;
using DOL.Database;

namespace DOL.GS;

/// <summary>
/// Pure policy seams for Darkness Falls. Live entrance authority remains in
/// DFEnterJumpPoint; these helpers make its keep-control and local-PvP rules
/// independently testable.
/// </summary>
public enum eDarknessFallsGoal
{
    None,
    Pve,
    Pvp,
}

public static class AutonomousDarknessFallsPolicy
{
    public const ushort RegionId = 249;

    // Solo bots grind Darkness Falls from level 20 (owner 2026-11-01), only against
    // targets that con blue or easier. Groups keep their own rules.
    public const int SoloMinimumLevel = 20;
    public const ConColor SoloMaximumCon = ConColor.BLUE;

    // Each new camp pick from level 20 can become a Darkness Falls goal, solo or
    // grouped, while the dungeon has room. The usual dungeon draw stays as well.
    public const int PveGoalPercent = 15;
    public const int PvpGoalPercent = 15;
    public const int GoalPopulationLimit = 150;

    // The wings only join deep inside. Of the 1,367 proved spawns, rooms below monster
    // level 40 are at least 13,400 units further from the second entrance than the
    // first, and every route into another wing passes aggressive level 57-65 monsters
    // (2026-11-01). Lower levels stay on their own side, as on live (owner 2026-11-01).
    public const int PvpMinimumLevel = 45;
    public const float MeetingRoomGap = 7_500;
    public const int MeetingRoomChoices = 6;

    // Nearly every meeting room holds level 48+ monsters, above a solo bot's blue limit.
    // First private copy run, 2026-11-01: no solo bot took a PvP goal.
    // Playtest 2026-11-07: 98 deaths on solo "Hunt enemies" goals with orange allowed; yellow until the DF route scan is in.
    public const ConColor SoloPvpMaximumCon = ConColor.YELLOW;

    // Night playtest 2026-11-04: a level 17-23 party was wiped in DF at a level 33 demoniac familiar camp. A party's camp
    // band is measured from its average (up to 10 above it) and only checked against the highest member, so a room could be
    // 16 levels above its lowest. DF stays open to parties from an average of 20; a room more than 10 levels above the
    // lowest member (the top of the ordinary party band) is not.
    public const int PartyLevelsAboveLowestMember = 10;

    /// <summary>A party may take a DF room when its average is 20+ and the room's lowest monster is at most 10 levels above its lowest member. Pure.</summary>
    public static bool PartyFitsCamp(int averageLevel, int lowestMemberLevel, int campLowestLevel) =>
        averageLevel >= SoloMinimumLevel && campLowestLevel - lowestMemberLevel <= PartyLevelsAboveLowestMember;

    /// <summary>roll is 0-99. A full dungeon or a level below 20 keeps the ordinary camp pick.</summary>
    public static eDarknessFallsGoal RollGoal(int level, int population, int roll)
    {
        if (level < SoloMinimumLevel || population >= GoalPopulationLimit)
            return eDarknessFallsGoal.None;
        if (roll < PveGoalPercent)
            return eDarknessFallsGoal.Pve;
        return level >= PvpMinimumLevel && roll < PveGoalPercent + PvpGoalPercent
            ? eDarknessFallsGoal.Pvp : eDarknessFallsGoal.None;
    }

    /// <summary>How much further a room is from the second closest realm entrance than from the closest.</summary>
    public static float EntranceGap(float albion, float midgard, float hibernia)
    {
        float nearest = Math.Min(albion, Math.Min(midgard, hibernia));
        float farthest = Math.Max(albion, Math.Max(midgard, hibernia));
        float second = albion + midgard + hibernia - nearest - farthest;
        return second - nearest;
    }

    public static bool IsMeetingRoom(float albion, float midgard, float hibernia)
    {
        float gap = EntranceGap(albion, midgard, hibernia);
        return float.IsFinite(gap) && gap <= MeetingRoomGap;
    }

    public static ushort HomeRegion(eRealm realm) => realm switch
    {
        eRealm.Albion => 1,
        eRealm.Midgard => 100,
        eRealm.Hibernia => 200,
        _ => 0,
    };

    // Normal home-side entrances. Other real DF entries (including relic
    // portals) remain available to players, but autonomous bots must not
    // choose them solely because they are closer in a straight line.
    public static ushort HomeEntranceZonePointId(eRealm realm) => realm switch
    {
        eRealm.Albion => 81,
        eRealm.Midgard => 84,
        eRealm.Hibernia => 87,
        _ => 0,
    };

    // Each physical DF exit has a DB row for every realm. Matching the row's
    // Realm and TargetRegion alone would still let a bot leave via another
    // faction's corridor. These are the exits below each home entrance.
    public static ushort HomeExitZonePointId(eRealm realm) => realm switch
    {
        eRealm.Albion => 74,
        eRealm.Midgard => 70,
        eRealm.Hibernia => 72,
        _ => 0,
    };

    public static bool CanEnter(
        eRealm realm,
        eRealm currentOwner,
        eRealm previousOwner,
        long nowTick,
        long lastSwapTick,
        long gracePeriod,
        bool allowAllRealms,
        bool normalServer)
    {
        if (!normalServer || allowAllRealms)
            return true;
        if (realm == eRealm.None)
            return false;
        if (realm == previousOwner && lastSwapTick + Math.Max(0, gracePeriod) >= nowTick)
            return true;
        return realm == currentOwner;
    }

    public static bool CanUseRegionEdge(eRealm realm, ushort sourceRegion, ushort targetRegion, Func<eRealm, bool> canEnter)
    {
        if (sourceRegion == RegionId && targetRegion != RegionId)
            return HomeRegion(realm) != 0 && targetRegion == HomeRegion(realm);
        if (targetRegion != RegionId || sourceRegion == RegionId)
            return true;
        // Autonomous travel enters through its own realm's home-side tunnel.
        // Relic-keep portal rows for other realms are valid player content,
        // but must not become a shortcut to another realm's DF wing for bots.
        ushort homeRegion = HomeRegion(realm);
        return homeRegion != 0 && sourceRegion == homeRegion && canEnter?.Invoke(realm) == true;
    }

    /// <summary>Autonomous bots use only their normal home entrance and the
    /// physical exit below it. Hibernia's normal entrance is a realm-neutral
    /// DB row, so its exact ID and home source make that exception safe.
    /// Player portal behavior and the database are not changed.</summary>
    public static bool CanUsePortalRow(eRealm realm, DbZonePoint point)
    {
        if (point == null) return false;
        if (point.TargetRegion == RegionId && point.SourceRegion != RegionId)
            return HomeRegion(realm) != 0 && point.SourceRegion == HomeRegion(realm) &&
                point.Id == HomeEntranceZonePointId(realm) &&
                (realm == eRealm.Hibernia ? point.Realm == 0 : point.Realm == (ushort)realm);
        if (point.SourceRegion != RegionId || point.TargetRegion == RegionId)
            return true;
        return HomeRegion(realm) != 0 && point.TargetRegion == HomeRegion(realm) &&
            point.Realm == (ushort)realm && point.Id == HomeExitZonePointId(realm);
    }

    /// <summary>The installed outdoor itinerary reverses across this exact
    /// Lough Derg/Valley seam while heading for Hibernia's home DF entrance.
    /// A solo bot without a connected real ticket should replan here rather
    /// than oscillate until the forty-five-minute watchdog expires.</summary>
    public static bool IsAuditedHiberniaExteriorLoop(eRealm realm, DbZonePoint crossing,
        ushort currentRegion, ushort currentZone, Vector3 position, int groupSize) =>
        groupSize <= 1 && realm == eRealm.Hibernia &&
        crossing?.Id == HomeEntranceZonePointId(eRealm.Hibernia) &&
        CanUsePortalRow(realm, crossing) && currentRegion == HomeRegion(realm) &&
        currentZone == 206 &&
        Vector3.DistanceSquared(position, new Vector3(385088, 483424, 7262)) <= 192 * 192;

    /// <summary>An already-inside bot may still select its own physical exit
    /// after ordinary DF goals are closed by a stale or absent certificate.
    /// This never admits a new entrance or another faction's exit.</summary>
    public static bool CanEvacuateThroughHomeExit(eRealm realm, ushort currentRegion, DbZonePoint point) =>
        currentRegion == RegionId && point?.SourceRegion == RegionId &&
        point.TargetRegion != RegionId && CanUsePortalRow(realm, point);

    public static bool MustRetireOrdinaryGoal(ushort goalRegion, bool ordinaryCatalogReady) =>
        goalRegion == RegionId && !ordinaryCatalogReady;

    public static bool CanEngageLocalOpponent(
        eRealm attackerRealm,
        eRealm targetRealm,
        ushort attackerRegion,
        ushort targetRegion,
        bool targetAlive,
        bool allowedByServerRules) =>
        attackerRegion == RegionId && targetRegion == RegionId && targetAlive && allowedByServerRules &&
        attackerRealm != eRealm.None && targetRealm != eRealm.None && attackerRealm != targetRealm;
}
