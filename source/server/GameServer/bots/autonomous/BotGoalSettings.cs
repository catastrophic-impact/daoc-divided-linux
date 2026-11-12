using System;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OfflineDaoc.Configuration;

// Linked into the launcher: validation, defaults and disk format have one owner.
// Battlegrounds (added 2026-10-07) defaults to 0, so settings files saved before it stay valid.
public sealed record BotGoalWeights(int SoloPve, int GroupPve, int RvR, int Battlegrounds = 0)
{
    public int Total => SoloPve + GroupPve + RvR + Battlegrounds;
    public bool Allows(int kind) => kind switch { 0 => SoloPve > 0, 1 => GroupPve > 0, 2 => RvR > 0, 3 => Battlegrounds > 0, _ => false };

    // excludeBattlegrounds: the level has no battleground bracket (1-14, 36-50).
    public int Choose(double roll, bool excludeGroup = false, bool excludeBattlegrounds = false)
    {
        if (!double.IsFinite(roll) || roll < 0 || roll >= 1) throw new ArgumentOutOfRangeException(nameof(roll));
        int group = excludeGroup ? 0 : GroupPve;
        int battlegrounds = excludeBattlegrounds ? 0 : Battlegrounds;
        int total = SoloPve + group + RvR + battlegrounds;
        // A battleground-only row grinds solo while no bracket fits; group-only populations must wait for a
        // legal roster, not silently solo.
        if (total == 0) return Battlegrounds > 0 && excludeBattlegrounds ? 0 : 1;
        double value = roll * total;
        return value < SoloPve ? 0 : value < SoloPve + group ? 1 : value < SoloPve + group + RvR ? 2 : 3;
    }
}

/// <summary>
/// Where autonomous gamebots stop gaining experience (owner request 2026-11-08), so every level range keeps bots.
/// Each bot draws its stop level from its own id, so it never changes between starts. The five shares are percentages
/// of the roster and must total 100. A bot already past its stop level keeps its level; it only stops gaining.
/// </summary>
public sealed record BotLevelSpread(bool Enabled, int Stop1To9, int Stop10To19, int Stop20To34, int Stop35To49, int Reach50)
{
    public static BotLevelSpread Defaults => new(false, 10, 10, 15, 15, 50);
    public int Total => Stop1To9 + Stop10To19 + Stop20To34 + Stop35To49 + Reach50;
    public static readonly (int Low, int High)[] Ranges = [(1, 9), (10, 19), (20, 34), (35, 49), (50, 50)];

    /// <summary>The level this bot stops at: 50 when the spread is off or the bot draws the level 50 share.</summary>
    public int StopLevelFor(long botId)
    {
        if (!Enabled || Total != 100) return 50;
        ulong draw = Mix((ulong)botId);
        int roll = (int)(draw % 100);
        int[] shares = [Stop1To9, Stop10To19, Stop20To34, Stop35To49, Reach50];
        int range = 0, cumulative = shares[0];
        while (range < shares.Length - 1 && roll >= cumulative) cumulative += shares[++range];
        (int low, int high) = Ranges[range];
        return low + (int)(Mix(draw) % (ulong)(high - low + 1));
    }

    // SplitMix64: a stable spread of consecutive roster ids.
    private static ulong Mix(ulong value)
    {
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    public void Validate()
    {
        int[] shares = [Stop1To9, Stop10To19, Stop20To34, Stop35To49, Reach50];
        if (shares.Any(share => share < 0 || share > 100) || Total != 100)
            throw new InvalidDataException("Level spread: percentages must be 0–100 and add up to exactly 100%.");
    }
}

public sealed record BotGoalSettings
{
    public const string FileName = "bot-goals.json";
    public int Version { get; init; } = 1;
    public required BotGoalWeights Levels1To19 { get; init; }
    public required BotGoalWeights Levels20To49 { get; init; }
    public required BotGoalWeights Level50 { get; init; }
    // Added 2026-11-08; settings files saved before it read as off.
    public BotLevelSpread LevelSpread { get; init; } = BotLevelSpread.Defaults;
    public static BotGoalSettings Defaults => new()
    {
        Levels1To19 = new(60, 40, 0), Levels20To49 = new(40, 40, 20), Level50 = new(20, 40, 40),
    };
    public BotGoalWeights ForLevel(int level) => level >= 50 ? Level50 : level >= 20 ? Levels20To49 : Levels1To19;

    public void Validate()
    {
        if (Version != 1) throw new InvalidDataException("Unsupported Bot Goals Setting version.");
        ValidateRow(Levels1To19, "Levels 1–19");
        ValidateRow(Levels20To49, "Levels 20–49");
        ValidateRow(Level50, "Level 50");
        if (Levels1To19.RvR != 0) throw new InvalidDataException("Levels 1–19 cannot have RvR goals.");
        if (Level50.Battlegrounds != 0) throw new InvalidDataException("Level 50 cannot have battleground goals (the battlegrounds are for levels 15–35).");
        (LevelSpread ?? throw new InvalidDataException("Level spread is missing.")).Validate();
    }

    private static void ValidateRow(BotGoalWeights row, string name)
    {
        if (row == null || row.SoloPve < 0 || row.GroupPve < 0 || row.RvR < 0 || row.Battlegrounds < 0 ||
            row.SoloPve > 100 || row.GroupPve > 100 || row.RvR > 100 || row.Battlegrounds > 100 || row.Total != 100)
            throw new InvalidDataException(name + ": percentages must be 0–100 and add up to exactly 100%.");
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        IgnoreReadOnlyProperties = true,
    };

    public static BotGoalSettings Load(string path)
    {
        if (!File.Exists(path)) return Defaults;
        var settings = JsonSerializer.Deserialize<BotGoalSettings>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("Bot Goals Setting file is empty.");
        settings.Validate();
        return settings;
    }

    public void Save(string path, Func<bool> serverStopped)
    {
        Validate();
        if (!serverStopped()) throw new InvalidOperationException("Stop the server before saving bot goals.");
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, this, Json);
                file.Flush(true);
            }
            if (!serverStopped()) throw new InvalidOperationException("The server started; bot goals were not saved.");
            File.Move(temporary, fullPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
