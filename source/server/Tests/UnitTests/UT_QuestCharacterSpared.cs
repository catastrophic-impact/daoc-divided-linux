using DOL.Database;
using DOL.GS.Quests;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Night 2026-11-04: bots killed Keeper Rasa 398 times and Bjame 327 times, the characters their quest steps talk to.</summary>
[TestFixture]
public sealed class UT_QuestCharacterSpared
{
    private static string Step(DataQuest.eStepType type) => ((byte)type).ToString();

    [Test]
    public void GiversAndTalkTargetsAreSpared()
    {
        var quest = new DbDataQuest
        {
            StartName = "Keeper Rasa",
            StepType = $"{Step(DataQuest.eStepType.Kill)}|{Step(DataQuest.eStepType.Interact)}|{Step(DataQuest.eStepType.DeliverFinish)}",
            TargetName = "bwca;200|Bjame;100|Lady Eilwen;51",
        };
        var names = QuestTalkTruce.BotSparedNames([quest]);
        Assert.That(names.Contains("keeper rasa"), Is.True);
        Assert.That(names, Does.Contain("Bjame"));
        Assert.That(names, Does.Contain("Lady Eilwen"));
        Assert.That(names, Does.Not.Contain("bwca"));
    }

    [Test]
    public void ACharacterAnotherQuestKillsIsNotSpared()
    {
        var talk = new DbDataQuest { StartName = "Tiomoid", StepType = Step(DataQuest.eStepType.Whisper), TargetName = "Shiake" };
        var kill = new DbDataQuest { StartName = "Elder", StepType = Step(DataQuest.eStepType.KillFinish), TargetName = "Shiake;200" };
        var names = QuestTalkTruce.BotSparedNames([talk, kill]);
        Assert.That(names, Does.Contain("Tiomoid"));
        Assert.That(names, Does.Not.Contain("Shiake"));
    }

    [Test]
    public void ASparedCharacterIsNotAMonsterToBots()
    {
        try
        {
            QuestTalkTruce.SetBotSparedNamesForTests([new DbDataQuest { StartName = "Keeper Rasa", StepType = "", TargetName = "" }]);
            Assert.That(QuestTalkTruce.SparesBots("Keeper Rasa"), Is.True);
            Assert.That(QuestTalkTruce.SparesBots("black wraith"), Is.False);
            Assert.That(QuestTalkTruce.SparesBots((GameNPC)null), Is.False);
        }
        finally
        {
            QuestTalkTruce.SetBotSparedNamesForTests(null);
        }
    }
}
