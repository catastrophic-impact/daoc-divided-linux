using System;
using System.Numerics;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Night 2026-11-04: 753 seam cycles at region 200 384960/385088,385504 (zones 211/212).</summary>
[TestFixture]
public sealed class UT_SeamCycleQuarantine
{
    [SetUp, TearDown]
    public void Reset() => AutonomousSeamCrossingQuarantine.ResetForTests();

    [Test]
    public void ThreeBotsCyclingOnTheSeamBenchBothItsCrossings()
    {
        var west = new Vector3(384960, 385504, 6699);
        var east = new Vector3(385088, 385504, 6713);
        DateTime now = new(2026, 11, 5, 0, 10, 0, DateTimeKind.Utc);
        foreach (long bot in new long[] { 35633, 35069, 35100 })
        {
            AutonomousSeamCrossingQuarantine.ReportFailure(200, west, bot, now);
            AutonomousSeamCrossingQuarantine.ReportFailure(200, east, bot, now);
        }
        Assert.That(AutonomousSeamCrossingQuarantine.IsQuarantined(200, west, now), Is.True);
        Assert.That(AutonomousSeamCrossingQuarantine.IsQuarantined(200, east, now), Is.True);
        Assert.That(AutonomousSeamCrossingQuarantine.IsQuarantined(200, west, now.AddHours(3)), Is.False, "benched for two hours");
    }
}
