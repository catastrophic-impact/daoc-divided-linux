using System;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Night 2026-11-04: 25 of 64 Storm Sovereigns kept a saved Sword/Spear plan after the Hammer/Axe remake.</summary>
[TestFixture]
public sealed class UT_SavedBuildFitsClass
{
    private static readonly string[] StormLines = ["Hammer", "Axe", "Shields", "Parry", "Stealth"];
    private static bool Storm(string line) => StormLines.Contains(line, StringComparer.OrdinalIgnoreCase);

    // Bjornungrim's saved plan from the night (trimmed): Spear, from before the remake.
    private const string OldSpearPlan =
        "{\"WeaponOneType\":11,\"WeaponTwoType\":14,\"DamageType\":0,\"SpecType\":58,\"Is2H\":true," +
        "\"SpecLines\":[{\"Spec\":\"Spear\",\"SpecCap\":50,\"levelRatio\":1},{\"Spec\":\"Parry\",\"SpecCap\":50,\"levelRatio\":0.6}]}";

    [Test]
    public void APlanForALineTheClassLostDoesNotFit()
    {
        Assert.That(BotLifetimeBuild.FitsClass(OldSpearPlan, Storm, ["Hammer", "Parry", "Shields"]), Is.False);
    }

    [Test]
    public void ACurrentPlanFits()
    {
        var plan = new BotSpec { SpecLines = [new BotSpecLine("Hammer", 50, 1f), new BotSpecLine("Shields", 50, 0.9f)] };
        Assert.That(BotLifetimeBuild.FitsClass(BotLifetimeBuild.Encode(plan), Storm, []), Is.True);
    }

    [Test]
    public void ALineTheFreshPlanAlsoTrainsIsNeverRejected()
    {
        // Otherwise a class whose own plan names a line it lacks would be respecced on every login.
        Assert.That(BotLifetimeBuild.FitsClass(OldSpearPlan, Storm, ["Spear"]), Is.True);
    }

    [Test]
    public void EmptyOrUnreadablePlansAreLeftToRestore()
    {
        Assert.That(BotLifetimeBuild.FitsClass(null, Storm, []), Is.True);
        Assert.That(BotLifetimeBuild.FitsClass("not json", Storm, []), Is.True);
    }
}
