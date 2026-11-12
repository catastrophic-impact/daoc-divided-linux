using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS
{
    public sealed partial class AutonomousWorldBotController
    {
        private long _nextRouteThreatScanTick;
        // The previous scan, for the ROUTE_THREAT line: on 2026-11-02 406 of 907 low level red and purple sightings came with
        // the bot already inside the monster's aggro range, after minutes of walking.
        private long _lastRouteScanTick;
        private Vector2 _lastRouteScanAt;
        private float _lastRouteScanLength;
        // A solo bot that died recently treats a yellow on its route like an orange (AutonomousRouteThreatPolicy.Decide).
        private long _routeCautionUntilTick;
        public const long RouteCautionAfterDeathMilliseconds = 30 * 60_000;
        private long _lastRouteThreatRetargetTick = long.MinValue / 2;
        private readonly Dictionary<GameNPC, int> _routeThreatDetours = new();
        // Monsters next to the route that cannot be walked to (a ledge above the road): they
        // cannot reach the route either, so they are passed by instead of pulled. Run 8: route
        // pulls on such monsters left bots standing until the 15-minute watchdog.
        private readonly HashSet<GameNPC> _unreachableRouteThreats = new();
        // The last route pull: the same monster found again, still out of combat, means the pull
        // never landed. After three it is passed by like an unreachable one.
        private GameNPC _lastRouteThreatPull;

        /// <summary>Forgets a route pull whose monster has left the world (see <see cref="GameLiving.ForgetDeparted"/>).</summary>
        public void ForgetDeparted()
        {
            if (GameLiving.HasDeparted(_lastRouteThreatPull))
                _lastRouteThreatPull = null;
            _unreachableRouteThreats.RemoveWhere(GameLiving.HasDeparted);
        }
        private int _routeThreatPullRepeats;
        private long _lastRouteThreatPullTick;
        private const int RouteThreatPullAttempts = 3;

        /// <summary>Turn-backs from one monster on a keep march before the bot walks on past it.</summary>
        public const int RvrTravelTurnBacksBeforeWalkingOn = 2;
        private GameNPC _rvrTurnBackFrom;
        private int _rvrTurnBacks;
        private long _rvrLastTurnBackTick;

        /// <summary>Counts this turn-back from <paramref name="blocker"/> (a gap over two minutes starts over).</summary>
        private int RvrTravelTurnBacks(GameNPC blocker, long now)
        {
            _rvrTurnBacks = blocker == _rvrTurnBackFrom && now - _rvrLastTurnBackTick < 120_000 ? _rvrTurnBacks + 1 : 1;
            _rvrTurnBackFrom = blocker;
            _rvrLastTurnBackTick = now;
            return _rvrTurnBacks;
        }
        // A party holding in front of a route monster it may not pull yet. Run 17 (2026-10-17): 12,472 of 13,547 level 50
        // party route-threat decisions were the same hold repeated (114 loops in 78 parties, one for 98 minutes): only
        // the leader looks ahead, only the party's puller may open a pull, and a leader that was not the puller held
        // forever. Now the puller takes the pull, and a hold ends after RouteThreatHoldLimitMilliseconds.
        private GameNPC _routeThreatHold;
        private long _routeThreatHoldSince;
        public const long RouteThreatHoldLimitMilliseconds = 75_000;

        /// <summary>
        /// Open-world route threat awareness (see AutonomousRouteThreatPolicy). Called right before
        /// a travelling bot issues its walk order. True consumes this travel turn (a pull started,
        /// the bot is holding, or the camp was given up); false lets the walk continue, with a
        /// detour waypoint set when the bot is going around a monster.
        /// </summary>
        private bool GuardOpenWorldTravel(GameBot bot, Vector3 destination) =>
            GuardRoute(bot, destination, alone: false, out _);

        /// <summary>When a lone walk last turned back from a monster; the walk holds until then.</summary>
        private long _loneWalkHoldUntilTick;
        public const long LoneWalkHoldMilliseconds = 20_000;

        /// <summary>
        /// The route scan for a walk a bot makes on its own, even inside a party: to a stablemaster's horse. Only camp travel
        /// and zone-seam walks were scanned, and on a 2026-11-02 private copy run "Approaching x's horse" was behind 24 of
        /// 136 low level deaths to red and purple monsters before camp (none once scanned). The walk has no camp to give up:
        /// where a solo bot would, it backs out of the monster's reach instead (<paramref name="turnedBack"/>) and holds for
        /// <see cref="LoneWalkHoldMilliseconds"/>. The walk to a party's meeting point is not scanned: see HandleGroupRendezvous.
        /// </summary>
        private bool GuardLoneWalk(GameBot bot, Vector3 destination, out bool turnedBack) =>
            GuardRoute(bot, destination, alone: true, out turnedBack);

        /// <summary>
        /// The walk to a party's meeting point: bends around monsters far above the bot and sidesteps one on the way,
        /// but never stops or turns back (a member that held for the way to clear died waiting or fell behind, see
        /// HandleGroupRendezvous). On 2026-11-02 private copy runs this walk was behind 52 and 53 low level deaths in
        /// 14 minutes: members of one party walked one after another from Cotswold Village into the same Camelot Hills
        /// bandit camp, released there and walked into it again.
        /// </summary>
        private bool GuardMeetupWalk(GameBot bot, Vector3 destination) =>
            GuardRoute(bot, destination, alone: true, out _, mayTurnBack: false);

        /// <summary>
        /// A frontier march to a keep (the RvR objective's own travel), scanned for PvE monsters like a lone walk: it goes
        /// around one, pulls one it can take, or backs out of the reach of one it cannot and holds. Night 2026-11-04: route
        /// threat scanning was off for every bot on an RvR objective, and levels 40-49 on frontier tours walked into purple
        /// PvE monsters. Enemy players and bots are never route threats (IsExperienceMonster).
        /// </summary>
        private bool GuardRvrTravel(GameBot bot, Vector3 destination) =>
            GuardRoute(bot, destination, alone: true, out _, rvrTravel: true);

        private bool GuardRoute(GameBot bot, Vector3 destination, bool alone, out bool turnedBack, bool mayTurnBack = true,
            bool rvrTravel = false)
        {
            using var aiProfile = AiProfile.Scope(AiSystem.ControllerRouteThreats); // AI_PROFILE: measures only
            turnedBack = false;
            if (!AutonomousRouteThreatPolicy.Enabled || bot.Brain is not BotBrain brain ||
                bot.CurrentZone == null || bot.CurrentZone.IsDungeon || bot.CurrentRegion?.IsDungeon == true ||
                bot.IsOnStableMasterRoute || bot.IsPlayerLedGroup || brain.HasAggro || bot.InCombat || bot.IsAttacking ||
                !AutonomousRouteThreatPolicy.ScansFor(AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR), rvrTravel) ||
                AutonomousRealmRaid.GetView(bot.Group) != null)
                return false;
            bool grouped = !alone && _groupDirective?.IsDynamic == true;
            if (grouped && _groupDirective.Leader != bot)
                return false;
            long now = GameLoop.GameLoopTime;
            // Still backing off from the last monster: the turn is spent, the reaction was already taken.
            if (alone && mayTurnBack && now < _loneWalkHoldUntilTick)
                return true;
            // A solo bot hurt in a fight on the road, or about to open a route pull without the health, power or
            // endurance for it, rests where it stands first (AutonomousSoloFightReadiness).
            if (!grouped && mayTurnBack && bot.Group == null && HoldRoadRest(bot, now))
                return true;
            if (now < _nextRouteThreatScanTick)
                return false;
            // Keep looking while walking a detour: the way to the sidestep is scanned too, and a
            // new monster there is fought or makes the bot give up (no detour inside a detour).
            bool detouring = _routeRecoveryWaypoint.HasValue;
            Vector3 scanTarget = _routeRecoveryWaypoint ?? destination;
            _nextRouteThreatScanTick = now + AutonomousRouteThreatPolicy.ScanMilliseconds + bot.ObjectID % 500;

            IPathfindingMgr nav = PathfindingProvider.Instance;
            Zone zone = bot.CurrentZone;
            if (!nav.IsAvailable || !nav.HasNavmesh(zone))
                return false;

            Span<Vector3> route = stackalloc Vector3[257];
            route[0] = new(bot.X, bot.Y, bot.Z);
            int count = bot.movementComponent.CopyUpcomingPath(scanTarget, route[1..]) + 1;
            string pathSource = "target";
            // The path the bot walks (to an approach point, a seam or a sidestep) when it was not planned to scanTarget.
            if (count < 2 && bot.IsMoving)
            {
                count = bot.movementComponent.CopyActivePath(route[1..]) + 1;
                pathSource = "active";
            }
            if (count < 2)
                pathSource = "straight";
            if (count < 2)
            {
                // Before the first path exists: the straight leg toward the target.
                route[1] = scanTarget;
                count = 2;
            }
            ReadOnlySpan<Vector3> corridor = route[..count];
            long previousScanTick = _lastRouteScanTick;
            Vector2 previousScanAt = _lastRouteScanAt;
            float previousScanLength = _lastRouteScanLength;
            _lastRouteScanTick = now;
            _lastRouteScanAt = new(bot.X, bot.Y);
            _lastRouteScanLength = 0;
            for (int i = 1; i < count && _lastRouteScanLength < AutonomousRouteThreatPolicy.LookAhead; i++)
                _lastRouteScanLength += Vector3.Distance(route[i - 1], route[i]);

            // A solo bot bends around monsters far above it, remembered death spots and its own repeat-death spots
            // before the one-monster rules below (stefanrows' route bends; AutonomousPveDangerBypass). Level 50s too
            // since 2026-10-20: no level-50 bot ever bent a route, and two died 72 and 61 times to one glimmer pack.
            if (!grouped && !detouring && TryDangerBypass(bot, nav, zone, corridor))
                return false;

            eCharacterClass characterClass = (eCharacterClass)(bot.CharacterClass?.ID ?? 0);
            bool ranged = BotSpellPower.IsOffensiveCaster(bot) ||
                          characterClass is eCharacterClass.Scout or eCharacterClass.Hunter or eCharacterClass.Ranger;
            int margin = ranged ? AutonomousRouteThreatPolicy.RangedMargin : AutonomousRouteThreatPolicy.MeleeMargin;

            GameNPC[] threats = bot.GetNPCsInRadius((ushort)AutonomousRouteThreatPolicy.ScanRadius)
                .Where(npc => IsExperienceMonster(npc) && npc.IsAlive &&
                              (!npc.InCombat || AutonomousRouteThreatPolicy.WatchesBusyMonster(ConLevels.GetConColor(bot.GetConLevel(npc)))) &&
                              npc.Brain is StandardMobBrain mob && mob.AggroRange > 0 && mob.CanAggroTarget(bot))
                .OrderBy(npc => bot.GetDistanceTo(npc)).Take(24).ToArray();
            GameNPC blocker = null;
            float first = float.MaxValue;
            foreach (GameNPC npc in threats)
            {
                var mob = (StandardMobBrain)npc.Brain;
                Vector3 position = new(npc.X, npc.Y, npc.Z);
                if (Vector2.Distance(new(position.X, position.Y), new(destination.X, destination.Y)) <=
                    AutonomousRouteThreatPolicy.CampSkipRadius)
                    continue;
                // The walk's destination is often an approach point or a sidestep, not the camp: the camp's own monsters
                // and the camp's own species at yellow or lower are the work, not a reason to give the camp up
                // (playtest 2026-11-07: 578 give-ups were to the bot's own target).
                if (_camp != null && _camp.RegionId == bot.CurrentRegionID &&
                    (Vector2.Distance(new(position.X, position.Y), new(_camp.X, _camp.Y)) <= AutonomousRouteThreatPolicy.CampSkipRadius ||
                     AutonomousRouteThreatPolicy.IsCampWork(npc.Name, ConLevels.GetConColor(bot.GetConLevel(npc)), _camp.MonsterName)))
                    continue;
                // The monster this detour is already going around.
                if (detouring && _routeThreatDetours.ContainsKey(npc))
                    continue;
                if (_unreachableRouteThreats.Contains(npc))
                    continue;
                if (AutonomousDungeonPolicy.IntersectsCorridor(corridor, position, mob.AggroRange + margin,
                        AutonomousRouteThreatPolicy.LookAhead, out float along) && along < first)
                {
                    blocker = npc;
                    first = along;
                }
            }
            if (blocker == null)
            {
                PruneRouteThreatDetours();
                return false;
            }

            var blockerBrain = (StandardMobBrain)blocker.Brain;
            Vector3 blockerPosition = new(blocker.X, blocker.Y, blocker.Z);
            GameNPC[] packMembers = threats.Where(other =>
                other == blocker || Math.Abs(other.Z - blocker.Z) <= 160 && other.Brain is StandardMobBrain otherBrain &&
                other.GetDistanceTo(blocker) < Math.Max(320, blockerBrain.AggroRange + otherBrain.AggroRange + 80)).ToArray();
            int pack = packMembers.Length;
            ConColor seen = ConLevels.GetConColor(bot.GetConLevel(blocker));
            ConColor con = AutonomousMobLessons.Felt(seen, blocker.Name, grouped);
            bool bossLike = blocker is IGameEpicNpc ||
                            AutonomousPveTargetPolicy.IsUnreachableFlyer(blocker.Flags, blocker.Z, bot.Z) && !ranged;

            Vector3? detour = detouring ? null : FindRouteThreatDetour(nav, zone, corridor, blocker,
                blockerBrain.AggroRange, margin, destination, threats);
            int detours = _routeThreatDetours.GetValueOrDefault(blocker);
            // A lone walk turns back instead of giving up a camp, so no cooldown applies to it.
            bool mayRetarget = alone || _camp != null &&
                               now - _lastRouteThreatRetargetTick >= AutonomousRouteThreatPolicy.RetargetCooldownMilliseconds;
            // A party is measured by its average level (a leader of 38 may walk with members of 23), a lone bot by its own.
            int levelsAbove = blocker.EffectiveLevel - (grouped ? _groupDirective.AverageLevel : bot.Level);
            bool mayForceRetarget = _camp != null &&
                                    now - _lastRouteThreatRetargetTick >= AutonomousRouteThreatPolicy.ForcedRetargetCooldownMilliseconds;
            bool strongParty = grouped && AutonomousRouteThreatPolicy.IsStrongParty(bot.Group?.MemberCount ?? 0,
                bot.Group?.GetMembersInTheGroup().OfType<GameBot>().Any(member => member.CharacterClass != null &&
                    BotPartyRoles.IsHealingClass((eCharacterClass)member.CharacterClass.ID)) == true, _groupDirective.AverageLevel);
            int hardestAbove = levelsAbove;
            if (strongParty && con == ConColor.PURPLE)
            {
                (int campSize, int campTopLevel) = PurpleCampAround(bot, blocker);
                pack = Math.Max(pack, campSize);
                hardestAbove = Math.Max(campTopLevel, packMembers.Max(member => member.EffectiveLevel)) - _groupDirective.AverageLevel;
            }
            RouteThreatAction action = AutonomousRouteThreatPolicy.Decide(con, grouped, pack, bossLike,
                detour.HasValue, detours, mayRetarget, cautious: !grouped && now < _routeCautionUntilTick,
                mayAbandonHopeless: alone || _camp != null, levelsAboveReference: levelsAbove, mayForceRetarget: mayForceRetarget,
                strongParty: strongParty,
                highLevelSolo: !grouped && bot.Level >= AutonomousRouteThreatPolicy.HighLevelSoloMinimum,
                denseWorld: AutonomousAtlantisPolicy.IsAtlantisRegion(bot.CurrentRegionID),
                hardestLevelsAbove: hardestAbove, provenDeadly: AutonomousMobLessons.IsDeadly(blocker.Name, grouped));
            if (blocker.InCombat)
                action = AutonomousRouteThreatPolicy.ForBusyMonster(action,
                    detour.HasValue && detours < AutonomousRouteThreatPolicy.MaxDetoursPerThreat, alone || _camp != null);
            // A walk that may not stop only takes a way around; anything else walks on as before.
            if (!mayTurnBack && action != RouteThreatAction.Detour)
                action = RouteThreatAction.Ignore;
            if (action == RouteThreatAction.Ignore)
                return false;

            if (BotDiagnostics.Enabled) Log.Info($"AUTONOMOUS_ROUTE_THREAT bot={bot.Name} id={bot.DatabaseID} class=\"{bot.ClassName}\" level={bot.Level} " +
                     $"group={(grouped ? _groupDirective.GroupId : alone ? "alone" : "solo")} action={action} target=\"{blocker.Name}\" " +
                     $"targetLevel={blocker.Level} con={con} seen={seen} pack={pack} aggro={blockerBrain.AggroRange} along={first:0} " +
                     $"detours={detours} levelsAbove={levelsAbove} strong={strongParty} busy={blocker.InCombat} " +
                     $"dist={bot.GetDistanceTo(blocker)} scanGap={(previousScanTick == 0 ? -1 : now - previousScanTick)} moved={Vector2.Distance(previousScanAt, new(bot.X, bot.Y)):0} " +
                     $"lastScanned={previousScanLength:0} scanned={_lastRouteScanLength:0} points={count} path={pathSource} detouring={detouring} " +
                     $"zone=\"{zone.Description}\" at={blocker.X},{blocker.Y},{blocker.Z}");

            switch (action)
            {
                case RouteThreatAction.Detour:
                    _routeThreatDetours[blocker] = detours + 1;
                    _routeRecoveryWaypoint = detour.Value;
                    _routeRecoveryArrivalRadius = 60;
                    _nextMoveOrderTick = 0;
                    bot.ForcePathReplot();
                    SetStatus(bot, $"Going around {blocker.Name}", GoalText(),
                        $"Taking a way around the {(pack > 1 ? $"pack of {pack}" : "monster")} on the route instead of walking into its aggro",
                        blocker.Name, _camp?.ZoneName ?? string.Empty);
                    return false;

                case RouteThreatAction.Retarget when alone && rvrTravel && (int)con <= (int)ConColor.ORANGE && !bossLike &&
                        RvrTravelTurnBacks(blocker, now) > RvrTravelTurnBacksBeforeWalkingOn:
                    // A keep march has no other road. Overnight run 2026-11-08: level 50s stood in the fellwood camp on the
                    // Pennine road (rvr-keep-56) and by the isalf forayers in Odin's Gate (rvr-keep-79), turning back from a
                    // yellow pack with "no way out" every scan until three stalls released the job, then marching back to
                    // the same spot (700 RVR_KEEP_TRAVEL_STALL lines, two-thirds at those two spots). Walk on and fight if they come.
                    foreach (GameNPC member in packMembers)
                        _unreachableRouteThreats.Add(member);
                    if (BotDiagnostics.Enabled) Log.Info($"AUTONOMOUS_ROUTE_THREAT_WALK_ON bot={bot.Name} id={bot.DatabaseID} level={bot.Level} " +
                             $"target=\"{blocker.Name}\" pack={pack} con={con} zone=\"{zone.Description}\" at={blocker.X},{blocker.Y},{blocker.Z}");
                    return false;

                case RouteThreatAction.Retarget when alone:
                    turnedBack = true;
                    _loneWalkHoldUntilTick = now + LoneWalkHoldMilliseconds;
                    bot.StopMovingOnPath();
                    bot.StopMoving();
                    BackAwayFrom(bot, nav, zone, blocker, packMembers, margin, threats);
                    return true;

                case RouteThreatAction.Retarget:
                    _lastRouteThreatRetargetTick = now;
                    NoteRouteBlocker(bot, packMembers, margin, now, con);
                    AbandonCamp(bot, mayRetarget || !grouped
                        ? $"Route blocked by {blocker.Name} (level {blocker.Level}, {pack} together) with no way around"
                        : $"Route blocked by {blocker.Name} (level {blocker.Level}, {pack} together), too strong for the party and no way around");
                    BackAwayFrom(bot, nav, zone, blocker, packMembers, margin, threats);
                    return true;

                default: // Pull
                    if (!AutonomousNavigationSurface.TryFloor(nav, zone, blockerPosition, out Vector3 blockerFloor) ||
                        !AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, new(bot.X, bot.Y, bot.Z), blockerFloor))
                    {
                        _unreachableRouteThreats.Add(blocker);
                        if (BotDiagnostics.Enabled) Log.Info($"AUTONOMOUS_ROUTE_THREAT_UNREACHABLE bot={bot.Name} id={bot.DatabaseID} target=\"{blocker.Name}\" " +
                                 $"at={blocker.X},{blocker.Y},{blocker.Z} zone=\"{zone.Description}\"");
                        return false;
                    }
                    if (!grouped && !AutonomousSoloFightReadiness.ReadyForRoutePull(bot.HealthPercent, bot.ManaPercent,
                            bot.EndurancePercent, bot.MaxMana > 0))
                    {
                        if (BotDiagnostics.Enabled) Log.Info($"AUTONOMOUS_ROUTE_PULL_REST bot={bot.Name} id={bot.DatabaseID} level={bot.Level} " +
                                 $"target=\"{blocker.Name}\" hp={bot.HealthPercent} power={bot.ManaPercent} endurance={bot.EndurancePercent} " +
                                 $"dist={bot.GetDistanceTo(blocker)} zone=\"{zone.Description}\"");
                        BeginRoadRest(bot, now, $"Resting before clearing {blocker.Name} from the route");
                        return true;
                    }
                    _routeThreatPullRepeats = blocker == _lastRouteThreatPull && now - _lastRouteThreatPullTick < 45_000
                        ? _routeThreatPullRepeats + 1 : 0;
                    _lastRouteThreatPull = blocker;
                    _lastRouteThreatPullTick = now;
                    if (_routeThreatPullRepeats >= RouteThreatPullAttempts)
                    {
                        _unreachableRouteThreats.Add(blocker);
                        _routeThreatPullRepeats = 0;
                        if (BotDiagnostics.Enabled) Log.Info($"AUTONOMOUS_ROUTE_THREAT_PULL_FAILED bot={bot.Name} id={bot.DatabaseID} target=\"{blocker.Name}\" " +
                                 $"attempts={RouteThreatPullAttempts} at={blocker.X},{blocker.Y},{blocker.Z} zone=\"{zone.Description}\"");
                        return false;
                    }
                    GameBot puller = grouped ? AutonomousBotGroupCoordinator.PartyPuller(bot) ?? bot : bot;
                    if (grouped && !AutonomousBotGroupCoordinator.CanInitiateNewPull(puller))
                    {
                        if (_routeThreatHold != blocker)
                        {
                            _routeThreatHold = blocker;
                            _routeThreatHoldSince = now;
                        }
                        if (now - _routeThreatHoldSince < RouteThreatHoldLimitMilliseconds)
                        {
                            bot.StopMovingOnPath();
                            bot.StopMoving();
                            SetStatus(bot, "Holding before a route threat", GoalText(),
                                $"Waiting for the whole party before clearing {blocker.Name} from the route",
                                blocker.Name, _camp?.ZoneName ?? string.Empty);
                            return true;
                        }
                        // The party could not get ready to pull: go around if there is a way, else walk on and fight it
                        // if it comes (the party defends itself as on any road).
                        _routeThreatHold = null;
                        if (BotDiagnostics.Enabled) Log.Info($"AUTONOMOUS_ROUTE_THREAT_HOLD_EXPIRED bot={bot.Name} id={bot.DatabaseID} group={_groupDirective.GroupId} " +
                                 $"target=\"{blocker.Name}\" puller={puller.Name} seconds={RouteThreatHoldLimitMilliseconds / 1000} " +
                                 $"then={(detour.HasValue ? "detour" : "pass")} at={blocker.X},{blocker.Y},{blocker.Z} zone=\"{zone.Description}\"");
                        if (detour.HasValue)
                        {
                            _routeThreatDetours[blocker] = detours + 1;
                            _routeRecoveryWaypoint = detour.Value;
                            _routeRecoveryArrivalRadius = 60;
                            _nextMoveOrderTick = 0;
                            bot.ForcePathReplot();
                            return false;
                        }
                        _unreachableRouteThreats.Add(blocker);
                        return false;
                    }
                    _routeThreatHold = null;
                    _lastEngagedCon = con;
                    _routeInterruptedByCombat = true;
                    AutonomousBotGroupCoordinator.MarkCombatObserved(bot.Group);
                    if (puller != bot && puller.Brain is BotBrain pullerBrain)
                    {
                        // The party's puller opens on it, as it opens camp pulls; the leader holds the party meanwhile.
                        puller.StopMovingOnPath();
                        puller.StopMoving();
                        puller.TargetObject = blocker;
                        pullerBrain.AddToAggroList(blocker, Math.Max(25, blocker.EffectiveLevel * 10));
                        pullerBrain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                        bot.StopMovingOnPath();
                        bot.StopMoving();
                        SetStatus(bot, $"Clearing the route: {blocker.Name}", GoalText(),
                            $"{puller.Name} is pulling a monster ahead on the route", blocker.Name, _camp?.ZoneName ?? string.Empty);
                        return true;
                    }
                    bot.StopMovingOnPath();
                    bot.StopMoving();
                    bot.TargetObject = blocker;
                    brain.AddToAggroList(blocker, Math.Max(25, blocker.EffectiveLevel * 10));
                    brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                    if (SavageBotCombatPolicy.MustMeleePull(characterClass))
                        BeginSavageMeleePull(bot, blocker);
                    SetStatus(bot, $"Clearing the route: {blocker.Name}", GoalText(),
                        ranged
                            ? "Opening from range on a monster ahead on the route instead of walking into its aggro"
                            : "Taking on a monster ahead on the route before it can jump from behind",
                        blocker.Name, _camp?.ZoneName ?? string.Empty);
                    return true;
            }
        }

        /// <summary>
        /// The pack a camp was given up for, remembered for this bot and its party (AutonomousRouteBlockMemory). A red or
        /// purple one is also passed on to the realm's bots of about the same level, as word of mouth.
        /// </summary>
        private void NoteRouteBlocker(GameBot bot, GameNPC[] pack, int margin, long now, ConColor con)
        {
            var members = new (Vector2 Position, int AggroRange)[pack.Length];
            for (int i = 0; i < pack.Length; i++)
                members[i] = (new(pack[i].X, pack[i].Y), (pack[i].Brain as StandardMobBrain)?.AggroRange ?? 0);
            (Vector2 center, float radius) = AutonomousRouteThreatPolicy.PackRetreat(members, margin);
            AutonomousRouteBlockMemory.Note(FailedGoalOwnerKey(bot), bot.CurrentRegionID, center, radius, now);
            AutonomousRouteBlockMemory.Note(WarbandGoalKey(), bot.CurrentRegionID, center, radius, now);
            if (AutonomousRouteBlockMemory.PassedOn(con))
                AutonomousRouteBlockMemory.Note(AutonomousRouteBlockMemory.WordOfMouthKey(bot.Realm, bot.Level, bot.CurrentRegionID),
                    bot.CurrentRegionID, center, radius, now, AutonomousRouteBlockMemory.WordOfMouthSlots);
        }

        /// <summary>A camp in this region whose straight way from the bot passes a pack the bot, its party or its realm's bots of its level just gave up a camp for.</summary>
        private bool IsBehindRouteBlocker(GameBot bot, ushort region, float x, float y, long now) =>
            region == bot.CurrentRegionID &&
            (AutonomousRouteBlockMemory.Crosses(FailedGoalOwnerKey(bot), region, new(bot.X, bot.Y), new(x, y), now) ||
             AutonomousRouteBlockMemory.Crosses(WarbandGoalKey(), region, new(bot.X, bot.Y), new(x, y), now) ||
             AutonomousRouteBlockMemory.Crosses(AutonomousRouteBlockMemory.WordOfMouthKey(bot.Realm, bot.Level, region), region,
                 new(bot.X, bot.Y), new(x, y), now));

        /// <summary>A spot that lies inside the reach of a pack the bot, its party or its realm's bots of its level just gave up a camp for.</summary>
        private bool IsInsideRouteBlocker(GameBot bot, ushort region, float x, float y, long now) =>
            AutonomousRouteBlockMemory.Within(FailedGoalOwnerKey(bot), region, new(x, y), now) ||
            AutonomousRouteBlockMemory.Within(WarbandGoalKey(), region, new(x, y), now) ||
            AutonomousRouteBlockMemory.Within(AutonomousRouteBlockMemory.WordOfMouthKey(bot.Realm, bot.Level, region), region, new(x, y), now);

        /// <summary>Where the bot stands and the nearest blocker it remembers, for the log line of a search that every blocker emptied.</summary>
        private string NearestRouteBlockerText(GameBot bot, long now)
        {
            Vector2 here = new(bot.X, bot.Y);
            (string Owner, long Key)[] owners =
            [
                ("own", FailedGoalOwnerKey(bot)), ("party", WarbandGoalKey()),
                ("realm", AutonomousRouteBlockMemory.WordOfMouthKey(bot.Realm, bot.Level, bot.CurrentRegionID)),
            ];
            string text = $" at={bot.CurrentRegionID}:{bot.X},{bot.Y}";
            float best = float.MaxValue;
            foreach ((string owner, long key) in owners)
            {
                if (!AutonomousRouteBlockMemory.TryNearest(key, bot.CurrentRegionID, here, now, out Vector2 at, out float radius))
                    continue;
                float distance = Vector2.Distance(at, here);
                if (distance >= best)
                    continue;
                best = distance;
                text = $" at={bot.CurrentRegionID}:{bot.X},{bot.Y} blocker={(int)at.X},{(int)at.Y} reach={(int)radius} distance={(int)distance} from={owner}";
            }
            return text;
        }

        private bool HasRouteBlockers(GameBot bot, long now) =>
            AutonomousRouteBlockMemory.HasAny(FailedGoalOwnerKey(bot), now) || AutonomousRouteBlockMemory.HasAny(WarbandGoalKey(), now) ||
            AutonomousRouteBlockMemory.HasAny(AutonomousRouteBlockMemory.WordOfMouthKey(bot.Realm, bot.Level, bot.CurrentRegionID), now);

        /// <summary>
        /// Who stands around a purple, counted from the monster and not from the leader: the pack above comes from a scan around
        /// the leader, so one 1,500 units down the route looks alone with its camp behind it. On 2026-11-01 one griffolich in
        /// Iarnwood read as 1 or 2 together, and the next one, 1,090 units off, as 6. Anything yellow or above to the leader counts,
        /// aggressive or not: friends get called in. Only a strong party that may pull this purple pays for the search.
        /// </summary>
        private static (int Size, int TopLevel) PurpleCampAround(GameBot bot, GameNPC blocker)
        {
            int size = 1, top = blocker.EffectiveLevel;
            foreach (GameNPC npc in blocker.GetNPCsInRadius((ushort)AutonomousRouteThreatPolicy.CampCountRadius))
            {
                if (npc == blocker || !npc.IsAlive || !IsExperienceMonster(npc) || Math.Abs(npc.Z - blocker.Z) > 160 ||
                    ConLevels.GetConColor(bot.GetConLevel(npc)) < ConColor.YELLOW)
                    continue;
                size++;
                top = Math.Max(top, npc.EffectiveLevel);
            }
            return (size, top);
        }

        /// <summary>
        /// After giving up a camp because of a monster or its pack, walks out of the whole pack's reach (by a way that
        /// passes no other monster's aggro) instead of waiting out the replan beside it
        /// (AutonomousRouteThreatPolicy.RetreatClearance, PackRetreat). Every attempt is logged as AUTONOMOUS_ROUTE_RETREAT.
        /// </summary>
        private const int RetreatFloorSearch = 1536;

        /// <summary>True when the bot is walking away (or already clear); false when no way out was found.</summary>
        private bool BackAwayFrom(GameBot bot, IPathfindingMgr nav, Zone zone, GameNPC threat, GameNPC[] pack, int margin,
            GameNPC[] others, float minimumRadius = 0)
        {
            var members = new (Vector2 Position, int AggroRange)[pack.Length];
            for (int i = 0; i < pack.Length; i++)
                members[i] = (new(pack[i].X, pack[i].Y), (pack[i].Brain as StandardMobBrain)?.AggroRange ?? 0);
            (Vector2 center, float radius) = AutonomousRouteThreatPolicy.PackRetreat(members, margin);
            radius = Math.Max(radius, minimumRadius);
            Vector2 here = new(bot.X, bot.Y);
            float distance = Vector2.Distance(here, center);
            if (distance >= radius)
                return true;
            Vector3 start = new(bot.X, bot.Y, bot.Z);
            foreach (Vector2 candidate in AutonomousRouteThreatPolicy.RetreatCandidates(here, center, radius))
            {
                if (others.Any(npc => !pack.Contains(npc) && npc.Brain is StandardMobBrain mob &&
                                      AutonomousRouteThreatPolicy.DistanceToSegment(here, candidate, new(npc.X, npc.Y)) <
                                      mob.AggroRange + margin))
                    continue;
                // The point is 1,000-1,600 units off, so its floor can be a hillside above or below the bot. The local floor
                // check (128 up) failed every uphill point: 2,506 of 10,882 retreats on 2026-10-29 found no way out, most
                // in Gripklosa Mountains and Vigilant Rock, and those bots died three times as often as the ones that left.
                Vector3? found = nav.GetClosestPoint(zone, new(candidate.X, candidate.Y, bot.Z), 64, 64, RetreatFloorSearch, nav.DefaultFilters);
                if (found is not { } floor || Vector2.DistanceSquared(new(floor.X, floor.Y), candidate) > 96 * 96 ||
                    !AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, start, floor))
                    continue;
                bot.PathTo(floor, bot.MaxSpeed);
                // The next plan starts once the bot is clear (the usual replan wait is longer than the walk).
                float walkDistance = Vector2.Distance(here, new(floor.X, floor.Y));
                long walk = (long)(walkDistance * 1000 / Math.Max((short)50, bot.MaxSpeed)) + 1_000;
                _nextPlanTick = Math.Max(_nextPlanTick, GameLoop.GameLoopTime + walk);
                SetStatus(bot, $"Backing away from {threat.Name}", "Find a reachable level-appropriate XP camp",
                    "Getting out of its reach before choosing another camp", threat.Name);
                if (BotDiagnostics.Enabled) Log.Info($"AUTONOMOUS_ROUTE_RETREAT bot={bot.Name} id={bot.DatabaseID} level={bot.Level} result=walking " +
                         $"target=\"{threat.Name}\" pack={pack.Length} from={distance:0} radius={radius:0} walk={walkDistance:0} " +
                         $"zone=\"{zone.Description}\" at={bot.X},{bot.Y},{bot.Z} to={floor.X:0},{floor.Y:0},{floor.Z:0}");
                return true;
            }
            if (BotDiagnostics.Enabled) Log.Info($"AUTONOMOUS_ROUTE_RETREAT bot={bot.Name} id={bot.DatabaseID} level={bot.Level} result=no_way_out " +
                     $"target=\"{threat.Name}\" pack={pack.Length} from={distance:0} radius={radius:0} " +
                     $"zone=\"{zone.Description}\" at={bot.X},{bot.Y},{bot.Z}");
            return false;
        }

        private long _nextDangerBypassLogTick;

        private bool TryDangerBypass(GameBot bot, IPathfindingMgr nav, Zone zone, ReadOnlySpan<Vector3> corridor)
        {
            var mobs = bot.GetNPCsInRadius((ushort)AutonomousPveDangerBypass.ScanRadius)
                .Where(npc => IsExperienceMonster(npc) && npc.IsAlive && npc.Brain is StandardMobBrain mob &&
                              mob.AggroRange > 0 && mob.AggroLevel > 0)
                .Select(npc => new AutonomousPveDangerBypass.MobView(new(npc.X, npc.Y), npc.Level, npc.Name, true))
                .ToArray();
            DateTime utc = DateTime.UtcNow;
            float spotScan = AutonomousPveDangerBypass.ScanRadius + AutonomousPveDangerBypass.RememberedSpotRadius;
            var spots = AutonomousPveBossDanger.SpotsNear(bot.CurrentRegionID, bot.X, bot.Y, bot.Level, utc, spotScan);
            foreach (var (x, y) in AutonomousRepeatDeathSpots.SpotsNear(bot.DatabaseID, bot.CurrentRegionID, bot.X, bot.Y, utc, spotScan))
                spots.Add((x, y, bot.Level));
            // Where several other bots of this level or lower died recently (word of mouth).
            foreach (var (x, y) in AutonomousSharedDeathSpots.SpotsNear(bot.CurrentRegionID, bot.Level, bot.X, bot.Y, utc, spotScan))
                spots.Add((x, y, bot.Level));
            List<AutonomousPveDangerBypass.Danger> dangers = AutonomousPveDangerBypass.BuildDangers(bot.Level, mobs, spots);
            if (dangers.Count == 0)
                return false;
            Vector3 here = corridor[0];
            Vector3 ahead = AutonomousPveDangerBypass.PointAlong(corridor, AutonomousPveDangerBypass.LookAhead);
            Zone aheadZone = bot.CurrentRegion.GetZone((int)ahead.X, (int)ahead.Y);
            foreach (Vector2 candidate in AutonomousPveDangerBypass.BypassCandidates(new(here.X, here.Y), new(ahead.X, ahead.Y), dangers))
            {
                if (bot.CurrentRegion.GetZone((int)candidate.X, (int)candidate.Y) != zone)
                    continue;
                Vector3? floor = nav.GetClosestPoint(zone, new(candidate.X, candidate.Y, here.Z), 96, 96, 768, nav.DefaultFilters);
                if (!floor.HasValue || Vector2.DistanceSquared(new(floor.Value.X, floor.Value.Y), candidate) > 150 * 150 ||
                    !AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, here, floor.Value) ||
                    aheadZone == zone && !AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, floor.Value, ahead))
                    continue;
                _routeRecoveryWaypoint = floor.Value;
                _routeRecoveryArrivalRadius = 60;
                _nextMoveOrderTick = 0;
                bot.ForcePathReplot();
                long now = GameLoop.GameLoopTime;
                if (now >= _nextDangerBypassLogTick && Log.IsInfoEnabled)
                {
                    _nextDangerBypassLogTick = now + 60_000;
                    var nearest = dangers.OrderBy(danger => Vector2.DistanceSquared(danger.Center, candidate)).First();
                    if (BotDiagnostics.Enabled) Log.Info($"AUTONOMOUS_DANGER_BYPASS bot={bot.Name} id={bot.DatabaseID} level={bot.Level} danger=\"{nearest.Name}\" " +
                             $"dangerLevel={nearest.Level} radius={(int)nearest.Radius} dangers={dangers.Count} zone=\"{zone.Description}\" " +
                             $"at={bot.X},{bot.Y} bend={(int)candidate.X},{(int)candidate.Y}");
                }
                SetStatus(bot, "Going around a dangerous area", GoalText(),
                    "Taking the long way around monsters far above this level", string.Empty, _camp?.ZoneName ?? string.Empty);
                return true;
            }
            return false;
        }

        /// <summary>
        /// A reachable sidestep beside the monster that stays out of every nearby monster's aggro
        /// range; the shorter way of the two sides wins.
        /// </summary>
        private static Vector3? FindRouteThreatDetour(IPathfindingMgr nav, Zone zone, ReadOnlySpan<Vector3> corridor,
            GameNPC blocker, int aggroRange, int margin, Vector3 destination, GameNPC[] threats)
        {
            Vector3 start = corridor[0];
            Vector3 position = new(blocker.X, blocker.Y, blocker.Z);
            if (!AutonomousRouteThreatPolicy.NearestOnRoute(corridor, position, AutonomousRouteThreatPolicy.LookAhead,
                    out Vector3 routePoint, out Vector2 direction) ||
                !AutonomousNavigationSurface.TryFloor(nav, zone, start, out Vector3 startFloor))
                return null;
            // Rejoin the route a sidestep's length past the monster (or at the destination if nearer).
            float sidestep = aggroRange + margin + AutonomousRouteThreatPolicy.DetourClearance;
            Vector3 past = new(routePoint.X + direction.X * sidestep * 1.5f, routePoint.Y + direction.Y * sidestep * 1.5f, routePoint.Z);
            Vector3 rejoin = Vector3.Distance(routePoint, destination) < Vector3.Distance(routePoint, past) ? destination : past;
            Vector3? best = null;
            float bestCost = float.MaxValue;
            foreach (int side in new[] { 1, -1 })
            foreach (float alongFraction in AutonomousRouteThreatPolicy.DetourAlongFractions)
            {
                Vector3 candidate = AutonomousRouteThreatPolicy.DetourPoint(position, direction, aggroRange, margin,
                    routePoint.Z, side, alongFraction);
                // Quick filter: both straight legs pass outside every nearby monster's aggro.
                if (threats.Any(npc => npc.Brain is StandardMobBrain mob &&
                        !AutonomousRouteThreatPolicy.LegsClear(start, candidate, rejoin, new(npc.X, npc.Y, npc.Z),
                            mob.AggroRange + margin / 2f)))
                    continue;
                Vector3? floor = nav.GetClosestPoint(zone, candidate, 96, 96, 320, nav.DefaultFilters);
                if (!floor.HasValue || zone != blocker.CurrentRegion?.GetZone((int)floor.Value.X, (int)floor.Value.Y))
                    continue;
                bool inAggro = threats.Any(npc => npc.Brain is StandardMobBrain mob &&
                    Math.Abs(npc.Z - floor.Value.Z) <= 200 &&
                    Vector2.Distance(new(npc.X, npc.Y), new(floor.Value.X, floor.Value.Y)) < mob.AggroRange + margin);
                if (inAggro || !AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, startFloor, floor.Value) ||
                    !PathClearOfAggro(nav, zone, startFloor, floor.Value, threats, margin) ||
                    !PathClearOfAggro(nav, zone, floor.Value, rejoin, threats, margin))
                    continue;
                float cost = Vector3.Distance(start, floor.Value) + Vector3.Distance(floor.Value, destination);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = floor.Value;
                }
            }
            return best;
        }

        /// <summary>
        /// The real navmesh path between two points stays outside every monster's aggro range
        /// (half the safety margin may be used).
        /// </summary>
        private static bool PathClearOfAggro(IPathfindingMgr nav, Zone zone, Vector3 from, Vector3 to,
            GameNPC[] threats, int margin)
        {
            Span<WrappedPathfindingNode> nodes = stackalloc WrappedPathfindingNode[256];
            PathfindingResult result = nav.GetPathStraight(zone, from, to, nav.DefaultFilters, nodes);
            if (result.NodeCount == 0)
                return false;
            int count = Math.Min(result.NodeCount, nodes.Length);
            Span<Vector3> path = stackalloc Vector3[count + 1];
            path[0] = from;
            float length = 0;
            for (int i = 0; i < count; i++)
            {
                path[i + 1] = nodes[i].Position;
                length += Vector3.Distance(path[i], path[i + 1]);
            }
            foreach (GameNPC npc in threats)
            {
                if (npc.Brain is not StandardMobBrain mob)
                    continue;
                if (AutonomousDungeonPolicy.IntersectsCorridor(path, new(npc.X, npc.Y, npc.Z),
                        mob.AggroRange + margin / 2f, length + 1, out _))
                    return false;
            }
            return true;
        }

        private void PruneRouteThreatDetours()
        {
            if (_unreachableRouteThreats.Count > 0)
            {
                _unreachableRouteThreats.RemoveWhere(npc => !npc.IsAlive || npc.ObjectState != GameObject.eObjectState.Active);
                if (_unreachableRouteThreats.Count > 32) _unreachableRouteThreats.Clear();
            }
            if (_routeThreatDetours.Count == 0)
                return;
            foreach (GameNPC npc in _routeThreatDetours.Keys.Where(npc => !npc.IsAlive ||
                         npc.ObjectState != GameObject.eObjectState.Active).ToArray())
                _routeThreatDetours.Remove(npc);
            if (_routeThreatDetours.Count > 32)
                _routeThreatDetours.Clear();
        }
    }
}
