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

namespace DOL.UnitTests
{
    // Loot redesign phase 5 (owner 2026-11-05): personal loot, each group member rolling for an item of their own
    // class, and optional bad-luck protection (RandomItems.BadLuckStep, off by default).
    [TestFixture, NonParallelizable]
    public class UT_LootPhase5
    {
        private sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public override byte Level { get; set; }
            public override int EffectiveLevel => Level;
            public override eRealm Realm { get; set; }
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public ICharacterClass TestClass;
            public override ICharacterClass CharacterClass => TestClass ?? new ClassEnchanter();
        }

        private sealed class Player : GamePlayer
        {
            private Player() : base(null, null) { }
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
        }

        private sealed class Mob : GameNPC
        {
            public override byte Level { get; set; }
            public override int EffectiveLevel => Level;
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
        }

        // Rolls come from a script; items record who and what class they were made for.
        private sealed class Scripted : ROGMobGenerator
        {
            public Queue<bool> Hits = new();
            public bool Otherwise;
            public readonly List<double> Chances = [];
            public readonly List<(GameLiving Owner, eCharacterClass Class)> Made = [];

            protected override bool RollDropChance(double chance)
            {
                Chances.Add(chance);
                return Hits.Count > 0 ? Hits.Dequeue() : Otherwise;
            }

            protected override DbItemTemplate GenerateItemTemplate(GameLiving owner, eCharacterClass characterClass, byte level, int con, bool adjustQuality = true)
            {
                Made.Add((owner, characterClass));
                return new DbItemTemplate { Id_nb = Guid.NewGuid().ToString(), Name = "made for " + owner.Name, Realm = 0, MaxCount = 1, PackSize = 1 };
            }
        }

        private sealed class TestServer : GameServer
        {
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
            protected override IObjectDatabase DataBaseImpl => Db;
        }

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
        private static long _nextId = 930000;
        private GameServer _previous;

        [SetUp]
        public void SetUp()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((TestServer)RuntimeHelpers.GetUninitializedObject(typeof(TestServer)));
            LootRulesFile.TestOverride = null;
        }

        [TearDown]
        public void TearDown()
        {
            LootRulesFile.TestOverride = null;
            GameServer.LoadTestDouble(_previous);
        }

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

        private static Bot NewBot(string name, ICharacterClass characterClass = null, byte level = 10)
        {
            Bot bot = Inert<Bot>();
            bot.Level = level;
            bot.Realm = eRealm.Hibernia;
            bot.Name = name;
            bot.TestClass = characterClass;
            bot.DatabaseID = Interlocked.Increment(ref _nextId);
            bot.InternalID = AutonomousBotEconomy.GetOwnerId(bot.DatabaseID);
            typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot)).SetValue(bot, true);
            Field(typeof(GameNPC), bot, "m_ownBrain", new BotBrain { Body = bot });
            return bot;
        }

        private static Mob NewMob(byte level = 10)
        {
            Mob mob = Inert<Mob>();
            mob.Level = level;
            mob.Name = "large frog";
            return mob;
        }

        private static void GroupOf(params Bot[] bots)
        {
            Group group = new(bots[0]);
            var members = (List<GameLiving>)typeof(Group).GetField("_groupMembers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(group);
            foreach (Bot bot in bots)
            {
                bot.Group = group;
                members.Add(bot);
            }
        }

        private static LootRules Rules(bool personal = true, double badLuckStep = 0, double badLuckMax = 25) =>
            new() { RandomItems = new RandomItemLootRules { PersonalLoot = personal, BadLuckStep = badLuckStep, BadLuckMax = badLuckMax } };

        #region Personal loot

        [Test]
        public void EachWinnerGetsAnItemOfTheirOwnClass()
        {
            LootRulesFile.TestOverride = Rules();
            Bot druid = NewBot("druid", new ClassDruid()), hero = NewBot("hero", new ClassHero()), bard = NewBot("bard", new ClassBard()),
                ranger = NewBot("ranger", new ClassRanger()), warden = NewBot("warden", new ClassWarden()), eldritch = NewBot("eldritch", new ClassEldritch());
            GroupOf(druid, hero, bard, ranger, warden, eldritch);
            var generator = new Scripted { Otherwise = true };

            LootDrop[] drops = generator.GenerateLoot(NewMob(), druid).GetDrops();

            Assert.That(drops, Has.Length.EqualTo(2), "six members: two drops");
            Assert.That(generator.Chances, Has.Count.EqualTo(6), "everyone rolls, as before");
            foreach ((GameLiving owner, eCharacterClass characterClass) in generator.Made)
                Assert.That(characterClass, Is.EqualTo((eCharacterClass)((Bot)owner).CharacterClass.ID), owner.Name);
            Assert.That(drops.Select(d => d.IntendedOwner), Is.EquivalentTo(generator.Made.Select(m => m.Owner)), "each item is meant for the member it was made for");
        }

        [Test]
        public void TheCapFavoursNobody()
        {
            LootRulesFile.TestOverride = Rules();
            Bot first = NewBot("first"), second = NewBot("second"), third = NewBot("third");
            GroupOf(first, second, third);
            var winners = new HashSet<GameLiving>();
            for (int i = 0; i < 60; i++)
            {
                var generator = new Scripted { Otherwise = true };
                winners.Add(generator.GenerateLoot(NewMob(), first).GetDrops().Single().IntendedOwner);
            }
            Assert.That(winners, Has.Count.EqualTo(3), "the one drop a three-member group may get goes to anyone");
        }

        [Test]
        public void AVeryHighLevelMobStillGuaranteesADrop()
        {
            LootRulesFile.TestOverride = Rules();
            Bot first = NewBot("first", level: 70), second = NewBot("second", level: 70);
            GroupOf(first, second);
            var generator = new Scripted { Otherwise = false };
            LootDrop drop = generator.GenerateLoot(NewMob(70), first).GetDrops().Single();
            Assert.That(drop.IntendedOwner, Is.AnyOf(first, second));
        }

        [Test]
        public void ASoloDropIsMeantForTheKiller()
        {
            LootRulesFile.TestOverride = Rules();
            Bot bot = NewBot("solo");
            Assert.That(new Scripted { Otherwise = true }.GenerateLoot(NewMob(), bot).GetDrops().Single().IntendedOwner, Is.SameAs(bot));
        }

        [Test]
        public void WithPersonalLootOffDropsGoToTheKillsOwners()
        {
            LootRulesFile.TestOverride = Rules(personal: false);
            Bot first = NewBot("first"), second = NewBot("second"), third = NewBot("third");
            GroupOf(first, second, third);
            LootDrop drop = new Scripted { Otherwise = true }.GenerateLoot(NewMob(), first).GetDrops().Single();
            Assert.That(drop.IntendedOwner, Is.Null);
        }

        #endregion

        #region Bad luck

        [Test]
        public void MissesRaiseTheNextRollUntilAnItemDrops()
        {
            LootRulesFile.TestOverride = Rules(badLuckStep: 5, badLuckMax: 12);
            Bot bot = NewBot("unlucky");
            var generator = new Scripted { Hits = new Queue<bool>([false, false, false, false, true, false]) };
            for (int i = 0; i < 6; i++)
                generator.GenerateLoot(NewMob(), bot);
            Assert.That(generator.Chances, Is.EqualTo(new[] { 24.0, 29, 34, 36, 36, 24 }), "+5 a miss, at most +12, cleared by the drop");
            Assert.That(ROGMobGenerator.BadLuck(bot), Is.EqualTo(5));
        }

        [Test]
        public void BadLuckIsOffByDefault()
        {
            Bot bot = NewBot("plain");
            var generator = new Scripted { Otherwise = false };
            for (int i = 0; i < 3; i++)
                generator.GenerateLoot(NewMob(), bot);
            Assert.That(generator.Chances, Is.All.EqualTo(24.0));
            Assert.That(ROGMobGenerator.BadLuck(bot), Is.EqualTo(0));
        }

        [Test]
        public void AHitPastTheCapNeitherDropsNorClearsBadLuck()
        {
            LootRulesFile.TestOverride = Rules(badLuckStep: 5);
            Bot first = NewBot("first"), second = NewBot("second"), third = NewBot("third");
            GroupOf(first, second, third);
            foreach (Bot bot in new[] { first, second, third })
                bot.TempProperties.SetProperty(ROGMobGenerator.BadLuckProperty, 10.0);

            LootDrop drop = new Scripted { Otherwise = true }.GenerateLoot(NewMob(), first).GetDrops().Single();

            Assert.That(ROGMobGenerator.BadLuck(drop.IntendedOwner), Is.EqualTo(0));
            Assert.That(new[] { first, second, third }.Where(b => b != drop.IntendedOwner).Select(ROGMobGenerator.BadLuck), Is.All.EqualTo(10));
        }

        #endregion

        #region Pick up

        private sealed class FakeOwner : IGameStaticItemOwner
        {
            public TryPickUpResult Result;
            public int Tries;
            public string Name => "fake owner";
            public object GameStaticItemOwnerComparand => null;
            public TryPickUpResult TryAutoPickUpMoney(GameMoney money) { Tries++; return Result; }
            public TryPickUpResult TryAutoPickUpItem(WorldInventoryItem item) { Tries++; return Result; }
            public TryPickUpResult TryPickUpMoney(GamePlayer source, GameMoney money) => TryPickUpResult.DoesNotWant;
            public TryPickUpResult TryPickUpItem(GamePlayer source, WorldInventoryItem item) => TryPickUpResult.DoesNotWant;
        }

        private static WorldInventoryItem Item() => new(GameInventoryItem.Create(new DbItemTemplate
        {
            Id_nb = Guid.NewGuid().ToString(), Name = "personal pants", Item_Type = (int)eInventorySlot.LegsArmor,
            Object_Type = (int)eObjectType.Cloth, MaxCount = 1, PackSize = 1, MaxCondition = 100, MaxDurability = 100,
        }));

        [Test]
        public void APlayerWithoutAutolootKeepsTheirPersonalItem()
        {
            Player player = Inert<Player>();
            Field(typeof(GamePlayer), player, "m_dbCharacter", new DbCoreCharacter { Autoloot = false });
            Assert.That(player.Autoloot, Is.False);
            FakeOwner group = new() { Result = TryPickUpResult.Success };
            WorldInventoryItem item = Item();

            bool picked = AbstractServerRules.TryAutoPickUp(item, [(player, true), (group, false)]);

            Assert.That(picked, Is.False, "left on the ground");
            Assert.That(group.Tries, Is.EqualTo(0), "the group never got it");
            Assert.That(item.Owners, Is.EquivalentTo(new IGameStaticItemOwner[] { player }), "only the player may pick it up");
        }

        [Test]
        public void APersonalItemIsTheOwnersOnAHandPickupInAGroup()
        {
            Player player = Inert<Player>();
            FakeOwner group = new(), battleGroup = new();
            WorldInventoryItem personal = Item();
            personal.AddOwner(player);
            Assert.That(personal.IsPersonalTo(player, group, battleGroup), Is.True, "skips the group autosplit");
            Assert.That(personal.IsPersonalTo(player, null, null), Is.True);

            WorldInventoryItem shared = Item();
            shared.AddOwner(player);
            shared.AddOwner(group);
            Assert.That(shared.IsPersonalTo(player, group, battleGroup), Is.False, "a group drop still autosplits");

            WorldInventoryItem someoneElses = Item();
            someoneElses.AddOwner(group);
            Assert.That(someoneElses.IsPersonalTo(player, group, null), Is.False);
        }

        [Test]
        public void ABotThatTurnsAPersonalItemDownHandsItOn()
        {
            FakeOwner bot = new() { Result = TryPickUpResult.DoesNotWant }, group = new() { Result = TryPickUpResult.Success };
            Assert.That(AbstractServerRules.TryAutoPickUp(Item(), [(bot, true), (group, false)]), Is.True);
            Assert.That(group.Tries, Is.EqualTo(1));
        }

        /// <summary>DR-272: a drop rolled for an account companion is its own: worn if an upgrade, else kept in its backpack.</summary>
        [Test]
        public void ACompanionKeepsTheDropsRolledForIt()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GameBot.KeepCompanionLoot(true, () => true, () => throw new AssertionException("stored an equipped item"), out bool equipped),
                    Is.EqualTo(TryPickUpResult.Success));
                Assert.That(equipped, Is.True);

                Assert.That(GameBot.KeepCompanionLoot(true, () => false, () => true, out equipped), Is.EqualTo(TryPickUpResult.Success));
                Assert.That(equipped, Is.False, "not an upgrade: kept in the backpack");

                Assert.That(GameBot.KeepCompanionLoot(true, () => false, () => false, out _), Is.EqualTo(TryPickUpResult.Blocked),
                    "a full backpack hands it on");
                Assert.That(GameBot.KeepCompanionLoot(false, () => true, () => true, out _), Is.EqualTo(TryPickUpResult.DoesNotWant),
                    "a dead or missing companion hands it on");
            });

            // A companion with a full backpack: the group still gets the drop.
            FakeOwner companion = new() { Result = TryPickUpResult.Blocked }, group = new() { Result = TryPickUpResult.Success };
            Assert.That(AbstractServerRules.TryAutoPickUp(Item(), [(companion, true), (group, false)]), Is.True);
            Assert.That(group.Tries, Is.EqualTo(1));
        }

        [Test]
        public void ABlockedIntendedOwnerHandsItOn()
        {
            FakeOwner intended = new() { Result = TryPickUpResult.Blocked }, group = new() { Result = TryPickUpResult.Success };
            Assert.That(AbstractServerRules.TryAutoPickUp(Item(), [(intended, true), (group, false)]), Is.True);
        }

        [Test]
        public void ABlockedOwnerStillStopsTheRest()
        {
            FakeOwner top = new() { Result = TryPickUpResult.Blocked }, next = new() { Result = TryPickUpResult.Success };
            Assert.That(AbstractServerRules.TryAutoPickUp(Item(), [(top, false), (next, false)]), Is.False);
            Assert.That(next.Tries, Is.EqualTo(0));
        }

        #endregion
    }
}
