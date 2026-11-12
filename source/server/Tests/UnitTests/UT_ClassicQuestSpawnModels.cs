using System.IO;
using System.Linq;
using System.Text.Json;
using DOL.GS.Quests;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// Owner 2026-11-10 (player report): Anklebiter came with model 0, drawn as nothing, and attacked unseen. Every quest
/// monster in classic-quests.json needs a body (build_questmodels165.py), and the server never spawns one without.
/// </summary>
[TestFixture]
public class UT_ClassicQuestSpawnModels
{
    [Test]
    public void ModelZeroIsNoBody()
    {
        Assert.That(ClassicQuests.IsBodied(0), Is.False);
        Assert.That(ClassicQuests.IsBodied(249), Is.True);
    }

    [Test]
    public void EveryQuestSpawnInTheShippedConfigHasAModelOrATemplate()
    {
        string path = FindConfig();
        if (path == null)
            Assert.Ignore("runtime/server/classic-quests.json not found from the test folder");
        var config = JsonSerializer.Deserialize<ClassicQuests.Config>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var bodiless = config.Quests
            .SelectMany(quest => quest.Value.Steps.Where(step => step?.Spawn != null)
                .Select(step => (Quest: quest.Key, step.Spawn)))
            .Where(entry => entry.Spawn.Model == 0 && entry.Spawn.TemplateId <= 0)
            .Select(entry => $"{entry.Quest} {entry.Spawn.Name}")
            .ToArray();
        Assert.That(bodiless, Is.Empty);
    }

    private static string FindConfig()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        for (int depth = 0; directory != null && depth < 10; depth++, directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "runtime", "server", "classic-quests.json");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
