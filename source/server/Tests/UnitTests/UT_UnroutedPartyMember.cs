using NUnit.Framework;
using static DOL.GS.AutonomousRouteRecoveryPolicy;

namespace DOL.GS.Tests;

/// <summary>Night 2026-11-04: 431 of 1,232 group outcomes had members in different regions with no legal route.</summary>
[TestFixture]
public sealed class UT_UnroutedPartyMember
{
    [Test]
    public void AMemberWaitsAMinuteThenGoesHomeThenLeaves()
    {
        Assert.That(UnroutedMemberStep(30_000, recalled: false), Is.EqualTo(UnroutedMemberAction.Wait));
        Assert.That(UnroutedMemberStep(60_000, recalled: false), Is.EqualTo(UnroutedMemberAction.Recall));
        Assert.That(UnroutedMemberStep(30_000, recalled: true), Is.EqualTo(UnroutedMemberAction.Wait));
        Assert.That(UnroutedMemberStep(61_000, recalled: true), Is.EqualTo(UnroutedMemberAction.Leave));
    }
}
