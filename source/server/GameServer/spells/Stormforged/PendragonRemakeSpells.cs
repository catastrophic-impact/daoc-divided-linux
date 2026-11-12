using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS.Spells
{
    /// <summary>
    /// Pendragon Sovereign (class 65) remake spells: ID = FirstId + tier * 16 + key (65800-65927, clear of the Storm's 63600
    /// and the Tuatha's 65600 ranges and the old royal table). Tier 0 keys 9-15 are the Valor buff icons "Valor 1" to
    /// "Valor 7" (not in the spellbook; <see cref="PendragonValor"/> shows them). Seven old royal buttons are kept
    /// (chant, Will, Armor, Royal Lion, Lion of Albion, King's Advance, Veil of Avalon).
    /// </summary>
    public static class PendragonRemakeSpells
    {
        public const int FirstId = 65800;
        public const int Keys = 9;
        public const int ValorIconFirstKey = 9, ValorIcons = 7;

        public const int CrownfireLance = 0, GuardOfTheCrown = 1, SovereignsChallenge = 2, RallyToTheCrown = 3, ConsecratedArmament = 4,
            DecreeStandFast = 5, BindingRadiance = 6, Judgment = 7, FieldOfJudgement = 8;

        public static bool Owns(int id) => id >= FirstId && id < FirstId + StormSovereignAbilities.Levels.Length * 16 && (id - FirstId) % 16 < Keys;
        public static int Key(int id) => Owns(id) ? (id - FirstId) % 16 : -1;
        public static bool Toggle(int id) => Key(id) == ConsecratedArmament;
        /// <summary>Field of Judgement's ground object: objects.csv 2585 "Ward - Rejuvenation", drawn in the caster's realm.</summary>
        public const ushort FieldModel = 2585;
        public static bool IsValorIcon(int id) => id >= FirstId + ValorIconFirstKey && id < FirstId + ValorIconFirstKey + ValorIcons;
        public static int ValorIconId(int valor) => FirstId + ValorIconFirstKey + Math.Clamp(valor, 1, ValorIcons) - 1;

        public static IEnumerable<(int Id, MLAbility Ability)> Build()
        {
            for (int tier = 0; tier < StormSovereignAbilities.Levels.Length; tier++)
                for (int key = 0; key < Keys; key++)
                    yield return (FirstId + tier * 16 + key, Make(key, StormSovereignAbilities.Levels[tier]));
            for (int i = 0; i < ValorIcons; i++)
                yield return (FirstId + ValorIconFirstKey + i, new MLAbility
                {
                    Path = "PendragonSovereign", Level = 1, Positive = true, KeepsStealth = true, Describe = s => s.Description, Run = (_, _) => { }
                });
        }

        private static MLAbility Make(int key, int level)
        {
            MLAbility Ability(bool positive, Action<MasterLevelAbilityHandler, GameLiving> run, params MLMod[] mods) =>
                new() { Path = "PendragonSovereign", Level = level, Positive = positive, KeepsStealth = positive, Run = run,
                    Mods = mods, Describe = s => s.Description };
            int Value(int n) => Math.Max(1, (int)(n * StormSovereignAbilities.Scale(level)));
            switch (key)
            {
                case CrownfireLance:
                    // Instant with 3+ Valor (StormSovereignHandler); +10% damage per Valor held, none spent.
                    return Ability(false, (h, t) => Strike(h, t, h.Spell.Damage * PendragonValor.Held(h.Caster)));
                case GuardOfTheCrown:
                    // The guarded ally gains 20% absorption and a holy ward; Knight's Guard (RA) makes the ward 25/50/75% stronger.
                    return Ability(true, (h, t) =>
                    {
                        GameLiving ally = t ?? h.Caster;
                        h.Hold(ally, 15000);
                        int rank = h.Caster is GamePlayer player ? player.GetAbilityLevel(RealmAbilities.PendragonKnightsGuard.KeyName) : 0;
                        PendragonRemakeStyles.Ward(h, ally, (int)(ally.MaxHealth * 0.10 * (1 + 0.25 * Math.Clamp(rank, 0, 3)) * PendragonValor.Held(h.Caster)));
                        PendragonValor.Add(h.Caster, 1);
                    }, MLMod.Buff(eProperty.ArmorAbsorption, 20));
                case SovereignsChallenge:
                    return Ability(false, (h, _) =>
                    {
                        var enemies = h.Enemies(h.Caster, h.Spell.Radius).ToList();
                        foreach (GameLiving enemy in enemies)
                            h.Cast(enemy, 1, eSpellType.Taunt, db => db.Value = 400 + h.Caster.Level * 10);
                        PendragonValor.Add(h.Caster, Math.Clamp(enemies.Count, 1, 3));
                    });
                case RallyToTheCrown:
                    return Ability(true, (h, _) =>
                    {
                        // 10% at once, +2% per Valor held.
                        int percent = 10 + 2 * PendragonValor.Count(h.Caster);
                        foreach (GameLiving member in h.Group(h.Spell.Radius))
                        {
                            h.Heal(member, member.MaxHealth * percent / 100);
                            h.Cast(member, 2, eSpellType.HealOverTime, db => { db.Target = "Realm"; db.Value = Value(150); db.Frequency = 30; db.Duration = 12; });
                        }
                    });
                case ConsecratedArmament:
                    // Maintained: the weapon procs live in StormSovereignEffect; magic resistance outside the buff cap.
                    return Ability(true, (h, _) => h.Hold(h.Caster), MLMod.Resists(MLCategory.Ability, Value(10)));
                case DecreeStandFast:
                    return Ability(true, (h, _) =>
                    {
                        foreach (GameLiving member in h.Group(h.Spell.Radius))
                        {
                            h.Hold(member, 15000);
                            PendragonRemakeStyles.Ward(h, member, (int)(member.MaxHealth / 10 * PendragonValor.Held(h.Caster)));
                        }
                    }, new MLMod(eProperty.BlockChance, MLCategory.Ability, 10), new MLMod(eProperty.ParryChance, MLCategory.Ability, 10));
                case BindingRadiance:
                    return Ability(false, (h, t) =>
                    {
                        // Valor held: +10% damage each, and at 3+ Valor the stun lasts 6 seconds instead of 4.
                        int valor = PendragonValor.Count(h.Caster);
                        h.Cast(t, valor >= 3 ? 4 : 1, eSpellType.StyleStun, db => db.Duration = valor >= 3 ? 6 : 4);
                        foreach (GameLiving enemy in h.Enemies(t, 300).Where(e => e != t))
                            h.Cast(enemy, 2, eSpellType.SpeedDecrease, db => { db.Value = 40; db.Duration = 8; });
                        Strike(h, t, h.Spell.Damage * PendragonValor.Held(h.Caster));
                    });
                case Judgment:
                    return Ability(false, (h, _) =>
                    {
                        int valor = PendragonValor.Spend(h.Caster);
                        double damage = h.Spell.Damage * PendragonValor.Bonus(valor);
                        foreach (GameLiving enemy in h.Enemies(h.Caster, h.Spell.Radius).ToList())
                        {
                            Strike(h, enemy, damage);
                            if (valor >= 5)
                                h.Cast(enemy, 3, eSpellType.StyleStun, db => db.Duration = 2);
                        }
                        PendragonValor.OnJudgment(h, valor, ward: false);
                    });
                case FieldOfJudgement:
                    return Ability(false, (h, _) => h.Field(new Point3D(h.Caster.X, h.Caster.Y, h.Caster.Z), new MasterLevelFieldOptions
                    {
                        // The Albion healing-ward crystal, not the Storm Sovereign's thundercloud (owner 2026-11-09: lightning on the Alb class).
                        Name = h.Spell.Name, Model = FieldModel, Interval = 3000, Both = true, SuppressEffect = true, PulseEffect = h.Spell.ClientEffect,
                        OnPulse = (field, living) =>
                        {
                            // Each pulse reads the Valor held then: +10% damage and healing per point.
                            double held = PendragonValor.Held(field.StormOwner);
                            if (field.IsHostile(living))
                                Strike(field.Ability, living, field.Ability.Spell.Damage * held);
                            else
                                field.Ability.Heal(living, (int)(living.MaxHealth * 3 / 100 * held));
                        }
                    }));
                default:
                    return Ability(true, (h, _) => h.Hold(h.Caster));
            }
        }

        private static void Strike(MasterLevelAbilityHandler h, GameLiving target, double damage) =>
            StormSovereignAbilities.Strike(h, target, damage, eDamageType.Heat);
    }
}
