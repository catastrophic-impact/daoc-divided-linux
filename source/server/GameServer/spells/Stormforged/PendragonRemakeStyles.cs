using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;

namespace DOL.GS.Spells
{
    /// <summary>
    /// Pendragon Sovereign (class 65) Sword, Polearm and Shields styles. Each style's proc spell is <see cref="FirstEffectId"/> +
    /// its index in source/tools/sovereign/pendragon_remake_styles.json (names, levels, chains). Defensive and reactive
    /// styles earn Valor; Judgments spend it (<see cref="PendragonValor"/>) for holy fire and a party heal or ward.
    /// </summary>
    public static class PendragonRemakeStyles
    {
        public const int FirstStyleId = 7300;
        public const int FirstEffectId = 63900;
        public const int Count = 61;

        // Damage at level 50 (lower levels scale down, never below level 10 strength).
        private const double Opener = 60, Step = 90, Finisher = 150, Capstone = 210, Signature = 240, Ambush = 150;

        public static bool OwnsEffect(int id) => id is >= FirstEffectId and < FirstEffectId + Count;

        /// <summary>Valor a style earns when it lands (also paid when its hit killed the target).</summary>
        public static int Builds(int index) => index switch
        {
            15 or 34 or 55 => 2,
            3 or 6 or 18 or 23 or 26 or 38 or 40 or 43 or 44 or 46 or 49 or 52 => 1,
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
                Path = "PendragonSovereign", Level = 1, Positive = positive, Mods = mods, Describe = s => s.Description,
                Run = (h, t) => Effect(index, h, t)
            };
        }

        private static (bool Positive, MLMod[] Mods) Mods(int index) => index switch
        {
            3 => (true, new[] { new MLMod(eProperty.ParryChance, MLCategory.Ability, 10) }),                               // Riposte of Camelot
            6 => (false, new[] { MLMod.Debuff(eProperty.BlockChance, 20) }),                                                // Shield-Breaker
            8 => (true, new[] { MLMod.Buff(eProperty.ArmorAbsorption, 20) }),                                               // Caliburn's Oath
            27 => (false, new[] { MLMod.Debuff(eProperty.BlockChance, 20), MLMod.Debuff(eProperty.ParryChance, 20) }),     // Splinter the Shield
            36 => (true, new[] { MLMod.Speed(1.3) }),                                                                        // Charge of Albion
            37 => (true, new[] { new MLMod(eProperty.MeleeDamage, MLCategory.Ability, 10) }),                               // Banner of the Dragon
            41 or 53 => (true, new[] { new MLMod(eProperty.BlockChance, MLCategory.Ability, 30) }),                         // Hold the Gate, Unbroken
            47 => (false, new[] { MLMod.Debuff(eProperty.BlockChance, 20) }),                                               // Shatterguard
            48 => (true, new[] { MLMod.Buff(eProperty.ArmorAbsorption, 15) }),                                              // Walls of Camelot
            59 => (true, new[] { new MLMod(eProperty.BlockChance, MLCategory.Ability, 10), new MLMod(eProperty.ParryChance, MLCategory.Ability, 10) }),
            60 => (true, new[] { MLMod.Buff(eProperty.ArmorAbsorption, 20) }),                                              // Pendragon's Shield
            _ => (false, Array.Empty<MLMod>())
        };

        private static void Effect(int index, MasterLevelAbilityHandler h, GameLiving t)
        {
            GameLiving me = h.Caster;
            int level = me.Level;
            double D(double atFifty) => atFifty * StormSovereignAbilities.Scale(Math.Max(level, StormSovereignAbilities.StyleFloorLevel));
            void Hit(GameLiving target, double amount) => StormSovereignAbilities.Strike(h, target, amount, eDamageType.Heat);
            IEnumerable<GameLiving> Near(GameLiving center, int radius) => h.Enemies(center, radius);
            int Valor(int n) { PendragonValor.Add(me, n); return n; }
            int SelfWard(int perLevel) => Math.Max(20, level * perLevel);
            // A Judgment: spends all Valor for +20% each on every target, a 2-second stun at 5+, then the party heal or ward.
            int Judge(IEnumerable<GameLiving> targets, double baseDamage, bool ward, bool executeBelow25 = false)
            {
                int valor = PendragonValor.Spend(me);
                double bonus = PendragonValor.Bonus(valor);
                foreach (var e in targets.ToList())
                {
                    double damage = baseDamage * bonus * (executeBelow25 && e.HealthPercent < 25 ? 2 : 1);
                    Hit(e, damage);
                    if (valor >= 5)
                        Stun(h, e, 2);
                }
                PendragonValor.OnJudgment(h, valor, ward);
                return valor;
            }
            GameLiving[] One() => new[] { t };

            Valor(Builds(index));
            switch (index)
            {
                // ---------------- Sword: Excalibur, the duelist knight ----------------
                case 0: Hit(t, D(Opener) * 0.7); Burn(h, t); break;                                                           // Crowned Cut
                case 1: Hit(t, D(Opener) * 0.7); Snare(h, t, 30, 8); break;                                                   // Lady of the Lake
                case 2: Judge(One(), D(Finisher), ward: false); break;                                                         // Judgment of Excalibur
                case 3: Hit(t, D(Opener) * 0.6); h.Hold(me, 10000); break;                                                     // Riposte of Camelot
                case 4: Hit(t, D(Step) * 1.25); break;                                                                         // Gawain's Answer
                case 5: Judge(One(), D(Finisher), ward: false); Stun(h, t, 3); break;                                          // Sun at Noon
                case 6: Hit(t, D(Opener) * 0.6); h.Hold(t, 10000); break;                                                      // Shield-Breaker
                case 7: AttackSpeed(h, t, 25, 12); Hit(t, D(Opener) * 0.7); break;                                             // Kay's Cut
                case 8: Judge(One(), D(Finisher), ward: false); h.Hold(me, 10000); break;                                      // Caliburn's Oath
                case 9: Hit(t, D(Opener)); Snare(h, t, 40, 8); Burn(h, t); break;                                              // Bedivere's Turn
                case 10: Disarm(h, t, 5); break;                                                                               // Return the Sword
                case 11: Hit(t, D(Step)); Burn(h, t); break;                                                                   // Mordred's Bane
                case 12: Hit(t, D(Step) * (t.HealthPercent < 50 ? 1.25 : 1.0)); break;                                         // Camlann
                case 13: Judge(One(), D(Capstone), ward: false, executeBelow25: true); break;                                  // Last Light of Camelot
                case 14: Hit(t, D(Ambush)); Stun(h, t, 3); break;                                                              // Hidden Blade of Avalon
                case 15: Burn(h, t); break;                                                                                    // Mists of Avalon (+2 Valor)
                case 16: Hit(t, D(Opener)); h.Heal(me, (int)(D(Opener) * 0.4)); break;                                         // Grail Light
                case 17: foreach (var m in h.Group(1500)) h.Heal(m, m.MaxHealth / 10); break;                                 // Grail Quest
                case 18: Hit(t, D(Opener) * 0.6); Hit(t, D(Opener) * 0.6); break;                                              // Lancelot's Fury
                case 19: Judge(Near(me, 350).Append(t).Distinct(), D(Signature), ward: false); break;                          // Excalibur Unsheathed

                // ---------------- Polearm: Rhongomyniad, the lancer ----------------
                case 20: Hit(t, D(Opener)); break;                                                                             // Couched Lance
                case 21: Stun(h, t, 3); break;                                                                                 // Unhorse
                case 22: Judge(One(), D(Finisher), ward: true); break;                                                         // Judgment of the Lance
                case 23: Hit(t, D(Opener)); break;                                                                             // Lancer's Answer
                case 24: foreach (var e in new[] { t }.Concat(Near(t, 250).Where(e => e != t).Take(3))) Hit(e, D(Step) * 0.6); break; // Sweep of the Field
                case 25:                                                                                                       // Field of Camlann
                {
                    var hit = Near(me, 350).Append(t).Distinct().ToList();
                    Judge(hit, D(Finisher), ward: true);
                    foreach (var e in hit) Burn(h, e);
                    break;
                }
                case 26: Hit(t, D(Opener) * 0.7); break;                                                                       // The Tilt (the hit interrupts)
                case 27: h.Hold(t, 10000); break;                                                                              // Splinter the Shield
                case 28: Judge(One(), D(Finisher), ward: true); Stun(h, t, 4); break;                                          // Breaker of Hosts
                case 29: Snare(h, t, 40, 8); Hit(t, D(Opener) * 0.7); break;                                                   // Flank of the Charge
                case 30: Root(h, t, 8); break;                                                                                 // Rider Down
                case 31: Hit(t, D(Step)); Burn(h, t); break;                                                                   // Dragon's Tail
                case 32: foreach (var e in Near(t, 300).Append(t).Distinct()) Hit(e, D(Step) * 0.7); break;                   // Wyrmfire
                case 33: Judge(One(), D(Capstone), ward: true, executeBelow25: true); break;                                   // Rhongomyniad
                case 34: Hit(t, D(Ambush)); break;                                                                             // Silent Lance (+2 Valor)
                case 35: Stun(h, t, 3); break;                                                                                 // Pin to the Earth
                case 36: Snare(h, t, 40, 8); h.Hold(me, 6000); break;                                                          // Charge of Albion
                case 37: foreach (var m in h.Group(1500)) h.Hold(m, 15000); break;                                             // Banner of the Dragon
                case 38:                                                                                                       // Pendragon's Reach
                {
                    var swept = Near(me, 400).Append(t).Distinct().ToList();
                    foreach (var e in swept) Hit(e, D(Opener) * 0.6);
                    Valor(Math.Clamp(swept.Count, 1, 3) - Builds(index));
                    break;
                }
                case 39: Judge(Near(me, 500).Append(t).Distinct(), D(Signature), ward: true); break;                           // The Red Dragon

                // ---------------- Shields: Pridwen, the guardian ----------------
                case 40: Taunt(h, t); Hit(t, D(Opener) * 0.7); break;                                                          // King's Rebuke
                case 41: h.Hold(me, 8000); break;                                                                              // Hold the Gate
                case 42: Stun(h, t, 4); break;                                                                                 // Pridwen's Answer
                case 43: Ward(h, me, SelfWard(5)); break;                                                                      // Stand Fast
                case 44: foreach (var e in Near(me, 350)) { Taunt(h, e); Hit(e, D(Step) * 0.5); } break;                      // Shield of the Round Table
                case 45: Judge(One(), D(Finisher), ward: true); break;                                                         // Judgment of the Round Table
                case 46: Stun(h, t, 3); break;                                                                                 // Crown's Bash
                case 47: h.Hold(t, 10000); break;                                                                              // Shatterguard
                case 48: Judge(One(), D(Finisher), ward: false); foreach (var m in h.Group(1500)) h.Hold(m, 10000); break;    // Walls of Camelot
                case 49: Stun(h, t, 9); break;                                                                                 // Pridwen's Bash
                case 50: Snare(h, t, 40, 8); Hit(t, D(Opener) * 0.7); break;                                                   // Shield Rush
                case 51: Stun(h, t, 2); break;                                                                                 // Ram the Line
                case 52: Hit(t, D(Opener) * 0.7); break;                                                                       // Hill of Badon (the hit interrupts)
                case 53: h.Hold(me, 8000); break;                                                                              // Unbroken
                case 54:                                                                                                       // The Throne Stands
                {
                    var all = Near(me, 500).Append(t).Distinct().ToList();
                    foreach (var e in all) Taunt(h, e);
                    Judge(all, D(Capstone), ward: true);
                    foreach (var e in all) Burn(h, e);
                    break;
                }
                case 55: Stun(h, t, 4); break;                                                                                 // Hidden Pridwen (+2 Valor)
                case 56: Ward(h, me, SelfWard(5)); break;                                                                      // Guarded Retreat
                case 57:                                                                                                       // Oath of Protection
                    h.Heal(me, me.MaxHealth * 5 / 100);
                    foreach (var ally in Guarded(me))
                        h.Heal(ally, ally.MaxHealth / 10);
                    break;
                case 58: foreach (var m in h.Group(1500)) h.Heal(m, m.MaxHealth / 10); break;                                 // Fealty
                case 59: foreach (var m in h.Group(1500)) h.Hold(m, 15000); break;                                             // Rally to the King
                case 60: Judge(One(), D(Signature), ward: true); foreach (var m in h.Group(1500)) h.Hold(m, 10000); break;    // Pendragon's Shield
            }
        }

        /// <summary>The allies this Pendragon guards with the class's Guard ability.</summary>
        public static IEnumerable<GameLiving> Guarded(GameLiving me) =>
            me.effectListComponent.GetAbilityEffects(eEffect.Guard).OfType<GuardECSGameEffect>()
                .Where(g => g.Source == me && g.Target != null && g.Target != me && g.Target.IsAlive).Select(g => g.Target).Distinct().ToList();

        // Built fresh each time, because the values follow the Pendragon's level (as in the other remakes).
        private static void Apply(MasterLevelAbilityHandler h, GameLiving target, int slot, eSpellType type, Action<DbSpell> setup)
        {
            var db = new DbSpell
            {
                SpellID = h.Spell.ID * 1000 + 800 + slot, Name = h.Spell.Name, Type = type.ToString(), Target = "Enemy",
                Icon = h.Spell.Icon, ClientEffect = h.Spell.ClientEffect, DamageType = (int)eDamageType.Heat
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
        private static void Disarm(MasterLevelAbilityHandler h, GameLiving t, int seconds) =>
            Apply(h, t, 11, eSpellType.Disarm, db => db.Duration = seconds);

        /// <summary>A holy-fire ward on an ally (or the Pendragon): absorbs half of each blow up to the amount, 20 seconds.</summary>
        public static void Ward(MasterLevelAbilityHandler h, GameLiving ally, int amount) =>
            // Its own name: Guard of the Crown and Decree showed two same-named icons, the buff and this ward (playtest 2026-11-07).
            Apply(h, ally, 5, eSpellType.BothAblativeArmor, db => { db.Name = WardName(h.Spell.Name); db.Target = ally == h.Caster ? "Self" : "Realm"; db.Value = Math.Max(1, amount); db.Damage = 50; db.Duration = 20; });

        public static string WardName(string spellName) => spellName.EndsWith(" Ward") ? spellName : spellName + " Ward";

        /// <summary>Holy fire burning for 12 seconds.</summary>
        public static void Burn(MasterLevelAbilityHandler h, GameLiving t) =>
            Apply(h, t, 6, eSpellType.DamageOverTime, db =>
            {
                db.Duration = 12; db.Frequency = 30; db.Damage = 10 + h.Caster.Level; db.DamageType = (int)eDamageType.Heat;
            });
    }
}
