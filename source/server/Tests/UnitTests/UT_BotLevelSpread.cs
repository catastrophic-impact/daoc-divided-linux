using System;
using System.IO;
using System.Linq;
using DOL.GS;
using NUnit.Framework;
using OfflineDaoc.Configuration;

/// <summary>Level spread (owner request 2026-11-08): some gamebots stop gaining experience below 50.</summary>
[TestFixture]
public class UT_BotLevelSpread
{
    [Test]
    public void OffByDefaultAndEveryBotReachesFifty()
    {
        Assert.That(BotGoalSettings.Defaults.LevelSpread.Enabled, Is.False);
        Assert.That(Enumerable.Range(1, 2_000).All(id => BotGoalSettings.Defaults.LevelSpread.StopLevelFor(id) == 50), Is.True);
    }

    [Test]
    public void StopLevelsFollowTheSharesAndStayInsideTheirRanges()
    {
        var spread = BotLevelSpread.Defaults with { Enabled = true };
        int[] stops = Enumerable.Range(1, 20_000).Select(id => spread.StopLevelFor(id)).ToArray();
        Assert.That(stops.All(stop => stop is >= 1 and <= 50), Is.True);
        double Share(int low, int high) => stops.Count(stop => stop >= low && stop <= high) * 100.0 / stops.Length;
        Assert.That(Share(1, 9), Is.EqualTo(10).Within(1.5));
        Assert.That(Share(10, 19), Is.EqualTo(10).Within(1.5));
        Assert.That(Share(20, 34), Is.EqualTo(15).Within(1.5));
        Assert.That(Share(35, 49), Is.EqualTo(15).Within(1.5));
        Assert.That(Share(50, 50), Is.EqualTo(50).Within(1.5));
        // Every level of a range is used, not only its ends.
        Assert.That(Enumerable.Range(1, 50).All(level => stops.Contains(level)), Is.True);
    }

    [Test]
    public void AStopLevelNeverChangesForTheSameBot()
    {
        var spread = BotLevelSpread.Defaults with { Enabled = true };
        var again = new BotLevelSpread(true, 10, 10, 15, 15, 50);
        Assert.That(Enumerable.Range(1, 500).All(id => spread.StopLevelFor(id) == again.StopLevelFor(id)), Is.True);
    }

    [Test]
    public void AZeroShareRangeGetsNoBots()
    {
        var spread = new BotLevelSpread(true, 0, 0, 100, 0, 0);
        Assert.That(Enumerable.Range(1, 2_000).All(id => spread.StopLevelFor(id) is >= 20 and <= 34), Is.True);
    }

    [TestCase(10, 10, 15, 15, 45)]
    [TestCase(-5, 15, 35, 15, 40)]
    [TestCase(101, 0, 0, 0, 0)]
    public void SharesMustTotalOneHundred(int lowest, int low, int middle, int high, int fifty)
    {
        var settings = BotGoalSettings.Defaults with { LevelSpread = new BotLevelSpread(true, lowest, low, middle, high, fifty) };
        Assert.Throws<InvalidDataException>(settings.Validate);
    }

    [Test]
    public void SavesAndLoadsAndOldFilesReadAsOff()
    {
        string folder = Path.Combine(Path.GetTempPath(), "level-spread-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, BotGoalSettings.FileName);
        try
        {
            var settings = BotGoalSettings.Defaults with { LevelSpread = new BotLevelSpread(true, 20, 20, 20, 20, 20) };
            settings.Save(path, () => true);
            Assert.That(BotGoalSettings.Load(path), Is.EqualTo(settings));

            // A bot-goals.json saved before the level spread existed.
            File.WriteAllText(path, """
                {
                  "Version": 1,
                  "Levels1To19": { "SoloPve": 60, "GroupPve": 40, "RvR": 0, "Battlegrounds": 0 },
                  "Levels20To49": { "SoloPve": 40, "GroupPve": 40, "RvR": 20, "Battlegrounds": 0 },
                  "Level50": { "SoloPve": 20, "GroupPve": 40, "RvR": 40, "Battlegrounds": 0 }
                }
                """);
            Assert.That(BotGoalSettings.Load(path).LevelSpread, Is.EqualTo(BotLevelSpread.Defaults));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    // The launcher's "+ Lv.25" and "+ Lv.50" bots (MainForm.BotStartExperience) start at these floors.
    [TestCase(25, 146_999_950L)]
    [TestCase(50, 169_999_999_950L)]
    public void LauncherStartExperienceIsTheLevelFloor(int level, long launcherExperience)
    {
        Assert.That(GamePlayer.GetExperienceAmountForLevel(level - 1), Is.EqualTo(launcherExperience));
        Assert.That(GamePlayer.GetExperienceAmountForLevel(level), Is.GreaterThan(launcherExperience));
    }
}
