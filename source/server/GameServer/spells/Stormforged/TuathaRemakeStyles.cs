using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;

namespace DOL.GS.Spells
{
    /// <summary>
    /// Tuatha Sovereign (class 67) Scythe, Blades and Shields styles. Each style's proc spell is <see cref="FirstEffectId"/> +
    /// its index in source/tools/sovereign/tuatha_remake_styles.json (names, levels, chains). Styles mark enemies with Sidhe
    /// Marks and detonate them; the courts add their riders (see <see cref="TuathaCourt"/>).
    /// </summary>
    public static class TuathaRemakeStyles
    {
        public const int FirstStyleId = 7200;
        public const int FirstEffectId = 63800;
        public const int Count = 61;

        // Damage at level 50 (lower levels scale down, never below level 10 strength).
        private const double Opener = 60, Step = 90, Finisher = 150, Capstone = 210, Signature = 240, Ambush = 150;

        public static bool OwnsEffect(int id) => id is >= FirstEffectId and < FirstEffectId + Count;

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
                Path = "TuathaSovereign", Level = 1, Positive = positive, Mods = mods, Describe = s => s.Description,
                Run = (h, t) => Effect(index, h, t)
            };
        }

        private static (bool Positive, MLMod[] Mods) Mods(int index) => index switch
        {
            38 => (false, MLMod.Resists(MLCategory.Debuff, 15, physical: true)),                         // Mark of the Morrigan
            27 => (true, new[] { new MLMod(eProperty.EvadeChance, MLCategory.Ability, 20), new MLMod(eProperty.ParryChance, MLCategory.Ability, 20) }),
            28 => (true, new[] { MLMod.Buff(eProperty.ArmorAbsorption, 20) }),
            48 => (true, new[] { MLMod.Buff(eProperty.ArmorAbsorption, 15) }),                            // Guardian of Tara
            53 => (true, new[] { new MLMod(eProperty.BlockChance, MLCategory.Ability, 30) }),
            58 => (true, new[] { new MLMod(eProperty.EvadeChance, MLCategory.Ability, 10), new MLMod(eProperty.BlockChance, MLCategory.Ability, 10) }),
            // Fey Sidestep: +10% evade 10 s.
            23 => (true, new[] { new MLMod(eProperty.EvadeChance, MLCategory.Ability, 10) }),
            _ => (false, Array.Empty<MLMod>())
        };

        private static void Effect(int index, MasterLevelAbilityHandler h, GameLiving t)
        {
            GameLiving me = h.Caster;
            int level = me.Level;
            double D(double atFifty) => atFifty * StormSovereignAbilities.Scale(Math.Max(level, StormSovereignAbilities.StyleFloorLevel));
            void Hit(GameLiving target, double amount, int crit = 0) => StormSovereignAbilities.Strike(h, target, amount, eDamageType.Spirit, crit);
            void Mark(GameLiving target, int n) { if (target == null || !target.IsAlive) return; TuathaMarks.Add(me, target, n); if (TuathaCourt.IsUnseelie(me)) Rot(h, target); }
            IEnumerable<GameLiving> Near(GameLiving center, int radius) => h.Enemies(center, radius);
            IEnumerable<GameLiving> Sweep(int extra) => new[] { t }.Concat(Near(t, 250).Where(e => e != t).Take(extra));
            // Detonate one enemy: +25% per mark, the court's rider; returns the marks it had.
            int Burst(GameLiving target, double baseDamage, int crit = 0, bool executeBelow25 = false)
            {
                int marks = TuathaMarks.Detonate(me, target);
                double damage = baseDamage * TuathaMarks.Bonus(me, marks);
                if (executeBelow25 && target.HealthPercent < 25)
                    damage *= 2;
                Hit(target, damage, crit);
                TuathaCourt.OnDetonation(h, marks, damage);
                return marks;
            }
            int Ward(int perLevel) => (int)(Math.Max(20, level * perLevel) * TuathaCourt.WardFactor(me));

            switch (index)
            {
                // ---------------- Scythe: Reaper of the Unseen ----------------
                case 0: foreach (var e in Sweep(2)) { Hit(e, e == t ? D(Opener) : D(Opener) * 0.5); Mark(e, 1); } break;   // Reaper's Arc
                case 1: foreach (var e in Sweep(2)) { Mark(e, 1); Snare(h, e, 30, 8); } break;                              // Gleaning
                case 2: foreach (var e in Near(me, 350).ToList()) Burst(e, D(Finisher)); break;                              // Unseen Harvest
                case 3: Hit(t, D(Opener)); Mark(t, 2); break;                                                                 // Moonlit Counter
                case 4: foreach (var e in Near(t, 350)) { Hit(e, D(Step)); Mark(e, 1); } break;                              // Waning Crescent
                case 5:                                                                                                       // Pale Moon
                    foreach (var e in Near(me, 350).ToList())
                        if (Burst(e, D(Finisher)) >= 3)
                            Stun(h, e, 3);
                    break;
                case 6: foreach (var e in Sweep(4)) { Hit(e, D(Opener) * 0.6); Snare(h, e, 40, 8); } break;                   // Thresh
                case 7: foreach (var e in Sweep(4)) AttackSpeed(h, e, 25, 12); break;                                         // Winnow
                case 8:                                                                                                       // Grain of the Dead
                    foreach (var e in Near(me, 350).ToList())
                    {
                        int marks = Burst(e, D(Finisher));
                        TuathaCourt.OnDetonation(h, marks, D(Finisher));                                                      // court rider doubled
                    }
                    break;
                case 9: Hit(t, D(Opener)); Snare(h, t, 40, 8); Mark(t, 1); break;                                             // Crescent Hook
                case 10: foreach (var e in Near(t, 250).Append(t).Distinct()) Root(h, e, 8); break;                          // Bind the Sheaf
                case 11: Hit(t, D(Step)); Mark(t, 2); break;                                                                  // Reaper's Shadow
                case 12: Wound(h, t, eDamageType.Spirit); Mark(t, 1); break;                                                  // Sickle of Samhain
                case 13: Burst(t, D(Capstone), executeBelow25: true); break;                                                  // Last Harvest
                case 14: Hit(t, D(Ambush)); Mark(t, 3); break;                                                                // Veiled Reaping
                case 15: Stun(h, t, 3); break;                                                                                // Silent Scythe
                case 16: Hit(t, D(Opener)); h.Heal(me, (int)(D(Opener) * 0.3)); break;                                        // Feast of Samhain
                case 17: foreach (var m in h.Group(1500)) h.Heal(m, m.MaxHealth / 10); Mark(t, 1); break;                    // Moonfeast
                case 18: foreach (var e in Near(me, 500)) Mark(e, 2); break;                                                  // Call of the Morrigan
                case 19:                                                                                                      // Wings of the Morrigan
                {
                    double total = 0;
                    foreach (var e in Near(me, 500).ToList())
                        total += D(Signature) * TuathaMarks.Bonus(me, Burst(e, D(Signature)));
                    foreach (var m in h.Group(1500))
                        h.Heal(m, (int)(total * 0.2));
                    break;
                }

                // ---------------- Blades: the glamour duelist ----------------
                case 20: Wound(h, t, eDamageType.Body); Hit(t, D(Opener) * 0.7); Mark(t, 1); break;                          // Thornkiss
                case 21: Hit(t, D(Opener) * 0.7); Mark(t, 2); break;                                                          // Rosecut
                case 22:                                                                                                      // Briar Crown
                {
                    int marks = Burst(t, D(Finisher));
                    for (int i = 0; i < marks; i++)
                        Hit(t, 10 + level);
                    break;
                }
                case 23: Hit(t, D(Opener)); Mark(t, 2); h.Hold(me, 10000); break;                                             // Fey Sidestep
                case 24: Hit(t, D(Opener)); Hit(t, D(Opener)); Mark(t, 2); break;                                             // Glamour Flurry
                case 25:                                                                                                      // Dance of Lugh
                    Burst(t, D(Finisher));
                    if (me.ControlledBrain?.Body is StormWolf drake && drake.IsAlive && me.IsWithinRadius(drake, 2000))
                        Hit(t, D(Finisher) * 0.5);
                    Stun(h, t, 3);
                    break;
                case 26: Hit(t, D(Opener)); Snare(h, t, 30, 8); Mark(t, 1); break;                                            // Silver Riposte
                case 27: h.Hold(me, 10000); break;                                                                            // Mirror of Manannan
                case 28: Stun(h, t, 4); h.Hold(me, 10000); break;                                                             // Hand of Nuada
                case 29: Hit(t, D(Opener)); Snare(h, t, 40, 8); Mark(t, 1); break;                                            // Thorn Hook
                case 30: AttackSpeed(h, t, 25, 12); Hit(t, D(Opener) * 0.7); break;                                           // Wither
                case 31: Wound(h, t, eDamageType.Body); Snare(h, t, 50, 8); Mark(t, 2); break;                               // Backthorn
                case 32: Hit(t, D(Step) * (1 + 0.25 * TuathaMarks.Count(me, t))); break;                                      // Gouge of the Sidhe
                case 33: Burst(t, D(Capstone), crit: 15, executeBelow25: true); break;                                        // Sidhe's Claim
                case 34: Hit(t, D(Ambush) * 1.2); Wound(h, t, eDamageType.Body); Mark(t, 3); break;                          // Glamoured Strike
                case 35: Stun(h, t, 3); break;                                                                                // Fade and Fall
                case 36: Hit(t, D(Opener)); h.Heal(me, Math.Max(1, level * 3)); break;                                        // Moonsip
                case 37:                                                                                                      // Dew of Tir na Nog
                    h.Heal(me, me.MaxHealth / 10);
                    MasterLevelAbilityHandler.Cure(me, eEffect.Snare, eEffect.MovementSpeedDebuff);
                    break;
                case 38: TuathaMarks.Add(me, t, TuathaMarks.Max); h.Hold(t, 15000); break;                                    // Mark of the Morrigan (mods below)
                case 39:                                                                                                      // Queen's Blade
                {
                    var bleeds = StormRemakeStyles.Wounds(me, t);
                    int marks = TuathaMarks.Detonate(me, t);
                    double damage = D(Signature) * 2 * TuathaMarks.Bonus(me, marks) * (1 + 0.5 * bleeds.Count);
                    foreach (var bleed in bleeds) bleed.End();
                    Hit(t, damage);
                    TuathaCourt.OnDetonation(h, marks, damage);
                    break;
                }

                // ---------------- Shields: the thornwarden ----------------
                case 40: Taunt(h, t); Hit(t, D(Opener) * 0.7); Mark(t, 1); break;                                             // Tara's Rebuke
                case 41: Thorns(h, 15); break;                                                                                // Bramble Guard
                case 42: Stun(h, t, 4); break;                                                                                // Crown of Thorns
                case 43: Ablative(h, me, Ward(5)); break;                                                                     // Hold the Hill
                case 44: foreach (var e in Near(me, 350)) { Hit(e, D(Step)); Taunt(h, e); Mark(e, 1); } break;               // Thornwall
                case 45: foreach (var m in h.Group(1500)) Ablative(h, m, Ward(3)); break;                                     // The Undying Court
                case 46: Stun(h, t, 3); break;                                                                                // Brehon's Bash
                case 47: foreach (var e in Near(t, 250).Append(t).Distinct()) Root(h, e, 8); break;                          // Rooted in Tara
                case 48: foreach (var m in h.Group(1500)) h.Hold(m, 10000); break;                                            // Guardian of Tara
                case 49: Stun(h, t, 9); Mark(t, 1); break;                                                                    // Thornslam
                case 50: Snare(h, t, 40, 8); Hit(t, D(Opener) * 0.7); break;                                                  // Shield Rush
                case 51: Stun(h, t, 2); break;                                                                                // Crushing Rim
                case 52: Hit(t, D(Opener) * 0.7); Mark(t, 1); break;                                                          // Hill-Fort Slam (the hit interrupts)
                case 53: h.Hold(me, 8000); break;                                                                             // Wall of Tara
                case 54: foreach (var e in Near(me, 500).ToList()) { Taunt(h, e); Burst(e, D(Finisher)); } break;            // Throne of the Sidhe
                case 55: Stun(h, t, 4); Hit(t, D(Opener)); Mark(t, 2); break;                                                 // Glamoured Ambush
                case 56: Ablative(h, me, Ward(5)); break;                                                                     // Ambush Guard
                case 57: h.Heal(me, me.MaxHealth * 5 / 100); Thorns(h, 10); break;                                            // Rowan Bulwark
                case 58: foreach (var m in h.Group(1500)) h.Hold(m, 15000); break;                                            // Oath of the Fianna
                case 59: foreach (var m in h.Group(1500)) h.Heal(m, m.MaxHealth / 10); break;                                 // Embrace of the Court
                case 60: foreach (var m in h.Group(1500)) { Ablative(h, m, Ward(4)); } Thorns(h, 15); break;                // Thornwall of Tara
            }
        }

        // Built fresh each time, because the values follow the Tuatha's level (as in StormRemakeStyles).
        private static void Apply(MasterLevelAbilityHandler h, GameLiving target, int slot, eSpellType type, Action<DbSpell> setup)
        {
            var db = new DbSpell
            {
                SpellID = h.Spell.ID * 1000 + 800 + slot, Name = h.Spell.Name, Type = type.ToString(), Target = "Enemy",
                Icon = h.Spell.Icon, ClientEffect = h.Spell.ClientEffect, DamageType = (int)eDamageType.Spirit
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
        private static void Root(MasterLevelAbilityHandler h, GameLiving t, int seconds) =>
            Apply(h, t, 9, eSpellType.SpeedDecrease, db => { db.Value = 99; db.Duration = seconds; });
        private static void AttackSpeed(MasterLevelAbilityHandler h, GameLiving t, int percent, int seconds) =>
            Apply(h, t, 3, eSpellType.CombatSpeedDebuff, db => { db.Value = percent; db.Duration = seconds; });
        private static void Taunt(MasterLevelAbilityHandler h, GameLiving t) =>
            Apply(h, t, 4, eSpellType.Taunt, db => db.Value = 400 + h.Caster.Level * 10);

        // Evasion of the Sidhe (RA) makes thorns 10/20/30% stronger.
        private static void Thorns(MasterLevelAbilityHandler h, int seconds)
        {
            int rank = h.Caster is GamePlayer player ? player.GetAbilityLevel(RealmAbilities.TuathaEvasionOfTheSidhe.KeyName) : 0;
            double damage = (5 + h.Caster.Level / 2.0) * (1 + 0.1 * Math.Clamp(rank, 0, 3));
            Apply(h, h.Caster, 10, eSpellType.DamageShield, db => { db.Target = "Self"; db.Damage = damage; db.Duration = seconds; db.Frequency = 0; });
        }

        private static void Ablative(MasterLevelAbilityHandler h, GameLiving ally, int amount) =>
            Apply(h, ally, 5, eSpellType.BothAblativeArmor, db => { db.Target = ally == h.Caster ? "Self" : "Realm"; db.Value = amount; db.Damage = 50; db.Duration = 20; });

        public static void Wound(MasterLevelAbilityHandler h, GameLiving t, eDamageType type) =>
            Apply(h, t, type == eDamageType.Spirit ? 6 : 7, eSpellType.DamageOverTime, db =>
            {
                db.Duration = 12; db.Frequency = 30; db.Damage = 10 + h.Caster.Level; db.DamageType = (int)type;
            });

        /// <summary>Unseelie Court: each mark rots its enemy (spirit damage over 12 s, stronger with more marks).</summary>
        public static void Rot(MasterLevelAbilityHandler h, GameLiving t) =>
            Apply(h, t, 8, eSpellType.DamageOverTime, db =>
            {
                db.Duration = 12; db.Frequency = 30; db.Damage = 4 + h.Caster.Level / 5.0 * TuathaMarks.Count(h.Caster, t);
                db.DamageType = (int)eDamageType.Spirit;
            });
    }
}
