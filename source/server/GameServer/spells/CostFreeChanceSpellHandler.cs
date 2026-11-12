using System;

namespace DOL.GS.Spells
{
    /// <summary>
    /// The Champion's Valor auras (1.129: Champion's Ambience 2% ... Champion's Aura 10%): while the pulsing group effect
    /// is on, each spell has Value% chance to cost no power and each style Value% chance to cost no endurance
    /// (<see cref="CostFreeChance"/>). Before DR-101 they were siege-damage songs.
    /// </summary>
    [SpellHandler(eSpellType.CostFreeChance)]
    public class CostFreeChanceSpellHandler(GameLiving caster, Spell spell, SpellLine line) : SpellHandler(caster, spell, line)
    {
        public override string ShortDescription => $"Group members' spells and styles have a {Spell.Value}% chance to cost no power or endurance.";
    }

    /// <summary>Reads the best active cost-free aura on a living. Pure but for the effect list.</summary>
    public static class CostFreeChance
    {
        /// <summary>The live values are 2-10%; anything above is clamped so bad data cannot make every cast free.</summary>
        public const int MaximumPercent = 50;

        public static int Percent(GameLiving living)
        {
            if (living?.effectListComponent == null)
                return 0;
            int best = 0;
            foreach (ECSGameSpellEffect effect in living.effectListComponent.GetSpellEffects(eEffect.CostFreeChance))
            {
                if (effect.IsActive && effect.SpellHandler?.Spell != null)
                    best = Math.Max(best, (int) effect.SpellHandler.Spell.Value);
            }
            return Math.Clamp(best, 0, MaximumPercent);
        }

        /// <summary>True when this cast or style is free: one roll per cost, against the best aura on the living.</summary>
        public static bool Rolls(GameLiving living) => Rolls(Percent(living), Util.Random(1, 100));

        /// <summary>The roll itself: <paramref name="roll"/> is 1-100.</summary>
        public static bool Rolls(int percent, int roll) => percent > 0 && roll <= percent;
    }
}
