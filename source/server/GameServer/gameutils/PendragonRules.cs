using DOL.GS.Spells;

namespace DOL.GS
{
    /// <summary>Albion and Hibernia identities; all three realms intentionally use one combat balance table.</summary>
    public static class PendragonRules
    {
        public const int SpellOffset = 1600;
        public const int TuathaOffset = 10600;
        public static bool IsPendragon(int classId) => classId == (int)eCharacterClass.PendragonSovereign;
        public static bool IsTuatha(int classId) => classId == (int)eCharacterClass.TuathaSovereign;
        public static bool IsSovereign(int classId) => IsPendragon(classId) || IsTuatha(classId) || StormforgedRules.IsStormforged(classId);
        public static bool IsPendragon(GameLiving living) => ClassOf(living) is int id && IsPendragon(id);
        public static bool IsTuatha(GameLiving living) => ClassOf(living) is int id && IsTuatha(id);
        private static int? ClassOf(GameLiving living) => living switch
        {
            GamePlayer player => player.CharacterClass?.ID,
            IGamePlayer bot => bot.CharacterClass?.ID,
            _ => null
        };
        /// <summary>The offset of this owner's realm copy of the Midgard royal spells.</summary>
        public static int RealmOffset(GameLiving living) => IsPendragon(living) ? SpellOffset : IsTuatha(living) ? TuathaOffset : 0;
        /// <summary>The damage type a realm's royal spells and procs deal instead of Midgard's.</summary>
        public static eDamageType RoyalDamage(GameLiving living, eDamageType midgard) =>
            IsPendragon(living) ? eDamageType.Heat : IsTuatha(living) ? eDamageType.Spirit : midgard;
        public static int NormalizeSpell(int id) => id switch
        {
            >= 64000 and <= 64572 => id - SpellOffset,
            >= 73000 and <= 73572 => id - TuathaOffset,
            _ => id
        };
        public static bool IsRoyalRank(int id) => NormalizeSpell(id) is >= 62400 and < 62656;
        // Pendragon remake (2026-11-06): Slash, Polearm, Shields, Parry and Stealth.
        public static bool IsRogSkill(eProperty property) => property is eProperty.Skill_Slashing or eProperty.Skill_Polearms or
            eProperty.Skill_Shields or eProperty.Skill_Parry or eProperty.Skill_Stealth or eProperty.AllMeleeWeaponSkills or eProperty.AllSkills;
        // Tuatha remake (2026-11-06): Scythe, Blades, Shields, Parry and Stealth.
        public static bool IsTuathaSkill(eProperty property) => property is eProperty.Skill_Scythe or eProperty.Skill_Blades or
            eProperty.Skill_Shields or eProperty.Skill_Parry or eProperty.Skill_Stealth or eProperty.AllMeleeWeaponSkills or eProperty.AllSkills;
    }
}
