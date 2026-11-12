using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.Effects;

namespace DOL.GS.Spells
{
    /// <summary>
    /// Storm Sovereign (class 64) Hammer, Axe and Shields styles, 20 per line. Each style's proc spell is
    /// <see cref="FirstEffectId"/> + its index in source/tools/sovereign/storm_remake_styles.json, which holds the
    /// names, unlock levels and chains the world seeder writes. Pendragon and Tuatha keep the old shared styles.
    /// </summary>
    public static class StormRemakeStyles
    {
        public const int FirstStyleId = 7100;
        public const int FirstEffectId = 63500;
        public const int Count = 61;   // 20 per line, plus Stormslam (60), the Shields anytime stun

        // Damage at level 50 (lower levels scale down, never below level 10 strength).
        private const double Opener = 60, Step = 90, Finisher = 160, Capstone = 220, Signature = 260, Ambush = 150;

        public static bool OwnsEffect(int id) => id is >= FirstEffectId and < FirstEffectId + Count;

        /// <summary>
        /// Tempest a style builds (five for the styles that fill it). Paid even when the style's own hit kills the
        /// target: the effect itself only runs on a living target, so one-shot kills built nothing (owner, 2026-11-04).
        /// </summary>
        public static int Builds(int index) => index switch
        {
            0 or 3 or 20 or 24 or 40 or 51 or 56 or 60 => 1,
            14 or 34 or 54 => 2,
            18 or 38 or 58 => StormTempest.MaxCharges,
            _ => 0
        };

        public static IEnumerable<(int Id, MLAbility Ability)> Build()
        {
            for (int index = 0; index < Count; index++)
                yield return (FirstEffectId + index, Make(index));
        }

        private static MLAbility Make(int index)
        {
            var (positive, mods) = Mods(index);
            return new MLAbility
            {
                Path = "StormSovereign", Level = 1, Positive = positive, Mods = mods, Describe = s => s.Description,
                Run = (h, t) => Effect(index, h, t)
            };
        }

        // Property changes held by a style's effect; Positive marks the ones held on the Sovereign or the party.
        private static (bool Positive, MLMod[] Mods) Mods(int index) => index switch
        {
            1 or 19 or 47 => (false, new[] { MLMod.Debuff(eProperty.ArmorAbsorption, 15) }),
            11 => (false, new[] { MLMod.Debuff(eProperty.ArmorAbsorption, 10) }),
            38 => (false, MLMod.Resists(MLCategory.Debuff, 15, physical: true)),
            28 => (true, new[] { MLMod.Buff(eProperty.ArmorAbsorption, 20) }),
            48 => (true, new[] { MLMod.Buff(eProperty.ArmorAbsorption, 15) }),
            // Block chance is read from the ability bonus, in whole percent.
            41 => (true, new[] { new MLMod(eProperty.BlockChance, MLCategory.Ability, 10) }),
            52 => (true, new[] { new MLMod(eProperty.BlockChance, MLCategory.Ability, 30) }),
            _ => (false, Array.Empty<MLMod>())
        };

        private static void Effect(int index, MasterLevelAbilityHandler h, GameLiving t)
        {
            GameLiving me = h.Caster;
            int level = me.Level;
            double D(double atFifty) => atFifty * StormSovereignAbilities.Scale(Math.Max(level, StormSovereignAbilities.StyleFloorLevel));
            void Hit(GameLiving target, double amount, eDamageType type = eDamageType.Energy, int crit = 0) =>
                StormSovereignAbilities.Strike(h, target, amount, type, crit);
            void Build(int charges) => StormTempest.Add(me, charges);
            IEnumerable<GameLiving> Near(GameLiving center, int radius) => h.Enemies(center, radius);
            int Ward(int perLevel) => Math.Max(20, level * perLevel);

            switch (index)
            {
                // ---------------- Hammer: thunder and packs ----------------
                case 0: Hit(t, D(Opener)); Build(1); break;                                           // Thunderstroke
                case 1: Hit(t, D(Opener)); h.Hold(t, 12000); break;                                   // Shatterguard
                case 2:                                                                               // Thor's Judgement
                {
                    int c = StormTempest.Spend(me);
                    double damage = D(Finisher) * StormTempest.Bonus(c);
                    Hit(t, damage);
                    foreach (var enemy in Near(t, 350).Where(e => e != t).Take(3 + c))
                        Hit(enemy, damage * 0.5);
                    break;
                }
                case 3: Hit(t, D(Opener)); Build(1); break;                                           // Storm Counter
                case 4: foreach (var enemy in Near(t, 350)) Hit(enemy, D(Step)); break;               // Rolling Thunder
                case 5: Stun(h, t, 3); Hit(t, D(Finisher)); break;                                    // Mjolnir's Answer
                case 6: Ablative(h, me, Ward(4)); break;                                              // Thunder Aegis
                case 7: Hit(t, D(Step) * 1.5); break;                                                 // Hammer of Reprisal
                case 8:                                                                               // Wall of Thunder
                    foreach (var member in h.Group(1500)) Ablative(h, member, Ward(3));
                    foreach (var enemy in Near(me, 350)) Hit(enemy, D(Step));
                    break;
                case 9: Snare(h, t, 40, 8); Hit(t, D(Opener)); break;                                 // Earthshaker
                case 10: foreach (var enemy in Near(t, 250)) { Stun(h, enemy, 2); Hit(enemy, D(Opener)); } break; // Quake
                case 11: Hit(t, D(Step)); h.Hold(t, 12000); break;                                    // Backbreaker
                case 12: AttackSpeed(h, t, 25, 12); Hit(t, D(Opener)); break;                         // Spinecrack
                case 13:                                                                              // Thunderfall
                {
                    double damage = D(Capstone) * StormTempest.Bonus(StormTempest.Spend(me));
                    Hit(t, damage);
                    foreach (var enemy in Near(t, 350).Where(e => e != t)) Hit(enemy, damage * 0.5);
                    break;
                }
                case 14: Hit(t, D(Ambush)); Build(2); break;                                          // Hidden Thunder
                case 15: Stun(h, t, 3); break;                                                        // Silent Storm
                case 16: Hit(t, D(Opener)); MasterLevelAbilityHandler.Restore(me, me, 10, 0, h.Spell.Name); break; // Stormcaller
                case 17: foreach (var enemy in Near(me, 350)) Hit(enemy, D(Step) * 1.3); break;       // Skybreaker
                case 18: Hit(t, D(Opener) * 0.5); StormTempest.Set(me, StormTempest.MaxCharges); break; // Gathering Storm
                case 19:                                                                              // Ragnarok's Hammer
                {
                    double damage = D(Signature) * StormTempest.Bonus(StormTempest.Spend(me));
                    foreach (var enemy in Near(me, 500)) { Hit(enemy, damage); h.Hold(enemy, 12000); }
                    break;
                }

                // ---------------- Axe: winter executioner ----------------
                case 20: Wound(h, t, eDamageType.Cold); Hit(t, D(Opener) * 0.7, eDamageType.Cold); Build(1); break; // Winterbite
                case 21: Wound(h, t, eDamageType.Body); Hit(t, D(Opener) * 0.7, eDamageType.Cold); break;            // Bloodmark
                case 22:                                                                              // Executioner's Storm
                {
                    double damage = D(Finisher) * (1 + 0.25 * Wounds(me, t).Count) * StormTempest.Bonus(StormTempest.Spend(me));
                    Hit(t, t.HealthPercent < 25 ? damage * 2 : damage, eDamageType.Cold);
                    break;
                }
                case 23: Hit(t, D(Opener), eDamageType.Cold); Snare(h, t, 30, 8); break;             // Frost Rend
                case 24: Hit(t, D(Opener)); Hit(t, D(Opener), eDamageType.Cold); Build(1); break;     // Royal Fury
                case 25:                                                                              // Wolf's Reckoning
                    Hit(t, D(Finisher), eDamageType.Cold);
                    if (me.ControlledBrain?.Body is StormWolf wolf && wolf.IsAlive && me.IsWithinRadius(wolf, 2000))
                        Hit(t, D(Finisher) * 0.5);
                    break;
                case 26: Ablative(h, me, Ward(4)); Snare(h, t, 20, 6); break;                         // Iceguard
                case 27: Hit(t, D(Step), eDamageType.Cold); h.Heal(me, me.MaxHealth * 5 / 100); break; // Winter's Reprisal
                case 28: Stun(h, t, 4); h.Hold(me, 10000); break;                                     // Glacier Wall
                case 29: Hit(t, D(Opener), eDamageType.Cold); Snare(h, t, 40, 8); break;             // Frost Hook
                case 30: AttackSpeed(h, t, 25, 12); Hit(t, D(Opener) * 0.7, eDamageType.Cold); break; // Shiver
                case 31: Wound(h, t, eDamageType.Body); Snare(h, t, 50, 8); break;                    // Hamstring
                case 32: Hit(t, D(Step) * 1.5 * (Wounds(me, t).Count > 0 ? 1.25 : 1), eDamageType.Cold); break; // Gutting Frost
                case 33:                                                                              // Widowmaker
                {
                    double damage = D(Capstone) * StormTempest.Bonus(StormTempest.Spend(me));
                    Hit(t, t.HealthPercent < 25 ? damage * 2 : damage, eDamageType.Cold, 15);
                    break;
                }
                case 34: Hit(t, D(Ambush) * 1.2, eDamageType.Cold); Wound(h, t, eDamageType.Body); Build(2); break; // Hidden Wrath
                case 35: Stun(h, t, 3); break;                                                        // Silent Winter
                case 36: Hit(t, D(Opener), eDamageType.Cold); h.Heal(me, Math.Max(1, level * 3)); break; // Hungering Frost
                case 37:                                                                              // Frozen Heart
                    h.Heal(me, me.MaxHealth / 10);
                    MasterLevelAbilityHandler.Cure(me, eEffect.Snare, eEffect.MovementSpeedDebuff);
                    break;
                case 38: h.Hold(t, 15000); StormTempest.Set(me, StormTempest.MaxCharges); break;      // Mark of Fenrir
                case 39:                                                                              // Fimbulwinter
                {
                    var wounds = Wounds(me, t);
                    double damage = D(Signature) * (1 + 0.5 * wounds.Count) * StormTempest.Bonus(StormTempest.Spend(me));
                    foreach (var wound in wounds) wound.End();
                    Hit(t, damage, eDamageType.Cold);
                    break;
                }

                // ---------------- Shields: stormguard ----------------
                case 40: Taunt(h, t); Hit(t, D(Opener) * 0.7); Build(1); break;                       // Royal Rebuke
                case 41: Hit(t, D(Opener) * 0.7); h.Hold(me, 10000); break;                           // Break Their Guard
                case 42: Stun(h, t, 4); break;                                                        // Crownfall
                case 43: Ablative(h, me, Ward(5)); break;                                             // Hold the Throne
                case 44: foreach (var enemy in Near(me, 350)) { Hit(enemy, D(Step)); Taunt(h, enemy); } break; // Thunder Wall
                case 45:                                                                              // The Unbroken Crown
                {
                    double bonus = StormTempest.Bonus(StormTempest.Spend(me));
                    foreach (var member in h.Group(1500)) Ablative(h, member, (int)(Ward(3) * bonus));
                    foreach (var enemy in Near(me, 350)) Hit(enemy, D(Step) * bonus);
                    break;
                }
                case 46: Stun(h, t, 3); break;                                                        // Stormguard Bash
                case 47: h.Hold(t, 12000); AttackSpeed(h, t, 20, 12); break;                          // Shieldbreaker
                case 48: foreach (var member in h.Group(1500)) h.Hold(member, 10000); break;          // Guardian's Oath
                case 49: Snare(h, t, 40, 8); Hit(t, D(Opener) * 0.7); break;                          // Shield Rush
                case 50: Stun(h, t, 2); break;                                                        // Crushing Rim
                case 51: Hit(t, D(Opener) * 0.7); Build(1); break;                                    // Rampart Slam (the hit interrupts)
                case 52: h.Hold(me, 8000); break;                                                     // Wall of Midgard
                case 53:                                                                              // Throne of Storms
                {
                    double damage = D(Finisher) * StormTempest.Bonus(StormTempest.Spend(me));
                    foreach (var enemy in Near(me, 500)) { Taunt(h, enemy); Hit(enemy, damage); }
                    break;
                }
                case 54: Stun(h, t, 4); Hit(t, D(Opener)); Build(2); break;                           // Hidden Crown
                case 55: Ablative(h, me, Ward(5)); break;                                             // Ambush Guard
                case 56: h.Heal(me, me.MaxHealth * 5 / 100); Hit(t, D(Opener) * 0.7); Build(1); break; // Bulwark
                case 57: foreach (var member in h.Group(1500)) h.Heal(member, member.MaxHealth / 10); break; // Crown's Embrace
                case 58: Hit(t, D(Opener) * 0.5); StormTempest.Set(me, StormTempest.MaxCharges); break; // Oathbinding
                case 59:                                                                              // Stormwall
                {
                    double bonus = StormTempest.Bonus(StormTempest.Spend(me));
                    foreach (var member in h.Group(1500)) Ablative(h, member, (int)(Ward(4) * bonus));
                    foreach (var enemy in Near(me, 350)) Hit(enemy, D(Finisher) * bonus);
                    break;
                }
                case 60: Stun(h, t, 9); Build(1); break;                                              // Stormslam (Slam-style anytime stun)
            }
        }

        /// <summary>The Sovereign's own damage-over-time effects on the target (Axe wounds).</summary>
        public static List<ECSGameSpellEffect> Wounds(GameLiving owner, GameLiving target) =>
            target.effectListComponent.GetSpellEffects(eEffect.DamageOverTime)
                .Where(effect => !effect.IsEnding && effect.SpellHandler?.Caster == owner).ToList();

        // Built fresh each time (not cached like MasterLevelAbilityHandler.Cast), because the values follow the Sovereign's level.
        private static void Apply(MasterLevelAbilityHandler h, GameLiving target, int slot, eSpellType type, Action<DbSpell> setup)
        {
            var db = new DbSpell
            {
                SpellID = h.Spell.ID * 1000 + 800 + slot, Name = h.Spell.Name, Type = type.ToString(), Target = "Enemy",
                Icon = h.Spell.Icon, ClientEffect = h.Spell.ClientEffect, DamageType = (int)eDamageType.Energy
            };
            setup(db);
            MasterLevelAbilityHandler.StockMessages(db);
            var spell = new Spell(db, 50) { InternalID = h.Spell.InternalID };
            ScriptMgr.CreateSpellHandler(h.Caster, spell, SkillBase.GetSpellLine(GlobalSpellsLines.Reserved_Spells))?.StartSpell(target);
        }

        private static void Stun(MasterLevelAbilityHandler h, GameLiving t, int seconds) =>
            Apply(h, t, 1, eSpellType.StyleStun, db => db.Duration = seconds);
        private static void Snare(MasterLevelAbilityHandler h, GameLiving t, int percent, int seconds) =>
            Apply(h, t, 2, eSpellType.SpeedDecrease, db => { db.Value = percent; db.Duration = seconds; });
        private static void AttackSpeed(MasterLevelAbilityHandler h, GameLiving t, int percent, int seconds) =>
            Apply(h, t, 3, eSpellType.CombatSpeedDebuff, db => { db.Value = percent; db.Duration = seconds; });
        private static void Taunt(MasterLevelAbilityHandler h, GameLiving t) =>
            Apply(h, t, 4, eSpellType.Taunt, db => db.Value = 400 + h.Caster.Level * 10);
        // Shieldwall of Asgard (RA) makes every style storm shield 10/20/30% stronger.
        private static void Ablative(MasterLevelAbilityHandler h, GameLiving ally, int amount)
        {
            int rank = h.Caster is GamePlayer player ? player.GetAbilityLevel(RealmAbilities.SovereignShieldwall.KeyName) : 0;
            amount = (int)(amount * (1 + 0.1 * Math.Clamp(rank, 0, 3)));
            Apply(h, ally, 5, eSpellType.BothAblativeArmor, db => { db.Target = ally == h.Caster ? "Self" : "Realm"; db.Value = amount; db.Damage = 50; db.Duration = 20; });
        }
        public static void Wound(MasterLevelAbilityHandler h, GameLiving t, eDamageType type) =>
            Apply(h, t, type == eDamageType.Cold ? 6 : 7, eSpellType.DamageOverTime, db =>
            {
                db.Duration = 12; db.Frequency = 30; db.Damage = 10 + h.Caster.Level;
                db.DamageType = (int)PendragonRules.RoyalDamage(h.Caster, type);
            });
    }
}
