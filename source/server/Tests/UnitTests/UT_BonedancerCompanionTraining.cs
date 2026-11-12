using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// DR-243 (Discord 2026-11-10): an account-companion Bonedancer trained Darkness whatever line the owner picked.
/// The legacy Bonedancer plan migration saw Suppression above 1 as a mixed spread and re-spent every point into
/// the bot plan's primary. Owner-trained companions must never enter that migration.
/// </summary>
[TestFixture]
public sealed class UT_BonedancerCompanionTraining
{
    [Test]
    public void AccountCompanionNeverEntersThePlanMigration()
    {
        Assert.That(GameBot.BonedancerPlanMigrationApplies(true, false, null), Is.False);
        Assert.That(GameBot.BonedancerPlanMigrationApplies(true, true, null), Is.False);
    }

    [Test]
    public void SpawnHelperAndUnplannedWorldBotStillMigrate()
    {
        Assert.That(GameBot.BonedancerPlanMigrationApplies(false, false, null), Is.True);
        Assert.That(GameBot.BonedancerPlanMigrationApplies(false, true, ""), Is.True);
    }

    [Test]
    public void WorldBotWithSavedBuildDoesNotMigrate()
    {
        Assert.That(GameBot.BonedancerPlanMigrationApplies(false, true, "plan"), Is.False);
    }
}
