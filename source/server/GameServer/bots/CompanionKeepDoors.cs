using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS.Keeps;

namespace DOL.GS
{
    /// <summary>
    /// Discord 2026-11-08 (BornToReignGamingYT): a /raid 80 took a keep, then stayed inside when the player clicked the
    /// door to leave. Taking a keep resets its doors shut (GameKeepDoor.Reset); a player passes a friendly door by
    /// clicking it, but only gamebots could (GameKeepDoor.TryTraverse), and the closed door splits the navmesh, so the
    /// raid's formation walk had no route and the 3,000 leash recall never fired a few hundred units from the player.
    /// A bot following a player (raid, /spawn companions, gamebots in the player's group) that has no walk to its
    /// formation spot near a friendly keep now walks to the friendly door that leads toward the player and passes it
    /// the way a player does. Log tag COMPANION_KEEP_DOOR.
    /// </summary>
    public static class CompanionKeepDoors
    {
        public const int KeepRadius = 3000;
        public const int NearSlotDistance = 250;
        public const float TowardLeaderMargin = 40;
        private const long CheckMilliseconds = 2_000;
        private const long WalkMilliseconds = 15_000;
        private const long SameDoorMilliseconds = 8_000;
        private const long SettleMilliseconds = 600;

        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(CompanionKeepDoors));

        private struct State
        {
            public long Next, WalkUntil, LastUntil, SettleUntil;
            public GameKeepDoor Door, Last;
        }

        private static readonly WeakKeyMap<GameBot, State> States = new();

        /// <summary>A door candidate for <see cref="ChooseDoor"/>: where it stands and whether the bot can reach it.</summary>
        public readonly record struct Candidate(Vector3 Position, bool Reachable, bool JustPassed);

        /// <summary>
        /// The door to use, or -1: one the bot can reach (or stands at), at about the bot's height, that is nearer the
        /// leader than the bot is (never back through the door just passed), nearest the bot first.
        /// </summary>
        public static int ChooseDoor(Vector3 bot, Vector3 leader, IReadOnlyList<Candidate> doors)
        {
            float botToLeader = Vector2.Distance(new(bot.X, bot.Y), new(leader.X, leader.Y));
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < doors.Count; i++)
            {
                Candidate door = doors[i];
                if (door.JustPassed || !door.Reachable || Math.Abs(door.Position.Z - bot.Z) > 500) continue;
                if (Vector2.Distance(new(door.Position.X, door.Position.Y), new(leader.X, leader.Y)) + TowardLeaderMargin >= botToLeader) continue;
                float distance = Vector2.Distance(new(bot.X, bot.Y), new(door.Position.X, door.Position.Y));
                if (distance < bestDistance) { bestDistance = distance; best = i; }
            }
            return best;
        }

        private static bool Friendly(AbstractGameKeep keep, GameBot bot) => DOL.GS.ServerRules.HybridCoopRules.Enabled
            ? !DOL.GS.ServerRules.HybridCoopRules.KeepEnemy(keep, bot)
            : keep.Realm == bot.Realm;

        /// <summary>
        /// Called from the follow code before the formation walk. True: this turn the bot walks to a door or passed one
        /// (skip the formation walk); false: follow as usual.
        /// </summary>
        public static bool Follow(GameBot bot, GamePlayer leader, Vector3 slot)
        {
            if (bot == null || leader == null || !bot.IsAlive || bot.CurrentRegion == null ||
                bot.CurrentRegion != leader.CurrentRegion || GameServer.KeepManager == null) return false;
            long now = GameLoop.GameLoopTime;
            States.TryGetValue(bot, out State state);
            if (now < state.SettleUntil) return true;
            if (state.Door != null && now < state.WalkUntil)
                return Walk(bot, leader, ref state, now);
            state.Door = null;
            if (now < state.Next) { States[bot] = state; return false; }
            state.Next = now + CheckMilliseconds;
            States[bot] = state;

            Vector3 here = new(bot.X, bot.Y, bot.Z);
            if (Vector2.Distance(new(here.X, here.Y), new(slot.X, slot.Y)) < NearSlotDistance) return false;
            AbstractGameKeep[] keeps = GameServer.KeepManager.GetKeepsOfRegion(bot.CurrentRegionID)
                .Where(k => Friendly(k, bot) && Vector2.Distance(new(k.X, k.Y), new(here.X, here.Y)) <= KeepRadius).ToArray();
            if (keeps.Length == 0) return false;
            var nav = PathfindingProvider.Instance;
            Zone zone = bot.CurrentZone;
            if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return false;
            Vector3 leaderPosition = new(leader.X, leader.Y, leader.Z);
            if (AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, here, slot) ||
                AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, here, leaderPosition)) return false;

            GameKeepDoor[] doors = keeps.SelectMany(k => k.Doors.Values).Where(d => d.ObjectState == GameObject.eObjectState.Active).ToArray();
            var candidates = doors.Select(d =>
            {
                Vector3 at = new(d.X, d.Y, d.Z);
                bool atDoor = bot.IsWithinRadius(d, WorldMgr.INTERACT_DISTANCE) && Math.Abs(d.Z - bot.Z) <= 160;
                bool justPassed = d == state.Last && now < state.LastUntil;
                // Only doors that pass the cheap tests get the walk check.
                bool reachable = atDoor || !justPassed && Math.Abs(d.Z - bot.Z) <= 500 &&
                    AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, here, at);
                return new Candidate(at, reachable, justPassed);
            }).ToArray();
            int chosen = ChooseDoor(here, leaderPosition, candidates);
            if (chosen < 0) return false;
            state.Door = doors[chosen];
            state.WalkUntil = now + WalkMilliseconds;
            return Walk(bot, leader, ref state, now);
        }

        private static bool Walk(GameBot bot, GamePlayer leader, ref State state, long now)
        {
            GameKeepDoor door = state.Door;
            if (bot.IsWithinRadius(door, WorldMgr.INTERACT_DISTANCE) && Math.Abs(door.Z - bot.Z) <= 160)
            {
                bot.StopMovingOnPath();
                bot.StopMoving();
                bool passed = door.TryTraverse(bot);
                state.Door = null;
                state.Next = now + CheckMilliseconds;
                if (passed)
                {
                    state.Last = door;
                    state.LastUntil = now + SameDoorMilliseconds;
                    state.SettleUntil = now + SettleMilliseconds;
                    bot.ForcePathReplot();
                    if (Log.IsInfoEnabled)
                        Log.Info($"COMPANION_KEEP_DOOR bot={bot.Name} leader={leader.Name} keep=\"{door.Component?.Keep?.Name}\" door={door.DoorId} " +
                                 $"region={bot.CurrentRegionID} x={bot.X} y={bot.Y} z={bot.Z}");
                }
                States[bot] = state;
                return passed;
            }
            States[bot] = state;
            bot.PathTo(new Vector3(door.X, door.Y, door.Z), bot.MaxSpeed);
            return true;
        }
    }
}
