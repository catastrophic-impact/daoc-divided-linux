using System;
using System.Collections.Concurrent;
using System.Linq;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// Drops Atlantean Glass from mobs in Atlantis. A world only uses it when it has a LootGenerator
    /// row for `DOL.GS.LootGeneratorAtlanteanGlass`. The regions are a server property, so a new
    /// Atlantis region does not need a build.
    /// </summary>
    public class LootGeneratorAtlanteanGlass : LootGeneratorBase
    {
        public const string GlassItemId = "atlanteanglass";
        public const int NamedCount = 5;

        private static readonly Lazy<DbItemTemplate> _glass = new(() => GameServer.Database.FindObjectByKey<DbItemTemplate>(GlassItemId));
        private static string _regionText;
        private static ushort[] _regions = [];

        public override LootList GenerateLoot(GameNPC mob, GameObject killer)
        {
            return GenerateLoot(LootContext.For(mob, killer));
        }

        public override LootList GenerateLoot(LootContext context)
        {
            GameNPC mob = context.Mob;
            LootList loot = base.GenerateLoot(mob, context.Killer);

            try
            {
                GameLiving owner = context.Owner;

                if (owner == null || _glass.Value == null || !InAtlantis(mob.CurrentRegionID) || mob.Level < ServerProperties.Properties.ARTIFACT_GLASS_MIN_LEVEL || owner.GetConLevel(mob) <= -3)
                    return loot;

                if (Util.Chance(DropChance(mob) / 100.0))
                    loot.AddFixed(_glass.Value, IsNamed(mob) ? NamedCount : 1);
            }
            catch
            {
                return loot;
            }

            return loot;
        }

        private static bool InAtlantis(ushort region)
        {
            string text = ServerProperties.Properties.ARTIFACT_GLASS_REGIONS;

            if (text != _regionText)
            {
                _regions = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(value => ushort.TryParse(value, out ushort id) ? id : (ushort) 0)
                    .Where(id => id != 0)
                    .ToArray();
                _regionText = text;
            }

            return _regions.Contains(region);
        }

        // Most spawns seen at once of a capitalised name in a region. It only grows, so a camp that is being killed
        // off does not turn its last members into bosses.
        private static readonly ConcurrentDictionary<(ushort Region, string Name), int> _mostSpawns = new();

        /// <summary>
        /// A boss for glass (always drops, NamedCount): an epic NPC, or a capitalised name with at most
        /// artifact_glass_named_max_spawns spawns in its region. Capitalised trash ("Statue Warrior" x30,
        /// "Uamhas' Brood" x47) drops like any other monster (DR-238). 0 turns the spawn limit off.
        /// </summary>
        public static bool IsGlassBoss(bool epic, bool capitalised, int spawnsOfName, int maxSpawns)
        {
            return epic || capitalised && (maxSpawns <= 0 || spawnsOfName <= maxSpawns);
        }

        private static bool IsNamed(GameNPC mob)
        {
            if (mob is GameEpicNPC)
                return true;

            if (!LootContext.IsNamedMob(mob))
                return false;

            int max = ServerProperties.Properties.ARTIFACT_GLASS_NAMED_MAX_SPAWNS;

            if (max <= 0)
                return true;

            var key = (mob.CurrentRegionID, mob.Name);

            // Known trash needs no second look; a boss is counted again each kill in case more of it have spawned.
            if (!_mostSpawns.TryGetValue(key, out int most) || most <= max)
            {
                int live = WorldMgr.GetNPCsByNameFromRegion(mob.Name, mob.CurrentRegionID, mob.Realm)?.Length ?? 0;
                most = _mostSpawns.AddOrUpdate(key, live, (_, old) => Math.Max(old, live));
            }

            return IsGlassBoss(false, true, most, max);
        }

        /// <summary>Drop chance in percent.</summary>
        public static double DropChance(GameNPC mob)
        {
            return IsNamed(mob) ? 100 : ServerProperties.Properties.LOOTGENERATOR_ATLANTEANGLASS_BASE_CHANCE;
        }
    }
}
