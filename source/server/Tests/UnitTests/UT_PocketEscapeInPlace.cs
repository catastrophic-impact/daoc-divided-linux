using System.Numerics;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Night 2026-11-04: 289 pocket escapes from a capital spawn to the same capital spawn, one every 33 s.</summary>
[TestFixture]
public sealed class UT_PocketEscapeInPlace
{
    [Test]
    public void AnEscapeToWhereTheBotStandsIsInPlace()
    {
        var tirNaNog = new Vector3(33197, 31200, 8002);
        Assert.That(AutonomousRouteRecoveryPolicy.IsEscapeInPlace(201, tirNaNog, 201, tirNaNog), Is.True);
        Assert.That(AutonomousRouteRecoveryPolicy.IsEscapeInPlace(201, tirNaNog, 201, tirNaNog + new Vector3(250, 0, 0)), Is.True);
        Assert.That(AutonomousRouteRecoveryPolicy.IsEscapeInPlace(201, tirNaNog, 201, tirNaNog + new Vector3(900, 0, 0)), Is.False);
        Assert.That(AutonomousRouteRecoveryPolicy.IsEscapeInPlace(249, tirNaNog, 201, tirNaNog), Is.False);
    }

    [Test]
    public void BetweenTaskCampsAreTold()
    {
        Assert.That(AutonomousRouteRecoveryPolicy.IsBetweenTaskCamp("service-Vendor-12345"), Is.True);
        Assert.That(AutonomousRouteRecoveryPolicy.IsBetweenTaskCamp("service-RealmExchange-199"), Is.True);
        Assert.That(AutonomousRouteRecoveryPolicy.IsBetweenTaskCamp("rvr-stage-Midgard"), Is.True);
        Assert.That(AutonomousRouteRecoveryPolicy.IsBetweenTaskCamp("town-idle"), Is.True);
        Assert.That(AutonomousRouteRecoveryPolicy.IsBetweenTaskCamp("craft-4411"), Is.True);
        Assert.That(AutonomousRouteRecoveryPolicy.IsBetweenTaskCamp("capnbry:153:7:7:hrimthursa shaman"), Is.False);
        Assert.That(AutonomousRouteRecoveryPolicy.IsBetweenTaskCamp("quest:20134"), Is.False);
        Assert.That(AutonomousRouteRecoveryPolicy.IsBetweenTaskCamp(null), Is.False);
    }
}
