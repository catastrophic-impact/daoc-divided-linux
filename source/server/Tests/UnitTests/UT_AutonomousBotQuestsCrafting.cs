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
using DOL.GS.Quests;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture, NonParallelizable]
    public class UT_AutonomousBotQuestsCrafting
    {
        private sealed class TestBot : GameBot
        {
            private TestBot() : base((OfflineWorldBotRecord)null) { }
            public override byte Level { get; set; }
            public override int EffectiveLevel => Level;
            public override eRealm Realm { get; set; }
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public override ICharacterClass CharacterClass => new ClassEnchanter();
            public override void RefreshItemBonuses() { }
        }

        private sealed class TestMob : GameNPC
        {
            public override byte Level { get; set; }
            public override int EffectiveLevel => Level;
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
        }

        private sealed class TestServer : GameServer
        {
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
            protected override IObjectDatabase DataBaseImpl => TemplateDatabase;
        }

        private static readonly Dictionary<string, DbItemTemplate> Templates = new(StringComparer.OrdinalIgnoreCase);
        private static readonly IObjectDatabase TemplateDatabase = DispatchProxy.Create<IObjectDatabase, TemplateReads>();

        public class TemplateReads : DispatchProxy
        {
            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name == "FindObjectByKey" && args[0] is string key)
                    return Templates.GetValueOrDefault(key);
                Type result = method.ReturnType;
                if (result == typeof(bool))
                    return true;
                if (result.IsGenericType && typeof(IEnumerable).IsAssignableFrom(result))
                    return Activator.CreateInstance(typeof(List<>).MakeGenericType(result.GetGenericArguments()[0]));
                return result.IsValueType && result != typeof(void) ? Activator.CreateInstance(result) : null;
            }
        }

        private GameServer _previousServer;
        private static long _nextId = 930000;

        [OneTimeSetUp]
        public void Logging() => DOL.Logging.LoggerManager.InitializeWithExplicitLibrary(null, DOL.Logging.LogLibrary.None);

        [SetUp]
        public void SetUp()
        {
            _previousServer = GameServer.Instance;
            GameServer.LoadTestDouble((TestServer)RuntimeHelpers.GetUninitializedObject(typeof(TestServer)));
            Templates.Clear();
            AutonomousBotQuests.SetCatalogForTests([], []);
            AutonomousBotQuests.Load([]);
            // The real craft, without the translations its constructor reads.
            var craft = (WeaponCrafting)RuntimeHelpers.GetUninitializedObject(typeof(WeaponCrafting));
            craft.eSkill = eCraftingSkill.WeaponCrafting;
            CraftSkills[(int)eCraftingSkill.WeaponCrafting - 1] = craft;
        }

        private static AbstractCraftingSkill[] CraftSkills => (AbstractCraftingSkill[])typeof(CraftingMgr)
            .GetField("m_craftingskills", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        [TearDown]
        public void TearDown()
        {
            CraftSkills[(int)eCraftingSkill.WeaponCrafting - 1] = null;
            GameServer.LoadTestDouble(_previousServer);
        }

        private static void Field(Type type, object target, string name, object value) =>
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static T Inert<T>() where T : GameLiving
        {
            T living = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            Field(typeof(GameLiving), living, "<TempProperties>k__BackingField", new PropertyCollection());
            if (living is GameNPC)
                Field(typeof(GameNPC), living, "m_brains", new ArrayList());
            return living;
        }

        private static TestBot Bot(string name = "Quest bot", string skills = "")
        {
            TestBot bot = Inert<TestBot>();
            bot.Level = 20;
            bot.Realm = eRealm.Albion;
            bot.Name = name;
            bot.DatabaseID = Interlocked.Increment(ref _nextId);
            bot.InternalID = AutonomousBotEconomy.GetOwnerId(bot.DatabaseID);
            bot.Inventory = new BotInventory(bot.InternalID);
            typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot)).SetValue(bot, true);
            typeof(GameBot).GetProperty(nameof(GameBot.PersistentRecord)).SetValue(bot,
                new OfflineWorldBotRecord { BotId = bot.DatabaseID, SerializedCraftingSkills = skills });
            Field(typeof(GameNPC), bot, "m_ownBrain", new BotBrain { Body = bot });
            Field(typeof(GameBot), bot, "_experienceLock", new Lock());
            return bot;
        }

        private static TestMob Mob(string name, string internalId = null)
        {
            TestMob mob = Inert<TestMob>();
            mob.Level = 20;
            mob.Name = name;
            mob.InternalID = internalId ?? Guid.NewGuid().ToString();
            return mob;
        }

        private static DbItemTemplate Item(string id, int price = 10, int pack = 1, int maxCount = 1, int model = 1) =>
            Templates[id] = new DbItemTemplate { Id_nb = id, Name = id, Level = 1, Quality = 95, MaxCount = maxCount, PackSize = pack,
                MaxCondition = 10000, MaxDurability = 10000, Price = price, Model = model, IsTradable = true, IsDropable = true };

        private static DbDataQuest Row(string types, string targets, string stepItems = "", string collects = "", string source = "") => new()
        {
            ID = 20001,
            Name = "Test quest",
            StartName = "Giver",
            StartRegionID = 1,
            StepType = types,
            TargetName = targets,
            StepItemTemplates = stepItems,
            CollectItemTemplate = collects,
            SourceName = source,
            MinLevel = 15,
            MaxLevel = 50,
            RewardMoney = "0|0|500",
            RewardXP = "0|0|1000",
            AllowedClasses = "1|2",
            FinalRewardItemTemplates = "reward_ring",
        };

        private static ClassicQuests.QuestInfo Info(params ClassicQuests.StepInfo[] steps)
        {
            var info = new ClassicQuests.QuestInfo();
            info.Steps.Add(null);
            info.Steps.AddRange(steps);
            return info;
        }

        private static AutonomousBotQuests.Entry Entry(BotQuestPlan plan, eRealm realm = eRealm.Albion, int hardest = 20) => new(plan, realm, hardest);

        #region Planning

        [Test]
        public void ClassicStepsCompileToTheirActions()
        {
            BotQuestPlan plan = AutonomousBotQuestPlanner.Build(
                Row("6|0|8|3", "Archibald;1|ebony fellwood;1||Lady Nimue;1", "|cq_heart;50|cq_lisreen|cq_lisreen",
                    source: "SEARCH;3;;1;1000;2000;500;5"), Info(new(), new(), new(), new()), out string skip);

            Assert.That(skip, Is.Null);
            Assert.That(plan.Steps.Select(step => step.Action), Is.EqualTo(new[]
                { eBotQuestAction.Talk, eBotQuestAction.Kill, eBotQuestAction.Search, eBotQuestAction.HandIn }));
            Assert.That(plan.Steps[1].GivenAtEnd, Is.EqualTo("cq_heart"));
            Assert.That(plan.Steps[1].GivenChance, Is.EqualTo(50));
            Assert.That(plan.Steps[2].Point, Is.EqualTo(new BotQuestPoint(1, 1000, 2000, 0)));
            Assert.That(plan.Steps[2].SearchSeconds, Is.EqualTo(5));
            Assert.That(plan.Steps[3].Finishes, Is.True);
            Assert.That(plan.Steps[3].NeededItem, Is.EqualTo("cq_lisreen"));
            Assert.That(plan.Steps[3].GivenAtStart, Is.Empty, "The search step already gave the item the delivery takes");
            Assert.That(plan.AllowedClasses, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void ADeliveryIsHandedItsItemAtItsStart()
        {
            BotQuestPlan plan = AutonomousBotQuestPlanner.Build(Row("3", "Friend;1", "cq_letter"), Info(new ClassicQuests.StepInfo()), out _);
            Assert.That(plan.Steps[0].GivenAtStart, Is.EqualTo(new[] { "cq_letter" }));
            Assert.That(plan.Steps[0].GivenAtEnd, Is.Empty, "DataQuest gives a delivery's item at the start only");
        }

        [Test]
        public void ACollectStepHuntsItsDropsUntilTheBotHasTheItem()
        {
            var info = Info(new ClassicQuests.StepInfo
            {
                Custom = new ClassicQuests.CustomStep { Drops = [new ClassicQuests.StepDrop { Mob = "giant dragonfly", Item = "cq_wing", Chance = 100 }] },
            });
            BotQuestPlan plan = AutonomousBotQuestPlanner.Build(Row("11", "Nia;51", collects: "cq_wing"), info, out _);
            BotQuestStep step = plan.Steps[0];

            Assert.That(step.Action, Is.EqualTo(eBotQuestAction.HandIn));
            Assert.That(step.HuntForItem(_ => 0), Is.EqualTo("giant dragonfly"));
            Assert.That(step.HuntForItem(_ => 1), Is.Null);
        }

        [TestCase("die_to", AutonomousBotQuestPlanner.SkipStepDieTo)]
        [TestCase("quest", AutonomousBotQuestPlanner.SkipStepNestedQuest)]
        [TestCase("say", AutonomousBotQuestPlanner.SkipStepSay)]
        public void StepsABotCannotDoSkipTheQuestWithAReason(string kind, string reason)
        {
            var info = Info(new ClassicQuests.StepInfo { Custom = new ClassicQuests.CustomStep { Kind = kind } });
            Assert.That(AutonomousBotQuestPlanner.Build(Row("9", ""), info, out string skip), Is.Null);
            Assert.That(skip, Is.EqualTo(reason));
        }

        [Test]
        public void GroupNestedAndSpokenStepsCompileToTheirActions()
        {
            var info = Info(
                new ClassicQuests.StepInfo { Custom = new ClassicQuests.CustomStep { Kind = "group" } },
                new ClassicQuests.StepInfo { Custom = new ClassicQuests.CustomStep { Kind = "quest", Quest = "First|Second|Third", Count = 2 } },
                new ClassicQuests.StepInfo { Custom = new ClassicQuests.CustomStep { Kind = "say", Words = "Rise", NightOnly = true, Region = 1, X = 10, Y = 20, Radius = 400 } });
            BotQuestPlan plan = AutonomousBotQuestPlanner.Build(Row("8|8|9", "||"), info, out string skip);

            Assert.That(skip, Is.Null);
            Assert.That(plan.Steps.Select(step => step.Action), Is.EqualTo(new[] { eBotQuestAction.Grouped, eBotQuestAction.FinishQuests, eBotQuestAction.Say }));
            Assert.That(plan.Steps[1].NestedQuests, Is.EqualTo(new[] { "First", "Second", "Third" }));
            Assert.That(plan.Steps[1].NestedCount, Is.EqualTo(2));
            Assert.That(plan.Steps[2].Words, Is.EqualTo("Rise"));
            Assert.That(plan.Steps[2].NightOnly, Is.True);
            Assert.That(plan.Steps[2].Point, Is.EqualTo(new BotQuestPoint(1, 10, 20, 0)));
        }

        [Test]
        public void ANestedStepNeedsEnoughQuestsBotsCanRun()
        {
            BotQuestPlan Outer(int id, int count) => new()
            {
                Id = id, Name = $"Outer {id}",
                Steps = [new BotQuestStep { Number = 1, Action = eBotQuestAction.FinishQuests, NestedQuests = ["Inner", "Scripted"], NestedCount = count }],
            };
            List<BotQuestPlan> kept = AutonomousBotQuestPlanner.KeepRunnableChains([new BotQuestPlan { Id = 1, Name = "Inner" }, Outer(2, 1), Outer(3, 2)], out int removed);

            Assert.That(kept.Select(plan => plan.Id), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(removed, Is.EqualTo(1), "Two quests asked, and bots can run only one");
        }

        [Test]
        public void ACollectNothingProvidesIsSkipped()
        {
            Assert.That(AutonomousBotQuestPlanner.Build(Row("11", "Nia;51", collects: "cq_rare"), Info(new ClassicQuests.StepInfo()), out string skip), Is.Null);
            Assert.That(skip, Is.EqualTo(AutonomousBotQuestPlanner.SkipItemNoSource));
        }

        [Test]
        public void ASearchWithNoAreaIsSkipped()
        {
            Assert.That(AutonomousBotQuestPlanner.Build(Row("9", ""), Info(new ClassicQuests.StepInfo()), out string skip), Is.Null);
            Assert.That(skip, Is.EqualTo(AutonomousBotQuestPlanner.SkipNoSearchArea));
        }

        [Test]
        public void AChainFallsWithItsFirstLink()
        {
            BotQuestPlan Plan(int id, string name, params string[] deps) => new() { Id = id, Name = name, Dependencies = deps };
            List<BotQuestPlan> kept = AutonomousBotQuestPlanner.KeepRunnableChains(
                [Plan(1, "Free"), Plan(2, "After free", "Free"), Plan(3, "After scripted", "Some scripted quest"), Plan(4, "Third link", "#3"),
                 Plan(5, "Closed by four", "!#4")], out int removed);

            Assert.That(kept.Select(plan => plan.Id), Is.EqualTo(new[] { 1, 2, 5 }));
            Assert.That(removed, Is.EqualTo(2));
        }

        [TestCase(14, false)]
        [TestCase(15, true)]
        [TestCase(21, true)]
        [TestCase(22, false)]
        public void AQuestIsInLevelFromItsMinimumForAFewLevels(int level, bool offered) =>
            Assert.That(AutonomousBotQuestPlanner.InLevelWindow(level, 15, 50), Is.EqualTo(offered));

        [Test]
        public void OffersFollowRealmClassDependenciesAndTheFight()
        {
            BotQuestPlan plan = new() { Id = 7, Name = "Second", MinLevel = 15, MaxLevel = 50, AllowedClasses = [5], Dependencies = ["First"] };
            bool Offer(eRealm realm = eRealm.Albion, int level = 16, int cls = 5, int hardest = 16, string[] done = null, int[] finished = null) =>
                AutonomousBotQuests.Offers(Entry(plan, eRealm.Albion, hardest), realm, level, cls, 0, finished ?? [1], done ?? ["First"], []);

            Assert.That(Offer(), Is.True);
            Assert.That(Offer(realm: eRealm.Midgard), Is.False);
            Assert.That(Offer(cls: 6), Is.False);
            Assert.That(Offer(level: 30), Is.False, "Past the level window");
            Assert.That(Offer(done: []), Is.False, "Its dependency is not finished");
            Assert.That(Offer(finished: [1, 7]), Is.False, "Already done");
            Assert.That(Offer(hardest: 40), Is.False, "A purple monster is no solo errand");
        }

        #endregion

        #region Running

        private static AutonomousBotQuests.Run Accepted(GameBot bot, BotQuestPlan plan)
        {
            AutonomousBotQuests.Run run = AutonomousBotQuests.Begin(bot, plan);
            AutonomousBotQuests.Accept(bot, run);
            return run;
        }

        private static BotQuestPlan KillDeliverPlan() => AutonomousBotQuestPlanner.Build(
            Row("0|2|3", "wolf;0|Keeper;1|Giver;1", "cq_pelt|cq_note|", "|cq_pelt|cq_note"), Info(new(), new(), new()), out _);

        [Test]
        public void AQuestRunsFromKillToHandInAndPaysItsReward()
        {
            Item("cq_pelt");
            Item("cq_note");
            Item("reward_ring");
            TestBot bot = Bot();
            BotQuestPlan plan = KillDeliverPlan();
            AutonomousBotQuests.Run run = Accepted(bot, plan);
            Assert.That(run.Current.Action, Is.EqualTo(eBotQuestAction.Kill));

            AutonomousBotQuests.OnKilled(bot, Mob("somebody else"));
            Assert.That(run.Step, Is.EqualTo(1), "Another monster does not count");
            AutonomousBotQuests.OnKilled(bot, Mob("WOLF"));
            Assert.That(run.Step, Is.EqualTo(2));
            Assert.That(AutonomousBotQuests.Carried(bot, "cq_pelt"), Is.EqualTo(1));
            Assert.That(AutonomousBotQuests.Carried(bot, "cq_note"), Is.EqualTo(1), "Delivery step hands its item when it begins");

            Assert.That(AutonomousBotQuests.CompleteStep(bot, run), Is.True);
            Assert.That(AutonomousBotQuests.Carried(bot, "cq_pelt"), Is.Zero, "Handed in");
            Assert.That(AutonomousBotQuests.CompleteStep(bot, run), Is.True);

            Assert.That(AutonomousBotQuests.Carried(bot, "cq_note"), Is.Zero);
            Assert.That(AutonomousBotQuests.Carried(bot, "reward_ring"), Is.EqualTo(1));
            Assert.That(bot.Experience, Is.EqualTo(1000), "The final step's real experience");
            Assert.That(AutonomousBotQuests.Current(bot), Is.Null);
            Assert.That(run.Record.Finished, Is.EqualTo(1));
            Assert.That(run.Record.Active, Is.False);
        }

        [Test]
        public void AStepWhoseItemDidNotDropStaysOpen()
        {
            Item("cq_pelt");
            TestBot bot = Bot();
            BotQuestPlan plan = AutonomousBotQuestPlanner.Build(Row("0|1", "wolf;0|bear;0", "cq_pelt;1|", ""), Info(new(), new()), out _);
            AutonomousBotQuests.Run run = Accepted(bot, plan);

            // The drop is a real 1% roll, so the kill it comes on is not known (it came on the first one once, DR-255).
            // What must hold on every kill: the step is done exactly when the pelt is in the pack.
            int kills = 0;
            while (run.Step == 1 && kills < 4000)
            {
                kills++;
                TestMob wolf = Mob("wolf");
                AutonomousBotQuests.OnKilled(bot, wolf);
                run.LastKillId = null;
                Assert.That(run.Step == 2, Is.EqualTo(AutonomousBotQuests.Carried(bot, "cq_pelt") == 1),
                    $"kill {kills}: the step closes only with the pelt");
            }
            Assert.That(run.Step, Is.EqualTo(2), "4000 kills at 1% bring the pelt");
        }

        [Test]
        public void OneKillCountsOnce()
        {
            TestBot bot = Bot();
            BotQuestPlan plan = AutonomousBotQuestPlanner.Build(Row("0|0|1", "wolf;0|wolf;0|wolf;0"), Info(new(), new(), new()), out _);
            AutonomousBotQuests.Run run = Accepted(bot, plan);
            TestMob wolf = Mob("wolf");

            AutonomousBotQuests.OnKilled(bot, wolf);
            AutonomousBotQuests.OnKilled(bot, wolf);
            Assert.That(run.Step, Is.EqualTo(2));
        }

        [Test]
        public void GivingUpTakesTheErrandItemsAndWaitsBeforeARetry()
        {
            Item("cq_note");
            TestBot bot = Bot();
            BotQuestPlan plan = AutonomousBotQuestPlanner.Build(Row("3", "Friend;1", "cq_note"), Info(new ClassicQuests.StepInfo()), out _);
            AutonomousBotQuests.Run run = Accepted(bot, plan);
            Assert.That(AutonomousBotQuests.Carried(bot, "cq_note"), Is.EqualTo(1));

            for (int i = 0; i < AutonomousBotQuests.MaximumStepFailures; i++)
                AutonomousBotQuests.NoteStepFailure(bot, "No road");

            Assert.That(AutonomousBotQuests.Carried(bot, "cq_note"), Is.Zero);
            Assert.That(run.Record.Active, Is.False);
            Assert.That(run.Record.Abandoned, Is.EqualTo(1));
            Assert.That(run.Record.RetryUtc, Is.Not.Empty);
        }

        [TestCase("No live Zrit-Kla", eBotQuestPark.Respawn)]
        [TestCase("Keeper was not there", eBotQuestPark.Respawn)]
        [TestCase("Route blocked by bandit leader", eBotQuestPark.Route)]
        [TestCase("Defeated on the way to wolf", eBotQuestPark.Defeat)]
        [TestCase("No legal region route to 51", eBotQuestPark.None)]
        [TestCase("The step ran past its time", eBotQuestPark.None)]
        public void FailuresThatPassAreSetAsideAndTheRestCount(string reason, eBotQuestPark kind) =>
            Assert.That(AutonomousBotQuests.Classify(reason), Is.EqualTo(kind));

        [Test]
        public void AMissingNamedMonsterSetsTheQuestAsideUntilItsRespawn()
        {
            TestBot bot = Bot();
            AutonomousBotQuests.Run run = Accepted(bot, KillDeliverPlan());

            AutonomousBotQuests.NoteStepFailure(bot, "No live wolf");

            Assert.That(run.Record.Active, Is.True, "The bot keeps the quest");
            Assert.That(run.Record.Parks, Is.EqualTo(1));
            Assert.That(AutonomousBotQuests.Parked(bot, run, DateTime.UtcNow), Is.True);
            Assert.That(AutonomousBotQuests.Parked(bot, run, DateTime.UtcNow.AddMinutes(13)), Is.False, "Back after the respawn time");
            Assert.That(run.Record.ParkedUntilUtc, Is.Empty);
            Assert.That(run.Record.Parks, Is.EqualTo(1), "Parks count until the step moves on");
        }

        [Test]
        public void TimeAtAnotherCampIsNotTheStepsTime()
        {
            var run = new AutonomousBotQuests.Run { StepStartedTick = 0 };
            long later = AutonomousBotQuests.StepTimeoutMilliseconds + 60_000;
            Assert.That(AutonomousBotQuests.StepTimedOut(run, later), Is.True, "a step left untouched runs out");

            AutonomousBotQuests.HoldStepClock(run, later);   // still finishing the other camp at that moment
            Assert.That(AutonomousBotQuests.StepTimedOut(run, later + 60_000), Is.False, "the clock starts when the bot takes the step up");
            Assert.That(AutonomousBotQuests.StepTimedOut(run, later + AutonomousBotQuests.StepTimeoutMilliseconds), Is.True);
        }

        [Test]
        public void ADefeatWaitsForALevelOrTwoHours()
        {
            TestBot bot = Bot();
            AutonomousBotQuests.Run run = Accepted(bot, KillDeliverPlan());

            AutonomousBotQuests.NoteStepFailure(bot, "Defeated on the way to wolf");
            Assert.That(AutonomousBotQuests.Parked(bot, run, DateTime.UtcNow.AddMinutes(30)), Is.True);
            bot.Level++;
            Assert.That(AutonomousBotQuests.Parked(bot, run, DateTime.UtcNow), Is.False, "A level stronger, the bot goes back");
        }

        [Test]
        public void AQuestSetAsideTooOftenIsGivenUp()
        {
            TestBot bot = Bot();
            AutonomousBotQuests.Run run = Accepted(bot, KillDeliverPlan());

            for (int i = 0; i < AutonomousBotQuests.MaximumParks; i++)
            {
                AutonomousBotQuests.NoteStepFailure(bot, "No live wolf");
                Assert.That(run.Record.Active, Is.True);
                AutonomousBotQuests.Parked(bot, run, DateTime.UtcNow.AddHours(1));
            }
            AutonomousBotQuests.NoteStepFailure(bot, "No live wolf");

            Assert.That(run.Record.Active, Is.False);
            Assert.That(run.Record.LastReason, Is.EqualTo("No live wolf"));
            Assert.That(AutonomousBotQuests.OutcomeOf(run.Plan.Id).Abandoned, Is.EqualTo(1));
        }

        [Test]
        public void NightWordsWaitWithoutCountingAgainstTheQuest()
        {
            TestBot bot = Bot();
            AutonomousBotQuests.Run run = Accepted(bot, KillDeliverPlan());
            for (int i = 0; i < AutonomousBotQuests.MaximumParks + 2; i++)
                Assert.That(AutonomousBotQuests.Park(bot, run, eBotQuestPark.Night, "Night only"), Is.True);
            Assert.That(run.Record.Parks, Is.Zero);
        }

        [Test]
        public void QuestsEveryBotFailsAreTakenRarely()
        {
            Assert.That(AutonomousBotQuests.SuccessWeight(0, 0), Is.EqualTo(0.5));
            Assert.That(AutonomousBotQuests.SuccessWeight(8, 0), Is.GreaterThan(0.85));
            Assert.That(AutonomousBotQuests.SuccessWeight(1, 9), Is.LessThan(0.2));
            Assert.That(AutonomousBotQuests.SuccessWeight(0, AutonomousBotQuests.FailingQuestAbandons), Is.EqualTo(AutonomousBotQuests.FailingQuestWeight));
            Assert.That(AutonomousBotQuests.WeightedIndex([0.02, 0.9, 0.5], 1.42, 0.01), Is.EqualTo(0));
            Assert.That(AutonomousBotQuests.WeightedIndex([0.02, 0.9, 0.5], 1.42, 0.5), Is.EqualTo(1));
            Assert.That(AutonomousBotQuests.WeightedIndex([0.02, 0.9, 0.5], 1.42, 0.99), Is.EqualTo(2));
        }

        [Test]
        public void WhatBotsMadeOfAQuestSurvivesARestart()
        {
            AutonomousBotQuests.Load([
                new OfflineBotQuestRecord { BotId = 1, QuestId = 5, Finished = 2 },
                new OfflineBotQuestRecord { BotId = 2, QuestId = 5, Abandoned = 3, LastReason = "No live wolf" },
            ]);
            AutonomousBotQuests.Outcome outcome = AutonomousBotQuests.OutcomeOf(5);
            Assert.That((outcome.Finished, outcome.Abandoned, outcome.LastReason), Is.EqualTo((2, 3, "No live wolf")));
        }

        [Test]
        public void ANestedQuestRunsAndHandsTheBotBackToTheOuterQuest()
        {
            BotQuestPlan inner = AutonomousBotQuestPlanner.Build(Row("1", "wolf;0"), Info(new ClassicQuests.StepInfo()), out _);
            inner = new BotQuestPlan { Id = 30, Name = "Inner", GiverName = "Giver", MinLevel = 1, MaxLevel = 50, Steps = inner.Steps };
            var outer = new BotQuestPlan
            {
                Id = 31, Name = "Outer", GiverName = "Giver", MinLevel = 1, MaxLevel = 50,
                Steps =
                [
                    new BotQuestStep { Number = 1, Action = eBotQuestAction.FinishQuests, NestedQuests = ["Inner"], NestedCount = 1 },
                    new BotQuestStep { Number = 2, Action = eBotQuestAction.Talk, TargetName = "Giver", Finishes = true },
                ],
            };
            AutonomousBotQuests.SetCatalogForTests([Entry(inner), Entry(outer)], [new("Giver", 1, 0, 0, 0, eRealm.Albion, 50, false), new("wolf", 1, 0, 0, 0, eRealm.None, 20, true)]);
            TestBot bot = Bot();
            AutonomousBotQuests.Run outerRun = Accepted(bot, outer);
            Assert.That(AutonomousBotQuests.NestedDone(bot, outerRun.Current), Is.False);

            BotQuestPlan picked = AutonomousBotQuests.PickNested(bot, outerRun.Current, _ => true);
            Assert.That(picked, Is.SameAs(inner));
            AutonomousBotQuests.Run innerRun = AutonomousBotQuests.Begin(bot, picked, outer.Id);
            Assert.That(AutonomousBotQuests.Current(bot), Is.SameAs(innerRun));
            AutonomousBotQuests.Accept(bot, innerRun);
            AutonomousBotQuests.OnKilled(bot, Mob("wolf"));

            Assert.That(innerRun.Record.Finished, Is.EqualTo(1));
            AutonomousBotQuests.Run back = AutonomousBotQuests.Current(bot);
            Assert.That(back.Plan, Is.SameAs(outer), "The outer quest is the bot's again");
            Assert.That(AutonomousBotQuests.NestedDone(bot, back.Current), Is.True);
            Assert.That(AutonomousBotQuests.PickNested(bot, back.Current, _ => true), Is.Null, "Nothing left to run");
        }

        [Test]
        public void AnItemTheQuestNeedsIsHeldBack()
        {
            Item("cq_note");
            Item("loot");
            TestBot bot = Bot();
            BotQuestPlan plan = AutonomousBotQuestPlanner.Build(Row("3", "Friend;1", "cq_note"), Info(new ClassicQuests.StepInfo()), out _);
            Accepted(bot, plan);
            DbInventoryItem note = bot.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack).First();

            Assert.That(AutonomousBotQuests.IsHeldForQuest(bot, note), Is.True);
            Assert.That(AutonomousBotQuests.IsHeldForQuest(bot, GameInventoryItem.Create(Templates["loot"])), Is.False);
            Assert.That(AutonomousBotQuests.IsHeldForQuest(Bot("Other bot"), note), Is.False);
        }

        [Test]
        public void AWalkthroughsLevelShareBeatsAFixedReward() =>
            Assert.That(ClassicQuests.ScaleXP(0.1, 1000, 50), Is.EqualTo(100));

        #endregion

        #region Quest monsters

        [Test]
        public void QuestMonstersOpenOnlyToTheBotOnThatStep()
        {
            TestBot hunter = Bot("Hunter"), other = Bot("Other");
            Accepted(hunter, AutonomousBotQuestPlanner.Build(Row("1", "Fjall;1"), Info(new ClassicQuests.StepInfo()), out _));
            TestMob fjall = Mob("Fjall");
            fjall.TempProperties.SetProperty(ClassicQuests.EventSpawnProperty, hunter.Name);

            Assert.That(AutonomousAuditedCampPolicy.IsBotExcludedNpc(fjall), Is.True, "Every other caller keeps the rule");
            Assert.That(AutonomousAuditedCampPolicy.IsBotExcludedNpc(fjall, hunter), Is.False);
            Assert.That(AutonomousAuditedCampPolicy.IsBotExcludedNpc(fjall, other), Is.True);
            Assert.That(AutonomousAuditedCampPolicy.IsBotExcludedNpc(Mob("Another monster").WithEventFor(hunter), hunter), Is.True,
                "Not the monster its step wants");
        }

        [Test]
        public void AnEventMonsterOpensOnlyToTheBotItCameFor()
        {
            TestBot hunter = Bot("Hunter"), rival = Bot("Rival");
            BotQuestPlan plan = AutonomousBotQuestPlanner.Build(Row("1", "Shade;1"), Info(new ClassicQuests.StepInfo()), out _);
            Accepted(hunter, plan);
            Accepted(rival, plan);
            TestMob shade = Mob("Shade").WithEventFor(hunter);

            Assert.That(AutonomousBotQuests.MayHunt(hunter, shade), Is.True);
            Assert.That(AutonomousBotQuests.MayHunt(rival, shade), Is.False);
        }

        [Test]
        public void AnAuditedSpawnStaysShutEvenToABotOnThatStep()
        {
            TestBot hunter = Bot("Hunter");
            Accepted(hunter, AutonomousBotQuestPlanner.Build(Row("1", "hungry shriller;1"), Info(new ClassicQuests.StepInfo()), out _));
            TestMob shriller = Mob("hungry shriller", "c0502019-4ad3-41e7-9cee-eb68fcaba396");

            Assert.That(AutonomousAuditedCampPolicy.IsBotExcludedNpc(shriller, hunter), Is.True);
        }

        #endregion

        #region Crafting

        private static Recipe MakeRecipe(eCraftingSkill skill, int level, int realm = 0, long price = 10, int model = 519, string id = null, bool unique = false)
        {
            DbItemTemplate product = Item(id ?? $"product_{level}_{Guid.NewGuid():N}", 100);
            product.Realm = realm;
            product.Object_Type = (int)eObjectType.SlashingWeapon;
            DbItemTemplate material = Item("bars_" + price, (int)price, 1, 100, model);
            return new Recipe(product, [new Ingredient(2, material)], skill, level, !unique);
        }

        private static AbstractCraftingSkill Weaponcraft => CraftingMgr.getSkillbyEnum(eCraftingSkill.WeaponCrafting);

        [Test]
        public void SkillsKeepThePlayersFormatAndThePrimaryCraftFirst()
        {
            string saved = AutonomousBotCrafting.Join(eCraftingSkill.WeaponCrafting,
                [eCraftingSkill.Tailoring, eCraftingSkill.WeaponCrafting, eCraftingSkill.MetalWorking]);

            Assert.That(saved, Is.EqualTo("1|1;11|1;6|1"));
            Assert.That(AutonomousBotCrafting.Primary(saved), Is.EqualTo(eCraftingSkill.WeaponCrafting));
            Assert.That(AutonomousBotCrafting.SkillValue(saved, eCraftingSkill.MetalWorking), Is.EqualTo(1));
            Assert.That(AutonomousBotCrafting.SkillValue(saved, eCraftingSkill.Alchemy), Is.Zero);
            Assert.That(AutonomousBotCrafting.SerializeSkills(AutonomousBotCrafting.ParseSkills(saved)), Is.EqualTo(saved));
            Assert.That(AutonomousBotCrafting.ParseSkills("not|a|skill;;99|5;1|x"), Is.Empty);
        }

        [Test]
        public void GainsAddAPointAndSubSkillsStopAtTheirCap()
        {
            string saved = "1|500;6|1300";
            Assert.That(AutonomousBotCrafting.Gain(saved, eCraftingSkill.WeaponCrafting, true), Is.EqualTo("1|501;6|1300"));
            Assert.That(AutonomousBotCrafting.Gain(saved, eCraftingSkill.MetalWorking, false), Is.EqualTo(saved));
            Assert.That(AutonomousBotCrafting.Gain(saved, eCraftingSkill.ClothWorking, false), Is.EqualTo(saved));
        }

        [Test]
        public void ClassesFollowCraftsThatSuitThem()
        {
            Assert.That(AutonomousBotCrafting.ProfessionFor(eCharacterClass.Ranger, eClassType.Hybrid, 4), Is.EqualTo(eCraftingSkill.Fletching));
            Assert.That(AutonomousBotCrafting.ProfessionFor(eCharacterClass.Eldritch, eClassType.ListCaster, 4), Is.EqualTo(eCraftingSkill.Tailoring));
            Assert.That(AutonomousBotCrafting.ProfessionFor(eCharacterClass.Hero, eClassType.PureTank, 4), Is.EqualTo(eCraftingSkill.WeaponCrafting));
            Assert.That(AutonomousBotCrafting.ProfessionFor(eCharacterClass.Hero, eClassType.PureTank, 5), Is.EqualTo(eCraftingSkill.ArmorCrafting));
        }

        [Test]
        public void StationsFollowTheCraftsToolChecks()
        {
            Assert.That(AutonomousBotCrafting.StationFor(eCraftingSkill.WeaponCrafting, (int)eObjectType.Sword, []), Is.EqualTo("forge"));
            Assert.That(AutonomousBotCrafting.StationFor(eCraftingSkill.Tailoring, (int)eObjectType.Cloth, [522]), Is.Null);
            Assert.That(AutonomousBotCrafting.StationFor(eCraftingSkill.Tailoring, (int)eObjectType.Studded, [521, 519]), Is.EqualTo("forge"));
            Assert.That(AutonomousBotCrafting.StationFor(eCraftingSkill.Fletching, (int)eObjectType.Longbow, []), Is.EqualTo("lathe"));
            Assert.That(AutonomousBotCrafting.StationFor(eCraftingSkill.Fletching, (int)eObjectType.Arrow, []), Is.Null);
        }

        [Test]
        public void TheRecipeChosenTeachesBestAndCanBeMade()
        {
            string saved = "1|100;6|100";
            Recipe orange = MakeRecipe(eCraftingSkill.WeaponCrafting, 110);
            Recipe grey = MakeRecipe(eCraftingSkill.WeaponCrafting, 15);
            Recipe impossible = MakeRecipe(eCraftingSkill.WeaponCrafting, 200);
            Recipe midgard = MakeRecipe(eCraftingSkill.WeaponCrafting, 105, (int)eRealm.Midgard);
            Recipe notSold = MakeRecipe(eCraftingSkill.WeaponCrafting, 112);

            AutonomousBotCrafting.Choice choice = AutonomousBotCrafting.ChooseRecipe([grey, impossible, midgard, notSold, orange],
                Weaponcraft, saved, eRealm.Albion, 1_000, recipe => recipe != notSold);

            Assert.That(choice.Recipe, Is.SameAs(orange));
            Assert.That(choice.Make, Is.EqualTo(84));
            Assert.That(choice.Gain, Is.EqualTo(55));
        }

        [Test]
        public void ASecondarySkillHoldsBackARecipe()
        {
            Recipe recipe = MakeRecipe(eCraftingSkill.WeaponCrafting, 150);
            Assert.That(AutonomousBotCrafting.MeetsSecondaryRequirement(Weaponcraft, recipe, "1|140;6|80"), Is.False);
            Assert.That(AutonomousBotCrafting.MeetsSecondaryRequirement(Weaponcraft, recipe, "1|140;6|90"), Is.True);
        }

        [Test]
        public void ARecipeTheBotCannotPayForIsNotChosen()
        {
            Recipe dear = MakeRecipe(eCraftingSkill.WeaponCrafting, 110, price: 100);
            Assert.That(AutonomousBotCrafting.ChooseRecipe([dear], Weaponcraft, "1|100;6|100", eRealm.Albion, 199, _ => true), Is.Null);
            Assert.That(AutonomousBotCrafting.ChooseRecipe([dear], Weaponcraft, "1|100;6|100", eRealm.Albion, 200, _ => true), Is.Not.Null);
        }

        [Test]
        public void ShoppingCountsWhatIsCarriedAndBuysWholePacks()
        {
            DbItemTemplate bars = Item("bars", 7, 5);
            DbItemTemplate thread = Item("thread", 3);
            Recipe recipe = new(Item("sword"), [new Ingredient(4, bars), new Ingredient(1, thread)], eCraftingSkill.WeaponCrafting, 15, true);

            List<AutonomousBotCrafting.Purchase> list = AutonomousBotCrafting.Shopping(recipe, 3, item => item == bars ? 3 : 3);

            Assert.That(list, Has.Count.EqualTo(1), "Three threads are in the bag already");
            Assert.That(list[0].Material, Is.SameAs(bars));
            Assert.That(list[0].Packs, Is.EqualTo(2), "Nine more bars come in packs of five");
            Assert.That(list[0].Copper, Is.EqualTo(14));
        }

        [Test]
        public void ASessionSpendsAShareOfTheCoinAndHasAnEnd()
        {
            Recipe recipe = MakeRecipe(eCraftingSkill.WeaponCrafting, 15, price: 100);
            Assert.That(AutonomousBotCrafting.CopiesFor(recipe, 800), Is.EqualTo(1));
            Assert.That(AutonomousBotCrafting.CopiesFor(recipe, 799), Is.Zero);
            Assert.That(AutonomousBotCrafting.CopiesFor(recipe, 1_000_000), Is.EqualTo(AutonomousBotCrafting.CraftsPerSession));
        }

        [Test]
        public void TheTripRollFollowsTheEnginesRuleAndTheLevelFloor()
        {
            Assert.That(AutonomousBotCrafting.WantsTrip(false, 4, 0, 0), Is.False, "Too low a level");
            Assert.That(AutonomousBotCrafting.WantsTrip(false, 10, 0, 0.09), Is.True);
            Assert.That(AutonomousBotCrafting.WantsTrip(false, 10, 0, 0.11), Is.False);
            Assert.That(AutonomousBotCrafting.WantsTrip(true, 10, 0, 0.14), Is.True);
            Assert.That(AutonomousBotCrafting.WantsTrip(true, 10, 0, 0.155), Is.False);
            Assert.That(AutonomousBotCrafting.WantsTrip(true, 10, 3, 0.155), Is.True, "Three stacks of materials: the engine's 16%");
            Assert.That(AutonomousBotDecisionEngine.RollsCraft(false, 9, 0), Is.False);
        }

        [Test]
        public void AnAttemptUsesRealMaterialsAndMakesARealItem()
        {
            Recipe recipe = MakeRecipe(eCraftingSkill.WeaponCrafting, 15, id: "bronze_sword", unique: true);
            AutonomousBotCrafting.SetCatalogForTests([recipe], eCraftingSkill.WeaponCrafting);
            TestBot bot = Bot("Smith", "1|30;6|30");
            DbItemTemplate bars = recipe.Ingredients[0].Material;
            Assert.That(bot.Inventory.AddTemplate(GameInventoryItem.Create(bars), 4, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack), Is.True);
            var session = new AutonomousBotCrafting.Session(recipe, eCraftingSkill.WeaponCrafting, 2);

            DbInventoryItem first = AutonomousBotCrafting.Attempt(bot, session, out bool stop);

            Assert.That(stop, Is.False);
            Assert.That(first, Is.Not.Null, "A blue recipe never fails");
            Assert.That(first.IsCrafted, Is.True);
            Assert.That(first.Creator, Is.EqualTo("Smith"));
            Assert.That(first.Quality, Is.InRange(96, 100));
            Assert.That(bot.Inventory.AllItems.Single(item => item.Id_nb == bars.Id_nb).Count, Is.EqualTo(2), "Two bars used, two left");

            Assert.That(AutonomousBotCrafting.Attempt(bot, session, out stop), Is.Not.Null);
            Assert.That(bot.Inventory.AllItems.Any(item => item.Id_nb == bars.Id_nb), Is.False);
            Assert.That(AutonomousBotCrafting.Attempt(bot, session, out stop), Is.Null);
            Assert.That(stop, Is.True, "Out of materials");
        }

        [Test]
        public void SkillRisesWithWorkAndOnlyEverOnThePlayersTable()
        {
            Recipe recipe = MakeRecipe(eCraftingSkill.WeaponCrafting, 15, id: "copper_sword");
            TestBot bot = Bot("Smith", "1|30;6|30");
            DbItemTemplate bars = recipe.Ingredients[0].Material;
            var session = new AutonomousBotCrafting.Session(recipe, eCraftingSkill.WeaponCrafting, 1);
            for (int i = 0; i < 12 && AutonomousBotCrafting.SkillValue(bot.PersistentRecord.SerializedCraftingSkills, eCraftingSkill.WeaponCrafting) == 30; i++)
            {
                bot.Inventory.AddTemplate(GameInventoryItem.Create(bars), 2, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                DbInventoryItem made = AutonomousBotCrafting.Attempt(bot, session, out _);
                bot.Inventory.RemoveItem(made);
            }
            string saved = bot.PersistentRecord.SerializedCraftingSkills;

            Assert.That(AutonomousBotCrafting.SkillValue(saved, eCraftingSkill.WeaponCrafting), Is.GreaterThanOrEqualTo(30));
            Assert.That(AutonomousBotCrafting.SkillValue(saved, eCraftingSkill.MetalWorking), Is.EqualTo(
                30 + (AutonomousBotCrafting.SkillValue(saved, eCraftingSkill.WeaponCrafting) - 30)), "Metal bars train metalworking with it");
        }

        [Test]
        public void OnlyTheBotsOwnMaterialsCountAsMaterials()
        {
            Recipe recipe = MakeRecipe(eCraftingSkill.WeaponCrafting, 15);
            AutonomousBotCrafting.SetCatalogForTests([recipe], eCraftingSkill.WeaponCrafting);
            DbInventoryItem bars = GameInventoryItem.Create(recipe.Ingredients[0].Material);
            DbInventoryItem potions = GameInventoryItem.Create(Item("potion", 1, 1, 50));

            Assert.That(AutonomousBotCrafting.IsMaterialFor(Bot("Smith", "1|30"), bars), Is.True);
            Assert.That(AutonomousBotCrafting.IsMaterialFor(Bot("Smith", "1|30"), potions), Is.False);
            Assert.That(AutonomousBotCrafting.IsMaterialFor(Bot("Unskilled"), bars), Is.False);
        }

        [Test]
        public void OwnWorkIsThePieceTheBotMadeItself()
        {
            TestBot smith = Bot("Smith");
            DbInventoryItem sword = GameInventoryItem.Create(Item("sword"));
            sword.IsCrafted = true;
            sword.Creator = "Smith";
            Assert.That(smith.Inventory.AddItem(eInventorySlot.FirstEmptyBackpack, sword), Is.True);

            Assert.That(AutonomousBotCrafting.IsOwnWork(smith, sword), Is.True);
            Assert.That(AutonomousBotCrafting.UnsoldWork(smith), Is.EqualTo(1));
            Assert.That(AutonomousBotCrafting.IsOwnWork(Bot("Trader"), sword), Is.False, "Bought from another crafter");
            Assert.That(AutonomousBotCrafting.IsOwnWork(smith, GameInventoryItem.Create(Item("found sword"))), Is.False);
        }

        [Test]
        public void ABotsOwnWorkIsPostedWhateverItFetches()
        {
            DbInventoryItem crafted = GameInventoryItem.Create(Item("sword"));
            crafted.IsCrafted = true;
            DbInventoryItem found = GameInventoryItem.Create(Item("old sword"));

            Assert.That(AutonomousBotEconomy.WorthListing(crafted, 1), Is.True);
            Assert.That(AutonomousBotEconomy.WorthListing(found, 1), Is.False);
            Assert.That(AutonomousBotEconomy.WorthListing(found, 50), Is.True);
        }

        #endregion
    }

    internal static class TestMobExtensions
    {
        public static T WithEventFor<T>(this T mob, GameLiving owner) where T : GameNPC
        {
            mob.TempProperties.SetProperty(ClassicQuests.EventSpawnProperty, owner.Name);
            return mob;
        }
    }
}
