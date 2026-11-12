namespace DOL.GS.Spells
{
    /// <summary>
    /// Champion Stoutness..Vitality of Valor and Thane Minor Health..Bulwark of Thor (live 1.128c): a group chant raising
    /// max health and armor factor by Value%. It pulses like the Paladin chants; the strongest active rank counts.
    /// </summary>
    [SpellHandler(eSpellType.HealthArmorChant)]
    public class HealthArmorChantSpellHandler(GameLiving caster, Spell spell, SpellLine line) : SpellHandler(caster, spell, line)
    {
        public override string ShortDescription => $"Increases the group's maximum health and armor factor by {Spell.Value}%.";

        /// <summary>The percent <paramref name="living"/>'s max health and armor factor are raised by: its strongest chant, or 0.</summary>
        public static int Percent(GameLiving living)
        {
            if (living?.effectListComponent == null)
                return 0;

            int best = 0;
            foreach (ECSGameSpellEffect effect in living.effectListComponent.GetSpellEffects(eEffect.HealthArmorChant))
            {
                if (effect.IsActive)
                    best = System.Math.Max(best, (int) effect.SpellHandler.Spell.Value);
            }

            return best;
        }

        /// <summary>Chant percent plus the Cabalist pet sacrifice's (DR-094), as a multiplier.</summary>
        public static double Scale(GameLiving living) => 1 + (Percent(living) + SimulacrumSacrificeSpellHandler.HealthArmorPercent(living)) * 0.01;
    }
}
