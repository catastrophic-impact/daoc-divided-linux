using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS.Quests;

namespace DOL.GS
{
    public sealed partial class AutonomousWorldBotController
    {
        private const string QuestCampPrefix = "quest:";
        /// <summary>Close enough to speak to a character or use an item.</summary>
        private const int QuestSpeakRadius = 280;
        /// <summary>How close a bot comes to a point with no radius of its own.</summary>
        private const int QuestPointRadius = 300;
        private long _nextQuestPickTick;

        private static bool IsQuestCamp(CampDestination camp) =>
            camp?.Id.StartsWith(QuestCampPrefix, StringComparison.Ordinal) == true;

        /// <summary>
        /// Runs the bot's quest between fights. The step becomes the bot's camp, so the ordinary road, stable and danger
        /// handling takes it there and WorkCamp fights what a Kill step wants. True when this turn is used up here.
        /// </summary>
        private bool HandleQuestWork(GameBot bot)
        {
            if (!AutonomousBotQuests.Enabled)
                return false;
            long now = GameLoop.GameLoopTime;
            AutonomousBotQuests.Run run = AutonomousBotQuests.Current(bot);
            if (run == null)
            {
                if (IsQuestCamp(_camp))
                {
                    _camp = null;
                    _nextPlanTick = 0;
                }
                if (_camp != null || now < _nextQuestPickTick)
                    return false;
                _nextQuestPickTick = now + AutonomousBotQuests.PickIntervalMilliseconds + bot.ObjectID % 15_000;
                if (!AutonomousBotQuests.WantsQuest(bot.Level, Random.Shared.NextDouble()))
                    return false;
                HashSet<ushort> reachable = ReachableRegions(bot.Realm, bot.CurrentRegionID);
                // Darkness Falls has its own certified routes for camps; a quest there would ask for a road no bot has.
                BotQuestPlan plan = AutonomousBotQuests.Pick(bot, region => reachable.Contains(region) && region != AutonomousDarknessFallsPolicy.RegionId,
                    (region, x, y) => EstimateTravelMinutes(bot, region, x, y));
                if (plan == null)
                    return false;
                run = AutonomousBotQuests.Begin(bot, plan);
            }
            else
            {
                // A quest set aside waits while the bot does its other work; back on it, the bot first finishes the camp it is at.
                if (AutonomousBotQuests.Parked(bot, run, DateTime.UtcNow))
                {
                    LeaveQuestCamp(bot);
                    return false;
                }
                if (_camp != null && !IsQuestCamp(_camp))
                {
                    AutonomousBotQuests.HoldStepClock(run, now);
                    return false;
                }
            }

            switch (run.Step > 0 ? run.Current?.Action : null)
            {
                case eBotQuestAction.FinishQuests:
                    return HandleNestedQuestStep(bot, run);
                case eBotQuestAction.Grouped:
                    // Grouped bots never reach here: the step is done in OnGrouped. Alone, the quest waits for a party.
                    if (!AutonomousBotQuests.Park(bot, run, eBotQuestPark.Group, "Waiting for a group"))
                        AutonomousBotQuests.Abandon(bot, run, "No group came");
                    LeaveQuestCamp(bot);
                    return false;
            }

            if (AutonomousBotQuests.StepTimedOut(run, now))
            {
                AutonomousBotQuests.Abandon(bot, run, "The step ran past its time");
                LeaveQuestCamp(bot);
                return true;
            }
            AutonomousBotQuests.Goal goal = AutonomousBotQuests.GoalFor(bot, run, out string blocked);
            if (goal == null)
            {
                // DR-076: every spot of the step is far over this bot's level or behind a monster that is. Set it aside
                // (the bot may level, or the camp move) and give it up once it has been set aside too often.
                if (blocked == null || !AutonomousBotQuests.Park(bot, run, AutonomousBotQuests.Classify(blocked), blocked))
                    AutonomousBotQuests.Abandon(bot, run, blocked ?? "The step has no place in the world");
                LeaveQuestCamp(bot);
                return true;
            }
            AimQuestCamp(bot, run, goal);
            if (goal.Hunts)
            {
                TrySpawnQuestMonster(bot, run);
                return false; // travel and WorkCamp handle the hunt
            }

            bool sameRegion = bot.CurrentRegionID == goal.Region;
            int arrival = run.Step == 0 || run.Current?.Action is eBotQuestAction.Talk or eBotQuestAction.HandIn or eBotQuestAction.Choose
                ? QuestSpeakRadius : Math.Max(goal.Radius, QuestPointRadius);
            if (sameRegion && Distance(bot.X, bot.Y, goal.X, goal.Y) <= arrival)
                return ActOnQuestStep(bot, run, goal);
            if (sameRegion && Distance(bot.X, bot.Y, goal.X, goal.Y) <= CampArrivalRadius)
            {
                // Inside camp arrival range WorkCamp would take over: finish the walk here.
                IssuePath(bot, new Vector3(_camp.X, _camp.Y, _camp.Z), preciseArrival: true);
                SetStatus(bot, $"Walking to {goal.Name}", QuestGoalText(run), "On the last stretch of a quest step", goal.Name, _camp?.ZoneName ?? string.Empty);
                return true;
            }
            return false;
        }

        private static string QuestGoalText(AutonomousBotQuests.Run run) => $"Quest: {run.Plan.Name}";

        /// <summary>The step waits on other quests: the bot takes the next of them, and comes back when it ends.</summary>
        private bool HandleNestedQuestStep(GameBot bot, AutonomousBotQuests.Run run)
        {
            BotQuestStep step = run.Current;
            if (AutonomousBotQuests.NestedDone(bot, step))
            {
                AutonomousBotQuests.CompleteStep(bot, run);
                return true;
            }
            HashSet<ushort> reachable = ReachableRegions(bot.Realm, bot.CurrentRegionID);
            BotQuestPlan nested = AutonomousBotQuests.PickNested(bot, step,
                region => reachable.Contains(region) && region != AutonomousDarknessFallsPolicy.RegionId);
            LeaveQuestCamp(bot);
            if (nested == null)
            {
                AutonomousBotQuests.Abandon(bot, run, "No nested quest it can run");
                return true;
            }
            AutonomousBotQuests.Begin(bot, nested, run.Plan.Id);
            return true;
        }

        private void AimQuestCamp(GameBot bot, AutonomousBotQuests.Run run, AutonomousBotQuests.Goal goal)
        {
            string id = $"{QuestCampPrefix}{run.Plan.Id}:{run.Step}:{goal.Name}";
            int z = goal.Z;
            bool sameRegion = bot.CurrentRegionID == goal.Region;
            if (string.Equals(_camp?.Id, id, StringComparison.Ordinal) && (z != 0 || !sameRegion || _camp.Z != 0))
                return;
            Region region = WorldMgr.GetRegion(goal.Region);
            Zone zone = region?.GetZone(goal.X, goal.Y);
            if (z == 0 && sameRegion && zone != null)
            {
                // A walkthrough's point has no height: take the ground under it.
                IPathfindingMgr nav = PathfindingProvider.Instance;
                if (nav.IsAvailable && nav.HasNavmesh(zone) &&
                    nav.GetClosestPoint(zone, new Vector3(goal.X, goal.Y, bot.Z), 300, 300, 8_000, nav.DefaultFilters) is Vector3 floor)
                    z = (int)floor.Z;
                else
                    z = bot.Z;
            }
            _camp = new CampDestination(id, goal.Name, zone?.Description ?? region?.Description ?? string.Empty, goal.Region,
                goal.X, goal.Y, z, 1, zone?.IsDungeon == true, false);
            _campStartedTick = GameLoop.GameLoopTime;
            _emptyCampSinceTick = 0;
            _patrolDestination = null;
            _pendingStableChoice = null;
            if (bot.PersistentRecord != null)
                bot.PersistentRecord.CurrentCampId = id;
            BeginCampDiagnostics(bot);
            SetStatus(bot, $"Heading for {goal.Name}", QuestGoalText(run), run.Step == 0
                    ? "Going to take the quest from its giver"
                    : run.Current?.TargetName is { Length: > 0 } ? $"Quest step {run.Step} of {run.Plan.Steps.Length}" : "On a quest errand",
                goal.Name, _camp.ZoneName, true);
        }

        /// <summary>The monster a step calls up comes when the bot reaches the spot, as it does for a player.</summary>
        private void TrySpawnQuestMonster(GameBot bot, AutonomousBotQuests.Run run)
        {
            ClassicQuests.EventSpawn spawn = run.Current?.Spawn;
            if (spawn == null || run.Spawned || spawn.Peaceful)
                return;
            if (spawn.TriggerRadius > 0 && (bot.CurrentRegionID != spawn.Region ||
                !bot.IsWithinRadius(new Point3D(spawn.X, spawn.Y, spawn.Z), spawn.TriggerRadius)))
                return;
            run.Spawned = true;
            ClassicQuests.SpawnEventFor(bot, run.Plan.Id, run.Step, spawn);
        }

        private bool ActOnQuestStep(GameBot bot, AutonomousBotQuests.Run run, AutonomousBotQuests.Goal goal)
        {
            bot.StopMovingOnPath();
            bot.StopMoving();
            if (run.Step == 0)
            {
                if (!NearQuestCharacter(bot, run.Plan.GiverName))
                {
                    AutonomousBotQuests.NoteStepFailure(bot, "The giver was not there");
                    return true;
                }
                AutonomousBotQuests.Accept(bot, run);
                return true;
            }
            BotQuestStep step = run.Current;
            switch (step.Action)
            {
                case eBotQuestAction.Search:
                    // The search takes the step's time on the spot, as /search does.
                    if (run.WaitUntilTick == 0)
                    {
                        run.WaitUntilTick = GameLoop.GameLoopTime + Math.Max(1, step.SearchSeconds) * 1000L;
                        SetStatus(bot, $"Searching for {run.Plan.Name}", QuestGoalText(run), "Searching the marked area", goal.Name, _camp?.ZoneName ?? string.Empty);
                    }
                    if (GameLoop.GameLoopTime < run.WaitUntilTick)
                        return true;
                    if (step.GivenChance < 100 && !Util.Chance(step.GivenChance))
                    {
                        run.WaitUntilTick = 0;
                        return true;
                    }
                    break;
                case eBotQuestAction.Say:
                    if (step.NightOnly && bot.CurrentRegion?.IsNightTime == false)
                    {
                        AutonomousBotQuests.Park(bot, run, eBotQuestPark.Night, "The words work only at night");
                        LeaveQuestCamp(bot);
                        return true;
                    }
                    bot.Say(step.Words);
                    break;
                case eBotQuestAction.Talk:
                case eBotQuestAction.HandIn:
                case eBotQuestAction.Choose:
                    if (step.Spawn is { Peaceful: true } && !run.Spawned)
                    {
                        run.Spawned = true;
                        ClassicQuests.SpawnEventFor(bot, run.Plan.Id, run.Step, step.Spawn);
                        return true;
                    }
                    if (!NearQuestCharacter(bot, step.TargetName))
                    {
                        AutonomousBotQuests.NoteStepFailure(bot, $"{step.TargetName} was not there");
                        return true;
                    }
                    break;
            }
            string reward = step.Action == eBotQuestAction.Choose ? AutonomousBotQuests.ChooseReward(bot, step) : null;
            if (!AutonomousBotQuests.CompleteStep(bot, run, reward))
                SetStatus(bot, "Waiting on a quest", QuestGoalText(run), "No backpack room for what the step hands over", goal.Name);
            else
                _camp = null;
            return true;
        }

        private static bool NearQuestCharacter(GameBot bot, string name)
        {
            ushort radius = QuestSpeakRadius + 120;
            return bot.GetNPCsInRadius(radius).Any(npc => QuestNames.Same(name, npc.Name) && npc.IsAlive) ||
                   bot.GetItemsInRadius(radius).OfType<GameStaticItem>().Any(item => QuestNames.Same(name, item.Name));
        }

        private void LeaveQuestCamp(GameBot bot)
        {
            if (!IsQuestCamp(_camp))
                return;
            _camp = null;
            _campStartedTick = 0;
            _emptyCampSinceTick = 0;
            _patrolDestination = null;
            _nextPlanTick = 0;
            if (bot.PersistentRecord != null)
                bot.PersistentRecord.CurrentCampId = string.Empty;
        }
    }
}
