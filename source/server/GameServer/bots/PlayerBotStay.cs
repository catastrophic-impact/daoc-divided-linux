using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace DOL.GS
{
    /// <summary>
    /// /bots stay (DR-248, requested on Discord 2026-11-09/10): the bots in a player's group hold the spot they stand on
    /// instead of following. They still fight anything that attacks them and still take /bots attack; after a fight they
    /// walk back to their spot. /bots follow or /bots hunt ends it, and so does the player or the bot leaving the region
    /// (a teleport, a zone line, a release), so the usual follow-through-transfer rules bring them along and nobody is
    /// stranded. Companion bots and gamebots in the player's group both take it.
    /// </summary>
    public static class PlayerBotStay
    {
        /// <summary>Closer than this to its spot a bot counts as there; further, it walks back.</summary>
        public const int ArrivalTolerance = 40;

        private sealed class Post
        {
            public GamePlayer Leader;
            public ushort Region;
            public Vector3 Point;
        }

        private static readonly ConditionalWeakTable<GameBot, Post> Posts = new();

        /// <summary>Each bot holds the spot it stands on now. Returns how many bots took the order.</summary>
        public static int Hold(GamePlayer player, IEnumerable<GameBot> bots)
        {
            int count = 0;
            foreach (GameBot bot in bots)
            {
                if (bot == null || !bot.IsAlive)
                    continue;
                Posts.AddOrUpdate(bot, new Post { Leader = player, Region = bot.CurrentRegionID, Point = new Vector3(bot.X, bot.Y, bot.Z) });
                bot.StopFollowing();
                count++;
            }
            return count;
        }

        public static void Release(IEnumerable<GameBot> bots)
        {
            foreach (GameBot bot in bots)
                if (bot != null)
                    Posts.Remove(bot);
        }

        public static bool IsHeld(GameBot bot) => TryGetPost(bot, out _);

        /// <summary>The spot this bot holds, or false when it holds none (an order that no longer applies is dropped here).</summary>
        public static bool TryGetPost(GameBot bot, out Vector3 point)
        {
            point = default;
            if (bot == null || !Posts.TryGetValue(bot, out Post post))
                return false;
            if (!StillHolds(bot.PlayerGroupLeader == post.Leader, post.Region, bot.CurrentRegionID,
                    post.Leader?.CurrentRegionID ?? 0, post.Leader?.ObjectState == GameObject.eObjectState.Active))
            {
                Posts.Remove(bot);
                return false;
            }
            point = post.Point;
            return true;
        }

        /// <summary>An order holds while the same player leads the bot and both are still in the region it was given in. Pure.</summary>
        public static bool StillHolds(bool sameLeader, ushort postRegion, ushort botRegion, ushort leaderRegion, bool leaderInWorld) =>
            sameLeader && leaderInWorld && botRegion == postRegion && leaderRegion == postRegion;

        /// <summary>Whether a bot this far from its spot walks back. Pure.</summary>
        public static bool ShouldWalkBack(double distance) => distance > ArrivalTolerance;

        public static string Names(GamePlayer player, IEnumerable<GameBot> bots)
        {
            var held = new List<string>();
            foreach (GameBot bot in bots)
                if (IsHeld(bot))
                    held.Add(bot.Name);
            return string.Join(", ", held);
        }
    }
}
