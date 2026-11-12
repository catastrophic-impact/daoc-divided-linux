using DOL.GS.PlayerClass;
using DOL.GS.Spells;

namespace DOL.GS
{
    public class StatDebuffECSEffect : ECSGameSpellEffect
    {
        private bool _isForcedToSpecDebuff;

        public StatDebuffECSEffect(in ECSGameEffectInitParams initParams) : base(initParams) { }

        // HearthDAoC: its bonus is added at start and taken off at stop from Effectiveness (#190). Not a snare's: that one
        // is removed by spell ID, and EffectService sets it again from Effectiveness on every tick.
        internal override bool AppliesBonusAtStart => EffectType is not eEffect.MovementSpeedDebuff;

        public override void OnStartEffect()
        {
            base.OnStartEffect();

            // No interrupt here (DR-184). Live: Reaver pulsing debuffs stopped interrupting in 1.72, the Bonedancer
            // instant body debuffs in 1.87, and the instant body and damage debuff lines in 1.111.

            // Force Champion's stat debuffs to be applied as spec debuffs (see `StatCalculator`).
            if (SpellHandler.Caster is GamePlayer playerCaster &&
                playerCaster.CharacterClass is ClassChampion &&
                SpellHandler.SpellLine.KeyName is GlobalSpellsLines.Valor &&
                (EffectHelper.GetPlayerUpdateFromEffect(EffectType) & EffectHelper.PlayerUpdate.Stats) != 0)
            {
                _isForcedToSpecDebuff = true;
            }

            if (EffectType is eEffect.MovementSpeedDebuff)
            {
                double effectiveValue = SpellHandler.Spell.Value * Effectiveness;
                Owner.BuffBonusMultCategory1.Set((int) eProperty.MaxSpeed, this, 1.0 - effectiveValue * 0.01);
                Owner.OnMaxSpeedChange();
            }
            else
            {
                if (SpellHandler is not PropertyChangingSpell propertyChangingSpell)
                    return;

                foreach (eProperty property in EffectHelper.GetPropertiesFromEffect(EffectType))
                    ApplyBonus(Owner, _isForcedToSpecDebuff ? eBuffBonusCategory.SpecDebuff : propertyChangingSpell.BonusCategory1, property, SpellHandler.Spell.Value, Effectiveness, true);
            }

            // "Your agility is suppressed!"
            // "{0} seems uncoordinated!"
            OnEffectStartsMsg(true, true, true);
        }

        public override void OnStopEffect()
        {
            base.OnStartEffect();

            if (EffectType is eEffect.MovementSpeedDebuff)
            {
                Owner.BuffBonusMultCategory1.Remove((int) eProperty.MaxSpeed, this);
                Owner.OnMaxSpeedChange();
            }
            else
            {
                if (SpellHandler is not PropertyChangingSpell propertyChangingSpell)
                    return;

                foreach (eProperty property in EffectHelper.GetPropertiesFromEffect(EffectType))
                    ApplyBonus(Owner, _isForcedToSpecDebuff ? eBuffBonusCategory.SpecDebuff : propertyChangingSpell.BonusCategory1, property, SpellHandler.Spell.Value, Effectiveness, false);
            }

            // Let's not bother checking the effect type and simply attempt to start every regeneration timer instead.
            Owner.StartHealthRegeneration();
            Owner.StartEnduranceRegeneration();
            Owner.StartPowerRegeneration();

            // "Your coordination returns."
            // "{0}'s coordination returns."
            OnEffectExpiresMsg(true, false, true);
        }
    }
}
