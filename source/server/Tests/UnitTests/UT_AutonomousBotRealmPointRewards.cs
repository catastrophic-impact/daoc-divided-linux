using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Database.Handlers;
using DOL.GS;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public class UT_AutonomousBotRealmPointRewards
{
    private sealed class RewardServer : GameServer
    {
        protected override IServerRules ServerRulesImpl => new NormalServerRules();
        protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
    }

    private static readonly IObjectDatabase EmptyDatabase =
        DispatchProxy.Create<IObjectDatabase, EmptyReadDatabase>();

    public class EmptyReadDatabase : DispatchProxy
    {
        protected override object Invoke(MethodInfo method, object[] args)
        {
            Type result = method.ReturnType;
            if (result.IsGenericType && typeof(IEnumerable).IsAssignableFrom(result))
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(result.GetGenericArguments()[0]));
            return null;
        }
    }

    private GameServer _previousServer;

    [SetUp]
    public void SetUp()
    {
        _previousServer = GameServer.Instance;
        GameServer.LoadTestDouble((RewardServer)RuntimeHelpers.GetUninitializedObject(typeof(RewardServer)));
    }

    [TearDown]
    public void TearDown() => GameServer.LoadTestDouble(_previousServer);

    private sealed class RewardBot : GameBot
    {
        private RewardBot() : base((OfflineWorldBotRecord)null) { }
        public override byte Level { get; set; }
        public override eRealm Realm { get; set; }
    }

    private static void SetField(Type type, object target, string name, object value) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static RewardBot Bot(bool autonomous, bool temporary)
    {
        RewardBot bot = (RewardBot)RuntimeHelpers.GetUninitializedObject(typeof(RewardBot));
        SetField(typeof(GameLiving), bot, "<TempProperties>k__BackingField", new PropertyCollection());
        SetField(typeof(GameNPC), bot, "m_brains", new ArrayList());
        typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot))!.SetValue(bot, autonomous);
        typeof(GameBot).GetProperty(nameof(GameBot.IsTemporaryGroupHelper))!.SetValue(bot, temporary);
        SetField(typeof(GameNPC), bot, "m_ownBrain", new BotBrain { Body = bot });
        return bot;
    }

    [Test]
    public void OnlyPersistentAutonomousBotIsRealmPointVictim()
    {
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(Bot(true, false)), Is.True);
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(Bot(false, true)), Is.False,
            "/spawn companions must never be worth realm points");
        GameNPC ordinaryNpc = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
        SetField(typeof(GameNPC), ordinaryNpc, "m_brains", new ArrayList());
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(ordinaryNpc), Is.False,
            "ordinary NPCs and pets remain on the zero-RP NPC path");
    }

    [TestCase(25, 0, 25)]
    [TestCase(35, 0, 225)]
    [TestCase(35, 20, 425)]
    [TestCase(50, 0, 900)]
    [TestCase(50, 90, 1800)]
    [TestCase(50, 120, 2100)]
    public void VictimValueMatchesThe181PlayerFormula(int level, int realmLevel, int expected)
    {
        Assert.That(AutonomousBotRealmPointRewards.GetPlayerEquivalentRealmPointValue(
            (byte)level, realmLevel), Is.EqualTo(expected));
    }

    [Test]
    public void RewardMatchesPlayerKillCapDamageRankAndGroupFormula()
    {
        int actual = AutonomousBotRealmPointRewards.CalculateRealmPointReward(
            victimRealmPointValue: 990,
            victimRealmLevel: 90,
            awarderRealmPointValue: 900,
            awarderRealmLevel: 0,
            participantCount: 2,
            groupContributorCount: 2,
            damagePercent: 0.75,
            applyRealmRankAdjustment: true);

        int baseShare = 990 / 2;
        int damageShare = (int)(baseShare * 0.75);
        int rankAdjusted = (int)(damageShare * (1.0 + 2.0 * 90 / 900.0));
        int expected = rankAdjusted + (int)(rankAdjusted * 0.125);
        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase(1, 1)]
    [TestCase(20, 13)]
    [TestCase(35, 22)]
    [TestCase(50, 31)]
    public void BountyValueMatchesPlayerFormula(int level, int expected)
    {
        Assert.That(AutonomousBotRealmPointRewards.GetPlayerEquivalentBountyPointValue((byte)level),
            Is.EqualTo(expected));
        Assert.That(expected, Is.EqualTo((int)(1 + level * 0.6)), "GamePlayer.BountyPointsValue formula");
    }

    [Test]
    public void BountyRewardMatchesPlayerKillSplitCapAndDamage()
    {
        // Solo, full damage, level 50 victim and awarder: the whole value.
        Assert.That(AutonomousBotRealmPointRewards.CalculateBountyPointReward(31, 31, 1, 1.0), Is.EqualTo(31));
        // Two-man group dealing 75%: (31 / 2) * 0.75.
        Assert.That(AutonomousBotRealmPointRewards.CalculateBountyPointReward(31, 31, 2, 0.75), Is.EqualTo((int)(15 * 0.75)));
        // A low awarder is capped at twice their own value (level 10 player = 7, cap 14).
        Assert.That(AutonomousBotRealmPointRewards.CalculateBountyPointReward(31, 7, 1, 1.0), Is.EqualTo(14));
        // Damage above 100% is clamped.
        Assert.That(AutonomousBotRealmPointRewards.CalculateBountyPointReward(31, 31, 1, 1.5), Is.EqualTo(31));
    }

    [TestCase(0, 31, 1, 1.0)]
    [TestCase(31, 0, 1, 1.0)]
    [TestCase(31, 31, 0, 1.0)]
    [TestCase(31, 31, 1, 0.0)]
    public void BountyRewardIsZeroWithoutAValidShare(int victim, int awarder, int participants, double damage)
    {
        Assert.That(AutonomousBotRealmPointRewards.CalculateBountyPointReward(victim, awarder, participants, damage),
            Is.EqualTo(0));
    }

    [Test]
    public void ExperienceValueIsFourLivingsAsForAPlayer()
    {
        Assert.That(AutonomousBotRealmPointRewards.GetPlayerEquivalentExperienceValue(1_000), Is.EqualTo(4_000));
    }

    [Test]
    public void ExperienceRewardMatchesPlayerKillSplitCapAndDamage()
    {
        // Solo, full damage, equal levels: the whole value.
        Assert.That(AutonomousBotRealmPointRewards.CalculateExperienceReward(4_000, 4_000, 125, 1, 1.0, out long solo), Is.EqualTo(4_000));
        Assert.That(solo, Is.EqualTo(4_000));
        // Two-man group dealing 75%: the outpost bonus works from the split, the pay is also scaled by damage.
        Assert.That(AutonomousBotRealmPointRewards.CalculateExperienceReward(4_000, 4_000, 125, 2, 0.75, out long group), Is.EqualTo(2_000));
        Assert.That(group, Is.EqualTo(1_500));
        // A low awarder is capped at 125% of their own value.
        Assert.That(AutonomousBotRealmPointRewards.CalculateExperienceReward(40_000, 4_000, 125, 1, 1.0, out long capped), Is.EqualTo(5_000));
        Assert.That(capped, Is.EqualTo(5_000));
        // Damage above 100% is clamped.
        AutonomousBotRealmPointRewards.CalculateExperienceReward(4_000, 4_000, 125, 1, 1.5, out long clamped);
        Assert.That(clamped, Is.EqualTo(4_000));
    }

    [TestCase(0, 4000, 1, 1.0)]
    [TestCase(4000, 0, 1, 1.0)]
    [TestCase(4000, 4000, 0, 1.0)]
    [TestCase(4000, 4000, 1, 0.0)]
    public void ExperienceRewardIsZeroWithoutAValidShare(long victim, long awarder, int participants, double damage)
    {
        Assert.That(AutonomousBotRealmPointRewards.CalculateExperienceReward(victim, awarder, 125, participants, damage, out long paid),
            Is.EqualTo(0));
        Assert.That(paid, Is.EqualTo(0));
    }

    [Test]
    public void CompanionAndControlledPetResolveToPlayerOwnerWithoutBecomingVictims()
    {
        GamePlayer owner = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));
        RewardBot companion = Bot(false, true);
        typeof(GameBot).GetProperty(nameof(GameBot.Owner))!.SetValue(companion, owner);
        GameNPC pet = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
        SetField(typeof(GameNPC), pet, "m_brains", new ArrayList());
        SetField(typeof(GameNPC), pet, "m_ownBrain", new ControlledMobBrain(companion) { Body = pet });

        Assert.That(AutonomousBotRealmPointRewards.ResolveRootRewardOwner(companion), Is.SameAs(owner));
        Assert.That(AutonomousBotRealmPointRewards.ResolveRootRewardOwner(pet), Is.SameAs(owner));
        Assert.That(AutonomousRvrDefense.IsCombatant(owner), Is.True);
        Assert.That(AutonomousRvrDefense.IsCombatant(companion), Is.True);
        Assert.That(AutonomousRvrDefense.IsCombatant(pet), Is.True);
        SetField(typeof(GameNPC), pet, "m_ownBrain", new ControlledMobBrain(owner) { Body = pet });
        Assert.That(AutonomousRvrDefense.IsCombatant(pet), Is.True, "Direct human-owned pets are not IGamePlayer but must be defended against");
        Assert.That(AutonomousRvrDefense.IsCombatant(null), Is.False);
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(companion), Is.False);
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(pet), Is.False);
    }
}
