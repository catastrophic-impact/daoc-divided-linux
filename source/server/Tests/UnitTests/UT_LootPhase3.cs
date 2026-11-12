using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerProperties;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // Loot redesign phase 3 (owner 2026-11-05): a random item's bonus strength is chosen first from level and quality,
    // then split across the lines it rolled. Behind RandomItems.Budget.Enabled in loot-rules.json, off by default.
    [TestFixture, NonParallelizable]
    public class UT_LootPhase3
    {
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
        private static readonly LootRules BudgetOn = new() { RandomItems = new RandomItemLootRules { Budget = new RandomItemBudgetRules { Enabled = true } } };
        private readonly Dictionary<FieldInfo, object> _rogDefaults = new();
        private GameServer _previous;

        [OneTimeSetUp]
        public void InitializeItemFactory()
        {
            foreach (FieldInfo field in typeof(Properties).GetFields(BindingFlags.Public | BindingFlags.Static)
                         .Where(field => field.Name.StartsWith("ROG_")))
            {
                _rogDefaults[field] = field.GetValue(null);
                field.SetValue(null, field.GetCustomAttribute<ServerPropertyAttribute>().DefaultValue);
            }
            var prefixes = (IDictionary)typeof(GeneratedUniqueItem).GetField("hPropertyToMagicPrefix",
                BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            if (prefixes.Count == 0) GeneratedUniqueItem.InitializeHashtables();
        }

        [OneTimeTearDown]
        public void RestoreItemDefaults()
        {
            foreach (var entry in _rogDefaults) entry.Key.SetValue(null, entry.Value);
        }

        [SetUp]
        public void SetUp()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((TestServer)RuntimeHelpers.GetUninitializedObject(typeof(TestServer)));
        }

        [TearDown]
        public void TearDown()
        {
            LootRulesFile.TestOverride = null;
            GameServer.LoadTestDouble(_previous);
        }

        private static double Utility(int[] types, int[] values)
        {
            var item = (GeneratedUniqueItem)RuntimeHelpers.GetUninitializedObject(typeof(GeneratedUniqueItem));
            int[] t = [.. types, .. new int[11 - types.Length]];
            int[] v = [.. values, .. new int[11 - values.Length]];
            item.Bonus1Type = t[0]; item.Bonus1 = v[0]; item.Bonus2Type = t[1]; item.Bonus2 = v[1];
            item.Bonus3Type = t[2]; item.Bonus3 = v[2]; item.Bonus4Type = t[3]; item.Bonus4 = v[3];
            item.Bonus5Type = t[4]; item.Bonus5 = v[4];
            return item.BountyUtility;
        }

        #region Split

        private static readonly Func<int, int> NoCap = _ => int.MaxValue;

        [Test]
        public void SplitHitsTheTargetAndKeepsTheLinesInProportion()
        {
            int[] types = [(int)eProperty.Strength, (int)eProperty.Constitution, (int)eProperty.Resist_Body];
            int[] split = GeneratedUniqueItem.SplitBudget(types, [12, 6, 3], 32, NoCap);
            Assert.That(Utility(types, split), Is.EqualTo(32).Within(1.5));
            Assert.That(split[0], Is.GreaterThan(split[1]), "strength rolled twice constitution, and stays ahead");
        }

        [Test]
        public void SplitCanLowerAsWellAsRaise()
        {
            int[] types = [(int)eProperty.Strength, (int)eProperty.Resist_Heat];
            int[] split = GeneratedUniqueItem.SplitBudget(types, [30, 10], 15, NoCap);
            Assert.That(Utility(types, split), Is.EqualTo(15).Within(1.5));
        }

        [Test]
        public void ACappedLineHandsItsShareToTheOthers()
        {
            int[] types = [(int)eProperty.Strength, (int)eProperty.Resist_Body];
            int[] split = GeneratedUniqueItem.SplitBudget(types, [20, 1], 30, type => type == (int)eProperty.Strength ? 15 : int.MaxValue);
            Assert.That(split[0], Is.EqualTo(15));
            Assert.That(Utility(types, split), Is.EqualTo(30).Within(1.5), "10 from strength, the rest in the resist");
        }

        [Test]
        public void WhenEveryLineIsCappedTheItemStaysBelowTarget()
        {
            int[] types = [(int)eProperty.Strength];
            int[] split = GeneratedUniqueItem.SplitBudget(types, [10], 45, _ => 26);
            Assert.That(split[0], Is.EqualTo(26));
        }

        [Test]
        public void LinesWithoutUtilityAreLeftAlone()
        {
            int[] types = [(int)eProperty.AllFocusLevels, (int)eProperty.Intelligence];
            int[] split = GeneratedUniqueItem.SplitBudget(types, [50, 10], 20, NoCap);
            Assert.That(split[0], Is.EqualTo(50));
            Assert.That(split[1], Is.EqualTo(30), "20 utility of intelligence");
        }

        [Test]
        public void NoLineDropsToZero()
        {
            int[] types = [(int)eProperty.Strength, (int)eProperty.Skill_Slashing];
            int[] split = GeneratedUniqueItem.SplitBudget(types, [40, 1], 10, NoCap);
            Assert.That(split, Is.All.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void LineCapsFollowTheOldRollTimesTheFactor()
        {
            Assert.That(GeneratedUniqueItem.LineCap((int)eProperty.Strength, 51, 1.5), Is.EqualTo(26));
            Assert.That(GeneratedUniqueItem.LineCap((int)eProperty.Resist_Body, 51, 1.5), Is.EqualTo(11));
            Assert.That(GeneratedUniqueItem.LineCap((int)eProperty.Skill_Slashing, 51, 1.5), Is.EqualTo(6));
            Assert.That(GeneratedUniqueItem.LineCap((int)eProperty.MaxHealth, 51, 1.5), Is.EqualTo(77));
            Assert.That(GeneratedUniqueItem.LineCap((int)eProperty.AllFocusLevels, 51, 1.5), Is.EqualTo(int.MaxValue));
        }

        #endregion

        #region Target

        [Test]
        public void QualityRaisesTheTarget()
        {
            RandomItemBudgetRules budget = new();
            double low = budget.Target(51, 95, false, 0), mid = budget.Target(51, 97, false, 0), high = budget.Target(51, 99, false, 0);
            Assert.That(low, Is.LessThan(mid));
            Assert.That(mid, Is.LessThan(high));
            Assert.That(low, Is.EqualTo((51 * 0.88 - 6) * 0.85).Within(0.001));
            Assert.That(high, Is.EqualTo((51 * 0.88 - 6) * 1.05).Within(0.001));
        }

        [Test]
        public void TargetFloorProcAndMax()
        {
            RandomItemBudgetRules budget = new();
            Assert.That(budget.Target(5, 99, false, 0), Is.EqualTo(5 * 1.05).Within(0.001), "low levels aim at the floor");
            Assert.That(budget.Target(51, 99, true, 0), Is.EqualTo((51 * 0.88 - 6) * 1.05 * 0.7).Within(0.001));
            Assert.That(budget.Target(200, 99, false, 0), Is.EqualTo(80));
            Assert.That(budget.Target(51, 95, false, 40), Is.EqualTo(40).Within(0.001), "a promised floor holds");
            Assert.That(budget.Target(20, 95, false, 60), Is.EqualTo((20 * 0.88 - 6) * 1.05).Within(0.001), "but not past the level's best");
        }

        [Test]
        public void BudgetSettingsAreValidated()
        {
            Assert.That(() => LootRules.Parse("""{ "RandomItems": { "Budget": { "Floor": 90, "Max": 80 } } }"""),
                Throws.InstanceOf<System.IO.InvalidDataException>());
            Assert.That(LootRules.Parse("""{ "RandomItems": { "Budget": { "Enabled": true } } }""").RandomItems.Budget.PerLevel, Is.EqualTo(0.88));
        }

        #endregion

        #region Real items

        private static readonly eCharacterClass[] Classes =
            [eCharacterClass.Armsman, eCharacterClass.Cleric, eCharacterClass.Wizard, eCharacterClass.Scout, eCharacterClass.Infiltrator];

        private static List<GeneratedUniqueItem> Build(int count, byte level, int con, int seedClass = 0)
        {
            var items = new List<GeneratedUniqueItem>(count);
            for (int i = 0; i < count; i++)
            {
                GeneratedUniqueItem item = AtlasROGManager.GenerateMonsterLootROG(eRealm.Albion, Classes[(i + seedClass) % Classes.Length], level, false);
                item.GenerateItemQuality(con);
                items.Add(item);
            }
            return items;
        }

        private static bool Magical(GeneratedUniqueItem item) => item.BountyUtility > 0;

        [Test]
        public void BudgetItemsLandOnTheirTarget()
        {
            LootRulesFile.TestOverride = BudgetOn;
            RandomItemBudgetRules budget = BudgetOn.RandomItems.Budget;
            foreach (GeneratedUniqueItem item in Build(300, 51, 0).Where(Magical))
            {
                bool proc = item.ProcSpellID != 0 || item.ProcSpellID1 != 0;
                // monster loot asks for a random floor of level - 15 to level - 20: use the one this item got
                int floor = (int)typeof(GeneratedUniqueItem).GetField("m_utilityMinimum", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(item);
                double target = budget.Target(51, item.Quality, proc, floor);
                Assert.That(item.BountyUtility, Is.LessThanOrEqualTo(target + 5), item.Name);
            }
            double average = Build(300, 51, 0).Where(Magical).Average(item => item.BountyUtility);
            Assert.That(average, Is.EqualTo(budget.Target(51, 97, false, 0)).Within(6), "on average close to a quality 97 target");
        }

        [Test]
        public void BudgetItemsNeverCarryAllSkills()
        {
            LootRulesFile.TestOverride = BudgetOn;
            int[] allSkills = [163, 164, 167, 168, 213];
            foreach (GeneratedUniqueItem item in Build(200, 51, 0))
            {
                int[] types = [item.Bonus1Type, item.Bonus2Type, item.Bonus3Type, item.Bonus4Type, item.Bonus5Type];
                Assert.That(types.Intersect(allSkills), Is.Empty, item.Name);
            }
        }

        [Test]
        public void HigherQualityMeansStrongerBonuses()
        {
            LootRulesFile.TestOverride = BudgetOn;
            List<GeneratedUniqueItem> items = Build(600, 51, 3).Where(Magical).ToList();
            double low = items.Where(i => i.Quality <= 96).Average(i => i.BountyUtility);
            double high = items.Where(i => i.Quality >= 98).Average(i => i.BountyUtility);
            Assert.That(high, Is.GreaterThan(low));
        }

        [Test]
        public void ReRollingQualityReSplitsTheBudget()
        {
            LootRulesFile.TestOverride = BudgetOn;
            GeneratedUniqueItem item = Build(50, 51, 0).First(i => Magical(i) && i.ProcSpellID == 0 && i.ProcSpellID1 == 0);
            typeof(GeneratedUniqueItem).GetField("m_utilityMinimum", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(item, 0);
            for (int i = 0; i < 20; i++)
            {
                item.GenerateItemQuality(3);
                double target = BudgetOn.RandomItems.Budget.Target(51, item.Quality, false, 0);
                Assert.That(item.BountyUtility, Is.LessThanOrEqualTo(target + 5));
                // an item whose lines all sit at their caps cannot reach its target; every other one lands near it
                (int Type, int Value)[] lines = [(item.Bonus1Type, item.Bonus1), (item.Bonus2Type, item.Bonus2), (item.Bonus3Type, item.Bonus3),
                    (item.Bonus4Type, item.Bonus4), (item.Bonus5Type, item.Bonus5)];
                bool roomLeft = lines.Any(l => l.Type != 0 && l.Value < GeneratedUniqueItem.LineCap(l.Type, 51, BudgetOn.RandomItems.Budget.LineCapFactor));
                if (roomLeft)
                    Assert.That(item.BountyUtility, Is.GreaterThanOrEqualTo(target - 5));
            }
        }

        [Test]
        public void WithTheBudgetOffQualityLeavesBonusesAlone()
        {
            GeneratedUniqueItem item = Build(50, 51, 0).First(Magical);
            double before = item.BountyUtility;
            item.GenerateItemQuality(3);
            Assert.That(item.BountyUtility, Is.EqualTo(before));
        }

        // Measurement, not a check: run by name to compare the old roll with the budget (see TestContext output).
        [Test, Explicit]
        public void MeasureOldAgainstBudget()
        {
            foreach (byte level in new byte[] { 10, 20, 35, 51 })
            {
                foreach (bool on in new[] { false, true })
                {
                    LootRulesFile.TestOverride = on ? BudgetOn : null;
                    List<GeneratedUniqueItem> items = Build(3000, level, 0);
                    List<GeneratedUniqueItem> magical = items.Where(Magical).ToList();
                    double[] utilities = magical.Select(i => i.BountyUtility).OrderBy(u => u).ToArray();
                    string byQuality = string.Join(" ", Enumerable.Range(95, 5).Select(q =>
                    {
                        var at = magical.Where(i => i.Quality == q).ToList();
                        return at.Count == 0 ? $"q{q}:-" : $"q{q}:{at.Average(i => i.BountyUtility):0.0}";
                    }));
                    TestContext.Out.WriteLine(
                        $"level {level,2} {(on ? "budget" : "old   ")}: magical {magical.Count * 100.0 / items.Count:0}% " +
                        $"avg {utilities.Average():0.0} p10 {utilities[utilities.Length / 10]:0.0} median {utilities[utilities.Length / 2]:0.0} " +
                        $"p90 {utilities[utilities.Length * 9 / 10]:0.0} max {utilities[^1]:0.0} lines {magical.Average(Lines):0.00} | {byQuality}");
                }
            }
        }

        private static double Lines(GeneratedUniqueItem item) =>
            new[] { item.Bonus1, item.Bonus2, item.Bonus3, item.Bonus4, item.Bonus5 }.Count(v => v != 0);

        #endregion
    }
}
