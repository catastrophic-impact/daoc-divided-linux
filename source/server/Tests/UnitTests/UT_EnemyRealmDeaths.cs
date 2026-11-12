using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Night playtest 2026-11-04: a kill by an enemy realm's bot was learned as a failed PvE camp (new_max_con=GREEN).</summary>
[TestFixture]
public sealed class UT_EnemyRealmDeaths
{
    [Test]
    public void AKillByAnotherRealmIsAnEnemyRealmKill()
    {
        // Gormildsten, Savage 50 (Midgard), killed by the Hibernian bot Linaorine.
        Assert.That(AutonomousDeathDiagnostics.IsEnemyRealmKill(eRealm.Midgard, eRealm.Hibernia), Is.True);
        Assert.That(AutonomousDeathDiagnostics.IsEnemyRealmKill(eRealm.Albion, eRealm.Midgard), Is.True);
    }

    [Test]
    public void AMonsterKillIsNot()
    {
        Assert.That(AutonomousDeathDiagnostics.IsEnemyRealmKill(eRealm.Midgard, eRealm.None), Is.False);
        Assert.That(AutonomousDeathDiagnostics.IsEnemyRealmKill(eRealm.Midgard, (GameObject)null), Is.False);
    }

    [Test]
    public void AnOwnRealmKillIsNot()
    {
        Assert.That(AutonomousDeathDiagnostics.IsEnemyRealmKill(eRealm.Hibernia, eRealm.Hibernia), Is.False);
    }
}
