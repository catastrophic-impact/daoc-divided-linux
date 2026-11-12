namespace DOL.GS.PropertyCalc
{
    /// <summary>
    /// The player parry, block and evade formulas (per-mille, before buffs).
    /// Real players and GameBots (autonomous gamebots and /spawn companions)
    /// both use these, so a bot's defense comes from its class abilities, specs,
    /// level and equipment exactly like a player's. Ordinary NPCs keep their
    /// template ParryChance / BlockChance / EvadeChance.
    /// </summary>
    public static class PlayerDefenseFormula
    {
        /// <summary>Living objects that use the player defense rules.</summary>
        public static bool UsesPlayerDefense(GameObject living) => living is GamePlayer or GameBot;

        /// <summary>
        /// Share of a target's block and evade that is left against a player or bot wielding two weapons.
        /// 1.109 and the 2018-08-17 Grab Bag: they lower block and evade by 25% (DR-191).
        /// </summary>
        public const double DualWieldDefenseFactor = 0.75;

        /// <summary>RvR: several attackers do not lower a defender's parry (2020-10-23 Grab Bag, DR-193).</summary>
        public static bool IgnoresAttackerCountForParry(GameObject attacker, GameObject defender) =>
            UsesPlayerDefense(attacker) && UsesPlayerDefense(defender);

        /// <summary>
        /// Largest extra damage of a spell critical hit: 50% against players, bots and Necromancer pets
        /// (1.78: bolts no longer crit for up to 100% on Necromancer pets, DR-195), 100% against monsters.
        /// </summary>
        public static double MaxSpellCriticalBonus(GameObject target) =>
            UsesPlayerDefense(target) || target is NecromancerPet ? 0.5 : 1.0;

        /// <summary>Parry: only with the Parry specialization. 5% base, +0.5% per spec level above 1, dexterity bonus.</summary>
        public static int Parry(int dexterity, bool hasParrySpec, int modifiedParrySpec) =>
            hasParrySpec ? (dexterity * 2 - 100) / 4 + (modifiedParrySpec - 1) * (10 / 2) + 50 : 0;

        /// <summary>Block: 5% base, +0.5% per Shields level above 1, dexterity bonus (the Shield ability and a shield are checked in TryBlock).</summary>
        public static int Block(int dexterity, int modifiedShieldSpec) =>
            (dexterity * 2 - 100) / 4 + (modifiedShieldSpec - 1) * (10 / 2) + 50;

        /// <summary>Evade: only with the Evade ability; grows with its level, quickness and dexterity.</summary>
        public static int Evade(int quickness, int dexterity, int evadeAbilityLevel) =>
            evadeAbilityLevel > 0 ? (900 + quickness + dexterity) * evadeAbilityLevel * 5 / 100 : 0;
    }
}
