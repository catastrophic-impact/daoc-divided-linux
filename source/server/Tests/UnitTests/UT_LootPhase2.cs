using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    // Loot redesign phase 2 (owner 2026-11-05): loot tuning in loot-rules.json. Its defaults are the old constants, so
    // a missing file changes nothing; the owner's chosen 1 in 8 solo rate is RandomItems.ChanceMultiplier 0.5.
    [TestFixture, NonParallelizable]
    public class UT_LootPhase2
    {
        #region Rules file

        [Test]
        public void DefaultsSurviveARoundTrip()
        {
            string json = LootRules.Defaults.ToJson();
            Assert.That(LootRules.Parse(json).ToJson(), Is.EqualTo(json));
        }

        [Test]
        public void APartialFileKeepsTheOtherDefaults()
        {
            LootRules rules = LootRules.Parse("""
                // owner 2026-11-05: about 1 in 8 solo
                { "RandomItems": { "ChanceMultiplier": 0.5 } }
                """);
            Assert.That(rules.RandomItems.ChanceMultiplier, Is.EqualTo(0.5));
            Assert.That(rules.RandomItems.BaseChance, Is.EqualTo(14));
            Assert.That(rules.RandomItems.SoloBonus, Is.EqualTo(10));
            Assert.That(rules.RandomItems.GroupLevelPenalties, Has.Length.EqualTo(2));
            Assert.That(rules.Coin.Multiplier, Is.EqualTo(1.0));
        }

        [TestCase("""{ "RandomItems": { "ChanceMultipler": 0.5 } }""", TestName = "AMisspelledSettingIsRefused")]
        [TestCase("""{ "Version": 2 }""", TestName = "AnUnknownVersionIsRefused")]
        [TestCase("""{ "RandomItems": { "ChanceMultiplier": -1 } }""", TestName = "ANegativeMultiplierIsRefused")]
        [TestCase("""{ "RandomItems": { "BaseChance": 150 } }""", TestName = "AChanceOver100IsRefused")]
        [TestCase("""{ "RandomItems": { "GroupMembersPerDrop": 0 } }""", TestName = "ZeroMembersPerDropIsRefused")]
        [TestCase("""{ "Coin": null }""", TestName = "AMissingSectionIsRefused")]
        [TestCase("", TestName = "AnEmptyFileIsRefused")]
        public void BadFilesAreRefused(string json)
        {
            Assert.That(() => LootRules.Parse(json), Throws.InstanceOf<InvalidDataException>().Or.InstanceOf<System.Text.Json.JsonException>());
        }

        [Test]
        public void TheExampleFileParses()
        {
            DirectoryInfo folder = new(TestContext.CurrentContext.TestDirectory);
            while (folder != null && !File.Exists(Path.Combine(folder.FullName, "docs", "loot-rules.example.json")))
                folder = folder.Parent;
            Assume.That(folder, Is.Not.Null, "docs/loot-rules.example.json is not above the test folder");

            LootRules rules = LootRules.Parse(File.ReadAllText(Path.Combine(folder.FullName, "docs", "loot-rules.example.json")));
            Assert.That(rules.RandomItems.ChanceMultiplier, Is.EqualTo(0.5));
            Assert.That((rules with { RandomItems = rules.RandomItems with { ChanceMultiplier = 1.0 } }).ToJson(),
                Is.EqualTo(LootRules.Defaults.ToJson()), "apart from the multiplier the example holds the defaults");
        }

        [Test]
        public void ScaleAppliesEveryMultiplierAndStaysAPercent()
        {
            RandomItemLootRules rules = new() { ChanceMultiplier = 0.5, NamedChanceMultiplier = 3, FrontierChanceMultiplier = 2 };
            Assert.That(rules.Scale(24, named: false, frontier: false), Is.EqualTo(12));
            Assert.That(rules.Scale(24, named: true, frontier: false), Is.EqualTo(36));
            Assert.That(rules.Scale(24, named: true, frontier: true), Is.EqualTo(72));
            Assert.That(rules.Scale(80, named: true, frontier: true), Is.EqualTo(100));
            Assert.That(rules.Scale(-5, named: false, frontier: false), Is.EqualTo(0));
        }

        [TestCase(20, 0)] [TestCase(27, 0)] [TestCase(28, 3)] [TestCase(41, 6)]
        public void GroupPenaltiesMatchTheOldLevels(int mobLevel, int penalty)
        {
            Assert.That(new RandomItemLootRules().GroupLevelPenalty(mobLevel), Is.EqualTo(penalty));
        }

        #endregion

        #region Reading the file

        private sealed class FileServer : GameServer
        {
            public GameServerConfiguration Config;
            public override GameServerConfiguration Configuration => Config;
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
            protected override IObjectDatabase DataBaseImpl => Db;
        }

        private string _folder;
        private GameServer _previous;

        [SetUp]
        public void SetUp()
        {
            _previous = GameServer.Instance;
            _folder = Path.Combine(Path.GetTempPath(), "loot-rules-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            var config = (GameServerConfiguration)RuntimeHelpers.GetUninitializedObject(typeof(GameServerConfiguration));
            config.RootDirectory = _folder;
            var server = (FileServer)RuntimeHelpers.GetUninitializedObject(typeof(FileServer));
            server.Config = config;
            GameServer.LoadTestDouble(server);
            LootRulesFile.TestOverride = null;
            LootRulesFile.Refresh(true);
        }

        [TearDown]
        public void TearDown()
        {
            LootRulesFile.TestOverride = null;
            GameServer.LoadTestDouble(_previous);
            LootRulesFile.Refresh(true);
            Directory.Delete(_folder, true);
        }

        private string RulesPath => Path.Combine(_folder, LootRules.FileName);

        [Test]
        public void NoFileMeansDefaults()
        {
            Assert.That(LootRulesFile.Refresh(true).ToJson(), Is.EqualTo(LootRules.Defaults.ToJson()));
            Assert.That(LootRulesFile.LastError, Is.Null);
        }

        [Test]
        public void AnEditedFileIsReadAgain()
        {
            File.WriteAllText(RulesPath, """{ "RandomItems": { "ChanceMultiplier": 0.5 } }""");
            Assert.That(LootRulesFile.Refresh(false).RandomItems.ChanceMultiplier, Is.EqualTo(0.5));

            File.WriteAllText(RulesPath, """{ "RandomItems": { "ChanceMultiplier": 0.25 } }""");
            File.SetLastWriteTimeUtc(RulesPath, DateTime.UtcNow.AddMinutes(1));
            Assert.That(LootRulesFile.Refresh(false).RandomItems.ChanceMultiplier, Is.EqualTo(0.25));
        }

        [Test]
        public void ABrokenEditKeepsTheLastGoodRules()
        {
            File.WriteAllText(RulesPath, """{ "RandomItems": { "ChanceMultiplier": 0.5 } }""");
            LootRulesFile.Refresh(true);

            File.WriteAllText(RulesPath, """{ "RandomItems": { "ChanceMultiplier": "half" } }""");
            File.SetLastWriteTimeUtc(RulesPath, DateTime.UtcNow.AddMinutes(1));
            Assert.That(LootRulesFile.Refresh(false).RandomItems.ChanceMultiplier, Is.EqualTo(0.5));
            Assert.That(LootRulesFile.LastError, Is.Not.Null);
        }

        [Test]
        public void DeletingTheFileGoesBackToDefaults()
        {
            File.WriteAllText(RulesPath, """{ "RandomItems": { "ChanceMultiplier": 0.5 } }""");
            LootRulesFile.Refresh(true);
            File.Delete(RulesPath);
            Assert.That(LootRulesFile.Refresh(false).RandomItems.ChanceMultiplier, Is.EqualTo(1.0));
        }

        #endregion

        #region Random items read the rules

        private sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public override byte Level { get; set; }
            public override int EffectiveLevel => Level;
            public override eRealm Realm { get; set; }
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public override ICharacterClass CharacterClass => new ClassEnchanter();
        }

        private sealed class Mob : GameNPC
        {
            public override byte Level { get; set; }
            public override int EffectiveLevel => Level;
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
        }

        // Records each roll's chance and never drops, so no item is built.
        private sealed class ChanceRecorder : ROGMobGenerator
        {
            public readonly List<double> Chances = [];
            protected override bool RollDropChance(double chance) { Chances.Add(chance); return false; }
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
        private static long _nextId = 920000;

        private static void Field(Type type, object target, string name, object value)
        {
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private static T Inert<T>() where T : GameNPC
        {
            T npc = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            Field(typeof(GameLiving), npc, "<TempProperties>k__BackingField", new PropertyCollection());
            Field(typeof(GameNPC), npc, "m_brains", new ArrayList());
            return npc;
        }

        private static Bot NewBot(byte level)
        {
            Bot bot = Inert<Bot>();
            bot.Level = level;
            bot.Realm = eRealm.Hibernia;
            bot.Name = "phase two bot";
            bot.DatabaseID = Interlocked.Increment(ref _nextId);
            bot.InternalID = AutonomousBotEconomy.GetOwnerId(bot.DatabaseID);
            typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot)).SetValue(bot, true);
            Field(typeof(GameNPC), bot, "m_ownBrain", new BotBrain { Body = bot });
            return bot;
        }

        private static Mob NewMob(byte level, string name = "large frog")
        {
            Mob mob = Inert<Mob>();
            mob.Level = level;
            mob.Name = name;
            return mob;
        }

        private static Group GroupOf(params Bot[] bots)
        {
            Group group = new(bots[0]);
            var members = (List<GameLiving>)typeof(Group).GetField("_groupMembers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(group);
            foreach (Bot bot in bots)
            {
                bot.Group = group;
                members.Add(bot);
            }
            return group;
        }

        [Test]
        public void ASoloYellowKillKeepsTheOldChanceByDefault()
        {
            var generator = new ChanceRecorder();
            generator.GenerateLoot(NewMob(10), NewBot(10));
            Assert.That(generator.Chances, Is.EqualTo(new[] { 24.0 }), "14 base + 10 solo");
        }

        [Test]
        public void HalfTheMultiplierHalvesASoloKill()
        {
            LootRulesFile.TestOverride = new LootRules { RandomItems = new RandomItemLootRules { ChanceMultiplier = 0.5 } };
            var generator = new ChanceRecorder();
            generator.GenerateLoot(NewMob(10), NewBot(10));
            Assert.That(generator.Chances, Is.EqualTo(new[] { 12.0 }), "about 1 in 8");
        }

        [Test]
        public void AGroupKillUsesTheLevelPenaltiesAndTheMultiplier()
        {
            LootRulesFile.TestOverride = new LootRules { RandomItems = new RandomItemLootRules { ChanceMultiplier = 0.5 } };
            Bot leader = NewBot(50), member = NewBot(50);
            GroupOf(leader, member);
            var generator = new ChanceRecorder();
            generator.GenerateLoot(NewMob(50), leader);
            Assert.That(generator.Chances, Is.EqualTo(new[] { 4.0, 4.0 }), "(14 - 3 - 3) * 0.5 for each member");
        }

        [Test]
        public void NamedMobsUseTheNamedMultiplier()
        {
            LootRulesFile.TestOverride = new LootRules { RandomItems = new RandomItemLootRules { NamedChanceMultiplier = 2 } };
            var generator = new ChanceRecorder();
            generator.GenerateLoot(NewMob(10, "Grand Frog"), NewBot(10));
            Assert.That(generator.Chances, Is.EqualTo(new[] { 48.0 }));
        }

        #endregion
    }
}
