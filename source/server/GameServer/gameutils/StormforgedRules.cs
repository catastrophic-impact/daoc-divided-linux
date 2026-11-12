using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>All custom-class exceptions live behind class 64; stock Thanes are unchanged.</summary>
    public static class StormforgedRules
    {
        public static bool IsStormforged(int classId) => classId == (int)eCharacterClass.Stormforged;

        public static int CreationClass(int classId, int realm, bool enabled) =>
            enabled && realm == (int)eRealm.Midgard && classId == (int)eCharacterClass.MaulerMid
                ? (int)eCharacterClass.Stormforged : classId;

        public static byte OverviewClass(int classId, int realm) =>
            IsStormforged(classId) && realm == (int)eRealm.Midgard
                ? (byte)eCharacterClass.MaulerMid : PendragonRules.IsPendragon(classId) && realm == (int)eRealm.Albion
                ? (byte)eCharacterClass.MaulerAlb : (byte)classId;

        public static bool HasStormsong(GameLiving living) =>
            living.effectListComponent.ContainsEffectForEffectType(eEffect.StormforgedChant);

        public static int RestorePerHit(int damage, int percent) =>
            (int)Math.Min(int.MaxValue, Math.Max(0L, damage) * Math.Clamp(percent, 0, 1000) / 100);

        public static IEnumerable<Spell> VisibleSpellRanks(string lineKey, IEnumerable<Spell> spells)
        {
            var ordered = spells.OrderBy(spell => spell.Level).ThenBy(spell => spell.ID);
            return (lineKey.StartsWith(LinePrefix, StringComparison.Ordinal) || PendragonSpellbook.OwnsLine(lineKey)) ? ordered.TakeLast(1) : ordered;
        }

        private const string LinePrefix = "Stormforged.";
        private static Dictionary<int, int> _clientSlots;

        /// <summary>The single spellbook line the client sees in place of every Stormforged.* line.</summary>
        public static readonly SpellLine ClientLine = new("Stormforged", "Storm Sovereign", "StormforgedCareer", true);

        /// <summary>
        /// The client addresses a list spell by line index + level, so every merged spell needs a
        /// level of its own. Each ability type keeps one fixed slot: its unlock level, pushed past
        /// any slot already taken. Slots never move while leveling, so hotbar buttons stay valid.
        /// </summary>
        public static Dictionary<int, int> AssignClientSlots(IEnumerable<(string LineKey, IEnumerable<Spell> Spells)> lines)
        {
            var slots = new Dictionary<int, int>();
            int previous = 0;
            foreach (var line in lines.Select(line => (line.LineKey, Spells: line.Spells.ToList()))
                         .Where(line => line.Spells.Count > 0)
                         .OrderBy(line => line.Spells.Min(spell => spell.Level)).ThenBy(line => line.LineKey, StringComparer.Ordinal))
            {
                previous = Math.Max(line.Spells.Min(spell => spell.Level), previous + 1);
                foreach (Spell spell in line.Spells)
                    slots[spell.ID] = previous;
            }
            return slots;
        }

        private static Dictionary<int, int> ClientSlots => _clientSlots ??= AssignClientSlots(
            SkillBase.GetSpellLineKeys().Where(key => key.StartsWith(LinePrefix, StringComparison.Ordinal))
                .Select(key => (key, (IEnumerable<Spell>)SkillBase.GetSpellList(key))));

        /// <summary>The level written to and read from the client for a spell in this line.</summary>
        public static int ClientLevel(SpellLine line, Spell spell) =>
            PendragonSpellbook.For(line) is RoyalSpellbook book ? book.ClientLevel(spell) :
            ReferenceEquals(line, ClientLine) && ClientSlots.TryGetValue(spell.ID, out int slot) ? slot :
            SpellLineMerge.TryClientLevel(line, spell, out int merged) ? merged : spell.Level;

        /// <summary>
        /// The spell lines as the client sees them: all Stormforged.* lines merged into one, at the
        /// position of the first, and each stock line with its "(Live)" / "(1.130)" extras merged into
        /// one (<see cref="SpellLineMerge"/>). Other lists come back unchanged.
        /// </summary>
        public static IList<(SpellLine, List<Skill>)> ClientSpellLines(IList<(SpellLine, List<Skill>)> lines)
        {
            lines = SpellLineMerge.ClientSpellLines(PendragonSpellbook.ClientSpellLines(lines));
            int first = -1;
            for (int i = 0; i < lines.Count && first < 0; i++)
                if (lines[i].Item1.KeyName.StartsWith(LinePrefix, StringComparison.Ordinal))
                    first = i;
            if (first < 0)
                return lines;

            var merged = lines.Where(line => line.Item1.KeyName.StartsWith(LinePrefix, StringComparison.Ordinal))
                .SelectMany(line => line.Item2).OrderBy(skill => skill is Spell spell ? ClientLevel(ClientLine, spell) : skill.Level).ToList();
            var result = lines.Where(line => !line.Item1.KeyName.StartsWith(LinePrefix, StringComparison.Ordinal)).ToList();
            result.Insert(first, (ClientLine, merged));
            return result;
        }

        /// <summary>The real line a client-view spell belongs to, for casting and delves.</summary>
        public static SpellLine ServerLine(IList<(SpellLine, List<Skill>)> lines, SpellLine clientLine, Skill skill) =>
            (ReferenceEquals(clientLine, ClientLine) || PendragonSpellbook.For(clientLine) != null || SpellLineMerge.IsMerged(clientLine)) ? lines.FirstOrDefault(line => line.Item2.Contains(skill)).Item1 : clientLine;

        public static bool IsRogSkill(eProperty property) => property is
            eProperty.Skill_Hammer or eProperty.Skill_Axe or eProperty.Skill_Shields or eProperty.Skill_Parry or eProperty.Skill_Stealth or
            eProperty.AllMeleeWeaponSkills or eProperty.AllSkills;

        public static double BuffProgress(double percentage) => Math.Clamp(percentage / 100.0, 0, 1);
    }
}
