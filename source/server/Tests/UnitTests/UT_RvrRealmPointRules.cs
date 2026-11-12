using System.Reflection;
using DOL.GS.ServerProperties;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Realm point rules from the 2026-11-08 game-rules audit (DR-176, 177, 178, 179, 181, 182).</summary>
[TestFixture]
public class UT_RvrRealmPointRules
{
    private int _mez, _stun, _repair, _repairCap, _keepShare, _towerShare;

    [SetUp]
    public void SetUp()
    {
        _mez = Properties.MEZ_RP_POOL_PERCENT;
        _stun = Properties.STUN_RP_POOL_PERCENT;
        _repair = Properties.REPAIR_RP_PERCENT;
        _repairCap = Properties.REPAIR_RP_CAP;
        _keepShare = Properties.SIEGE_CONTRIBUTION_KEEP_PERCENT;
        _towerShare = Properties.SIEGE_CONTRIBUTION_TOWER_PERCENT;
    }

    [TearDown]
    public void TearDown()
    {
        Properties.MEZ_RP_POOL_PERCENT = _mez;
        Properties.STUN_RP_POOL_PERCENT = _stun;
        Properties.REPAIR_RP_PERCENT = _repair;
        Properties.REPAIR_RP_CAP = _repairCap;
        Properties.SIEGE_CONTRIBUTION_KEEP_PERCENT = _keepShare;
        Properties.SIEGE_CONTRIBUTION_TOWER_PERCENT = _towerShare;
    }

    private static T DefaultOf<T>(string field) =>
        (T)System.Convert.ChangeType(
            typeof(Properties).GetField(field)!.GetCustomAttribute<ServerPropertyAttribute>()!.DefaultValue, typeof(T));

    [Test]
    public void DefaultsFollowThePatchNotes()
    {
        Assert.That(DefaultOf<int>(nameof(Properties.HEAL_RP_PERCENT)), Is.EqualTo(5), "1.81: about 5% of hit points healed");
        Assert.That(DefaultOf<int>(nameof(Properties.MEZ_RP_POOL_PERCENT)), Is.EqualTo(5), "1.81: mez 5%");
        Assert.That(DefaultOf<int>(nameof(Properties.STUN_RP_POOL_PERCENT)), Is.EqualTo(10), "1.81: stun 10%");
        Assert.That(DefaultOf<int>(nameof(Properties.REPAIR_RP_PERCENT)), Is.EqualTo(10), "1.81: about 10% of the amount repaired");
        Assert.That(DefaultOf<bool>(nameof(Properties.UNDERPOPULATED_RP_BONUS)), Is.True, "1.122b");
        Assert.That(DefaultOf<int>(nameof(Properties.SIEGE_CONTRIBUTION_KEEP_PERCENT)), Is.EqualTo(50), "1.121: keep 50%");
        Assert.That(DefaultOf<int>(nameof(Properties.SIEGE_CONTRIBUTION_TOWER_PERCENT)), Is.EqualTo(20), "1.121: tower 20%");
        Assert.That(DefaultOf<int>(nameof(Properties.KEEP_RP_BASE)), Is.EqualTo(4500), "DR-179");
        Assert.That(DefaultOf<int>(nameof(Properties.TOWER_RP_BASE)), Is.EqualTo(500), "DR-179");
    }

    [TestCase(300, 5, 15, 0)]
    [TestCase(19, 5, 0, 19)]     // not a whole point yet: all of it waits for the next heal
    [TestCase(59, 5, 2, 19)]
    [TestCase(300, 0, 0, 300)]   // switched off
    [TestCase(0, 5, 0, 0)]
    public void HealsPayFivePercentAndKeepTheRest(int restored, int percent, int points, int carry)
    {
        Assert.That(RvrRealmPointRules.HealRealmPoints(restored, percent, out int left), Is.EqualTo(points));
        Assert.That(left, Is.EqualTo(carry));
    }

    [Test]
    public void AStunCountsTwiceAMez()
    {
        Properties.MEZ_RP_POOL_PERCENT = 5;
        Properties.STUN_RP_POOL_PERCENT = 10;
        Assert.That(RvrRealmPointRules.CrowdControlCredit(2000, stun: false), Is.EqualTo(100));
        Assert.That(RvrRealmPointRules.CrowdControlCredit(2000, stun: true), Is.EqualTo(200));
        Properties.MEZ_RP_POOL_PERCENT = 0;
        Assert.That(RvrRealmPointRules.CrowdControlCredit(2000, stun: false), Is.EqualTo(0));
    }

    [Test]
    public void RepairsPayTenPercentUpToTheCap()
    {
        Properties.REPAIR_RP_PERCENT = 10;
        Properties.REPAIR_RP_CAP = 150;
        Assert.That(RvrRealmPointRules.RepairRealmPoints(500), Is.EqualTo(50));
        Assert.That(RvrRealmPointRules.RepairRealmPoints(50_000), Is.EqualTo(150));
        Assert.That(RvrRealmPointRules.RepairRealmPoints(0), Is.EqualTo(0));
        Properties.REPAIR_RP_PERCENT = 0;
        Assert.That(RvrRealmPointRules.RepairRealmPoints(500), Is.EqualTo(0));
    }

    // 1.122b table: own population as a share of the largest realm's.
    [TestCase(100, 100, 0)]
    [TestCase(91, 100, 0)]
    [TestCase(90, 100, 10)]
    [TestCase(81, 100, 10)]
    [TestCase(80, 100, 25)]
    [TestCase(70, 100, 40)]
    [TestCase(60, 100, 55)]
    [TestCase(50, 100, 70)]
    [TestCase(40, 100, 85)]
    [TestCase(30, 100, 100)]
    [TestCase(0, 100, 100)]
    [TestCase(0, 0, 0)]          // nobody in the frontier: no bonus
    [TestCase(120, 100, 0)]
    public void OutnumberedRealmsGetTheTableBonus(int own, int largest, int percent)
    {
        Assert.That(RvrRealmPointRules.PopulationBonusPercent(own, largest), Is.EqualTo(percent));
    }

    [Test]
    public void SiegePoolsAreHalfAtAKeepAFifthAtATowerAndCapped()
    {
        Properties.SIEGE_CONTRIBUTION_KEEP_PERCENT = 50;
        Properties.SIEGE_CONTRIBUTION_TOWER_PERCENT = 20;
        Assert.That(RvrRealmPointRules.SiegeSharePercent(tower: false), Is.EqualTo(50));
        Assert.That(RvrRealmPointRules.SiegeSharePercent(tower: true), Is.EqualTo(20));
        Assert.That(RvrRealmPointRules.SiegePayout(500, 1000), Is.EqualTo(500));      // 1,000 earned at a keep
        Assert.That(RvrRealmPointRules.SiegePayout(50_000, 1000), Is.EqualTo(10_000)); // ten times a solo kill's worth
        Assert.That(RvrRealmPointRules.SiegePayout(0, 1000), Is.EqualTo(0));
    }
}
