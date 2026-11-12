using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.PacketHandler;
using DOL.GS.Spells;

namespace DOL.GS
{
    /// <summary>
    /// Sidhe Marks (Tuatha Sovereign, class 67): each Tuatha's own marks on each enemy, up to five. Styles and spells add
    /// them; detonations burst them for +25% damage each. Every mark that lands flashes a stock Eldritch "Ethereal" effect on
    /// the enemy, bigger as the count rises (owner's choice: effect flashes, no orbs). See docs/TUATHA-SOVEREIGN-REMAKE.md.
    /// </summary>
    public static class TuathaMarks
    {
        public const int Max = 5;
        public const int DecayMs = 20000;
        public const double BonusPerMark = 0.25;
        // Ethereal Concussion, Detonation and Devastation (Eldritch spirit effects); Major Ethereal Devastation bursts.
        public const ushort SmallFlash = 4501, MediumFlash = 4505, FullFlash = 4509, BurstFlash = 4510;
        private const string Key = "TuathaMarks";
        private static readonly object _lock = new();

        private sealed class Entry
        {
            public int Count;
            public long Last;
        }

        public static int Count(GameLiving owner, GameLiving target)
        {
            if (owner == null || target == null)
                return 0;
            lock (_lock)
            {
                var table = target.TempProperties.GetProperty<Dictionary<GameLiving, Entry>>(Key);
                if (table == null || !table.TryGetValue(owner, out Entry entry))
                    return 0;
                if (GameLoop.GameLoopTime - entry.Last >= DecayFor(owner))
                {
                    table.Remove(owner);
                    return 0;
                }
                return entry.Count;
            }
        }

        /// <summary>Adds marks (doubled in Otherworld Ascendant), flashes the enemy and returns the new count.</summary>
        public static int Add(GameLiving owner, GameLiving target, int amount)
        {
            if (owner == null || target == null || amount <= 0 || !target.IsAlive || owner == target)
                return 0;
            if (StormforgedHeroicRules.HasAvatar(owner))
                amount *= 2;
            int count;
            lock (_lock)
            {
                var table = target.TempProperties.GetProperty<Dictionary<GameLiving, Entry>>(Key);
                if (table == null)
                    target.TempProperties.SetProperty(Key, table = new Dictionary<GameLiving, Entry>());
                if (!table.TryGetValue(owner, out Entry entry) || GameLoop.GameLoopTime - entry.Last >= DecayFor(owner))
                    table[owner] = entry = new Entry();
                entry.Count = Math.Min(Max, entry.Count + amount);
                entry.Last = GameLoop.GameLoopTime;
                count = entry.Count;
            }
            Flash(owner, target, count >= Max ? FullFlash : count >= 3 ? MediumFlash : SmallFlash);
            return count;
        }

        /// <summary>Removes the owner's marks from the enemy and returns how many there were (Heart of the Court leaves some).</summary>
        public static int Detonate(GameLiving owner, GameLiving target)
        {
            int count = Count(owner, target);
            if (count == 0)
                return 0;
            lock (_lock)
            {
                var table = target.TempProperties.GetProperty<Dictionary<GameLiving, Entry>>(Key);
                int left = Math.Min(count, Leftover(owner));
                if (left > 0 && table.TryGetValue(owner, out Entry entry))
                {
                    entry.Count = left;
                    entry.Last = GameLoop.GameLoopTime;
                }
                else
                    table?.Remove(owner);
            }
            Flash(owner, target, BurstFlash);
            return count;
        }

        /// <summary>Damage multiplier for a detonation of <paramref name="marks"/>, with Morrigan's Edge.</summary>
        public static double Bonus(GameLiving owner, int marks) =>
            (1 + BonusPerMark * Math.Clamp(marks, 0, Max)) * (1 + 0.05 * RealmAbilityLevel(owner, RealmAbilities.TuathaMorrigansEdge.KeyName));

        public static void Clear(GameLiving target)
        {
            lock (_lock)
                target?.TempProperties.RemoveProperty(Key);
        }

        // Heart of the Court (RA): marks last 20/30/40 s; detonations leave 0/1/2.
        public static int DecayFor(GameLiving owner) => DecayMs + 10000 * Math.Clamp(RealmAbilityLevel(owner, RealmAbilities.TuathaHeartOfTheCourt.KeyName), 0, 2);
        private static int Leftover(GameLiving owner) => RealmAbilityLevel(owner, RealmAbilities.TuathaHeartOfTheCourt.KeyName) switch { >= 3 => 2, 2 => 1, _ => 0 };
        private static int RealmAbilityLevel(GameLiving owner, string key) => owner is GamePlayer player ? player.GetAbilityLevel(key) : 0;

        private static void Flash(GameLiving owner, GameLiving target, ushort effect)
        {
            if (target.CurrentRegion == null)
                return;
            foreach (GamePlayer viewer in target.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                viewer.Out.SendSpellEffectAnimation(owner, target, effect, 0, false, 1);
        }
    }

    /// <summary>
    /// The two faerie courts. The Tuatha holds one of two maintained effects (StormSovereignEffect, saved over relog):
    /// Seelie (summer: detonations heal the party, stronger style wards, evade) or Unseelie (winter: marks rot, detonations
    /// drain, critical chance). Turn of the Courts swaps them. Otherworld Ascendant gives both riders.
    /// </summary>
    public static class TuathaCourt
    {
        public static bool IsSeelie(GameLiving living) => Has(living, TuathaRemakeSpells.SeelieCourt) || StormforgedHeroicRules.HasAvatar(living) && PendragonRules.IsTuatha(living);
        public static bool IsUnseelie(GameLiving living) => Has(living, TuathaRemakeSpells.UnseelieCourt) || StormforgedHeroicRules.HasAvatar(living) && PendragonRules.IsTuatha(living);

        private static bool Has(GameLiving living, int spellId) => living != null && living.effectListComponent.GetSpellEffects(eEffect.MasterLevel)
            .Any(effect => !effect.IsEnding && effect.SpellHandler?.Spell?.ID == spellId);

        /// <summary>The rider a detonation of <paramref name="marks"/> worth <paramref name="damage"/> brings with it.</summary>
        public static void OnDetonation(MasterLevelAbilityHandler h, int marks, double damage)
        {
            if (marks <= 0)
                return;
            GameLiving owner = h.Caster;
            if (IsSeelie(owner))
                foreach (GameLiving member in h.Group(1500))
                    h.Heal(member, member.MaxHealth * 3 * marks / 100);
            if (IsUnseelie(owner))
                h.Heal(owner, (int)(damage * 0.5));
        }

        /// <summary>Seelie wards are half again as strong.</summary>
        public static double WardFactor(GameLiving owner) => IsSeelie(owner) ? 1.5 : 1.0;
    }

    /// <summary>The Regalia of Tara: the Tuatha Sovereign's armor set (tools/sovereign/tara_regalia_edits.py).</summary>
    public static class TaraRegalia
    {
        public const string Prefix = "tara_regalia_";
        public const string GrantKeyword = "Regalia of Tara";

        public static RegaliaSet Set { get; } = new("Regalia of Tara", Prefix, GrantKeyword, PendragonRules.IsTuatha,
            "Wear the court of Tir na Nog, sovereign. All seven pieces together hold its full glamour.", new (int, string, MLMod[])[]
            {
                (2, "+5% to all resistances", MLMod.Resists(MLCategory.Ability, 5, physical: true)),
                (4, "+10% melee and style damage", new[] { new MLMod(eProperty.MeleeDamage, MLCategory.Ability, 10), new MLMod(eProperty.StyleDamage, MLCategory.Ability, 10) }),
                (6, "+10% evade and spell damage", new[] { new MLMod(eProperty.EvadeChance, MLCategory.Ability, 10), new MLMod(eProperty.SpellDamage, MLCategory.Ability, 10) }),
                (7, "+10% maximum health, and every block, parry or evade marks the attacker twice", new[] { new MLMod(eProperty.Of_Toughness, MLCategory.Ability, 10) }),
            });

        public static bool IsPiece(DOL.Database.DbInventoryItem item) => Set.IsPiece(item);

        /// <summary>Marks a block, parry or evade puts on the attacker: 1, or 2 with the full set.</summary>
        public static int DefenseMarks(GamePlayer player) => player != null && Set.HasFullSet(player) ? 2 : 1;
    }
}
