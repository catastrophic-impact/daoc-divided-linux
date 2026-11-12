using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Night 2026-11-04: route threat scanning was off for RvR bots, and 40-49s on frontier tours walked into purples.</summary>
[TestFixture]
public sealed class UT_RvrRouteThreatScan
{
    [Test]
    public void AnRvrBotsKeepMarchIsScanned()
    {
        Assert.That(AutonomousRouteThreatPolicy.ScansFor(rvrObjective: true, rvrTravel: true), Is.True);
        Assert.That(AutonomousRouteThreatPolicy.ScansFor(rvrObjective: false, rvrTravel: false), Is.True);
        // Its other walks (a rally, a siege post) keep their own rules.
        Assert.That(AutonomousRouteThreatPolicy.ScansFor(rvrObjective: true, rvrTravel: false), Is.False);
    }
}
