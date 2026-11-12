using System;
using System.Collections.Generic;
using DOL.GS.PacketHandler;

namespace DOL.GS.Spells
{
    /// <summary>
    /// Cabalist pet sacrifice line (live 1.122b, Spirit Magic 3/13/23/33/43: Simulacrum's Soul, Fortitude, Resilience,
    /// Superiority, Brilliance; DR-094). The caster's simulacrum is given up and the caster takes its power for the
    /// duration; no new simulacrum can be summoned while the effect lasts (SummonSimulacrum).
    ///
    /// What a rank gives comes from the spell's custom values, each a percent, missing = 0:
    ///   MeleeReduction  melee damage taken is lowered by this much (OnAttacked)
    ///   HealthArmor     maximum health and armor factor are raised (HealthArmorChantSpellHandler.Percent)
    ///   SnareReduction  root and snare durations are shortened (eProperty.SpeedDecreaseDurationReduction)
    ///   MagicDamage     spell damage is raised (eProperty.SpellDamage)
    ///   ProcSpell       a spell id cast on every melee attacker that hits the caster (the 10% slow)
    /// </summary>
    [SpellHandler(eSpellType.SimulacrumSacrifice)]
    public class SimulacrumSacrificeSpellHandler(GameLiving caster, Spell spell, SpellLine line) : SpellHandler(caster, spell, line)
    {
        public const string MeleeReductionKey = "MeleeReduction";
        public const string HealthArmorKey = "HealthArmor";
        public const string SnareReductionKey = "SnareReduction";
        public const string MagicDamageKey = "MagicDamage";
        public const string ProcSpellKey = "ProcSpell";

        /// <summary>A custom value of the spell as a whole number, 0 when the row has none.</summary>
        public static int Param(Spell spell, string key)
        {
            if (spell == null)
                return 0;

            var result = spell.TryGetParamSingle(key, out int value);
            bool found = result is CustomParamsExtensions.RetrievalResult.Success or CustomParamsExtensions.RetrievalResult.SuccessFromConversion;
            return found ? Math.Max(0, value) : 0;
        }

        /// <summary>The living's active sacrifice effect, or null.</summary>
        public static ECSGameSpellEffect ActiveOn(GameLiving living)
        {
            if (living?.effectListComponent == null)
                return null;

            foreach (ECSGameSpellEffect effect in living.effectListComponent.GetSpellEffects(eEffect.SimulacrumSacrifice))
            {
                if (effect.IsActive && !effect.IsEnding)
                    return effect;
            }

            return null;
        }

        /// <summary>Percent added to the living's maximum health and armor factor (read beside the health/armor chants).</summary>
        public static int HealthArmorPercent(GameLiving living) =>
            ActiveOn(living) is { } effect ? Param(effect.SpellHandler.Spell, HealthArmorKey) : 0;

        /// <summary>What <paramref name="damage"/> becomes after a <paramref name="percent"/> reduction.</summary>
        public static int Reduced(int damage, int percent) => damage - damage * Math.Min(100, percent) / 100;

        /// <summary>
        /// Called for every attack on a living (GameLiving.OnAttackedByEnemy): lowers melee damage and answers a melee
        /// hit with the rank's slow.
        /// </summary>
        public static void OnAttacked(GameLiving victim, AttackData ad)
        {
            if (ad == null || !ad.IsMeleeAttack || ad.AttackResult is not (eAttackResult.HitUnstyled or eAttackResult.HitStyle))
                return;

            if (ActiveOn(victim) is not { } effect)
                return;

            Spell spell = effect.SpellHandler.Spell;
            int reduction = Param(spell, MeleeReductionKey);

            if (reduction > 0)
            {
                ad.Damage = Reduced(ad.Damage, reduction);
                ad.CriticalDamage = Reduced(ad.CriticalDamage, reduction);
            }

            int procId = Param(spell, ProcSpellKey);

            if (procId <= 0 || ad.Attacker is not { IsAlive: true } attacker || attacker == victim)
                return;

            Spell procSpell = SkillBase.GetSpellByID(procId);

            if (procSpell == null)
                return;

            ScriptMgr.CreateSpellHandler(victim, procSpell, SkillBase.GetSpellLine(GlobalSpellsLines.Item_Effects))?.StartSpell(attacker);
        }

        public override string ShortDescription
        {
            get
            {
                List<string> parts = ["Sacrifices your simulacrum in exchange for greater power."];
                int melee = Param(Spell, MeleeReductionKey);
                int healthArmor = Param(Spell, HealthArmorKey);
                int snare = Param(Spell, SnareReductionKey);
                int magic = Param(Spell, MagicDamageKey);

                if (melee > 0)
                    parts.Add($"Reduces all melee damage taken by {melee}%.");
                if (healthArmor > 0)
                    parts.Add($"Increases maximum health and armor factor by {healthArmor}%.");
                if (snare > 0)
                    parts.Add($"Reduces the duration of all root and snare effects by {snare}%.");
                if (Param(Spell, ProcSpellKey) > 0)
                    parts.Add("Melee attackers are slowed when they strike you.");
                if (magic > 0)
                    parts.Add($"Increases all magic damage by {magic}%.");

                parts.Add("You cannot summon a new simulacrum while this lasts.");
                return string.Join(" ", parts);
            }
        }

        private bool HasPetToSacrifice => Caster.ControlledBrain?.Body is { IsAlive: true };

        public override bool CheckBeginCast(GameLiving selectedTarget, bool quiet)
        {
            if (!HasPetToSacrifice)
            {
                if (!quiet)
                    MessageToCaster("You must have a simulacrum to sacrifice!", eChatType.CT_SpellResisted);

                return false;
            }

            return base.CheckBeginCast(selectedTarget, quiet);
        }

        public override bool CheckEndCast(GameLiving target)
        {
            if (!HasPetToSacrifice)
            {
                MessageToCaster("You must have a simulacrum to sacrifice!", eChatType.CT_SpellResisted);
                return false;
            }

            return base.CheckEndCast(target);
        }

        public override void FinishSpellCast(GameLiving target)
        {
            m_caster.Mana -= PowerCost(target); // the base handler leaves the power cost to each spell type
            base.FinishSpellCast(target);
        }

        public override bool StartSpell(GameLiving target)
        {
            if (!HasPetToSacrifice)
                return false;

            if (Caster is GamePlayer player)
                player.CommandNpcRelease();
            else if (Caster is IGamePlayer bot)
                bot.CommandNpcRelease();
            else
                Caster.ControlledBrain.Body.Die(Caster);

            MessageToCaster("You sacrifice your simulacrum and absorb its power.", eChatType.CT_Spell);
            return base.StartSpell(Caster);
        }

        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams) =>
            ECSGameEffectFactory.Create(initParams, static (in i) => new SimulacrumSacrificeECSEffect(i));
    }

    public class SimulacrumSacrificeECSEffect(in ECSGameEffectInitParams initParams) : ECSGameSpellEffect(initParams)
    {
        // What was added at start, so the same amounts come off at stop even if the spell row is reloaded meanwhile.
        private int _snare;
        private int _magic;

        public override void OnStartEffect()
        {
            Spell spell = SpellHandler.Spell;
            _snare = SimulacrumSacrificeSpellHandler.Param(spell, SimulacrumSacrificeSpellHandler.SnareReductionKey);
            _magic = SimulacrumSacrificeSpellHandler.Param(spell, SimulacrumSacrificeSpellHandler.MagicDamageKey);
            Owner.BaseBuffBonusCategory[eProperty.SpeedDecreaseDurationReduction] += _snare;
            Owner.BaseBuffBonusCategory[eProperty.SpellDamage] += _magic;
            Refresh();
            OnEffectStartsMsg(true, true, true);
        }

        public override void OnStopEffect()
        {
            Owner.BaseBuffBonusCategory[eProperty.SpeedDecreaseDurationReduction] -= _snare;
            Owner.BaseBuffBonusCategory[eProperty.SpellDamage] -= _magic;
            Refresh();
            OnEffectExpiresMsg(true, false, true);
        }

        /// <summary>Maximum health changed: clamp health to it and tell the client.</summary>
        private void Refresh()
        {
            if (Owner.Health > Owner.MaxHealth)
                Owner.Health = Owner.MaxHealth;

            Owner.StartHealthRegeneration();

            if (Owner is GamePlayer player)
            {
                player.Out.SendCharStatsUpdate();
                player.Out.SendStatusUpdate();
            }
        }
    }
}
