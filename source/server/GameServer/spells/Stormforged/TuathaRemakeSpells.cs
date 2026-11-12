using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS.Spells
{
    /// <summary>
    /// Tuatha Sovereign (class 67) remake spells: ID = FirstId + tier * 16 + key (65600-65727, clear of the Storm's 63600 range
    /// and the old royal table). Seelie and Unseelie Court are the two court effects (tier 0 only, not in the spellbook);
    /// Turn of the Courts seats the Tuatha in the other one for <see cref="CourtSeconds"/> (owner 2026-11-06: not permanent).
    /// </summary>
    public static class TuathaRemakeSpells
    {
        public const int FirstId = 65600;
        public const int Keys = 11;

        public const int SpiritLance = 0, TurnOfTheCourts = 1, BrambleBind = 2, TirNaNogRenewal = 3, SongOfDreams = 4,
            FaerieStep = 5, WildwoodArmament = 6, OtherworldBurst = 7, FairyRing = 8, SeelieKey = 9, UnseelieKey = 10;

        public const int SeelieCourt = FirstId + SeelieKey, UnseelieCourt = FirstId + UnseelieKey;
        public const int CourtSeconds = 60;
        /// <summary>Fairy Ring's ground object: objects.csv 2592 "Rune - Tanglesnare", drawn in the caster's realm (green for Hibernia).</summary>
        public const ushort FairyRingModel = 2592;

        public static bool Owns(int id) => id >= FirstId && id < FirstId + StormSovereignAbilities.Levels.Length * 16 && (id - FirstId) % 16 < Keys;
        public static int Key(int id) => Owns(id) ? (id - FirstId) % 16 : -1;
        // The courts are timed, not maintained: a toggle effect never expires.
        public static bool Toggle(int id) => Key(id) == WildwoodArmament;

        public static IEnumerable<(int Id, MLAbility Ability)> Build()
        {
            for (int tier = 0; tier < StormSovereignAbilities.Levels.Length; tier++)
                for (int key = 0; key < Keys; key++)
                    yield return (FirstId + tier * 16 + key, Make(key, StormSovereignAbilities.Levels[tier]));
        }

        private static MLAbility Make(int key, int level)
        {
            MLAbility Ability(bool positive, Action<MasterLevelAbilityHandler, GameLiving> run, params MLMod[] mods) =>
                new() { Path = "TuathaSovereign", Level = level, Positive = positive, KeepsStealth = positive, Run = run,
                    Mods = mods, Describe = s => s.Description };
            int Value(int n) => Math.Max(1, (int)(n * StormSovereignAbilities.Scale(level)));
            switch (key)
            {
                case SpiritLance:
                    return Ability(false, (h, t) =>
                    {
                        int marks = TuathaMarks.Detonate(h.Caster, t);
                        double damage = h.Spell.Damage * TuathaMarks.Bonus(h.Caster, marks);
                        Strike(h, t, damage);
                        TuathaCourt.OnDetonation(h, marks, damage);
                    });
                case TurnOfTheCourts:
                    return Ability(true, (h, _) =>
                    {
                        bool toUnseelie = TuathaCourtHas(h.Caster, SeelieCourt);
                        EndCourt(h.Caster, toUnseelie ? SeelieCourt : UnseelieCourt);
                        Spell court = SkillBase.GetSpellByID(toUnseelie ? UnseelieCourt : SeelieCourt);
                        if (court == null)
                            return;
                        new StormSovereignHandler(h.Caster, court, h.SpellLine).Hold(h.Caster, CourtSeconds * 1000);
                        h.MessageToCasterPublic($"You take your seat in the {(toUnseelie ? "Unseelie" : "Seelie")} Court for {CourtSeconds} seconds.");
                    });
                case SeelieKey:
                    // Summer: evade and a little absorption; detonations heal, style wards are stronger (TuathaCourt).
                    return Ability(true, (h, _) => h.Hold(h.Caster, CourtSeconds * 1000), new MLMod(eProperty.EvadeChance, MLCategory.Ability, 10), MLMod.Buff(eProperty.ArmorAbsorption, 5));
                case UnseelieKey:
                    // Winter: critical chance; marks rot and detonations drain (TuathaCourt, TuathaRemakeStyles).
                    return Ability(true, (h, _) => h.Hold(h.Caster, CourtSeconds * 1000),
                        new MLMod(eProperty.CriticalMeleeHitChance, MLCategory.Other, 10), new MLMod(eProperty.CriticalSpellHitChance, MLCategory.Ability, 10));
                case BrambleBind:
                    return Ability(false, (h, t) =>
                    {
                        foreach (GameLiving enemy in h.Enemies(t, h.Spell.Radius).Append(t).Distinct())
                        {
                            h.Cast(enemy, 1, eSpellType.SpeedDecrease, db => { db.Value = 99; db.Duration = 15; });
                            TuathaMarks.Add(h.Caster, enemy, 1);
                        }
                    });
                case TirNaNogRenewal:
                    return Ability(true, (h, _) =>
                    {
                        foreach (GameLiving member in h.Group(h.Spell.Radius))
                        {
                            h.Heal(member, member.MaxHealth / 20);
                            h.Cast(member, 2, eSpellType.HealOverTime, db => { db.Target = "Realm"; db.Value = Value(150); db.Frequency = 30; db.Duration = 12; });
                        }
                    });
                case SongOfDreams:
                    return Ability(false, (h, _) =>
                    {
                        foreach (GameLiving enemy in h.Enemies(h.Caster, h.Spell.Radius))
                            h.Cast(enemy, 1, eSpellType.Mesmerize, db => db.Duration = 20);
                    });
                case FaerieStep:
                    return Ability(false, (h, t) => StepBehind((StormSovereignHandler)h, t));
                case WildwoodArmament:
                    return Ability(true, (h, _) => h.Hold(h.Caster), MLMod.Resists(MLCategory.Ability, Value(10)));
                case OtherworldBurst:
                    return Ability(false, (h, _) =>
                    {
                        foreach (GameLiving enemy in h.Enemies(h.Caster, h.Spell.Radius))
                        {
                            int marks = TuathaMarks.Detonate(h.Caster, enemy);
                            double damage = h.Spell.Damage * TuathaMarks.Bonus(h.Caster, marks);
                            Strike(h, enemy, damage);
                            TuathaCourt.OnDetonation(h, marks, damage);
                        }
                    });
                case FairyRing:
                    return Ability(false, (h, _) => h.Field(new Point3D(h.Caster.X, h.Caster.Y, h.Caster.Z), new MasterLevelFieldOptions
                    {
                        // A green ground rune, not the Storm Sovereign's thundercloud. Outside a court the ring still
                        // burns enemies and mends allies (owner 2026-11-09: it did nothing once the courts became timed);
                        // the Unseelie Court adds the drain, the Seelie Court doubles the mending.
                        Name = h.Spell.Name, Model = FairyRingModel, Interval = 3000, Both = true, SuppressEffect = true, PulseEffect = h.Spell.ClientEffect,
                        OnPulse = (field, living) =>
                        {
                            GameLiving owner = field.StormOwner;
                            if (field.IsHostile(living))
                            {
                                Strike(field.Ability, living, field.Ability.Spell.Damage);
                                if (TuathaCourt.IsUnseelie(owner))
                                    field.Ability.Heal(owner, (int)(field.Ability.Spell.Damage * 0.5));
                            }
                            else
                                field.Ability.Heal(living, living.MaxHealth * (TuathaCourt.IsSeelie(owner) ? 6 : 3) / 100);
                        }
                    }));
                default:
                    return Ability(true, (h, _) => h.Hold(h.Caster));
            }
        }

        private static void Strike(MasterLevelAbilityHandler h, GameLiving target, double damage) =>
            StormSovereignAbilities.Strike(h, target, damage, eDamageType.Spirit);

        private static bool TuathaCourtHas(GameLiving living, int spellId) => living.effectListComponent.GetSpellEffects(eEffect.MasterLevel)
            .Any(effect => !effect.IsEnding && effect.SpellHandler?.Spell?.ID == spellId);

        private static void EndCourt(GameLiving living, int spellId)
        {
            foreach (var effect in living.effectListComponent.GetSpellEffects(eEffect.MasterLevel).Where(e => !e.IsEnding && e.SpellHandler?.Spell?.ID == spellId).ToList())
                effect.End();
        }

        /// <summary>Faerie Step: appear behind the enemy (opening the back styles) and mark it once.</summary>
        internal static void StepBehind(StormSovereignHandler h, GameLiving target)
        {
            GameLiving owner = h.Caster;
            if (target == null || !target.IsAlive || target.CurrentRegion != owner.CurrentRegion ||
                !owner.IsWithinRadius(target, h.Spell.Range) || !h.IsEnemy(target))
                return;
            double angle = target.Heading * Math.PI * 2 / 4096;
            var behind = new Point3D(target.X + (int)(Math.Sin(angle) * 70), target.Y - (int)(Math.Cos(angle) * 70), target.Z);
            if (!StormforgedTravel.TryResolvePoint(owner, behind, h.Spell.Range, out var safe))
                return;
            h.Animate(owner);
            owner.Stealth(false);
            if (!owner.MoveTo(owner.CurrentRegionID, safe.X, safe.Y, safe.Z, owner.GetHeading(target)))
                return;
            TuathaMarks.Add(owner, target, 1);
        }
    }
}
