using DOL.Database;
using DOL.GS.Spells;

namespace DOL.GS
{
    /// <summary>
    /// The Storm Regalia: the Storm Sovereign's own armor and cloak (the Stormforged models 4929-4933, winged helm 4942 and
    /// wolf cloak 4937). Item rows come from source/tools/sovereign/storm_regalia_edits.py; the Crownkeeper hands out
    /// missing pieces at level 50.
    /// </summary>
    public static class StormRegalia
    {
        public const string Prefix = "storm_regalia_";
        public const int GrantLevel = RegaliaSet.GrantLevel;
        public const string GrantKeyword = "Storm Regalia";

        public static RegaliaSet Set { get; } = new("Storm Regalia", Prefix, GrantKeyword, StormforgedRules.IsStormforged,
            "Wear the storm, sovereign. All seven pieces together hold its full strength.", new (int, string, MLMod[])[]
            {
                (2, "+5% to all resistances", MLMod.Resists(MLCategory.Ability, 5, physical: true)),
                (4, "+10% melee and style damage", new[] { new MLMod(eProperty.MeleeDamage, MLCategory.Ability, 10), new MLMod(eProperty.StyleDamage, MLCategory.Ability, 10) }),
                (6, "+10% armor absorption and spell damage", new[] { new MLMod(eProperty.ArmorAbsorption, MLCategory.Ability, 10), new MLMod(eProperty.SpellDamage, MLCategory.Ability, 10) }),
                (7, "+10% maximum health, and every block or parry builds 2 Tempest", new[] { new MLMod(eProperty.Of_Toughness, MLCategory.Ability, 10) }),
            });

        public static string[] Pieces => Set.Pieces;
        public static System.Collections.Generic.IReadOnlyList<(int Pieces, string Text, MLMod[] Mods)> Tiers => Set.Tiers;
        public static bool IsPiece(DbInventoryItem item) => Set.IsPiece(item);
        public static int Worn(GamePlayer player, DbInventoryItem arriving = null, DbInventoryItem leaving = null) => Set.Worn(player, arriving, leaving);
        public static bool HasFullSet(GamePlayer player) => Set.HasFullSet(player);

        /// <summary>Tempest for a block or parry: 1, or 2 with the full set.</summary>
        public static int DefenseTempest(GamePlayer player) => player != null && HasFullSet(player) ? 2 : 1;

        public static void Refresh(GamePlayer player, DbInventoryItem arriving = null, DbInventoryItem leaving = null) => Set.Refresh(player, arriving, leaving);
        public static void Grant(GamePlayer player, GameNPC giver) => Set.Grant(player, giver);
    }
}
