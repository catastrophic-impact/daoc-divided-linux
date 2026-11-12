using System;

namespace DOL.GS.PropertyCalc
{
    [PropertyCalculator(eProperty.MagicAbsorption)]
    public class MagicAbsorptionCalculator : PropertyCalculator
    {
        public override int CalcValue(GameLiving living, eProperty property)
        {
            // Placeholder, caps and usages unknown.
            int abilityBonus = living.AbilityBonus[property];
            int itemBonus = living.ItemBonus[property];
            int buffBonus = living.BaseBuffBonusCategory[property] + living.SpecBuffBonusCategory[property];
            int debuffMalus = living.DebuffCategory[property];
            return abilityBonus + buffBonus + itemBonus - debuffMalus + TankMagicResistance.For(living);
        }
    }

    /// <summary>
    /// Patch 1.81 (DR-189): from level 41 the Armsman, Warrior and Hero gain magic resistance that lowers spell damage
    /// only, reaching the full 15% at level 50. The notes give "2%-3% per level" without a table, so the steps below
    /// spread 15 points over levels 41 to 50; only the 15% at 50 is an official number.
    /// </summary>
    public static class TankMagicResistance
    {
        public const int FirstLevel = 41;
        public const int FullLevel = 50;
        public const int FullPercent = 15;

        public static bool HasIt(eCharacterClass characterClass) =>
            characterClass is eCharacterClass.Armsman or eCharacterClass.Warrior or eCharacterClass.Hero;

        public static int Percent(eCharacterClass characterClass, int level)
        {
            if (!HasIt(characterClass) || level < FirstLevel)
                return 0;

            int steps = Math.Min(level, FullLevel) - FirstLevel + 1;
            return (int) Math.Round(FullPercent * steps / (double) (FullLevel - FirstLevel + 1), MidpointRounding.AwayFromZero);
        }

        /// <summary>Real players, companion bots and gamebots all get it: they share the player classes.</summary>
        public static int For(GameLiving living)
        {
            ICharacterClass characterClass = living switch
            {
                GamePlayer player => player.CharacterClass,
                GameBot bot => bot.CharacterClass,
                _ => null
            };

            return characterClass == null ? 0 : Percent((eCharacterClass) characterClass.ID, living.Level);
        }
    }
}
