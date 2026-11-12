using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.Database.Handlers;
using DOL.GS;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture, NonParallelizable]
public class UT_ArrowMissChance
{
    private static readonly IObjectDatabase Empty = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
    private sealed class Server : GameServer { protected override IObjectDatabase DataBaseImpl => Empty; }
    private GameServer _previous;
    [SetUp] public void Setup()
    {
        _previous = GameServer.Instance;
        GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
    }
    [TearDown] public void Cleanup() => GameServer.LoadTestDouble(_previous);

    private sealed class Fighter : GameBot
    {
        private Fighter() : base((OfflineWorldBotRecord)null) { }
        public override byte Level { get; set; }
        public override int GetModified(eProperty property) => property == eProperty.LivingEffectiveLevel ? Level : 0;

        public static Fighter At(byte level)
        {
            var fighter = (Fighter)RuntimeHelpers.GetUninitializedObject(typeof(Fighter));
            fighter.Level = level;
            fighter.attackComponent = new AttackComponent(fighter);
            fighter.effectListComponent = EffectListComponent.Create(fighter);
            return fighter;
        }
    }

    private static int MissesOutOf(int shots, double missPercent) =>
        Enumerable.Range(0, shots).Count(_ => ArrowSpellHandler.RollsMiss(missPercent));

    [TestCase(1, 1.33)]   // The first level above an even con: this was every shot missing.
    [TestCase(3, 4.0)]
    [TestCase(9, 12.0)]
    public void SpellShotsMissByTheLevelGapNotEveryTime(int levelsAbove, double expectedMissPercent)
    {
        Fighter archer = Fighter.At(50), mob = Fighter.At((byte)(50 + levelsAbove));
        double missPercent = 100 - new Archery(archer, null, null).CalculateToHitChance(mob);
        Assert.That(missPercent, Is.EqualTo(expectedMissPercent).Within(0.01));

        const int shots = 200000;
        Assert.That(MissesOutOf(shots, missPercent) * 100.0 / shots, Is.EqualTo(expectedMissPercent).Within(0.5));
    }

    [TestCase(0.0, 0)]
    [TestCase(-5.0, 0)]
    [TestCase(100.0, 1000)]
    public void SpellShotsAtTheEdges(double missPercent, int expectedMisses)
    {
        Assert.That(MissesOutOf(1000, missPercent), Is.EqualTo(expectedMisses));
    }

    [TestCase(84.0, 0.962)]
    [TestCase(73.0, 0.544)]
    [TestCase(40.0, 0.0)]
    public void FarHigherTargetsLowerShotDamageWithoutGoingNegative(double hitChance, double expectedShare)
    {
        var shot = new Archery(Fighter.At(50), null, null);
        Assert.That(shot.AdjustDamageForHitChance(100, hitChance), Is.EqualTo(100 * expectedShare).Within(0.01));
        Assert.That(shot.AdjustDamageForHitChance(100, 85), Is.EqualTo(100));
    }

    // A bot archer with a bow starts at the live 13% base (DR-194), then 1.33 per level the target is above.
    [TestCase(1, 14.33)]
    [TestCase(9, 25.0)]
    public void BowFireMissesTheSameOldWay(int levelsAbove, double expectedMissPercent)
    {
        Fighter archer = Fighter.At(50), mob = Fighter.At((byte)(50 + levelsAbove));
        DbInventoryItem bow = GameInventoryItem.Create(new DbItemTemplate { Object_Type = (int)eObjectType.RecurvedBow });
        DbInventoryItem arrow = GameInventoryItem.Create(new DbItemTemplate { Object_Type = (int)eObjectType.Arrow, SPD_ABS = 0x15 });
        var action = new WeaponAction(archer, mob, bow, 1, 4000, eRangedAttackType.Normal, arrow);
        var ad = new AttackData { Attacker = archer, Target = mob };
        Assert.That(mob.attackComponent.GetMissChance(action, ad, null, bow), Is.EqualTo(expectedMissPercent).Within(0.01));
    }

    [Test]
    public void BaseMissIsLiveThirteenPercentForWeaponWieldersOnly()
    {
        DbInventoryItem sword = GameInventoryItem.Create(new DbItemTemplate { Object_Type = (int)eObjectType.SlashingWeapon });
        Fighter bot = Fighter.At(50);
        Assert.That(AttackComponent.BaseMissChance(bot, sword), Is.EqualTo(13));   // player or bot with a weapon: 87% to-hit
        Assert.That(AttackComponent.BaseMissChance(bot, null), Is.EqualTo(18));    // no weapon item: no hidden +5%
        var monster = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
        Assert.That(AttackComponent.BaseMissChance(monster, sword), Is.EqualTo(18)); // monsters keep the old base
    }
}
