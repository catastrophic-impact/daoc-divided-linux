using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DOL.GS
{
    /// <summary>
    /// Loot tuning read from runtime/server/loot-rules.json, beside bot-goals.json (loot redesign phase 2, owner
    /// 2026-11-05). Every default is the number the code used before, so a missing file changes nothing.
    /// </summary>
    public sealed record LootRules
    {
        public const string FileName = "loot-rules.json";

        public int Version { get; init; } = 1;
        public RandomItemLootRules RandomItems { get; init; } = new();
        public CoinLootRules Coin { get; init; } = new();

        public static LootRules Defaults => new();

        public void Validate()
        {
            if (Version != 1)
                throw new InvalidDataException("Unsupported loot rules version " + Version + ".");
            if (RandomItems == null || Coin == null)
                throw new InvalidDataException("RandomItems and Coin must both be present.");
            RandomItems.Validate();
            Coin.Validate();
        }

        internal static void Range(double value, double min, double max, string name)
        {
            if (!double.IsFinite(value) || value < min || value > max)
                throw new InvalidDataException($"{name} must be from {min} to {max} (it is {value}).");
        }

        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            IgnoreReadOnlyProperties = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public static LootRules Parse(string json)
        {
            LootRules rules = JsonSerializer.Deserialize<LootRules>(json, Json)
                ?? throw new InvalidDataException("The loot rules file is empty.");
            rules.Validate();
            return rules;
        }

        public string ToJson() => JsonSerializer.Serialize(this, Json);
    }

    /// <summary>A group's chance drops by Chance once the mob is above AboveLevel.</summary>
    public sealed record LootLevelPenalty(int AboveLevel, int Chance);

    /// <summary>Random (generated) items: ROGMobGenerator.</summary>
    public sealed record RandomItemLootRules
    {
        /// <summary>Every final chance is multiplied by this. 0.5 halves every random item drop.</summary>
        public double ChanceMultiplier { get; init; } = 1.0;

        /// <summary>Extra multiplier for named mobs (capitalized names) and epic NPCs.</summary>
        public double NamedChanceMultiplier { get; init; } = 1.0;

        /// <summary>Extra multiplier when the loot owner is in the frontier.</summary>
        public double FrontierChanceMultiplier { get; init; } = 1.0;

        /// <summary>Percent chance against an even con (yellow) mob, before the solo bonus.</summary>
        public int BaseChance { get; init; } = 14;

        /// <summary>Percent added per con step above yellow, taken away per step below blue (blue counts as yellow).</summary>
        public int ChancePerCon { get; init; } = 3;

        public int SoloBonus { get; init; } = 10;

        public LootLevelPenalty[] GroupLevelPenalties { get; init; } = [new(27, 3), new(40, 3)];

        /// <summary>A group may get one item per this many members (rounded), from 1 to GroupMaxDrops.</summary>
        public int GroupMembersPerDrop { get; init; } = 3;

        public int GroupMaxDrops { get; init; } = 3;

        /// <summary>Above this mob level a group may get one more item.</summary>
        public int GroupExtraDropAboveLevel { get; init; } = 65;

        /// <summary>Above this mob level a group always gets at least one item.</summary>
        public int GroupGuaranteedDropAboveLevel { get; init; } = 67;

        public int BattlegroupChance { get; init; } = 2;

        public int BattlegroupPlayersPerDrop { get; init; } = 50;

        public int EpicBattlegroupDropMultiplier { get; init; } = 2;

        /// <summary>
        /// Personal loot (phase 5, owner 2026-11-05): in a group or battlegroup each member who rolls an item gets one made
        /// for their own class, meant for them. Off: the old way, where the item is made for a random member's class and
        /// goes to the kill's owners.
        /// </summary>
        public bool PersonalLoot { get; init; } = true;

        /// <summary>
        /// Bad-luck protection (phase 5): every roll a participant misses adds this many percent to their next roll, up to
        /// BadLuckMax, until they get an item. 0 turns it off (the default, so drop rates stay as they were).
        /// </summary>
        public double BadLuckStep { get; init; } = 0;

        public double BadLuckMax { get; init; } = 25;

        /// <summary>How strong a random item's bonuses are (phase 3). Off by default: the old roll-then-trim is used.</summary>
        public RandomItemBudgetRules Budget { get; init; } = new();

        /// <summary>The final percent chance for a roll: the multipliers applied, kept from 0 to 100.</summary>
        public double Scale(double chance, bool named, bool frontier)
        {
            chance *= ChanceMultiplier;
            if (named)
                chance *= NamedChanceMultiplier;
            if (frontier)
                chance *= FrontierChanceMultiplier;
            return Math.Clamp(chance, 0, 100);
        }

        public double GroupLevelPenalty(int mobLevel) => GroupLevelPenalties.Where(p => mobLevel > p.AboveLevel).Sum(p => p.Chance);

        internal void Validate()
        {
            LootRules.Range(ChanceMultiplier, 0, 10, "RandomItems.ChanceMultiplier");
            LootRules.Range(NamedChanceMultiplier, 0, 10, "RandomItems.NamedChanceMultiplier");
            LootRules.Range(FrontierChanceMultiplier, 0, 10, "RandomItems.FrontierChanceMultiplier");
            LootRules.Range(BaseChance, 0, 100, "RandomItems.BaseChance");
            LootRules.Range(ChancePerCon, 0, 50, "RandomItems.ChancePerCon");
            LootRules.Range(SoloBonus, -100, 100, "RandomItems.SoloBonus");
            if (GroupLevelPenalties == null)
                throw new InvalidDataException("RandomItems.GroupLevelPenalties must be a list (it may be empty).");
            foreach (LootLevelPenalty penalty in GroupLevelPenalties)
            {
                if (penalty == null)
                    throw new InvalidDataException("RandomItems.GroupLevelPenalties has an empty entry.");
                LootRules.Range(penalty.AboveLevel, 0, 255, "RandomItems.GroupLevelPenalties.AboveLevel");
                LootRules.Range(penalty.Chance, -100, 100, "RandomItems.GroupLevelPenalties.Chance");
            }
            LootRules.Range(GroupMembersPerDrop, 1, 8, "RandomItems.GroupMembersPerDrop");
            LootRules.Range(GroupMaxDrops, 1, 8, "RandomItems.GroupMaxDrops");
            LootRules.Range(GroupExtraDropAboveLevel, 0, 255, "RandomItems.GroupExtraDropAboveLevel");
            LootRules.Range(GroupGuaranteedDropAboveLevel, 0, 255, "RandomItems.GroupGuaranteedDropAboveLevel");
            LootRules.Range(BattlegroupChance, 0, 100, "RandomItems.BattlegroupChance");
            LootRules.Range(BattlegroupPlayersPerDrop, 1, 500, "RandomItems.BattlegroupPlayersPerDrop");
            LootRules.Range(EpicBattlegroupDropMultiplier, 1, 10, "RandomItems.EpicBattlegroupDropMultiplier");
            LootRules.Range(BadLuckStep, 0, 50, "RandomItems.BadLuckStep");
            LootRules.Range(BadLuckMax, 0, 100, "RandomItems.BadLuckMax");
            if (Budget == null)
                throw new InvalidDataException("RandomItems.Budget must be present when it is written.");
            Budget.Validate();
        }
    }

    /// <summary>
    /// Budget-first bonuses (loot redesign phase 3, owner 2026-11-05): a random item's total bonus strength (utility,
    /// the same measure the old cap used) is chosen first from its level and quality, then split across the bonus
    /// lines it rolled, so quality matters and nothing loops. The old roll-then-trim stays while Enabled is false.
    /// </summary>
    public sealed record RandomItemBudgetRules
    {
        public bool Enabled { get; init; } = false;

        /// <summary>
        /// The level budget is level * PerLevel - LevelOffset. The defaults were fitted so a quality 96 item matches the old
        /// roll's average utility (measured 2026-11-06: about 4.5, 10.5, 21 and 35 at levels 10, 20, 35 and 51).
        /// </summary>
        public double PerLevel { get; init; } = 0.88;

        public double LevelOffset { get; init; } = 6;

        /// <summary>No item aims below this.</summary>
        public int Floor { get; init; } = 5;

        /// <summary>No item aims above this (the old cap's ceiling was 80).</summary>
        public int Max { get; init; } = 80;

        /// <summary>Quality at or below LowQuality gets LowQualityFactor of the level budget, at or above HighQuality
        /// gets HighQualityFactor, in a straight line between. Random items roll 95 to 99 quality.</summary>
        public int LowQuality { get; init; } = 95;
        public double LowQualityFactor { get; init; } = 0.85;
        public int HighQuality { get; init; } = 99;
        public double HighQualityFactor { get; init; } = 1.05;

        /// <summary>An item with a proc aims this much lower.</summary>
        public double ProcFactor { get; init; } = 0.7;

        /// <summary>One bonus line may reach this many times the most the old roll could give that line.</summary>
        public double LineCapFactor { get; init; } = 1.5;

        /// <summary>The total utility an item of this level and quality aims at.</summary>
        public double Target(int level, int quality, bool hasProc, double utilityMinimum)
        {
            double ceiling = Math.Min(Max, Math.Max(Floor, level * PerLevel - LevelOffset));
            double share = HighQuality <= LowQuality ? HighQualityFactor
                : LowQualityFactor + (HighQualityFactor - LowQualityFactor) * Math.Clamp((quality - LowQuality) / (double)(HighQuality - LowQuality), 0, 1);
            double target = ceiling * share;
            // a promised floor (quest rewards, frontier kills) still holds, up to the best this level can reach
            target = Math.Max(target, Math.Min(utilityMinimum, ceiling * Math.Max(LowQualityFactor, HighQualityFactor)));
            if (hasProc)
                target *= ProcFactor;
            return Math.Min(target, Max);
        }

        internal void Validate()
        {
            LootRules.Range(PerLevel, 0.1, 5, "RandomItems.Budget.PerLevel");
            LootRules.Range(LevelOffset, -100, 100, "RandomItems.Budget.LevelOffset");
            LootRules.Range(Floor, 0, 200, "RandomItems.Budget.Floor");
            LootRules.Range(Max, 1, 200, "RandomItems.Budget.Max");
            if (Floor > Max)
                throw new InvalidDataException("RandomItems.Budget.Floor must not be above Max.");
            LootRules.Range(LowQuality, 0, 100, "RandomItems.Budget.LowQuality");
            LootRules.Range(HighQuality, 0, 100, "RandomItems.Budget.HighQuality");
            LootRules.Range(LowQualityFactor, 0, 3, "RandomItems.Budget.LowQualityFactor");
            LootRules.Range(HighQualityFactor, 0, 3, "RandomItems.Budget.HighQualityFactor");
            LootRules.Range(ProcFactor, 0, 3, "RandomItems.Budget.ProcFactor");
            LootRules.Range(LineCapFactor, 0.5, 10, "RandomItems.Budget.LineCapFactor");
        }
    }

    /// <summary>Coin bags: LootGeneratorMoney. These multiply on top of the money_drop server property.</summary>
    public sealed record CoinLootRules
    {
        public double Multiplier { get; init; } = 1.0;

        public double NamedMultiplier { get; init; } = 1.0;

        internal void Validate()
        {
            LootRules.Range(Multiplier, 0, 100, "Coin.Multiplier");
            LootRules.Range(NamedMultiplier, 0, 100, "Coin.NamedMultiplier");
        }
    }

    /// <summary>
    /// The loot rules in use. The file is re-read when it changes (checked at most every 10 seconds), so an edit
    /// takes effect without a restart. A file that does not parse is reported and the last good rules stay in use.
    /// </summary>
    public static class LootRulesFile
    {
        private static readonly Logging.Logger log = Logging.LoggerManager.Create(typeof(LootRulesFile));
        private static readonly object _lock = new();
        private static LootRules _current = LootRules.Defaults;
        private static DateTime _loadedWriteTime = DateTime.MinValue;
        private static long _nextCheck;

        /// <summary>Tests set this to bypass the file; null reads the file.</summary>
        public static LootRules TestOverride { get; set; }

        /// <summary>The problem with the file the last time it was read, or null.</summary>
        public static string LastError { get; private set; }

        public static string FilePath
        {
            get
            {
                string root = GameServer.Instance?.Configuration?.RootDirectory;
                return string.IsNullOrEmpty(root) ? null : Path.Combine(root, LootRules.FileName);
            }
        }

        public static LootRules Current
        {
            get
            {
                if (TestOverride != null)
                    return TestOverride;

                long now = Environment.TickCount64;
                if (now >= _nextCheck)
                {
                    _nextCheck = now + 10_000;
                    Refresh(false);
                }

                return _current;
            }
        }

        /// <summary>Reads the file if it changed (or always, with force). Returns the rules now in use.</summary>
        public static LootRules Refresh(bool force)
        {
            lock (_lock)
            {
                string path = FilePath;
                try
                {
                    if (path == null || !File.Exists(path))
                    {
                        if (_loadedWriteTime != DateTime.MinValue || force)
                        {
                            _current = LootRules.Defaults;
                            _loadedWriteTime = DateTime.MinValue;
                            LastError = null;
                        }
                        return _current;
                    }

                    DateTime written = File.GetLastWriteTimeUtc(path);
                    if (!force && written == _loadedWriteTime)
                        return _current;

                    _loadedWriteTime = written;
                    _current = LootRules.Parse(File.ReadAllText(path));
                    LastError = null;
                    if (log.IsInfoEnabled)
                        log.Info("Loot rules loaded from " + path);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
                {
                    LastError = e.Message;
                    if (log.IsErrorEnabled)
                        log.Error($"Loot rules in {path} were not used, the last good rules stay: {e.Message}");
                }

                return _current;
            }
        }
    }
}
