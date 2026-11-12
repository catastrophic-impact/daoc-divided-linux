using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using DOL.GS.ServerRules;
using NUnit.Framework;
using static DOL.GS.IGameStaticItemOwner;

namespace DOL.UnitTests
{
    // Loot redesign phase 1 (owner 2026-11-05): one context per kill shared by every generator, and drops that can name
    // the participant they are meant for. No generator names one yet, so drop rates are unchanged.
    [TestFixture, NonParallelizable]
    public class UT_LootPhase1
    {
        private sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public override byte Level { get; set; }
            public override eRealm Realm { get; set; }
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public override ICharacterClass CharacterClass => new ClassEnchanter();
        }

        private sealed class Player : GamePlayer
        {
            private Player() : base(null, null) { }
            public override byte Level { get; set; }
            public override eRealm Realm { get; set; }
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
        }

        private sealed class Mob : GameNPC
        {
            public override byte Level { get; set; }
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
        }

        private sealed class TestServer : GameServer
        {
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
            protected override IObjectDatabase DataBaseImpl => Db;
        }

        // Every select is empty; writes throw.
        public class EmptyDb : DispatchProxy
        {
            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (!method.Name.StartsWith("Select") && !method.Name.StartsWith("Find"))
                    throw new InvalidOperationException("loot test attempted a database write: " + method.Name);
                Type result = method.ReturnType;
                if (result.IsGenericType && typeof(IEnumerable).IsAssignableFrom(result))
                    return Activator.CreateInstance(typeof(List<>).MakeGenericType(result.GetGenericArguments()[0]));
                return null;
            }
        }

        private static readonly IObjectDatabase Db = DispatchProxy.Create<IObjectDatabase, EmptyDb>();

        private static long _nextId = 910000;
        private GameServer _previous;

        [SetUp]
        public void SetUp()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((TestServer)RuntimeHelpers.GetUninitializedObject(typeof(TestServer)));
        }

        [TearDown]
        public void TearDown() { GameServer.LoadTestDouble(_previous); }

        private static void Field(Type type, object target, string name, object value)
        {
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private static T Inert<T>() where T : GameLiving
        {
            T living = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            Field(typeof(GameLiving), living, "<TempProperties>k__BackingField", new PropertyCollection());
            if (living is GameNPC)
                Field(typeof(GameNPC), living, "m_brains", new ArrayList());
            return living;
        }

        private static Bot NewBot(bool helper = false)
        {
            Bot bot = Inert<Bot>();
            bot.Level = 10;
            bot.Realm = eRealm.Hibernia;
            bot.Name = "phase one bot";
            bot.DatabaseID = Interlocked.Increment(ref _nextId);
            bot.InternalID = AutonomousBotEconomy.GetOwnerId(bot.DatabaseID);
            typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot)).SetValue(bot, !helper);
            typeof(GameBot).GetProperty(nameof(GameBot.IsTemporaryGroupHelper)).SetValue(bot, helper);
            Field(typeof(GameNPC), bot, "m_ownBrain", new BotBrain { Body = bot });
            return bot;
        }

        private static Mob NewMob(string name = "large frog")
        {
            Mob mob = Inert<Mob>();
            mob.Level = 10;
            mob.Name = name;
            return mob;
        }

        private static Mob Pet(GameLiving owner)
        {
            Mob pet = NewMob("frog pet");
            Field(typeof(GameNPC), pet, "m_ownBrain", new ControlledMobBrain(owner) { Body = pet });
            return pet;
        }

        private static DbItemTemplate Item(string name) => new() { Id_nb = name, Name = name };

        #region LootList

        [Test]
        public void IntendedOwnersSurviveAMerge()
        {
            Bot bot = NewBot();
            LootList first = new();
            first.AddFixed(Item("fang"), 2, bot);
            LootList second = new();
            second.AddFixed(Item("pelt"), 1);
            first.AddAll(second);

            LootDrop[] drops = first.GetDrops();
            Assert.That(drops.Select(drop => drop.Item.Name), Is.EqualTo(new[] { "fang", "fang", "pelt" }));
            Assert.That(drops.Select(drop => drop.IntendedOwner), Is.EqualTo(new GameLiving[] { bot, bot, null }));
        }

        [Test]
        public void RandomWinnersHaveNoIntendedOwner()
        {
            LootList loot = new();
            loot.AddRandom(100, Item("eye"));
            LootDrop drop = loot.GetDrops().Single();
            Assert.That(drop.Item.Name, Is.EqualTo("eye"));
            Assert.That(drop.IntendedOwner, Is.Null);
        }

        [Test]
        public void GetLootStillReturnsEveryItem()
        {
            LootList loot = new();
            loot.AddFixed(Item("fang"), 1, NewBot());
            loot.AddFixed(Item("pelt"), 1);
            Assert.That(loot.GetLoot().Select(item => item.Name), Is.EqualTo(new[] { "fang", "pelt" }));
        }

        #endregion

        #region LootMgr

        private sealed class ContextGenerator : LootGeneratorBase
        {
            public readonly List<LootContext> Seen = [];
            public GameLiving GiveTo;

            public override LootList GenerateLoot(LootContext context)
            {
                Seen.Add(context);
                LootList loot = new();
                loot.AddFixed(Item("context drop"), 1, GiveTo);
                return loot;
            }
        }

        private sealed class OldStyleGenerator : ILootGenerator
        {
            public GameNPC Mob;
            public GameObject Killer;
            public int ExclusivePriority { get; set; }
            public void Refresh(GameNPC mob) { }

            public LootList GenerateLoot(GameNPC mob, GameObject killer)
            {
                Mob = mob;
                Killer = killer;
                LootList loot = new();
                loot.AddFixed(Item("old drop"), 1);
                return loot;
            }
        }

        private sealed class BrokenGenerator : LootGeneratorBase
        {
            public override LootList GenerateLoot(LootContext context) => throw new InvalidOperationException("broken on purpose");
        }

        [Test]
        public void EveryGeneratorSharesOneContext()
        {
            Bot bot = NewBot();
            Mob mob = NewMob();
            LootContext context = LootContext.For(mob, bot);
            ContextGenerator a = new() { GiveTo = bot }, b = new();
            OldStyleGenerator old = new();

            LootDrop[] drops = LootMgr.GetDrops(context, new ILootGenerator[] { a, old, b });

            Assert.That(a.Seen.Single(), Is.SameAs(context));
            Assert.That(b.Seen.Single(), Is.SameAs(context));
            Assert.That(old.Mob, Is.SameAs(mob), "a generator that is not a LootGeneratorBase still gets mob and killer");
            Assert.That(old.Killer, Is.SameAs(bot));
            Assert.That(drops.Select(drop => drop.Item.Name), Is.EqualTo(new[] { "context drop", "old drop", "context drop" }));
            Assert.That(drops[0].IntendedOwner, Is.SameAs(bot));
            Assert.That(drops[1].IntendedOwner, Is.Null);
        }

        [Test]
        public void ABrokenGeneratorDoesNotStopTheOthers()
        {
            Bot bot = NewBot();
            LootDrop[] drops = LootMgr.GetDrops(LootContext.For(NewMob(), bot),
                new ILootGenerator[] { new BrokenGenerator(), new ContextGenerator() });
            Assert.That(drops.Single().Item.Name, Is.EqualTo("context drop"));
        }

        #endregion

        #region LootContext

        [Test]
        public void ASoloBotOwnsItsKill()
        {
            Bot bot = NewBot();
            LootContext context = LootContext.For(NewMob(), bot);
            Assert.That(context.Owner, Is.SameAs(bot));
            Assert.That(context.IsSolo, Is.True);
            Assert.That(context.Participants, Is.EqualTo(new GameLiving[] { bot }));
        }

        [Test]
        public void APetsKillBelongsToItsOwner()
        {
            Bot bot = NewBot();
            Assert.That(LootContext.For(NewMob(), Pet(Pet(bot))).Owner, Is.SameAs(bot));
        }

        [Test]
        public void AHelpersKillBelongsToItsPlayer()
        {
            Player player = Inert<Player>();
            Bot helper = NewBot(helper: true);
            typeof(GameBot).GetProperty(nameof(GameBot.Owner)).SetValue(helper, player);
            Assert.That(LootContext.For(NewMob(), helper).Owner, Is.SameAs(player));
        }

        [Test]
        public void NobodyOwnsAMobsKill()
        {
            LootContext context = LootContext.For(NewMob(), NewMob("other frog"));
            Assert.That(context.Owner, Is.Null);
            Assert.That(context.Participants, Is.Empty);
            Assert.That(context.IsSolo, Is.False);
        }

        [Test]
        public void GroupParticipantsAreTheMembersWhoRoll()
        {
            Bot leader = NewBot(), member = NewBot(), helper = NewBot(helper: true);
            Group group = new(leader);
            var members = (List<GameLiving>)typeof(Group).GetField("_groupMembers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(group);
            foreach (Bot bot in new[] { leader, member, helper })
            {
                bot.Group = group;
                members.Add(bot);
            }

            LootContext context = LootContext.For(NewMob(), leader);
            Assert.That(context.IsSolo, Is.False);
            Assert.That(context.Participants, Is.EquivalentTo(new GameLiving[] { leader, member }), "a temporary helper never rolls");
        }

        [TestCase("large frog", false)]
        [TestCase("Grand Frog", true)]
        public void NamedMobsHaveCapitalizedNames(string name, bool named)
        {
            Assert.That(LootContext.IsNamedMob(NewMob(name)), Is.EqualTo(named));
        }

        #endregion

        #region Pick up order

        [Test]
        public void TheIntendedOwnerTriesFirst()
        {
            Bot top = NewBot(), intended = NewBot();
            var order = AbstractServerRules.PickUpOrder(intended, [new ItemOwnerTotalDamagePair(top, 100)]);
            Assert.That(order.Select(entry => entry.Owner), Is.EqualTo(new IGameStaticItemOwner[] { intended, top }));
            Assert.That(order.Select(entry => entry.Intended), Is.EqualTo(new[] { true, false }));
        }

        [Test]
        public void AnIntendedOwnerWhoIsAlsoAnOwnerTriesOnce()
        {
            Bot top = NewBot();
            var order = AbstractServerRules.PickUpOrder(top, [new ItemOwnerTotalDamagePair(top, 100)]);
            Assert.That(order, Has.Count.EqualTo(1));
            Assert.That(order[0].Intended, Is.True);
        }

        [Test]
        public void WithoutAnIntendedOwnerTheOrderIsUnchanged()
        {
            Bot first = NewBot(), second = NewBot();
            var order = AbstractServerRules.PickUpOrder(null,
                [new ItemOwnerTotalDamagePair(first, 100), new ItemOwnerTotalDamagePair(second, 50)]);
            Assert.That(order.Select(entry => entry.Owner), Is.EqualTo(new IGameStaticItemOwner[] { first, second }));
            Assert.That(order.Select(entry => entry.Intended), Is.All.False);
        }

        [Test]
        public void ARaidShareKeepsItsOwnSplit()
        {
            Bot member = NewBot();
            var raid = new RealmRaidLootOwner([member], new RealmRaidLootOwner.Ledger());
            var order = AbstractServerRules.PickUpOrder(NewBot(), [new ItemOwnerTotalDamagePair(raid, 100)]);
            Assert.That(order.Select(entry => entry.Owner), Is.EqualTo(new IGameStaticItemOwner[] { raid }));
        }

        [Test]
        public void ADeadIntendedPlayerIsSkipped()
        {
            Player dead = Inert<Player>();
            Assert.That(dead.IsAlive, Is.False, "an inert player has no health");
            Bot top = NewBot();
            var order = AbstractServerRules.PickUpOrder(dead, [new ItemOwnerTotalDamagePair(top, 100)]);
            Assert.That(order.Select(entry => entry.Owner), Is.EqualTo(new IGameStaticItemOwner[] { top }));
        }

        #endregion
    }
}
