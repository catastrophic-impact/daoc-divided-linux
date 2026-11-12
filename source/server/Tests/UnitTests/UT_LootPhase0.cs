using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // Loot redesign phase 0 (owner 2026-11-05): the random item trim loop could spin forever, table refreshes stacked
    // duplicate rows, and the new loot simulator must never touch saved data.
    [TestFixture, NonParallelizable]
    public class UT_LootPhase0
    {
        private static readonly MethodInfo CapUtility =
            typeof(GeneratedUniqueItem).GetMethod("CapUtility", BindingFlags.Instance | BindingFlags.NonPublic);

        private static GeneratedUniqueItem Blank(byte level)
        {
            var item = (GeneratedUniqueItem)RuntimeHelpers.GetUninitializedObject(typeof(GeneratedUniqueItem));
            item.Level = level;
            item.Name = "test item";
            return item;
        }

        // Runs CapUtility on a worker so a regression fails the test instead of hanging the run.
        private static void Trim(GeneratedUniqueItem item, int mobLevel)
        {
            Task task = Task.Run(() => CapUtility.Invoke(item, [mobLevel, 1]));
            Assert.That(task.Wait(TimeSpan.FromSeconds(10)), Is.True, "CapUtility did not finish");
        }

        [Test]
        public void LineSixIsReportedAsLineSix()
        {
            GeneratedUniqueItem item = Blank(50);
            item.Bonus1Type = (int)eProperty.Strength;
            item.Bonus1 = 3;
            item.Bonus6Type = (int)eProperty.Strength;
            item.Bonus6 = 30;
            Assert.That(item.GetHighestUtilitySingleLine(), Is.EqualTo(6));
        }

        [Test]
        public void TrimLowersLineSixWhenLineTwoIsEmpty()
        {
            // Line 6 used to be reported as line 2; trimming the empty line 2 never lowered the total.
            GeneratedUniqueItem item = Blank(20);
            item.Bonus6Type = (int)eProperty.Strength;
            item.Bonus6 = 60;
            Trim(item, 20);
            Assert.That(item.Bonus6, Is.LessThan(60));
            Assert.That(item.BountyUtility, Is.LessThanOrEqualTo(15), "a level 20 cap always floors to 15");
        }

        [Test]
        public void TrimLowersArcherySkill()
        {
            // Skill_Archery (115) sat outside the reducer's "< 115" range and could never be cut.
            GeneratedUniqueItem item = Blank(20);
            item.Bonus1Type = (int)eProperty.Skill_Archery;
            item.Bonus1 = 10;
            Trim(item, 20);
            Assert.That(item.Bonus1, Is.LessThan(10));
        }

        [Test]
        public void SimulatorTalliesCoinItemsAndKills()
        {
            var coin = new DbItemTemplate { Name = "small chest", Price = 50 };
            var fang = new DbItemTemplate { Name = "viper fang" };
            int roll = 0;
            LootSimulator.Result result = LootSimulator.Run(() => roll++ % 2 == 0 ? [coin, fang] : [coin], 10);

            Assert.That(result.Kills, Is.EqualTo(10));
            Assert.That(result.KillsWithItem, Is.EqualTo(5));
            Assert.That(result.CoinPerKill, Is.EqualTo(50));
            Assert.That(result.Items["viper fang"], Is.EqualTo(5));
            Assert.That(LootSimulator.Report("viper", "tester", result), Has.Some.Contains("viper fang: 5"));
        }

        [Test]
        public void SimulatorFlagIsOnlySetDuringTheRun()
        {
            bool seen = false;
            LootSimulator.Run(() => { seen = LootSimulator.IsSimulating; return []; }, 1);
            Assert.That(seen, Is.True);
            Assert.That(LootSimulator.IsSimulating, Is.False);
        }

        [Test]
        public void SimulatorKillCountIsClamped()
        {
            Assert.That(LootSimulator.Run(() => [], 0).Kills, Is.EqualTo(1));
            Assert.That(LootSimulator.Run(() => [], LootSimulator.MaxKills + 5).Kills, Is.EqualTo(LootSimulator.MaxKills));
        }

        [Test]
        public void OneTimeDropsStayOutOfASimulation()
        {
            // An uninitialized generator has no table: outside a simulation this would throw inside GenerateLoot.
            var generator = (LootGeneratorOneTimeDrop)RuntimeHelpers.GetUninitializedObject(typeof(LootGeneratorOneTimeDrop));
            LootList loot = null;
            LootSimulator.Run(() => { loot = generator.GenerateLoot(null, null); return []; }, 1);
            Assert.That(loot, Is.Not.Null);
            Assert.That(loot.GetLoot(), Is.Empty);
        }

        #region Refresh

        private sealed class TestServer : GameServer
        {
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
            protected override IObjectDatabase DataBaseImpl => Db;
        }

        // Select* of MobXLootTemplate returns MobXRows; every other select is empty; writes throw.
        public class RowsDb : DispatchProxy
        {
            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (!method.Name.StartsWith("Select") && !method.Name.StartsWith("Find"))
                    throw new InvalidOperationException("loot test attempted a database write: " + method.Name);
                Type result = method.ReturnType;
                if (result.IsGenericType && typeof(IEnumerable).IsAssignableFrom(result))
                {
                    Type element = result.GetGenericArguments()[0];
                    if (element == typeof(DbMobXLootTemplate))
                        return new List<DbMobXLootTemplate>(MobXRows);
                    return Activator.CreateInstance(typeof(List<>).MakeGenericType(element));
                }
                return null;
            }
        }

        private static readonly IObjectDatabase Db = DispatchProxy.Create<IObjectDatabase, RowsDb>();
        private static readonly List<DbMobXLootTemplate> MobXRows = [];

        private sealed class Generator : LootGeneratorTemplate
        {
            public static Dictionary<string, List<DbMobXLootTemplate>> MobX => m_mobXLootTemplates;

            public static void Use(Dictionary<string, Dictionary<string, DbLootTemplate>> loot, Dictionary<string, List<DbMobXLootTemplate>> mobx)
            {
                m_lootTemplates = loot;
                m_mobXLootTemplates = mobx;
            }
        }

        private sealed class Mob : GameNPC
        {
            public override byte Level { get; set; }
        }

        [Test]
        public void RefreshReplacesTheMobsRows()
        {
            GameServer previous = GameServer.Instance;
            GameServer.LoadTestDouble((TestServer)RuntimeHelpers.GetUninitializedObject(typeof(TestServer)));
            try
            {
                MobXRows.Clear();
                MobXRows.Add(new DbMobXLootTemplate { MobName = "ghoulic viper", LootTemplateName = "ghoulic viper", DropCount = 1 });
                MobXRows.Add(new DbMobXLootTemplate { MobName = "ghoulic viper", LootTemplateName = "viper extras", DropCount = 1 });
                Generator.Use(new(), new());

                var generator = (LootGeneratorTemplate)RuntimeHelpers.GetUninitializedObject(typeof(LootGeneratorTemplate));
                var mob = (Mob)RuntimeHelpers.GetUninitializedObject(typeof(Mob));
                typeof(GameLiving).GetField("<TempProperties>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(mob, new PropertyCollection());
                typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(mob, new ArrayList());
                mob.Name = "Ghoulic Viper";

                generator.Refresh(mob);
                generator.Refresh(mob);

                Assert.That(Generator.MobX["ghoulic viper"], Has.Count.EqualTo(2), "two rows, not two per refresh");
            }
            finally
            {
                Generator.Use(null, null);
                MobXRows.Clear();
                GameServer.LoadTestDouble(previous);
            }
        }

        #endregion
    }
}
