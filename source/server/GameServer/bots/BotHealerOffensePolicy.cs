namespace DOL.GS
{
    public enum BotHarmfulSpellKind { Damage, CrowdControl, Debuff }

    /// <summary>
    /// When a primary healer (Cleric, Druid, Healer, Shaman) spends power on harmful spells. Night playtest 2026-11-04: Midgard
    /// Healer bots cast Hinder Spirit (a level 2 attack speed debuff) 964 times, Halt Attack 372 and Suspend Charge 289 against
    /// 217 casts of their best heal, and died at 19% power on average. Heals always come first (CheckHeals runs before any
    /// attack spell); this keeps the power for them. Add control (TryPveAddControl: a mez or stun on an add) is separate and
    /// untouched. Pure.
    /// </summary>
    public static class BotHealerOffensePolicy
    {
        public const int GroupDebuffPowerPercent = 70;
        public const int SoloDebuffPowerPercent = 40;
        public const int GroupDamagePowerPercent = 50;
        public const int SoloCrowdControlPowerPercent = 25;
        /// <summary>A party member under this health means the healer keeps its power for the next heal.</summary>
        public const int HurtMemberHealthPercent = 75;

        public static bool IsPrimaryHealer(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Cleric or eCharacterClass.Druid or eCharacterClass.Healer or eCharacterClass.Shaman;

        public static BotHarmfulSpellKind Kind(Spell spell) =>
            BotCasterPriority.IsDamage(spell) ? BotHarmfulSpellKind.Damage
            : spell?.SpellType is eSpellType.Stun or eSpellType.Mez or eSpellType.Mesmerize ? BotHarmfulSpellKind.CrowdControl
            : BotHarmfulSpellKind.Debuff;

        /// <summary>
        /// A healer in a party casts nothing harmful while a member is hurt; otherwise nukes from half power, debuffs from 70%
        /// and stuns or mezzes only the monster that is on the healer itself. A solo healer nukes freely, debuffs from 40% and
        /// controls from 25%. Every other class is unaffected.
        /// </summary>
        public static bool Allows(BotHarmfulSpellKind kind, bool primaryHealer, bool grouped, int powerPercent,
            bool memberHurt, bool targetOnHealer)
        {
            if (!primaryHealer)
                return true;
            if (grouped && memberHurt)
                return false;
            return kind switch
            {
                BotHarmfulSpellKind.Damage => !grouped || powerPercent >= GroupDamagePowerPercent,
                BotHarmfulSpellKind.CrowdControl => targetOnHealer || !grouped && powerPercent >= SoloCrowdControlPowerPercent,
                _ => powerPercent >= (grouped ? GroupDebuffPowerPercent : SoloDebuffPowerPercent),
            };
        }
    }
}
