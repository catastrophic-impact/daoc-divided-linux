using System;

namespace DOL.GS
{
    /// <summary>
    /// Bot affordability mirrors native costs without guessing free-cast procs.
    /// Only native settlement rolls free-cast effects, never AI evaluation.
    /// Quickcast is never used by a GameBot. Human spell costs are unchanged.
    /// </summary>
    public static class BotSpellPower
    {
        public static bool IsOffensiveCaster(GameBot bot) => bot.CharacterClass?.ClassType == eClassType.ListCaster &&
            bot.CharacterClass.ID is not (int)eCharacterClass.Valewalker and not (int)eCharacterClass.Vampiir;

        // A focus root channels until canceled, spending power while preventing
        // the aggressive caster rotation. Use ordinary roots/DoTs/nukes instead.
        // Human spell selection and damaging channels are unaffected.
        public static bool BlocksAttackerRotation(GameBot bot, Spell spell) =>
            IsOffensiveCaster(bot) && spell?.IsFocus == true &&
            spell.IsHarmful && spell.Damage <= 0;

        // A native channeled spell pays on each real pulse, not every AI tick.
        // Preserve the spell's native upkeep cost for bots and players alike.
        // Negative upkeep is a percentage of maximum power, like a negative Power
        // (the Valkyrie's Odin's Retribution chant costs 9% per pulse).
        public static int PulseCost(GameLiving caster, Spell spell) =>
            spell.PulsePower >= 0 ? spell.PulsePower : PulseCost(caster.MaxMana, spell.PulsePower);

        public static int PulseCost(int maxMana, int pulsePower) =>
            pulsePower >= 0 ? pulsePower : (int)Math.Ceiling(maxMana * -pulsePower / 100.0);

        public static int Cost(GameBot bot, Spell spell, SpellLine line)
        {
            if (spell == null) return 0;
            // These native handlers use health/endurance instead of mana.
            if (bot.CharacterClass?.ID == (int)eCharacterClass.Savage ||
                spell.SpellType is eSpellType.Archery)
                return 0;

            line = bot.ResolvePowerSpellLine(spell, line);
            // ResurrectSpellHandler charges a share of maximum power, not the spell's Power field. Using
            // Power let bots start raises they could not pay for; the native check then refused them without
            // a message (overnight run 2026-11-08: most raises of every class "refused without a message").
            // Without a corpse, price a raise of a same-level corpse; callers with one use ResurrectCost.
            if (spell.SpellType == eSpellType.Resurrect)
                return line?.Name == GlobalSpellsLines.Realm_Spells ? 0 : ResurrectCost(bot, null);
            double cost = spell.Power;
            if (cost < 0)
            {
                int basePool = bot.CharacterClass?.ManaStat is eStat stat && stat != eStat.UNDEFINED
                    ? bot.CalculateMaxMana(bot.Level, bot.GetBaseStat(stat))
                    : bot.MaxMana;
                cost *= basePool * -0.01;
            }

            if (bot.CharacterClass?.IsFocusCaster == true && line != null)
            {
                eProperty focusProperty = SkillBase.SpecToFocus(line.Spec);
                if (focusProperty != eProperty.Undefined)
                {
                    double focus = bot.GetModified(focusProperty) * 0.4;
                    if (spell.Level > 0) focus /= spell.Level;
                    focus = Math.Clamp(focus, 0, 0.4);
                    focus *= Math.Min(1, bot.GetModifiedSpecLevel(line.Spec) / (double)Math.Max(1, spell.Level));
                    cost *= 1.2 - focus;
                }
            }

            return Math.Max(0, (int)cost);
        }

        /// <summary>The power ResurrectSpellHandler.PowerCost charges to raise <paramref name="corpse"/> (same level when null).</summary>
        public static int ResurrectCost(GameLiving caster, GameLiving corpse) =>
            ResurrectCost(caster.MaxMana, caster.Level, corpse?.Level ?? caster.Level);

        public static int ResurrectCost(int maxMana, int casterLevel, int corpseLevel) =>
            (int)(maxMana * Math.Max(.1f, .5f + (corpseLevel - casterLevel) / (float)Math.Max(1, casterLevel)));
    }
}
