using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// What a loot roll needs to know about one kill, worked out once and shared by every generator
    /// (loot redesign phase 1, owner 2026-11-05). Generators that override
    /// <see cref="LootGeneratorBase.GenerateLoot(LootContext)"/> read it; older ones still get mob and killer.
    /// </summary>
    public sealed class LootContext
    {
        private IReadOnlyList<GameLiving> _participants;

        private LootContext(GameNPC mob, GameObject killer, GameLiving owner)
        {
            Mob = mob;
            Killer = killer;
            Owner = owner;
        }

        public GameNPC Mob { get; }

        public GameObject Killer { get; }

        /// <summary>
        /// Who the kill's item loot belongs to: a player, an autonomous gamebot or an account companion, with pets
        /// folded into their owner and a temporary /spawn helper into its player. Null when nobody may get items.
        /// </summary>
        public GameLiving Owner { get; }

        public Group Group => Owner?.Group;

        public BattleGroup BattleGroup =>
            (Owner as GamePlayer)?.TempProperties.GetProperty<BattleGroup>(BattleGroup.BATTLEGROUP_PROPERTY);

        public bool IsSolo => Owner != null && Group == null && BattleGroup == null;

        public bool IsNamed => IsNamedMob(Mob);

        public bool IsFrontier => Owner?.CurrentZone?.IsOF ?? false;

        /// <summary>The owner and, in a group, every member who rolls for loot and is within sight of the mob.</summary>
        public IReadOnlyList<GameLiving> Participants => _participants ??= FindParticipants();

        public int ConFor(GameLiving living) => living.GetConLevel(Mob);

        public static LootContext For(GameNPC mob, GameObject killer)
        {
            return new LootContext(mob, killer, ResolveOwner(killer));
        }

        /// <summary>Named mobs (capitalized names) and epic NPCs.</summary>
        public static bool IsNamedMob(GameNPC mob)
        {
            return mob is GameEpicNPC || mob?.Name != null && !mob.Name.ToLower().Equals(mob.Name);
        }

        /// <summary>Players, autonomous gamebots and account companions roll for loot; temporary helpers do not.</summary>
        public static bool IsLootParticipant(GameLiving living)
        {
            // Account companions are real characters and roll for loot like any group member.
            return living is GamePlayer || living is GameBot { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false } ||
                   living is GameBot { IsAccountCompanion: true };
        }

        public static IEnumerable<GameLiving> GroupLootParticipants(Group group)
        {
            return group.GetMembersInTheGroup().Where(IsLootParticipant);
        }

        public static GameLiving ResolveOwner(GameObject killer)
        {
            GameLiving owner = ResolveKiller(killer);
            // Helpers never own loot; their real owner may receive the kill's
            // normal loot through the existing reward-owner/pickup path.
            if (owner is GameBot { IsTemporaryGroupHelper: true, IsAccountCompanion: false } helper)
                owner = helper.Owner;
            return IsLootParticipant(owner) ? owner : null;
        }

        /// <summary>A real player, autonomous bot, or either one's controlled pet resolved to its owner.</summary>
        public static GameLiving ResolveKiller(GameObject killer)
        {
            if (killer is GamePlayer || killer is GameBot)
                return killer as GameLiving;

            if (killer is GameNPC npc && npc.Brain is IControlledBrain controlled)
                return controlled.GetLivingOwner() ?? controlled.GetPlayerOwner();

            return null;
        }

        private IReadOnlyList<GameLiving> FindParticipants()
        {
            if (Owner == null)
                return [];

            if (Group == null)
                return [Owner];

            return GroupLootParticipants(Group)
                .Where(member => member == Owner || member.GetDistance(Mob) <= WorldMgr.VISIBILITY_DISTANCE)
                .ToList();
        }
    }
}
