using System;

namespace DOL.GS.PropertyCalc
{
    /// <summary>
    /// The health regen rate calculator
    /// 
    /// BuffBonusCategory1 is used for all buffs
    /// BuffBonusCategory2 is used for all debuffs (positive values expected here)
    /// BuffBonusCategory3 unused
    /// BuffBonusCategory4 unused
    /// BuffBonusMultCategory1 unused
    /// </summary>
    [PropertyCalculator(eProperty.EnduranceRegenerationAmount)]
    public class EnduranceRegenerationAmountCalculator : PropertyCalculator
    {
        public EnduranceRegenerationAmountCalculator() { }

        public static double ActiveRegeneration(double buffs, double ability, double items, int debuff, bool freeTireless, int paidTireless = 0)
        {
            // Native Tireless is a floor, not another stacking point, and a paid Tireless realm ability (paidTireless, its
            // ability bonus) is not counted again on top of the free grant. Every other ability bonus, such as the Champion
            // Level 5 passive, stacks (issue #10: it used to be lost behind the floor's Math.Max).
            if (!freeTireless)
                return buffs + ability + items - Math.Abs(debuff);
            double paid = Math.Clamp(paidTireless, 0, Math.Max(0, ability));
            return Math.Max(buffs + items, Math.Max(1, paid)) + (ability - paid) - Math.Abs(debuff);
        }

        /// <summary>The ability bonus a paid Tireless realm ability gives (0 without it).</summary>
        public static int PaidTireless(GameLiving living)
        {
            RealmAbilities.RAEndRegenEnhancer tireless = living.GetAbility<RealmAbilities.RAEndRegenEnhancer>();
            return tireless == null ? 0 : tireless.GetAmountForLevel(tireless.Level);
        }

        public override int CalcValue(GameLiving living, eProperty property)
        {
            // In 1.65, tireless is supposed to run on its own timer and regenerate 1 endurance per (9 - level) seconds. Works in and out combat, but is notoriously bad.
            // The way endurance regeneration currently works doesn't allow for it to be implemented that way and we're still using Atlas' version of tireless instead.
            // It has a max level of 1, and always adds 1 endurance per tick.

            /*    Patch 1.87 - COMBAT AND REGENERATION CHANGES
                - The bonus to regeneration while standing out of combat has been greatly increased. The amount of ticks 
                    a player receives while standing has been doubled and it will now match the bonus to regeneration while sitting.
                    Players will no longer need to sit to regenerate faster.
                - Fatigue now regenerates at the standing rate while moving.
            */

            int debuff = living.SpecBuffBonusCategory[property];

            if (debuff < 0)
                debuff = -debuff;

            // Buffs allow to regenerate endurance even in combat and while moving.
            double regen = ActiveRegeneration(living.BaseBuffBonusCategory[property], living.AbilityBonus[property],
                living.ItemBonus[property], debuff, living.HasAbility(Abilities.Tireless), PaidTireless(living));

            regen += ModernItemBonusRules.MythicalRegen(living, property);
            // Buffs, items, abilities and mythirian regen also stack on the out-of-combat refill floor (issue #10).
            double bonus = regen;
            bool playerRules = living is GamePlayer || living is GameBot;
            if (playerRules)
                regen += ClassicRestRegeneration.BaseEndurancePerTick(living.IsSitting, living.InCombat, living.IsMoving);

            double modifier = ServerProperties.Properties.ENDURANCE_REGEN_AMOUNT_MODIFIER;
            regen *= modifier;
            return OutOfCombatRecovery.Amount(living, property, Math.Max(0, (int) regen), bonus, modifier);
        }
    }
}
