using System;
using System.Linq;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.GS.ServerProperties;

namespace DOL.GS;

/// <summary>What the realm's gamebots have learned about one monster, alone or grouped.</summary>
[DataTable(TableName = "offline_bot_mob_lessons")]
public sealed class OfflineMobLessonRecord : DataObject, ILivingServerRow
{
    public DataObject CopyForSave() => (OfflineMobLessonRecord)MemberwiseClone();

    [DataElement(AllowDbNull = false, Unique = true, Varchar = 128)] public string Lesson { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int Wins { get; set; }
    [DataElement(AllowDbNull = false)] public int Deaths { get; set; }
    [DataElement(AllowDbNull = false)] public string UpdatedUtc { get; set; } = string.Empty;
}

/// <summary>
/// Gamebots learn which monsters they can really beat. The route rules go by con colour, which says nothing about a
/// monster that hits far above or below its level. Every kill and every death at the hands of a monster is counted by name
/// and by alone or grouped, and once there is enough to go on, a monster bots keep beating feels one colour easier and one
/// that keeps killing them one colour harder. Owner 2026-10-31: "learning from what they can and cant currently do".
/// </summary>
public static class AutonomousMobLessons
{
    [ServerProperty("autonomous", "bot_mob_lessons",
        "Gamebots remember which monsters they beat or died to, and let that move how strong a monster feels on their routes.", true)]
    public static bool Enabled = true;

    /// <summary>Fewer fights than this teach nothing: one unlucky death is not a lesson.</summary>
    public const int MinimumFights = 12;
    public const double EasyDeathShare = 0.10;
    public const double DeadlyDeathShare = 0.40;

    internal static readonly LivingServerTable<string, OfflineMobLessonRecord> Lessons =
        new("mob lesson", row => row.Lesson, StringComparer.OrdinalIgnoreCase);

    public static string Key(string monster, bool grouped) => $"{monster}|{(grouped ? "group" : "solo")}";

    /// <summary>Enough fights, and too many of them lost. Pure.</summary>
    public static bool IsDeadly(int wins, int deaths)
    {
        int fights = wins + deaths;
        return fights >= MinimumFights && deaths / (double)fights >= DeadlyDeathShare;
    }

    /// <summary>
    /// For a purple the colour cannot say so: it is the top of the scale, so <see cref="Felt(ConColor, int, int, bool)"/> leaves it
    /// where it is, and without this a party would go on pulling one that keeps killing parties.
    /// </summary>
    public static bool IsDeadly(string monster, bool grouped) =>
        Enabled && Lessons.TryGet(Key(monster, grouped), out OfflineMobLessonRecord row) && IsDeadly(row.Wins, row.Deaths);

    /// <summary>
    /// How strong a monster feels after what bots have learned. Pure. A lone bot never learns that a purple is less than a
    /// red, however often others have managed it, and learning never makes a monster feel easier than yellow.
    /// </summary>
    public static ConColor Felt(ConColor con, int wins, int deaths, bool grouped)
    {
        int fights = wins + deaths;
        if (fights < MinimumFights)
            return con;
        if (IsDeadly(wins, deaths))
            return con >= ConColor.PURPLE ? con : con + 1;
        if (deaths / (double)fights > EasyDeathShare || con <= ConColor.YELLOW)
            return con;
        return !grouped && con == ConColor.PURPLE ? ConColor.RED : con - 1;
    }

    public static ConColor Felt(ConColor con, string monster, bool grouped) =>
        Enabled && Lessons.TryGet(Key(monster, grouped), out OfflineMobLessonRecord row)
            ? Felt(con, row.Wins, row.Deaths, grouped) : con;

    /// <summary>A kill or a death at level: a grey monster proves nothing either way.</summary>
    public static void OnDeath(GameLiving dead, GameObject killer)
    {
        if (!Enabled)
            return;
        if (dead is GameBot { IsAutonomousWorldBot: true } fallen)
        {
            // Only PvE monsters teach: an enemy realm's guard is no lesson about a camp.
            if (killer is GameNPC monster and not GameBot && monster.Realm == eRealm.None && monster.Brain is not IControlledBrain &&
                Teaches(fallen, monster))
                Count(monster.Name, fallen.Group?.MemberCount > 1, win: false);
            return;
        }
        if (dead is not GameNPC victim || victim.Brain is IControlledBrain || Owner(killer) is not { } bot || !Teaches(bot, victim))
            return;
        Count(victim.Name, bot.Group?.MemberCount > 1, win: true);
    }

    private static GameBot Owner(GameObject killer) => killer switch
    {
        GameBot { IsAutonomousWorldBot: true } bot => bot,
        GameNPC { Brain: IControlledBrain pet } => pet.GetLivingOwner() as GameBot is { IsAutonomousWorldBot: true } owner ? owner : null,
        _ => null,
    };

    private static bool Teaches(GameBot bot, GameNPC monster) => ConLevels.GetConColor(bot.GetConLevel(monster)) >= ConColor.BLUE;

    private static void Count(string monster, bool grouped, bool win)
    {
        string key = Key(monster, grouped);
        Lessons.Change(key, () => new OfflineMobLessonRecord { Lesson = key }, row =>
        {
            if (win)
                row.Wins++;
            else
                row.Deaths++;
            row.UpdatedUtc = DateTime.UtcNow.ToString("O");
        });
    }
}
