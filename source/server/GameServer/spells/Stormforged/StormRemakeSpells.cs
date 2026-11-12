using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS.Spells
{
    /// <summary>
    /// Storm Sovereign (class 64) spells that replace merged or reworked royal abilities. Their own IDs keep
    /// Pendragon and Tuatha, which share the old royal table, unchanged. ID = FirstId + tier * 16 + key.
    /// </summary>
    public static class StormRemakeSpells
    {
        public const int FirstId = 63600;
        public const int Keys = 9;

        // Runic Reversal (3) and Hunter's Mark (4) were merged away on 2026-11-04; their keys stay reserved.
        public const int RoyalDecree = 0, RallyToTheCrown = 1, RunicArmament = 2, RunicReversal = 3,
            HuntersMark = 4, RoyalThunder = 5, Stormbreak = 6, WintersGrasp = 7, Hammerfall = 8;

        /// <summary>With any Tempest these cast instantly and cost no power: the charges pay instead.</summary>
        public static bool PaysWithTempest(int id) => Key(id) is RoyalThunder or Stormbreak or WintersGrasp or Hammerfall;

        public static bool Owns(int id) => id >= FirstId && id < FirstId + StormSovereignAbilities.Levels.Length * 16 && (id - FirstId) % 16 < Keys;
        public static int Key(int id) => Owns(id) ? (id - FirstId) % 16 : -1;
        public static bool Toggle(int id) => Key(id) == RunicArmament;

        public static IEnumerable<(int Id, MLAbility Ability)> Build()
        {
            for (int tier = 0; tier < StormSovereignAbilities.Levels.Length; tier++)
                for (int key = 0; key < Keys; key++)
                    yield return (FirstId + tier * 16 + key, Make(key, StormSovereignAbilities.Levels[tier]));
        }

        private static MLAbility Make(int key, int level)
        {
            MLAbility Ability(bool positive, Action<MasterLevelAbilityHandler, GameLiving> run, params MLMod[] mods) =>
                new() { Path = "StormSovereign", Level = level, Positive = positive, KeepsStealth = positive, Run = run,
                    Mods = mods, Describe = s => s.Description };
            int Value(int n) => Math.Max(1, (int)(n * StormSovereignAbilities.Scale(level)));
            switch (key)
            {
                case RoyalDecree:
                    // Advance, Stand Fast and Strike True in one shout.
                    return Ability(true, (h, _) => { foreach (var member in h.Group(h.Spell.Radius)) h.Hold(member); },
                        MLMod.Speed(1.15), MLMod.Buff(eProperty.ArmorAbsorption, 10), MLMod.Buff(eProperty.ToHitBonus, 10), MLMod.Buff(eProperty.MeleeDamage, 10));
                case RallyToTheCrown:
                    return Ability(true, (h, _) =>
                    {
                        h.Heal(h.Caster, h.Caster.MaxHealth / 4);
                        foreach (var ally in h.Group(h.Spell.Radius).Where(ally => ally != h.Caster))
                            h.Heal(ally, ally.MaxHealth * 15 / 100);
                        foreach (var ally in h.Group(h.Spell.Radius))
                            MasterLevelAbilityHandler.Restore(h.Caster, ally, 0, 20, h.Spell.Name);
                    });
                case RunicArmament:
                    // Weapon procs and the old Runic Ward's extra magic resistance, as one maintained buff.
                    return Ability(true, (h, _) => h.Hold(h.Caster), MLMod.Resists(MLCategory.Ability, Value(10)));
                case RunicReversal:
                    return Ability(true, (h, _) => h.Hold(h.Caster));
                case HuntersMark:
                    return new MLAbility { Path = "StormSovereign", Level = level, KeepsStealth = true, Describe = s => s.Description,
                        Run = (h, _) => { if (h.Caster.IsStealthed) { h.Hold(h.Caster); StormTempest.Add(h.Caster, 3); } } };
                case RoyalThunder:
                    return Ability(false, (h, t) => StormSovereignAbilities.Strike(h, t, h.Spell.Damage * StormTempest.Bonus(StormTempest.Spend(h.Caster))));
                case Stormbreak:
                    return Ability(false, (h, _) =>
                    {
                        double damage = h.Spell.Damage * StormTempest.Bonus(StormTempest.Spend(h.Caster));
                        foreach (var enemy in h.Enemies(h.Caster, h.Spell.Radius))
                            StormSovereignAbilities.Strike(h, enemy, damage);
                    });
                case WintersGrasp:
                    return Ability(false, (h, t) =>
                    {
                        StormTempest.Take(h.Caster, 1);
                        StormSovereignAbilities.Strike(h, t, h.Spell.Damage, eDamageType.Cold);
                        h.Cast(t, 1, eSpellType.SpeedDecrease, db => { db.Duration = 8; db.Value = 30; });
                    });
                case Hammerfall:
                    return Ability(false, (h, t) =>
                    {
                        StormTempest.Take(h.Caster, 1);
                        StormSovereignAbilities.Hammerfall((StormSovereignHandler)h, t);
                    });
                default:
                    return Ability(true, (h, _) => h.Hold(h.Caster));
            }
        }
    }
}
