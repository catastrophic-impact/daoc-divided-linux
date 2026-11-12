using System;
using System.Runtime.CompilerServices;

namespace DOL.GS
{
    /// <summary>
    /// A gamebot in a player's group that can't reach them comes back. Bots beside the player go along through a teleport
    /// (TemporaryGroupStableTravel.TryBringGroupmateAlong), but one further off stayed behind for good while /bots status
    /// said "Following you": formation following does nothing across regions, and a walk that never arrived was not noticed
    /// (playtest 2026-10-31: a Paladin and a Spiritmaster, the second out of sight after two teleports).
    /// </summary>
    public static class PlayerLedStragglers
    {
        public const int FollowRadius = TemporaryGroupStableTravel.GroupmateTravelRadius;
        /// <summary>A walk this long without getting closer isn't going to arrive.</summary>
        public const long NoProgressMilliseconds = 30_000;
        /// <summary>Across regions there is nothing to walk, so this only lets a transfer finish.</summary>
        public const long OtherRegionMilliseconds = 5_000;
        public const float ProgressStep = 100f;
        /// <summary>About 40 seconds on foot. Further than this, closing the gap isn't following: the walk would take minutes.</summary>
        public const float WalkableDistance = 8_000f;

        private static readonly ConditionalWeakTable<GameBot, Separation> Separations = new();

        /// <summary>How long a bot has been apart from its leader. Pure, so the rule is tested without a world.</summary>
        public sealed class Separation
        {
            private bool _apart;
            private float _closest;
            private long _since;

            /// <returns>True once the bot counts as stranded.</returns>
            public bool Observe(bool sameRegion, float distance, long now)
            {
                if (sameRegion && distance <= FollowRadius)
                {
                    _apart = false;
                    return false;
                }
                if (!_apart)
                {
                    _apart = true;
                    _since = now;
                    _closest = float.MaxValue;
                }
                if (sameRegion && distance <= WalkableDistance && distance < _closest - ProgressStep)
                {
                    _closest = distance;
                    _since = now;
                }
                return now - _since >= (sameRegion ? NoProgressMilliseconds : OtherRegionMilliseconds);
            }
        }

        public static bool IsWith(GameBot bot, GamePlayer leader) =>
            bot.CurrentRegion == leader.CurrentRegion && bot.IsWithinRadius(leader, FollowRadius);

        /// <summary>Called from the follow state. True when the bot was moved to its leader.</summary>
        public static bool TryRegroup(GameBot bot, GamePlayer leader)
        {
            if (!Applies(bot, leader) || PlayerBotStay.IsHeld(bot))     // /bots stay: far from the leader on purpose
                return false;
            bool sameRegion = bot.CurrentRegion == leader.CurrentRegion;
            float distance = sameRegion ? bot.GetDistanceTo(leader) : float.MaxValue;
            if (!Separations.GetOrCreateValue(bot).Observe(sameRegion, distance, GameLoop.GameLoopTime) || bot.InCombat)
                return false;
            Separations.Remove(bot);
            return AutonomousStuckWatchdog.TryRecoverToPlayerLeader(bot, DateTime.UtcNow, "left behind by its leader", "BOT_LEADER_REGROUP");
        }

        /// <summary>/bots follow: the player asked now, so a bot too far to walk in comes without waiting.</summary>
        public static bool RegroupNow(GameBot bot, GamePlayer leader) =>
            Applies(bot, leader) && !IsWith(bot, leader) &&
            AutonomousStuckWatchdog.TryRecoverToPlayerLeader(bot, DateTime.UtcNow, "called back by its leader", "BOT_LEADER_REGROUP");

        private static bool Applies(GameBot bot, GamePlayer leader) =>
            bot is { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false, IsAlive: true } && bot.PlayerGroupLeader == leader &&
            BotGroupInvite.DutyOf(bot) == BotGroupInvite.Duty.None;
    }
}
