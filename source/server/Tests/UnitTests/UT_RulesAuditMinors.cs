using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using DOL.GS.PropertyCalc;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Smaller rules from the 2026-11-08 game-rules audit (DR-175, 180, 191, 192, 193, 195).</summary>
[TestFixture]
public class UT_RulesAuditMinors
{
    private int _worth;
    private int _worthReleased;
    private int _killBonus;

    [SetUp]
    public void SetUp()
    {
        _worth = Properties.RP_WORTH_SECONDS;
        _worthReleased = Properties.RP_WORTH_SECONDS_RELEASED;
        _killBonus = Properties.KILL_RP_BONUS_PERCENT;
    }

    [TearDown]
    public void TearDown()
    {
        Properties.RP_WORTH_SECONDS = _worth;
        Properties.RP_WORTH_SECONDS_RELEASED = _worthReleased;
        Properties.KILL_RP_BONUS_PERCENT = _killBonus;
    }

    private static T DefaultOf<T>(string field) =>
        (T)System.Convert.ChangeType(
            typeof(Properties).GetField(field)!.GetCustomAttribute<ServerPropertyAttribute>()!.DefaultValue, typeof(T));

    [Test]
    public void DefaultsFollowThePatchNotes()
    {
        Assert.That(DefaultOf<int>(nameof(Properties.RP_WORTH_SECONDS)), Is.EqualTo(300), "resurrected: 5 minutes");
        Assert.That(DefaultOf<int>(nameof(Properties.RP_WORTH_SECONDS_RELEASED)), Is.EqualTo(120), "1.123b: released, 2 minutes");
        Assert.That(DefaultOf<int>(nameof(Properties.KILL_RP_BONUS_PERCENT)), Is.EqualTo(25), "1.124: +25% from kills");
    }

    [Test]
    public void AReleasedVictimIsWorthRealmPointsSoonerThanAResurrectedOne()
    {
        Properties.RP_WORTH_SECONDS = 300;
        Properties.RP_WORTH_SECONDS_RELEASED = 120;
        Assert.That(AutonomousBotRealmPointRewards.RealmPointWorthSeconds(released: true), Is.EqualTo(120));
        Assert.That(AutonomousBotRealmPointRewards.RealmPointWorthSeconds(released: false), Is.EqualTo(300));

        // An owner who shortens the general wait never gets a longer one for released players.
        Properties.RP_WORTH_SECONDS = 60;
        Assert.That(AutonomousBotRealmPointRewards.RealmPointWorthSeconds(released: true), Is.EqualTo(60));
    }

    [TestCase(25, 1000, 1250)]
    [TestCase(25, 1, 1)]
    [TestCase(25, 0, 0)]
    [TestCase(0, 1000, 1000)]
    [TestCase(-10, 1000, 1000)]
    public void KillRealmPointsGetTheBonusBeforeAnythingElse(int percent, int points, int expected)
    {
        Properties.KILL_RP_BONUS_PERCENT = percent;
        Assert.That(AutonomousBotRealmPointRewards.ApplyKillBonus(points), Is.EqualTo(expected));
    }

    [Test]
    public void TheBonusIsInsideTheGamebotRewardBeforeRankAndGroupAdjustments()
    {
        Properties.KILL_RP_BONUS_PERCENT = 25;
        int actual = AutonomousBotRealmPointRewards.CalculateRealmPointReward(
            victimRealmPointValue: 1800, victimRealmLevel: 90, awarderRealmPointValue: 900, awarderRealmLevel: 0,
            participantCount: 2, groupContributorCount: 2, damagePercent: 0.75, applyRealmRankAdjustment: true);

        int withBonus = (int)(1800 / 2 * 0.75) * 125 / 100;
        int rankAdjusted = (int)(withBonus * (1.0 + 2.0 * 90 / 900.0));
        Assert.That(actual, Is.EqualTo(rankAdjusted + (int)(rankAdjusted * 0.125)));
    }

    private sealed class Monster : GameNPC
    {
        private Monster() : base(new NpcTemplate()) { }
    }

    private static T Bare<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    [Test]
    public void DualWieldingPlayersAndBotsLeaveThreeQuartersOfBlockAndEvade()
    {
        Assert.That(PlayerDefenseFormula.DualWieldDefenseFactor, Is.EqualTo(0.75));
        Assert.That(Bare<GamePlayer>().DualWieldDefensePenetrationFactor, Is.EqualTo(0.75));
        Assert.That(Bare<GameBot>().DualWieldDefensePenetrationFactor, Is.EqualTo(0.75));
        Assert.That(Bare<Monster>().DualWieldDefensePenetrationFactor, Is.EqualTo(0.5), "monsters keep the old value");
    }

    [Test]
    public void TwoHandedWeaponsNoLongerHalveParry()
    {
        Assert.That(Bare<GamePlayer>().TwoHandedDefensePenetrationFactor, Is.EqualTo(1.0));
        Assert.That(Bare<Monster>().TwoHandedDefensePenetrationFactor, Is.EqualTo(1.0));
    }

    [Test]
    public void SeveralAttackersLowerParryOnlyAgainstMonsters()
    {
        Assert.That(PlayerDefenseFormula.IgnoresAttackerCountForParry(Bare<GamePlayer>(), Bare<GamePlayer>()), Is.True);
        Assert.That(PlayerDefenseFormula.IgnoresAttackerCountForParry(Bare<GameBot>(), Bare<GamePlayer>()), Is.True);
        Assert.That(PlayerDefenseFormula.IgnoresAttackerCountForParry(Bare<Monster>(), Bare<GamePlayer>()), Is.False);
        Assert.That(PlayerDefenseFormula.IgnoresAttackerCountForParry(Bare<GamePlayer>(), Bare<Monster>()), Is.False);
    }

    [Test]
    public void SpellCritsCapAtHalfAgainstPlayersBotsAndNecromancerPets()
    {
        Assert.That(PlayerDefenseFormula.MaxSpellCriticalBonus(Bare<GamePlayer>()), Is.EqualTo(0.5));
        Assert.That(PlayerDefenseFormula.MaxSpellCriticalBonus(Bare<GameBot>()), Is.EqualTo(0.5));
        Assert.That(PlayerDefenseFormula.MaxSpellCriticalBonus(Bare<NecromancerPet>()), Is.EqualTo(0.5));
        Assert.That(PlayerDefenseFormula.MaxSpellCriticalBonus(Bare<Monster>()), Is.EqualTo(1.0));
    }
}
