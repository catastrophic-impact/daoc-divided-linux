using DOL.GS;

namespace DOL.GS.Spells
{
    /// <summary>
    /// +Value to one specialization skill, the skill named by the spell's "BuffProperty" custom value (an eProperty
    /// number, e.g. 52 Skill_Sword). Official item procs such as "Skill buff: Sword, Bonus 10%" (Darkness Falls and
    /// Bountycraft weapons). Value is added like the world's other single skill item buffs (FlexibleSkillBuff 803380,
    /// StaffSkillBuff): +10 for the official "10%". Buffs to different skills stack; the same skill overwrites.
    /// </summary>
    [SpellHandler(eSpellType.SpecSkillBuff)]
    public class SpecSkillBuff(GameLiving caster, Spell spell, SpellLine line) : SingleStatBuff(caster, spell, line)
    {
        public const string PropertyKey = "BuffProperty";

        public static eProperty PropertyOf(Spell spell)
        {
            if (spell == null)
                return eProperty.Undefined;

            var result = spell.TryGetParamSingle(PropertyKey, out int property);
            bool found = result is CustomParamsExtensions.RetrievalResult.Success or CustomParamsExtensions.RetrievalResult.SuccessFromConversion;
            return found && IsSkill((eProperty) property) ? (eProperty) property : eProperty.Undefined;
        }

        /// <summary>Only specialization skills may be buffed this way, never stats, resists or caps.</summary>
        public static bool IsSkill(eProperty property) =>
            property is >= eProperty.Skill_First and <= eProperty.Skill_Last;

        public override eProperty Property1 => PropertyOf(Spell);

        public override string ShortDescription => $"Increases {TargetPronoun} {PropertyToString(Property1)} by {Spell.Value}.";

        public override bool HasConflictingEffectWith(ISpellHandler compare)
        {
            if (compare.Spell.SpellType == eSpellType.SpecSkillBuff && PropertyOf(compare.Spell) != PropertyOf(Spell))
                return false;

            return base.HasConflictingEffectWith(compare);
        }

        public override void ApplyEffectOnTarget(GameLiving target)
        {
            if (Property1 == eProperty.Undefined)
                return;   // a row without a valid BuffProperty does nothing rather than buff property 0

            base.ApplyEffectOnTarget(target);
        }
    }
}
