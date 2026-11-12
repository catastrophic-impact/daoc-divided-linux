using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// Pure rules for account companions: characters on the player's own account that are
    /// summoned into the player's group as bots and keep everything they earn. Nothing here
    /// touches the world or the database, so every rule is unit tested directly.
    /// </summary>
    public static class AccountCompanionRules
    {
        public const int MaxLevel = 50;

        public enum SummonRefusal
        {
            None,
            Yourself,
            OtherRealm,
            UnsupportedClass,
            NeedsPromotion,
            AlreadySummoned,
            GroupFull,
        }

        public readonly record struct PlanLine(string Spec, int Cap, float Ratio);

        public static SummonRefusal Check(bool isOwnersActiveCharacter, int ownerRealm, int characterRealm,
            bool hasAdvancedClass, bool classSupported, bool alreadySummoned, bool groupFull)
        {
            if (isOwnersActiveCharacter) return SummonRefusal.Yourself;
            if (ownerRealm != characterRealm) return SummonRefusal.OtherRealm;
            if (alreadySummoned) return SummonRefusal.AlreadySummoned;
            if (!hasAdvancedClass) return SummonRefusal.NeedsPromotion;
            if (!classSupported) return SummonRefusal.UnsupportedClass;
            if (groupFull) return SummonRefusal.GroupFull;
            return SummonRefusal.None;
        }

        /// <summary>Same total as GamePlayer.VerifySpecPoints before anything is spent.</summary>
        public static int EarnedSpecPoints(int level, bool secondStage, int multiplier)
        {
            level = Math.Clamp(level, 1, MaxLevel);
            int points = -1;
            for (int i = 1; i <= level; i++)
            {
                if (i <= 5) points += i;
                if (i > 5) points += multiplier * i / 10;
                if (i > 40) points += multiplier * (i - 1) / 20;
            }
            if (secondStage && level != MaxLevel)
                points += multiplier * level / 20;
            return points;
        }

        /// <summary>Total points paid to raise a line from 1 to <paramref name="specLevel"/>.</summary>
        public static int LineCost(int specLevel) => (specLevel * (specLevel + 1) - 2) / 2;

        /// <summary>The free part of an autotrained line, exactly as GamePlayer.GetAutoTrainPoints(spec, 0).</summary>
        public static int AutotrainRefund(int specLevel, int characterLevel)
        {
            int maximum = Math.Max(1, characterLevel / 4);
            int refunded = Math.Min(maximum, specLevel);
            return (refunded * (refunded + 1) - 2) / 2;
        }

        public static int EffectiveLineCost(int specLevel, bool autotrain, int characterLevel) =>
            LineCost(specLevel) - (autotrain ? AutotrainRefund(specLevel, characterLevel) : 0);

        public static int SpentSpecPoints(IEnumerable<(int Level, bool Autotrain)> lines, int characterLevel) =>
            lines.Sum(line => EffectiveLineCost(line.Level, line.Autotrain, characterLevel));

        /// <summary>Points to raise a line one level, after any autotrain refund (0 for a free autotrain level).</summary>
        public static int NextLevelCost(int specLevel, bool autotrain, int characterLevel) =>
            EffectiveLineCost(specLevel + 1, autotrain, characterLevel) - EffectiveLineCost(specLevel, autotrain, characterLevel);

        /// <summary>A line can be trained up to the character's level, as at a trainer.</summary>
        public static int LineLimit(int characterLevel) => Math.Clamp(characterLevel, 1, MaxLevel);

        /// <summary>
        /// The level a respec leaves a line at, as GamePlayer.RespecSingleLine: an autotrained line
        /// keeps its free levels (character level / 4, from level 8), every other line goes to 1.
        /// </summary>
        public static int RespecLevel(bool autotrain, int characterLevel) =>
            autotrain && characterLevel >= 8 ? characterLevel / 4 : 1;

        /// <summary>Points a respec of the line gives back.</summary>
        public static int RespecRefund(int specLevel, bool autotrain, int characterLevel)
        {
            int target = Math.Min(specLevel, RespecLevel(autotrain, characterLevel));
            return EffectiveLineCost(specLevel, autotrain, characterLevel) - EffectiveLineCost(target, autotrain, characterLevel);
        }

        /// <summary>
        /// Hand training: raises a line by up to <paramref name="steps"/> levels, stopping at the
        /// character's level or when the next level costs more than the points left.
        /// </summary>
        public static int TrainLine(int specLevel, int steps, bool autotrain, int characterLevel, int points, out int cost)
        {
            cost = 0;
            int level = specLevel;
            int limit = LineLimit(characterLevel);
            for (; steps > 0 && level < limit; steps--)
            {
                int next = NextLevelCost(level, autotrain, characterLevel);
                if (cost + next > points)
                    break;
                cost += next;
                level++;
            }
            return level;
        }

        /// <summary>
        /// Spends only points that are actually free, with the same order the bots use
        /// (BotSpecLine ratios first, then any leftover into lines still under their caps).
        /// Lines outside the plan, including anything the player trained by hand, are never lowered.
        /// </summary>
        public static Dictionary<string, int> Spend(IReadOnlyDictionary<string, int> current,
            ICollection<string> autotrain, IEnumerable<PlanLine> plan, int characterLevel, int points, out int leftover)
        {
            var levels = new Dictionary<string, int>(current, StringComparer.OrdinalIgnoreCase);
            var autotrained = new HashSet<string>(autotrain ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            PlanLine[] lines = (plan ?? Enumerable.Empty<PlanLine>()).OrderByDescending(line => line.Ratio).ToArray();
            bool spendLeftover = false;

            while (points > 0)
            {
                bool spent = false;
                foreach (PlanLine line in lines)
                {
                    if (line.Ratio <= 0 && characterLevel < MaxLevel)
                        continue;
                    if (!levels.TryGetValue(line.Spec, out int level))
                        continue;
                    int limit = Math.Min(line.Cap, characterLevel);
                    if (level >= limit)
                        continue;
                    if (!spendLeftover && level >= (int)(characterLevel * line.Ratio))
                        continue;

                    bool isAutotrain = autotrained.Contains(line.Spec);
                    int cost = EffectiveLineCost(level + 1, isAutotrain, characterLevel) -
                               EffectiveLineCost(level, isAutotrain, characterLevel);
                    if (cost > points)
                        continue;

                    points -= cost;
                    levels[line.Spec] = level + 1;
                    spent = true;
                }

                if (spent)
                    continue;
                if (spendLeftover)
                    break;
                spendLeftover = true;
            }

            leftover = points;
            return levels;
        }

        /// <summary>
        /// What the owner reads when a companion joins. Auto-spend stays off by default (owner 2026-11-11, DR-273), so the
        /// message says so and how to turn it on: a player waited for a Berserker to train itself.
        /// </summary>
        public static string JoinMessage(string name, int level, string className, bool autoSpend, int unspentPoints)
        {
            string message = $"{name}, your level {level} {className}, joins your group with its own gear. " +
                             "Everything it earns is saved to its character.";
            if (autoSpend)
                return message + $" {name} spends its own specialization points.";
            string points = unspentPoints > 0 ? $"{name} has {unspentPoints} specialization points to spend. " : "";
            return message + $" {points}You train {name} yourself (/companion spec {name}), " +
                   $"or type /companion autospend {name} on to let it spend its own points.";
        }

        /// <summary>How well a bot plan matches what the character already trained.</summary>
        public static int PlanFit(IEnumerable<PlanLine> plan, Func<string, int> trained) =>
            (plan ?? Enumerable.Empty<PlanLine>()).Sum(line => Math.Max(0, trained(line.Spec) - 1));

        /// <summary>Base stat raises for levels gained, as GamePlayer.OnLevelUp gives them.</summary>
        public static (int Primary, int Secondary, int Tertiary) StatGains(int previousLevel, int newLevel)
        {
            int primary = 0, secondary = 0, tertiary = 0;
            for (int i = newLevel; i > Math.Max(previousLevel, 5); i--)
            {
                primary++;
                if ((i - 6) % 2 == 0) secondary++;
                if ((i - 6) % 3 == 0) tertiary++;
            }
            return (primary, secondary, tertiary);
        }

        /// <summary>Experience needed for the next level is <c>experienceForLevel(level)</c>, as for players.</summary>
        public static int LevelForExperience(int level, long experience, Func<int, long> experienceForLevel)
        {
            level = Math.Clamp(level, 1, MaxLevel);
            while (level < MaxLevel && experience >= experienceForLevel(level))
                level++;
            return level;
        }

        public static bool IsSecondStage(int level, long experience, Func<int, long> experienceForLevel)
        {
            if (level < 40 || level >= MaxLevel)
                return false;
            long current = experienceForLevel(level - 1);
            long next = experienceForLevel(level);
            return experience >= 1 + current + (next - current) / 2;
        }

        /// <summary>
        /// Replaces the levels of the given lines inside a player's SerializedSpecs string and
        /// keeps every other entry and the original order.
        /// </summary>
        public static string MergeSerializedSpecs(string existing, IEnumerable<(string Key, int Level)> trained)
        {
            var updates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach ((string key, int level) in trained ?? Enumerable.Empty<(string, int)>())
                if (!string.IsNullOrWhiteSpace(key))
                    updates[key] = level;

            var entries = new List<string>();
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string entry in (existing ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] parts = entry.Split('|');
                if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]))
                    continue;
                if (!written.Add(parts[0]))
                    continue;
                entries.Add(updates.TryGetValue(parts[0], out int level) ? $"{parts[0]}|{level}" : entry);
            }

            foreach ((string key, int level) in updates)
                if (written.Add(key))
                    entries.Add($"{key}|{level}");

            return string.Join(';', entries);
        }

        /// <summary>
        /// A character's saved model packs its height choice (bits 0x1800) and other creation
        /// data above the model ID. An NPC needs the bare model ID, or the client draws nothing.
        /// </summary>
        public static ushort NpcModel(int characterModel) => (ushort)(characterModel & 0x7FF);

        /// <summary>The NPC size for the character's short, average or tall choice.</summary>
        public static byte NpcSize(int characterModel) => (characterModel & 0x1800) switch
        {
            0x800 => 45,
            0x1800 => 55,
            _ => 50,
        };

        public static string RefusalMessage(SummonRefusal refusal, string name) => refusal switch
        {
            SummonRefusal.Yourself => "You are playing that character.",
            SummonRefusal.OtherRealm => $"{name} is from another realm and cannot join you.",
            SummonRefusal.AlreadySummoned => $"{name} is already with you.",
            SummonRefusal.NeedsPromotion => $"{name} has not chosen a class yet. Type /companion {name} <class> to choose one.",
            SummonRefusal.UnsupportedClass => $"{name}'s class has no companion AI yet, so it can only be played directly.",
            SummonRefusal.GroupFull => "Your group is full.",
            _ => string.Empty,
        };
    }
}
