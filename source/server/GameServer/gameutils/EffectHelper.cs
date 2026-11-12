using System;
using System.Collections.Generic;
using System.Reflection;
using DOL.Database;
using DOL.GS.Spells;
using DOL.Logging;

namespace DOL.GS
{
    public static class EffectHelper
    {
        private static readonly Logger log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        public static int GetConcentrationEffectActivationRange(eSpellType spellType)
        {
            return spellType is not eSpellType.EnduranceRegenBuff ? ServerProperties.Properties.BUFF_RANGE > 0 ? ServerProperties.Properties.BUFF_RANGE : 5000 : 1500;
        }

        public static void SendSpellAnimation(ECSGameSpellEffect e, bool noSound = false)
        {
            if (e == null || e.SpellHandler.Spell.ClientEffect == 0)
                return;

            ISpellHandler spellHandler = e.SpellHandler;
            Spell spell = spellHandler.Spell;
            GameLiving target;

            // Focus damage shield. Need to figure out why this is needed.
            if (spell.IsPulsing && spell.SpellType == eSpellType.DamageShield)
                target = spellHandler.Target;
            else
                target = e.Owner;

            foreach (GamePlayer player in e.Owner.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                player.Out.SendSpellEffectAnimation(spellHandler.Caster, target, spell.ClientEffect, 0, noSound, 1);
        }

        public static eEffect GetEffectFromSpell(Spell spell)
        {
            switch (spell.SpellType)
            {
                case eSpellType.WarlockPrimer: return eEffect.WarlockPrimer;
                case eSpellType.MaulerStance: return eEffect.MaulerStance;
                case eSpellType.VampiirFlight: return eEffect.MovementSpeedBuff;
                case eSpellType.VampiirDetermination: return eEffect.VampiirDetermination;
                case eSpellType.VampiirFeint: return eEffect.VampiirFeint;
                case eSpellType.HereticHellflower: return eEffect.HereticHellflower;
                case eSpellType.HereticCocoon: return eEffect.AblativeArmor;
                case eSpellType.DeathKnightForm: return eEffect.DeathKnightForm;
                case eSpellType.DeathKnightMagicWard: return eEffect.AblativeArmor;
                case eSpellType.DeathKnightIgnore: return eEffect.DeathKnightIgnore;
                case eSpellType.DeathKnightPowerShield: return eEffect.DeathKnightPowerShield;
                case eSpellType.DeathKnightEmpowerment: return eEffect.DeathKnightEmpowerment;
                case eSpellType.DeathKnightRegenerate: return eEffect.HealOverTime;
                case eSpellType.HealAllOverTime: return eEffect.HealOverTime;
                case eSpellType.DeathKnightSnare: return eEffect.MovementSpeedDebuff;
                case eSpellType.DeathKnightServant: return eEffect.Pet;
                case eSpellType.DeathKnightPowerLock: return eEffect.DeathKnightPowerLock;
                case eSpellType.MaulerControl: return eEffect.MaulerControl;
                case eSpellType.MaulerUtility: return eEffect.MaulerUtility;
                case eSpellType.MaulerStat: return eEffect.MaulerStat;
                case eSpellType.WarlockChamber: return eEffect.WarlockChamber;
                case eSpellType.WarlockChainDot: return eEffect.DamageOverTime;
                case eSpellType.StormforgedAura: return eEffect.StormforgedAura;
                case eSpellType.StormforgedChant: return eEffect.StormforgedChant;
                case eSpellType.StormforgedWard: return eEffect.StormforgedWard;
                case eSpellType.StormforgedAvatar: return eEffect.StormforgedAvatar;
                case eSpellType.StormforgedSecondThunder: return eEffect.StormforgedSecondThunder;
                case eSpellType.StormforgedStormguard: return eEffect.StormforgedStormguard;
                case eSpellType.StormforgedRide: return eEffect.StormforgedRide;
                case eSpellType.StormforgedEye: return eEffect.StormforgedEye;
                case eSpellType.ArcingPower: return eEffect.ArcingPower;
                case eSpellType.StormSovereignAbility:
                case eSpellType.MasterLevelAbility: return eEffect.MasterLevel;
                case eSpellType.DazzlingArray: return eEffect.MasterLevel;
                case eSpellType.EnervatingGas: return eEffect.MasterLevel;
                case eSpellType.InebriatingFumes: return eEffect.MasterLevel;
                case eSpellType.MentalSiphon: return eEffect.MasterLevel;
                case eSpellType.ChokingVapors: return eEffect.MasterLevel;
                case eSpellType.SenseDullingCloud: return eEffect.MasterLevel;
                case eSpellType.EnergyTempest: return eEffect.MasterLevel;

                #region Positive Effects

                case eSpellType.Bladeturn:
                    return eEffect.Bladeturn;
                case eSpellType.DamageAdd:
                    return eEffect.DamageAdd;
                //case eSpellType.DamageReturn:
                //    return eEffect.DamageReturn;
                case eSpellType.DamageShield: // FocusShield: Could be the wrong SpellType here.
                    return eEffect.FocusShield;
                case eSpellType.AblativeArmor:
                case eSpellType.MagicAblativeArmor:
                case eSpellType.BothAblativeArmor:
                case eSpellType.GroupAblativeHalfOnCaster:
                    return eEffect.AblativeArmor;
                case eSpellType.MeleeDamageBuff:
                    return eEffect.MeleeDamageBuff;
                case eSpellType.ParryBuff:
                    return eEffect.ParryBuff;
                case eSpellType.CombatSpeedBuff:
                    return eEffect.MeleeHasteBuff;
                //case eSpellType.Celerity: // Possibly the same as CombatSpeedBuff?
                //    return eEffect.Celerity;
                case eSpellType.SpeedOfTheRealm:
                case eSpellType.SpeedEnhancement:
                    return eEffect.MovementSpeedBuff;
                case eSpellType.HealOverTime:
                    return eEffect.HealOverTime;
                case eSpellType.CombatHeal:
                    return eEffect.CombatHeal;

                // Stats.
                case eSpellType.StrengthBuff:
                    return eEffect.StrengthBuff;
                case eSpellType.DexterityBuff:
                    return eEffect.DexterityBuff;
                case eSpellType.ConstitutionBuff:
                    return eEffect.ConstitutionBuff;
                case eSpellType.StrengthConstitutionBuff:
                    return eEffect.StrengthConBuff;
                case eSpellType.DexterityQuicknessBuff:
                    return eEffect.DexQuickBuff;
                case eSpellType.AcuityBuff:
                    return eEffect.AcuityBuff;
                case eSpellType.ArmorAbsorptionBuff:
                    return eEffect.PhysicalAbsorptionBuff; // Every ABS buff are applied as secondary ABS buff and don't modify armor ABS.
                case eSpellType.BaseArmorFactorBuff:
                    return eEffect.BaseAFBuff;
                case eSpellType.SpecArmorFactorBuff:
                    return eEffect.SpecAFBuff;
                case eSpellType.PaladinArmorFactorBuff:
                    return eEffect.PaladinAf;

                // Resists.
                case eSpellType.BodyResistBuff:
                    return eEffect.BodyResistBuff;
                case eSpellType.SpiritResistBuff:
                    return eEffect.SpiritResistBuff;
                case eSpellType.EnergyResistBuff:
                    return eEffect.EnergyResistBuff;
                case eSpellType.HeatResistBuff:
                    return eEffect.HeatResistBuff;
                case eSpellType.ColdResistBuff:
                    return eEffect.ColdResistBuff;
                case eSpellType.MatterResistBuff:
                    return eEffect.MatterResistBuff;
                case eSpellType.BodySpiritEnergyBuff:
                    return eEffect.BodySpiritEnergyBuff;
                case eSpellType.HeatColdMatterBuff:
                    return eEffect.HeatColdMatterBuff;
                case eSpellType.AllMagicResistBuff:
                case eSpellType.AllMagicResistsBuff:
                case eSpellType.AllSecondaryMagicResistsBuff:
                    return eEffect.AllMagicResistsBuff;
                case eSpellType.AllMeleeResistsBuff:
                    return eEffect.AllMeleeResistsBuff;
                case eSpellType.Fury:
                    return eEffect.TankFury;
                case eSpellType.Rampage:
                    return eEffect.TankRampage;
                case eSpellType.MetalGuard:
                    return eEffect.PhysicalAbsorptionBuff;
                case eSpellType.SiegeProtection:
                    return eEffect.SiegeProtection;
                case eSpellType.CostFreeChance:
                    return eEffect.CostFreeChance;
                case eSpellType.CrowdControlGuard:
                    return eEffect.CrowdControlGuard;
                case eSpellType.DamageReflection:
                    return eEffect.DamageReflection;
                case eSpellType.ArcheryDamageBuff:
                    return eEffect.ArcheryDamageBuff;
                case eSpellType.UninterruptibleAura:
                    return eEffect.UninterruptibleAura;
                case eSpellType.HelpingHand:
                    return eEffect.HelpingHand;
                case eSpellType.TrackStealth:
                    return eEffect.TrackStealth;
                case eSpellType.MeleeDamageTypeAura:
                    return eEffect.MeleeDamageTypeAura;
                case eSpellType.BolaShot:
                case eSpellType.TotemSnare:
                case eSpellType.PoisonSnare:
                    return eEffect.MovementSpeedDebuff;
                case eSpellType.WeaponSkillDebuff:
                    return eEffect.WeaponSkillDebuff;
                case eSpellType.HealthArmorChant:
                    return eEffect.HealthArmorChant;
                case eSpellType.SpellDamageDebuff:
                    return eEffect.SpellDamageDebuff;
                case eSpellType.PowerCostDebuff:
                    return eEffect.PowerCostDebuff;
                case eSpellType.SpellRangeBuff:
                    return eEffect.SpellRangeBuff;
                case eSpellType.DamageReduction:
                    return eEffect.DamageReduction;
                case eSpellType.StealthDetectionAura:
                    return eEffect.StealthDetectionAura;
                case eSpellType.StaffSkillBuff:
                    return eEffect.StaffSkillBuff;
                case eSpellType.HealingReceivedBuff:
                    return eEffect.HealingReceivedBuff;
                case eSpellType.HealingDoneBuff:
                    return eEffect.HealingDoneBuff;
                case eSpellType.SpellCritAura:
                    return eEffect.SpellCritAura;
                case eSpellType.IceGrip:
                    return eEffect.IceGrip;
                case eSpellType.FlameCocoon:
                    return eEffect.FlameCocoon;
                case eSpellType.BattleControl:
                    return eEffect.BattleControl;
                case eSpellType.AllyStealth:
                    return eEffect.AllyStealth;
                case eSpellType.Speedwarp:
                    return eEffect.Speedwarp;
                case eSpellType.StyleZeal:
                    return eEffect.StyleZeal;
                case eSpellType.MeleeTruth:
                    return eEffect.MeleeTruth;
                // DR-216 loyalty cloaks; ArtifactSpellDamage had no effect type, so its bonus never applied.
                case eSpellType.ArtifactSpellDamage:
                    return eEffect.SpellDamageBuff;
                case eSpellType.MeleeCritBuff:
                    return eEffect.MeleeCritBuff;
                case eSpellType.ParryBlockBuff:
                    return eEffect.ParryBlockBuff;
                case eSpellType.LoyalArcaneLeadership:
                    return eEffect.ArcaneLeadership;
                case eSpellType.IgnoreBladeturn:
                    return eEffect.IgnoreBladeturn;
                case eSpellType.GroupDDRadius:
                    return eEffect.GroupDDRadius;
                case eSpellType.ControlShrug:
                    return eEffect.ControlShrug;
                case eSpellType.DiseaseWard:
                    return eEffect.DiseaseWard;
                case eSpellType.SimulacrumSacrifice:
                    return eEffect.SimulacrumSacrifice;
                case eSpellType.InCombatSpeed:
                    return eEffect.MovementSpeedBuff;
                case eSpellType.Siegebreaker:
                    return eEffect.Siegebreaker;
                case eSpellType.BlightSurge:
                    return eEffect.BlightSurge;

                // Regens.
                case eSpellType.HealthRegenBuff:
                    return eEffect.HealthRegenBuff;
                case eSpellType.EnduranceRegenBuff:
                    return eEffect.EnduranceRegenBuff;
                case eSpellType.PowerRegenBuff:
                    return eEffect.PowerRegenBuff;

                // Misc.
                case eSpellType.OffensiveProc:
                case eSpellType.PoisonCoating:
                    return eEffect.OffensiveProc;
                case eSpellType.DefensiveProc:
                    return eEffect.DefensiveProc;
                case eSpellType.HereticPiercingMagic:
                    return eEffect.HereticPiercingMagic;

                #endregion

                #region Negative Effects

                case eSpellType.StyleBleeding:
                    return eEffect.Bleed;
                case eSpellType.DamageOverTime:
                    return eEffect.DamageOverTime;
                case eSpellType.Charm:
                    return eEffect.Charm;
                case eSpellType.DamageSpeedDecrease:
                case eSpellType.DamageSpeedDecreaseNoVariance:
                case eSpellType.StyleSpeedDecrease:
                case eSpellType.SpeedDecrease:
                case eSpellType.UnbreakableSpeedDecrease:
                case eSpellType.PreventFlight:
                    return eEffect.MovementSpeedDebuff;
                case eSpellType.MeleeDamageDebuff:
                    return eEffect.MeleeDamageDebuff;
                case eSpellType.StyleCombatSpeedDebuff:
                case eSpellType.CombatSpeedDebuff:
                    return eEffect.MeleeHasteDebuff;
                case eSpellType.Disease:
                    return eEffect.Disease;
                case eSpellType.Confusion:
                    return eEffect.Confusion;

                // Crowd control.
                case eSpellType.StyleStun:
                case eSpellType.Stun:
                    return eEffect.Stun;
                //case eSpellType.StunImmunity:
                //    return eEffect.StunImmunity;
                case eSpellType.Mesmerize:
                    return eEffect.Mez;
                case eSpellType.MesmerizeDurationBuff:
                    return eEffect.MesmerizeDurationBuff;
                //case eSpellType.MezImmunity:
                //    return eEffect.MezImmunity;
                //case eSpellType.SnareImmunity: // Not implemented.
                //    return eEffect.SnareImmunity;
                case eSpellType.Nearsight:
                    return eEffect.Nearsight;

                // Stats.
                case eSpellType.StrengthDebuff:
                    return eEffect.StrengthDebuff;
                case eSpellType.DexterityDebuff:
                    return eEffect.DexterityDebuff;
                case eSpellType.ConstitutionDebuff:
                    return eEffect.ConstitutionDebuff;
                case eSpellType.StrengthConstitutionDebuff:
                    return eEffect.StrConDebuff;
                case eSpellType.DexterityQuicknessDebuff:
                    return eEffect.DexQuiDebuff;
                case eSpellType.WeaponSkillConstitutionDebuff:
                    return eEffect.WsConDebuff;
                //case eSpellType.AcuityDebuff: // Not sure what this is yet.
                //    return eEffect.Acuity;
                case eSpellType.ArmorAbsorptionDebuff:
                    return eEffect.ArmorAbsorptionDebuff;
                case eSpellType.ArmorFactorDebuff:
                    return eEffect.ArmorFactorDebuff;

                // Resists.
                case eSpellType.BodyResistDebuff:
                    return eEffect.BodyResistDebuff;
                case eSpellType.SpiritResistDebuff:
                    return eEffect.SpiritResistDebuff;
                case eSpellType.EnergyResistDebuff:
                    return eEffect.EnergyResistDebuff;
                case eSpellType.HeatResistDebuff:
                    return eEffect.HeatResistDebuff;
                case eSpellType.ColdResistDebuff:
                    return eEffect.ColdResistDebuff;
                case eSpellType.MatterResistDebuff:
                    return eEffect.MatterResistDebuff;
                case eSpellType.SlashResistDebuff:
                    return eEffect.SlashResistDebuff;
                case eSpellType.CrushSlashThrustDebuff:
                    return eEffect.AllMeleeResistsDebuff;

                // Misc.
                case eSpellType.SavageCombatSpeedBuff:
                    return eEffect.MeleeHasteBuff;
                case eSpellType.SavageCrushResistanceBuff:
                case eSpellType.SavageDPSBuff:
                case eSpellType.SavageEnduranceHeal:
                case eSpellType.SavageEvadeBuff:
                case eSpellType.SavageStyleEvadeBuff:
                case eSpellType.SavageParryBuff:
                case eSpellType.SavageStyleParryBuff:
                case eSpellType.SavageSlashResistanceBuff:
                case eSpellType.SavageThrustResistanceBuff:
                case eSpellType.SavageMeleeResistanceBuff:
                    return eEffect.SavageBuff;
                case eSpellType.DirectDamage:
                    return eEffect.DirectDamage;
                case eSpellType.FacilitatePainworking:
                    return eEffect.FacilitatePainworking;
                case eSpellType.FatigueConsumptionBuff:
                    return eEffect.FatigueConsumptionBuff;
                case eSpellType.FatigueConsumptionDebuff:
                    return eEffect.FatigueConsumptionDebuff;
                case eSpellType.DirectDamageWithDebuff:
                    if (spell.DamageType == eDamageType.Body)
                        return eEffect.BodyResistDebuff;
                    else if (spell.DamageType == eDamageType.Cold)
                        return eEffect.ColdResistDebuff;
                    else if (spell.DamageType == eDamageType.Heat)
                        return eEffect.HeatResistDebuff;
                    else
                        return eEffect.Unknown;
                case eSpellType.PiercingMagic:
                    return eEffect.PiercingMagic;
                case eSpellType.PveResurrectionIllness:
                    return eEffect.ResurrectionIllness;
                case eSpellType.RvrResurrectionIllness:
                    return eEffect.RvrResurrectionIllness;

                #endregion

                // Pets.
                case eSpellType.SummonTheurgistPet:
                case eSpellType.SummonNoveltyPet:
                case eSpellType.SummonAnimistPet:
                case eSpellType.SummonAnimistFnF:
                case eSpellType.SummonSpiritFighter:
                case eSpellType.StormSovereignWolf:
                case eSpellType.StormforgedPet:
                case eSpellType.SummonHunterPet:
                case eSpellType.SummonUnderhill:
                case eSpellType.SummonDruidPet:
                case eSpellType.SummonSimulacrum:
                case eSpellType.SummonNecroPet:
                case eSpellType.SummonCommander:
                case eSpellType.SummonMinion:
                case eSpellType.SummonJuggernaut:
                case eSpellType.SummonAnimistAmbusher:
                    return eEffect.Pet;
                default:
                    return eEffect.Unknown;
            }
        }

        public static eEffect GetImmunityEffectFromSpell(Spell spell)
        {
            switch (spell.SpellType)
            {
                case eSpellType.Mesmerize:
                    return eEffect.MezImmunity;
                case eSpellType.StyleStun:
                case eSpellType.Stun:
                    return eEffect.StunImmunity;
                case eSpellType.SpeedDecrease:
                case eSpellType.UnbreakableSpeedDecrease:
                case eSpellType.BolaShot:
                    return eEffect.SnareImmunity;
                case eSpellType.Nearsight:
                    return eEffect.NearsightImmunity;
                default:
                    return eEffect.Unknown;
            }
        }

        public static eEffect GetNpcImmunityEffectFromSpell(Spell spell)
        {
            switch (spell.SpellType)
            {
                case eSpellType.Mesmerize:
                    return eEffect.NPCMezImmunity;
                case eSpellType.StyleStun:
                case eSpellType.Stun:
                    return eEffect.NPCStunImmunity;
                default:
                    return eEffect.Unknown;
            }
        }

        public static void SendSpellResistAnimation(ECSGameSpellEffect e)
        {
            if (e is null)
                return;

            foreach (GamePlayer player in e.Owner.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                player.Out.SendSpellEffectAnimation(e.SpellHandler.Caster, e.Owner, e.SpellHandler.Spell.ClientEffect, 0, false, 0);
        }

        public static List<eProperty> GetPropertiesFromEffect(eEffect e)
        {
            List<eProperty> list = new();

            switch (e)
            {
                case eEffect.StrengthBuff:
                case eEffect.StrengthDebuff:
                    list.Add(eProperty.Strength);
                    return list;
                case eEffect.DexterityBuff:
                case eEffect.DexterityDebuff:
                    list.Add(eProperty.Dexterity);
                    return list;
                case eEffect.ConstitutionBuff:
                case eEffect.ConstitutionDebuff:
                    list.Add(eProperty.Constitution);
                    return list;
                case eEffect.AcuityBuff:
                case eEffect.AcuityDebuff:
                    list.Add(eProperty.Acuity);
                    return list;
                case eEffect.StrengthConBuff:
                case eEffect.StrConDebuff:
                    list.Add(eProperty.Strength);
                    list.Add(eProperty.Constitution);
                    return list;
                case eEffect.WsConDebuff:
                    list.Add(eProperty.WeaponSkill);
                    list.Add(eProperty.Constitution);
                    return list;
                case eEffect.WeaponSkillDebuff:
                    list.Add(eProperty.WeaponSkill);
                    return list;
                case eEffect.SpellDamageDebuff:
                    list.Add(eProperty.SpellDamage);
                    return list;
                case eEffect.PowerCostDebuff:
                    list.Add(eProperty.SpellPowerCost);
                    return list;
                case eEffect.SpellRangeBuff:
                    list.Add(eProperty.SpellRange);
                    return list;
                case eEffect.SpellDamageBuff:
                    list.Add(eProperty.SpellDamage);
                    return list;
                case eEffect.MeleeCritBuff:
                    list.Add(eProperty.CriticalMeleeHitChance);
                    return list;
                case eEffect.ParryBlockBuff:
                    list.Add(eProperty.ParryChance);
                    list.Add(eProperty.BlockChance);
                    return list;
                case eEffect.ArcaneLeadership:
                    list.Add(eProperty.SpellRange);
                    list.Add(eProperty.ResistPierce);
                    return list;
                case eEffect.ControlShrug:
                    list.Add(eProperty.Resist_Body);
                    list.Add(eProperty.Resist_Cold);
                    list.Add(eProperty.Resist_Energy);
                    list.Add(eProperty.Resist_Heat);
                    list.Add(eProperty.Resist_Matter);
                    list.Add(eProperty.Resist_Spirit);
                    return list;
                case eEffect.StaffSkillBuff:
                    list.Add(eProperty.Skill_Staff);
                    return list;
                case eEffect.DexQuickBuff:
                case eEffect.DexQuiDebuff:
                    list.Add(eProperty.Dexterity);
                    list.Add(eProperty.Quickness);
                    return list;
                case eEffect.BaseAFBuff:
                case eEffect.SpecAFBuff:
                case eEffect.PaladinAf:
                case eEffect.ArmorFactorDebuff:
                    list.Add(eProperty.ArmorFactor);
                    return list;
                case eEffect.ArmorAbsorptionDebuff:
                    list.Add(eProperty.ArmorAbsorption);
                    return list;
                case eEffect.PhysicalAbsorptionBuff:
                    list.Add(eProperty.PhysicalAbsorption);
                    return list;
                case eEffect.MeleeDamageBuff:
                case eEffect.MeleeDamageDebuff:
                    list.Add(eProperty.MeleeDamage);
                    return list;
                case eEffect.NaturalResistDebuff:
                    list.Add(eProperty.Resist_Natural);
                    return list;
                case eEffect.BodyResistBuff:
                case eEffect.BodyResistDebuff:
                    list.Add(eProperty.Resist_Body);
                    return list;
                case eEffect.SpiritResistBuff:
                case eEffect.SpiritResistDebuff:
                    list.Add(eProperty.Resist_Spirit);
                    return list;
                case eEffect.EnergyResistBuff:
                case eEffect.EnergyResistDebuff:
                    list.Add(eProperty.Resist_Energy);
                    return list;
                case eEffect.HeatResistBuff:
                case eEffect.HeatResistDebuff:
                    list.Add(eProperty.Resist_Heat);
                    return list;
                case eEffect.ColdResistBuff:
                case eEffect.ColdResistDebuff:
                    list.Add(eProperty.Resist_Cold);
                    return list;
                case eEffect.MatterResistBuff:
                case eEffect.MatterResistDebuff:
                    list.Add(eProperty.Resist_Matter);
                    return list;
                case eEffect.HeatColdMatterBuff:
                    list.Add(eProperty.Resist_Heat);
                    list.Add(eProperty.Resist_Cold);
                    list.Add(eProperty.Resist_Matter);
                    return list;
                case eEffect.BodySpiritEnergyBuff:
                    list.Add(eProperty.Resist_Body);
                    list.Add(eProperty.Resist_Spirit);
                    list.Add(eProperty.Resist_Energy);
                    return list;
                case eEffect.AllMagicResistsBuff:
                case eEffect.TankFury:
                    list.Add(eProperty.Resist_Body);
                    list.Add(eProperty.Resist_Spirit);
                    list.Add(eProperty.Resist_Energy);
                    list.Add(eProperty.Resist_Heat);
                    list.Add(eProperty.Resist_Cold);
                    list.Add(eProperty.Resist_Matter);
                    return list;
                case eEffect.SlashResistBuff:
                case eEffect.SlashResistDebuff:
                    list.Add(eProperty.Resist_Slash);
                    return list;
                case eEffect.ThrustResistBuff:
                case eEffect.ThrustResistDebuff:
                    list.Add(eProperty.Resist_Thrust);
                    return list;
                case eEffect.CrushResistBuff:
                case eEffect.CrushResistDebuff:
                    list.Add(eProperty.Resist_Crush);
                    return list;
                case eEffect.AllMeleeResistsBuff:
                case eEffect.AllMeleeResistsDebuff:
                    list.Add(eProperty.Resist_Crush);
                    list.Add(eProperty.Resist_Thrust);
                    list.Add(eProperty.Resist_Slash);
                    return list;
                case eEffect.HealthRegenBuff:
                    list.Add(eProperty.HealthRegenerationAmount);
                    return list;
                case eEffect.PowerRegenBuff:
                    list.Add(eProperty.PowerRegenerationAmount);
                    return list;
                case eEffect.EnduranceRegenBuff:
                    list.Add(eProperty.EnduranceRegenerationAmount);
                    return list;
                case eEffect.MeleeHasteBuff:
                case eEffect.MeleeHasteDebuff:
                    list.Add(eProperty.MeleeSpeed);
                    return list;
                case eEffect.MovementSpeedBuff:
                case eEffect.MovementSpeedDebuff:
                    list.Add(eProperty.MaxSpeed);
                    return list;
                case eEffect.MesmerizeDurationBuff:
                    list.Add(eProperty.MesmerizeDurationReduction);
                    return list;
                case eEffect.VampiirDetermination:
                    list.Add(eProperty.MesmerizeDurationReduction);
                    list.Add(eProperty.StunDurationReduction);
                    list.Add(eProperty.SpeedDecreaseDurationReduction);
                    return list;
                case eEffect.FatigueConsumptionBuff:
                case eEffect.FatigueConsumptionDebuff:
                    list.Add(eProperty.FatigueConsumption);
                    return list;
                default:
                    return list;
            }
        }

        public static PlayerUpdate GetPlayerUpdateFromEffect(eEffect effect)
        {
            // Doesn't set PlayerUpdate.CONCENTRATION.
            PlayerUpdate playerUpdate = PlayerUpdate.Icons;

            switch (effect)
            {
                case eEffect.StrengthBuff:
                case eEffect.StrengthDebuff:
                case eEffect.Disease:
                {
                    playerUpdate |= PlayerUpdate.Stats;
                    playerUpdate |= PlayerUpdate.Encumbrance;
                    break;
                }
                case eEffect.StrengthConBuff:
                case eEffect.StrConDebuff:
                {
                    playerUpdate |= PlayerUpdate.Status;
                    playerUpdate |= PlayerUpdate.Stats;
                    playerUpdate |= PlayerUpdate.Encumbrance;
                    break;
                }
                case eEffect.ConstitutionBuff:
                case eEffect.ConstitutionDebuff:
                case eEffect.WsConDebuff:
                {
                    playerUpdate |= PlayerUpdate.Status;
                    playerUpdate |= PlayerUpdate.Stats;
                    break;
                }
                case eEffect.DexterityBuff:
                case eEffect.DexterityDebuff:
                case eEffect.QuicknessBuff:
                case eEffect.QuicknessDebuff:
                case eEffect.DexQuickBuff:
                case eEffect.DexQuiDebuff:
                case eEffect.AcuityBuff:
                case eEffect.AcuityDebuff:
                {
                    playerUpdate |= PlayerUpdate.Stats;
                    break;
                }
                case eEffect.BodyResistBuff:
                case eEffect.BodyResistDebuff:
                case eEffect.SpiritResistBuff:
                case eEffect.SpiritResistDebuff:
                case eEffect.EnergyResistBuff:
                case eEffect.EnergyResistDebuff:
                case eEffect.HeatResistBuff:
                case eEffect.HeatResistDebuff:
                case eEffect.ColdResistBuff:
                case eEffect.ColdResistDebuff:
                case eEffect.MatterResistBuff:
                case eEffect.MatterResistDebuff:
                case eEffect.HeatColdMatterBuff:
                case eEffect.BodySpiritEnergyBuff:
                case eEffect.AllMagicResistsBuff:
                case eEffect.TankFury:
                case eEffect.SlashResistBuff:
                case eEffect.SlashResistDebuff:
                case eEffect.ThrustResistBuff:
                case eEffect.ThrustResistDebuff:
                case eEffect.CrushResistBuff:
                case eEffect.CrushResistDebuff:
                case eEffect.AllMeleeResistsBuff:
                case eEffect.AllMeleeResistsDebuff:
                {
                    playerUpdate |= PlayerUpdate.Resists;
                    break;
                }
                case eEffect.BaseAFBuff:
                case eEffect.SpecAFBuff:
                case eEffect.PaladinAf:
                case eEffect.ArmorFactorDebuff:
                {
                    playerUpdate |= PlayerUpdate.WeaponArmor;
                    break;
                }
            }

            return playerUpdate;
        }

        public static void RestoreAllEffects(GamePlayer player)
        {
            IList<DbPlayerXEffect> savedEffects = DOLDB<DbPlayerXEffect>.SelectObjects(DB.Column("ChardID").IsEqualTo(player.ObjectId));

            if (savedEffects == null)
                return;

            GameServer.Database.DeleteObject(savedEffects);

            foreach (DbPlayerXEffect savedEffect in savedEffects)
            {
                Spell spell = SkillBase.GetSpellByID(savedEffect.Var1);

                if (spell == null)
                    continue;

                SpellLine line = SkillBase.GetSpellLine(savedEffect.SpellLine, false);

                if (line == null)
                    continue;

                ISpellHandler handler = ScriptMgr.CreateSpellHandler(player, spell, line);
                handler.Spell.Duration = savedEffect.Duration;
                handler.StartSpell(player);
                player.Out.SendStatusUpdate();
            }
        }

        public static void SaveAllEffects(GamePlayer player)
        {
            foreach (ECSGameSpellEffect effect in player.effectListComponent.GetSpellEffects())
            {
                try
                {
                    // Don't save effects from other players, as we won't be able to restore them correctly (different caster, dynamically scaled spell...)
                    if (effect.SpellHandler.Caster != player)
                        continue;

                    DbPlayerXEffect savedEffect = effect.GetSavedEffect();

                    if (savedEffect == null)
                        continue;

                    savedEffect.ChardID = player.ObjectId;
                    GameServer.Database.AddObject(savedEffect);
                }
                catch (Exception e)
                {
                    if (log.IsErrorEnabled)
                        log.Error($"Could not save effect (Effect: {effect}) (Player: {player})", e);
                }
            }
        }

        [Flags]
        public enum PlayerUpdate : ushort
        {
            PetWindow =     1 << 8,
            Icons =         1 << 7,
            Status =        1 << 6,
            Stats =         1 << 5,
            Resists =       1 << 4,
            WeaponArmor =   1 << 3,
            Encumbrance =   1 << 2,
            Concentration = 1,
            None =          0
        }
    }
}
