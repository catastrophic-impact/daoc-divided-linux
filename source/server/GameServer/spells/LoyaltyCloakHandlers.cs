using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.GS.PacketHandler;

namespace DOL.GS.Spells
{
    // The Cloak of the Loyal <class> abilities (live 1.124+, Herald 1.126 export) that no class spell already does
    // (DR-216). The spell rows are built by source/tools/world_editor/pending-fixes/make_class_epic_sets_edits.py.

    /// <summary>Checks the hooks call: AttackComponent (bladeturn), AbstractCCSpellHandler (shrug), SpellHandler.SelectTargets.</summary>
    public static class LoyaltyCloaks
    {
        /// <summary>Armsman and Valkyrie cloaks: the owner's melee attacks pass through bladeturn magic.</summary>
        public static bool IgnoresBladeturn(AttackData ad) =>
            ad != null && ad.IsMeleeAttack && LiveRound7.Has(ad.Attacker as GameLiving, eEffect.IgnoreBladeturn);

        /// <summary>Warrior cloak: Value% chance to shrug off a crowd control spell.</summary>
        public static bool ShrugsControl(GameLiving target)
        {
            int chance = StrongestEffect.Value(target, eEffect.ControlShrug);
            return chance > 0 && Util.Chance(Math.Min(100, chance));
        }

        /// <summary>Enchanter cloak: single target direct damage spells of the group hit everything within Value.</summary>
        public static ushort AdjustDirectDamageRadius(GameLiving caster, Spell spell, ushort radius)
        {
            if (radius > 0 || spell.Target is not eSpellTarget.ENEMY || spell.Range <= 0 ||
                spell.SpellType is not (eSpellType.DirectDamage or eSpellType.DirectDamageNoVariance or
                    eSpellType.DirectDamageWithDebuff or eSpellType.DirectDamageWithDebuffNoVariance))
                return radius;

            int bonus = StrongestEffect.Value(caster, eEffect.GroupDDRadius);
            return bonus > 0 ? (ushort) bonus : radius;
        }
    }

    /// <summary>Shadowblade cloak: melee critical hit chance +Value%.</summary>
    [SpellHandler(eSpellType.MeleeCritBuff)]
    public class MeleeCritBuff(GameLiving caster, Spell spell, SpellLine line) : SingleStatBuff(caster, spell, line)
    {
        public override string ShortDescription => $"Increases {TargetPronoun} chance to critically hit in melee by {Spell.Value}%.";
        public override eProperty Property1 => eProperty.CriticalMeleeHitChance;
    }

    /// <summary>Warden cloak: parry and block chance +Value%.</summary>
    [SpellHandler(eSpellType.ParryBlockBuff)]
    public class ParryBlockBuff(GameLiving caster, Spell spell, SpellLine line) : SingleStatBuff(caster, spell, line)
    {
        public override string ShortDescription => $"Increases {TargetPronoun} chance to parry and block by {Spell.Value}%.";
        public override eProperty Property1 => eProperty.ParryChance;
        public override eProperty Property2 => eProperty.BlockChance;
    }

    /// <summary>Necromancer cloak, Arcane Leadership: spell range and resist piercing +Value%.</summary>
    [SpellHandler(eSpellType.LoyalArcaneLeadership)]
    public class ArcaneLeadershipBuff(GameLiving caster, Spell spell, SpellLine line) : SingleStatBuff(caster, spell, line)
    {
        public override string ShortDescription => $"Increases {TargetPronoun} spell range and resist piercing by {Spell.Value}%.";
        public override eProperty Property1 => eProperty.SpellRange;
        public override eProperty Property2 => eProperty.ResistPierce;
    }

    /// <summary>Warrior cloak: all magic resists +Value% and a Value% chance to shrug off crowd control.</summary>
    [SpellHandler(eSpellType.ControlShrug)]
    public class ControlShrugBuff(GameLiving caster, Spell spell, SpellLine line) : SingleStatBuff(caster, spell, line)
    {
        public override string ShortDescription =>
            $"Increases {TargetPronoun} magic resistances by {Spell.Value}% and gives a {Spell.Value}% chance to shrug off crowd control.";
        public override eProperty Property1 => eProperty.Resist_Body;
    }

    /// <summary>Armsman and Valkyrie cloaks: melee attacks ignore bladeturn for the duration.</summary>
    [SpellHandler(eSpellType.IgnoreBladeturn)]
    public class IgnoreBladeturnSpellHandler(GameLiving caster, Spell spell, SpellLine line) : SpellHandler(caster, spell, line)
    {
        public override string ShortDescription => "Your melee attacks ignore bladeturn magic.";
    }

    /// <summary>Enchanter cloak: the group's single target direct damage spells get a radius of Value.</summary>
    [SpellHandler(eSpellType.GroupDDRadius)]
    public class GroupDDRadiusSpellHandler(GameLiving caster, Spell spell, SpellLine line) : SpellHandler(caster, spell, line)
    {
        public override string ShortDescription => $"Single target direct damage spells hit everything within {Spell.Value} of the target.";
    }

    /// <summary>Druid cloak: cures the owner's diseases at once and every Frequency for the duration.</summary>
    [SpellHandler(eSpellType.DiseaseWard)]
    public class DiseaseWardSpellHandler(GameLiving caster, Spell spell, SpellLine line) : SpellHandler(caster, spell, line)
    {
        public override string ShortDescription => $"Cures disease every {Spell.Frequency / 1000} seconds.";

        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams)
        {
            return ECSGameEffectFactory.Create(initParams, static (in i) => new DiseaseWardECSEffect(i));
        }

        public static void Cure(GameLiving owner)
        {
            foreach (ECSGameSpellEffect disease in owner.effectListComponent.GetSpellEffects(eEffect.Disease).ToList())
                disease.End();
        }
    }

    public class DiseaseWardECSEffect(in ECSGameEffectInitParams initParams) : ECSGameSpellEffect(initParams)
    {
        public override void OnStartEffect()
        {
            DiseaseWardSpellHandler.Cure(Owner);
            OnEffectStartsMsg(true, true, true);
        }

        public override void OnStopEffect()
        {
            OnEffectExpiresMsg(true, false, true);
        }

        public override void OnEffectPulse()
        {
            DiseaseWardSpellHandler.Cure(Owner);
        }
    }

    /// <summary>Wizard cloak: every class reuse timer (spells, styles, class abilities) is ready again. Realm abilities and items stay.</summary>
    [SpellHandler(eSpellType.RefreshClassTimers)]
    public class RefreshClassTimersSpellHandler(GameLiving caster, Spell spell, SpellLine line) : SpellHandler(caster, spell, line)
    {
        public override string ShortDescription => "Refreshes all class-based reuse timers.";

        public override bool StartSpell(GameLiving target)
        {
            if (Caster is not GamePlayer player)
                return false;

            List<Skill> refreshed = player.GetAllDisabledSkills()
                .Where(s => s is not RealmAbilities.RealmAbility && !(s is Spell sp && sp.ID >= 800000))
                .ToList();

            foreach (Skill skill in refreshed)
                player.RemoveDisabledSkill(skill);

            if (refreshed.Count > 0)
                player.Out.SendDisableSkill(refreshed.Select(s => new Tuple<Skill, int>(s, 0)).ToArray());

            player.TempProperties.SetProperty(GamePlayer.QUICK_CAST_CHANGE_TICK, 0L);
            MessageToCaster("Your class abilities are ready again.", eChatType.CT_Spell);
            return true;
        }
    }

    /// <summary>Eldritch cloak: the owner slips into stealth.</summary>
    [SpellHandler(eSpellType.SelfStealth)]
    public class SelfStealthSpellHandler(GameLiving caster, Spell spell, SpellLine line) : SpellHandler(caster, spell, line)
    {
        public override string ShortDescription => "Hides you in stealth.";

        public override bool StartSpell(GameLiving target)
        {
            if (Caster is not GamePlayer player)
                return false;

            if (player.InCombat)
            {
                MessageToCaster("You can't hide while in combat!", eChatType.CT_SpellResisted);
                return false;
            }

            if (!player.IsStealthed)
                player.Stealth(true);

            return true;
        }
    }

    /// <summary>Bard cloak: run speed Value% that stays up in combat (roots, snares, mezzes and stuns still land).</summary>
    [SpellHandler(eSpellType.InCombatSpeed)]
    public class InCombatSpeedSpellHandler(GameLiving caster, Spell spell, SpellLine line) : SpeedEnhancementSpellHandler(caster, spell, line)
    {
        public override string ShortDescription => $"Raises your speed to {Spell.Value}% of normal, even in combat.";
        protected override bool BlockedInCombat => false;
    }

    /// <summary>
    /// Minstrel cloak: summons a wild creature of the caster's level that anyone (the caster, allies or enemies) may
    /// charm. Model = Spell.Value, it leaves after Spell.Duration or when killed.
    /// </summary>
    [SpellHandler(eSpellType.CharmableDecoy)]
    public class CharmableDecoySpellHandler(GameLiving caster, Spell spell, SpellLine line) : SpellHandler(caster, spell, line)
    {
        public override string ShortDescription => $"Summons a creature that may be charmed by anyone for {Spell.Duration / 60000} minutes.";

        public override bool StartSpell(GameLiving target)
        {
            GameNPC beast = new()
            {
                Name = string.IsNullOrEmpty(Spell.Message1) ? "wild beast" : Spell.Message1,
                Model = (ushort) Spell.Value,
                Level = Caster.Level,
                Realm = eRealm.None,
                CurrentRegion = Caster.CurrentRegion,
                Heading = Caster.Heading,
                X = Caster.X + 50,
                Y = Caster.Y + 50,
                Z = Caster.Z,
                MaxSpeedBase = 200,
                Flags = 0,
            };
            beast.BodyType = (ushort) NpcTemplateMgr.eBodyType.Animal;
            beast.SetOwnBrain(new StandardMobBrain { AggroLevel = 0, AggroRange = 0 });
            if (!beast.AddToWorld())
                return false;

            int duration = Spell.Duration > 0 ? Spell.Duration : 600000;
            new ECSGameTimer(beast, _ =>
            {
                if (beast.ObjectState == GameObject.eObjectState.Active)
                {
                    if (beast.Brain is ControlledMobBrain controlled && controlled.Owner is GamePlayer owner)
                        owner.Out.SendMessage($"{beast.GetName(0, true)} wanders off.", eChatType.CT_SpellExpires, eChatLoc.CL_SystemWindow);
                    beast.Die(null);
                    beast.Delete();
                }
                return 0;
            }, duration);

            MessageToCaster($"{beast.GetName(0, true)} answers your call.", eChatType.CT_Spell);
            return true;
        }
    }
}
