using System;
using System.Linq;
using DOL.Events;
using DOL.GS.Spells;

namespace DOL.GS
{
    public static class StormforgedHeroicRules
    {
        public const int SecondThunderCooldown = 120000;

        public static bool HasAvatar(GameLiving owner) => owner.effectListComponent
            .GetSpellEffects(eEffect.StormforgedAvatar).Any(effect => effect.IsActive && !effect.IsEnding);

        public static void CastBurst(GameLiving owner, Spell parent, SpellLine line, GameLiving target)
        {
            Spell burst = SkillBase.GetSpellByID(parent.SubSpellID);
            if (burst != null)
                if (burst.SpellType == eSpellType.StormSovereignAbility)
                    new StormSovereignHandler(owner, burst, line).StartSpell(target);
                else
                    new StormforgedStrike(owner, burst, line).StartSpell(target);
        }
    }

    public class StormforgedAvatarEffect : ECSGameSpellEffect
    {
        private ushort _originalModel;
        private ushort _avatarModel;
        public const int SovereignCrownAbsorption = 20, SovereignCrownMeleeDamage = 50, SovereignCrownHaste = 20, SovereignCrownRegen = 10;

        public StormforgedAvatarEffect(in ECSGameEffectInitParams initParams) : base(initParams) { }

        public override void OnStartEffect()
        {
            base.OnStartEffect();
            _originalModel = Owner.Model;
            _avatarModel = (ushort)SpellHandler.Spell.Value;
            Owner.Model = _avatarModel;
            // Tuatha: the Morrigan form bursts in with a major Ethereal flash (owner 2026-11-06: the old form looked basic).
            if (PendragonRules.IsTuatha(Owner) && Owner.CurrentRegion != null)
                foreach (GamePlayer viewer in Owner.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                    viewer.Out.SendSpellEffectAnimation(Owner, Owner, TuathaMarks.BurstFlash, 0, false, 1);
            if (StormSovereignAbilities.Owns(SpellHandler.Spell.ID))
            {
                Owner.AbilityBonus[eProperty.ArmorAbsorption] += SovereignCrownAbsorption;
                Owner.AbilityBonus[eProperty.MeleeDamage] += SovereignCrownMeleeDamage;
                Owner.AbilityBonus[eProperty.MeleeSpeed] += SovereignCrownHaste;
                MLMod.Refresh(Owner);
                // Storm Sovereign: the crown fills Tempest, and finishers spend nothing while it lasts.
                if (Owner is GamePlayer { CharacterClass: var cls } && StormforgedRules.IsStormforged(cls.ID))
                    StormTempest.Set(Owner, StormTempest.MaxCharges);
                // Pendragon: the Lion of Albion fills Valor, and Judgments spend none while it lasts (PendragonValor.Spend).
                if (PendragonRules.IsPendragon(Owner))
                    PendragonValor.Set(Owner, PendragonValor.Max(Owner));
            }
        }

        public override void OnStopEffect()
        {
            if (StormSovereignAbilities.Owns(SpellHandler.Spell.ID))
            {
                Owner.AbilityBonus[eProperty.ArmorAbsorption] -= SovereignCrownAbsorption;
                Owner.AbilityBonus[eProperty.MeleeDamage] -= SovereignCrownMeleeDamage;
                Owner.AbilityBonus[eProperty.MeleeSpeed] -= SovereignCrownHaste;
                MLMod.Refresh(Owner);
            }
            if (Owner.Model == _avatarModel)
                Owner.Model = _originalModel;
            base.OnStopEffect();
        }

        public override void OnEffectPulse()
        {
            if (!Owner.IsAlive)
            {
                End();
                return;
            }
            int percent = StormSovereignAbilities.Owns(SpellHandler.Spell.ID) ? SovereignCrownRegen : 15;
            Owner.ChangeHealth(Owner, eHealthChangeType.Spell, Math.Max(1, Owner.MaxHealth * percent / 100));
            Owner.ChangeMana(Owner, eManaChangeType.Spell, Math.Max(1, Owner.MaxMana * percent / 100));
            Owner.ChangeEndurance(Owner, eEnduranceChangeType.Spell, Math.Max(1, Owner.MaxEndurance * percent / 100));
            if (!Owner.IsCrowdControlled && !Owner.IsStealthed)
                StormforgedHeroicRules.CastBurst(Owner, SpellHandler.Spell, SpellHandler.SpellLine, Owner);
        }
    }

    public abstract class StormforgedPermanentEffect : ECSGameSpellEffect
    {
        protected StormforgedPermanentEffect(in ECSGameEffectInitParams initParams) : base(initParams)
        {
            Duration = 0;
            ExpireTick = long.MaxValue;
        }
    }

    public class StormforgedSecondThunderEffect : StormforgedPermanentEffect
    {
        private readonly object _triggerLock = new();

        public StormforgedSecondThunderEffect(in ECSGameEffectInitParams initParams) : base(initParams) { }

        public static bool TryPreventFatalDamage(GameLiving owner, long incomingDamage)
        {
            if (incomingDamage <= 0 || incomingDamage < owner.Health)
                return false;
            foreach (var effect in owner.effectListComponent.GetSpellEffects(eEffect.StormforgedSecondThunder))
            {
                if (effect is StormforgedSecondThunderEffect secondThunder && secondThunder.TrySave(incomingDamage))
                    return true;
            }
            return false;
        }

        public bool TrySave(long incomingDamage)
        {
            lock (_triggerLock)
            {
                if (!IsActive || IsEnding || !Owner.IsAlive || incomingDamage < Owner.Health ||
                    Owner.GetSkillDisabledDuration(SpellHandler.Spell) > 0)
                    return false;
                // Use the shared spell cooldown so cancelling/recasting cannot rearm it early.
                Owner.DisableSkill(SpellHandler.Spell, StormforgedHeroicRules.SecondThunderCooldown);
                Owner.Health = Owner.MaxHealth;
                Owner.ChangeMana(Owner, eManaChangeType.Spell, Math.Max(0, Owner.MaxMana - Owner.Mana));
                Owner.ChangeEndurance(Owner, eEnduranceChangeType.Spell, Math.Max(0, Owner.MaxEndurance - Owner.Endurance));
            }
            CastRebirthBurst();
            return true;
        }

        protected virtual void CastRebirthBurst() =>
            StormforgedHeroicRules.CastBurst(Owner, SpellHandler.Spell, SpellHandler.SpellLine, Owner);
    }

    public class StormforgedStormguardEffect : StormforgedPermanentEffect
    {
        public StormforgedStormguardEffect(in ECSGameEffectInitParams initParams) : base(initParams) { }

        public override void OnStartEffect()
        {
            base.OnStartEffect();
            GameEventMgr.AddHandler(Owner, GameLivingEvent.AttackedByEnemy, OnAttacked);
        }

        public override void OnStopEffect()
        {
            GameEventMgr.RemoveHandler(Owner, GameLivingEvent.AttackedByEnemy, OnAttacked);
            base.OnStopEffect();
        }

        private void OnAttacked(DOLEvent e, object sender, EventArgs args)
        {
            if (args is AttackedByEnemyEventArgs attacked)
                TryRetaliate(attacked.AttackData);
        }

        public bool TryRetaliate(AttackData attack)
        {
            if (!IsActive || IsEnding || !Owner.IsAlive || Owner.IsCrowdControlled || attack == null ||
                attack.Target != Owner || !attack.IsMeleeAttack || attack.AttackResult != eAttackResult.Blocked ||
                attack.Attacker == null || attack.Attacker == Owner || !attack.Attacker.IsAlive)
                return false;
            CastRetaliation(attack.Attacker);
            return true;
        }

        protected virtual void CastRetaliation(GameLiving attacker) =>
            StormforgedHeroicRules.CastBurst(Owner, SpellHandler.Spell, SpellHandler.SpellLine, attacker);
    }
}
