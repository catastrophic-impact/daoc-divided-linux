using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using DOL.GS.ServerProperties;

namespace DOL.AI.Brain
{
    /// <summary>
    /// Who comes when a monster brings a friend (owner 2026-10-28). OpenDAoC counts only monsters of the same or an
    /// allied faction, so a monster with no faction never brought anyone: two thirds of the aggressive monsters in the
    /// database have none (Midgard's frore liches, windswept wraiths and chillsome wights among them). A monster with no
    /// faction now brings others of its own kind, as monsters did on live (wraiths bring wraiths): faction-less monsters
    /// of the same name. Monsters with a faction keep the faction rule.
    /// </summary>
    public static class BafFriends
    {
        [ServerProperty("pve", "baf_same_name_without_faction",
            "Does a monster with no faction bring others of its own kind (faction-less monsters with the same name) when it brings a friend? Monsters with a faction always use their faction.", true)]
        public static bool SameNameWithoutFaction = true;

        /// <summary>Pure rule: the faction when the pulled monster has one, otherwise the same name among faction-less monsters.</summary>
        public static bool IsFriend(bool pulledHasFaction, bool factionFriends, bool otherHasFaction, bool sameName, bool sameNameFallback) =>
            pulledHasFaction ? factionFriends : sameNameFallback && !otherHasFaction && sameName;

        public static bool CanBringFriends(GameNPC pulled) => pulled.Faction != null || SameNameWithoutFaction;

        /// <summary>
        /// Dawn of Light's Bring A Friend ranges (owner 2026-10-29: OpenDAoC's 450 units plus 150 per extra group member,
        /// up to 1,500, pulled whole rooms; in Cardova's first room adds came from other rooms and hallways). Close friends
        /// are within 2/5 of the pulled monster's aggro range (200 units for the usual 500); in a dungeon more come from up
        /// to 1,000 units, nearest first. How many come is unchanged (by group size). A friend must be in sight of the
        /// pulled monster: no adds through walls or from the next room.
        /// </summary>
        public const int DungeonReinforcementRadius = 1_000;

        public static int CloseRadius(int aggroRange) => Math.Max(0, aggroRange) * 2 / 5;

        /// <summary>
        /// Outdoors: the close range. In a dungeon the reach grows with the group like Dawn of Light's reinforcement
        /// search (from 250 units, 150 more per other member, at most 1,000): a small group no longer reaches 1,000.
        /// </summary>
        public static int Radius(int aggroRange, bool dungeon, int attackers = 1) =>
            dungeon ? Math.Max(CloseRadius(aggroRange), DungeonRadius(attackers)) : CloseRadius(aggroRange);

        public static int DungeonRadius(int attackers) =>
            Math.Min(DungeonReinforcementRadius, 250 + 150 * (Math.Max(1, attackers) - 1));

        private const string BafFightProperty = "OfflineDAoC.BafFight";
        /// <summary>A safety cap: a fight that brought friends never blocks new adds for longer than this.</summary>
        public const long BafFightMaxMilliseconds = 600_000;

        /// <summary>The monsters of the last pull that brought friends (the pulled one and its adds), and when.</summary>
        private sealed class BafFight
        {
            public GameNPC[] Monsters;
            public long Since;
        }

        /// <summary>
        /// Pure rule: a monster of the last pull that brought friends keeps that fight going while it is alive, in the
        /// world and still angry at the puller's side (the player, the group and their pets), for at most
        /// <see cref="BafFightMaxMilliseconds"/>. The old rule was "two minutes since the last adds", and every refused
        /// pull restarted those two minutes, so after one pull that brought friends every monster came alone for as long
        /// as the player kept pulling (True165 b3e8db903).
        /// </summary>
        public static bool StillInFight(bool alive, bool inWorld, bool angryAtPullerSide, long now, long since) =>
            alive && inWorld && angryAtPullerSide && now - since < BafFightMaxMilliseconds;

        /// <summary>
        /// Whether the puller's side is still in the fight that last brought friends (owner 2026-10-29: in Keltoi Fogou a
        /// respawned group joined a fight already under way): one of that pull's monsters is alive and still angry at
        /// them. When none is, the record is dropped and the next pull brings friends as usual.
        /// </summary>
        public static bool InBafFight(GamePlayer puller)
        {
            if (puller == null)
                return false;
            BafFight fight = puller.TempProperties.GetProperty<BafFight>(BafFightProperty);
            if (fight?.Monsters == null)
                return false;
            long now = GameLoop.GameLoopTime;
            List<GameLiving> side = null;
            foreach (GameNPC monster in fight.Monsters)
            {
                if (monster == null)
                    continue;
                side ??= PullerSide(puller);
                bool angry = monster.Brain is StandardMobBrain brain && brain.HasAggro && side.Any(brain.HasAggroOn);
                if (StillInFight(monster.IsAlive, monster.ObjectState == GameObject.eObjectState.Active, angry, now, fight.Since))
                    return true;
            }
            puller.TempProperties.RemoveProperty(BafFightProperty);
            return false;
        }

        /// <summary>Remembers the monsters of a pull that brought friends: the pulled one and its adds.</summary>
        public static void MarkBafFight(GamePlayer puller, GameNPC pulled, IEnumerable<GameNPC> adds)
        {
            if (puller == null)
                return;
            var monsters = new List<GameNPC>();
            if (pulled != null)
                monsters.Add(pulled);
            if (adds != null)
                monsters.AddRange(adds.Where(add => add != null));
            puller.TempProperties.SetProperty(BafFightProperty, new BafFight { Monsters = monsters.ToArray(), Since = GameLoop.GameLoopTime });
        }

        /// <summary>The puller, its group's members (players and companions) and their pets.</summary>
        private static List<GameLiving> PullerSide(GamePlayer puller)
        {
            var side = new List<GameLiving>();
            List<GameLiving> members = puller.Group?.GetMembersInTheGroup();
            if (members == null || members.Count == 0)
                members = [puller];
            foreach (GameLiving member in members)
            {
                if (member == null)
                    continue;
                side.Add(member);
                if (member.ControlledBrain?.Body is GameNPC pet)
                    side.Add(pet);
            }
            return side;
        }

        /// <summary>In sight along the walkable ground (the navmesh); where there is no navmesh, assumed so.</summary>
        public static bool InSight(GameNPC pulled, GameNPC other)
        {
            var nav = PathfindingProvider.Instance;
            Zone zone = pulled?.CurrentZone;
            if (nav == null || !nav.IsAvailable || zone == null || !nav.HasNavmesh(zone))
                return true;
            return nav.HasLineOfSight(zone, new System.Numerics.Vector3(pulled.X, pulled.Y, pulled.Z),
                new System.Numerics.Vector3(other.X, other.Y, other.Z), nav.DefaultFilters);
        }

        public static bool IsFriend(GameNPC pulled, GameNPC other) =>
            other != null && pulled != null && other is not GameBot &&
            IsFriend(pulled.Faction != null, pulled.Faction != null && other.IsFriend(pulled), other.Faction != null,
                string.Equals(pulled.Name, other.Name, StringComparison.OrdinalIgnoreCase), SameNameWithoutFaction);
    }
}
