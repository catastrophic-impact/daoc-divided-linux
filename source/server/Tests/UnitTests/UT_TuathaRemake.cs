using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerRules;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Tuatha Sovereign remake: the style table, Sidhe Marks, the spell IDs and the Regalia of Tara.</summary>
    [TestFixture, NonParallelizable]
    public class UT_TuathaRemake
    {
        private GameServer _previous;
        private PetTestLanguageScope _language;
        private readonly List<GameLiving> _actors = new();

        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
        }

        private sealed class Pool : GameNPC
        {
            public override byte Level { get; set; } = 50;
            public override int MaxHealth => 1000;
            public override bool IsAlive => true;
            public override int GetModified(eProperty p) => 0;
        }

        private sealed record StyleRow(int index, string line, string name, int level, string open, string after, double growth, string description);

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance;
            _language = new PetTestLanguageScope();
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var actor in _actors)
                TuathaMarks.Clear(actor);
            _actors.Clear();
            GameServer.LoadTestDouble(_previous);
            _language.Dispose();
        }

        private Pool NewPool()
        {
            var pool = new Pool();
            _actors.Add(pool);
            return pool;
        }

        private static List<StyleRow> Styles()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "source", "tools", "sovereign", "tuatha_remake_styles.json")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "tuatha_remake_styles.json not found above the test folder");
            return JsonSerializer.Deserialize<List<StyleRow>>(File.ReadAllText(Path.Combine(dir.FullName, "source", "tools", "sovereign", "tuatha_remake_styles.json")));
        }

        [Test]
        public void StyleTable_ScytheBladesShieldsAndAnEffectForEach()
        {
            var styles = Styles();
            Assert.That(styles, Has.Count.EqualTo(TuathaRemakeStyles.Count));
            Assert.That(styles.GroupBy(s => s.line).ToDictionary(g => g.Key, g => g.Count()),
                Is.EquivalentTo(new Dictionary<string, int> { ["Scythe"] = 20, ["Blades"] = 20, ["Shields"] = 21 }));
            Assert.That(styles.Select(s => s.index), Is.EqualTo(Enumerable.Range(0, TuathaRemakeStyles.Count)));
            foreach (var style in styles)
                Assert.That(MasterLevelAbilities.For(TuathaRemakeStyles.FirstEffectId + style.index)?.Run, Is.Not.Null, style.name);
        }

        [Test]
        public void StyleTable_ChainsFollowEarlierStylesAndBladesReactToEvades()
        {
            var styles = Styles();
            var byName = styles.ToDictionary(s => s.name);
            foreach (var style in styles.Where(s => s.open == "chain"))
            {
                Assert.That(byName[style.after].line, Is.EqualTo(style.line), style.name);
                Assert.That(byName[style.after].level, Is.LessThan(style.level), style.name);
            }
            Assert.That(styles.Single(s => s.name == "Fey Sidestep").open, Is.EqualTo("evade"));
            var slam = styles.Single(s => s.name == "Thornslam");
            Assert.That((slam.line, slam.open, slam.index), Is.EqualTo(("Shields", "anytime", 49)));
        }

        [Test]
        public void Marks_AreEachOwnersOwn_CapAtFiveAndDetonateForTwentyFivePercentEach()
        {
            var me = NewPool();
            var other = NewPool();
            var enemy = NewPool();
            Assert.That(TuathaMarks.Add(me, enemy, 3), Is.EqualTo(3));
            Assert.That(TuathaMarks.Add(me, enemy, 4), Is.EqualTo(5), "five marks at most");
            TuathaMarks.Add(other, enemy, 1);
            Assert.That(TuathaMarks.Count(other, enemy), Is.EqualTo(1), "another Tuatha's marks are separate");
            Assert.That(TuathaMarks.Bonus(me, 4), Is.EqualTo(2.0).Within(1e-9));
            Assert.That(TuathaMarks.Detonate(me, enemy), Is.EqualTo(5));
            Assert.That(TuathaMarks.Count(me, enemy), Is.Zero);
            Assert.That(TuathaMarks.Count(other, enemy), Is.EqualTo(1));
        }

        [Test]
        public void Marks_NeverLandOnYourself()
        {
            var me = NewPool();
            Assert.That(TuathaMarks.Add(me, me, 2), Is.Zero);
        }

        [Test]
        public void MarkingStyles_AddTheirMarksWhenTheEffectRuns()
        {
            var me = NewPool();
            var enemy = NewPool();
            var spell = new Spell(new DbSpell { SpellID = TuathaRemakeStyles.FirstEffectId + 14, Type = "StormSovereignAbility", Target = "Enemy", Duration = 10 }, 50);
            var handler = new StormSovereignHandler(me, spell, new SpellLine("Combat Style Effects", "Combat Style Effects", "", false));
            handler.Ability.Run(handler, enemy);   // Veiled Reaping: 3 marks
            Assert.That(TuathaMarks.Count(me, enemy), Is.EqualTo(3));
        }

        [Test]
        public void SpellIds_DoNotOverlapAnyOtherAbility()
        {
            foreach (var (id, _) in TuathaRemakeSpells.Build())
            {
                Assert.That(TuathaRemakeSpells.Owns(id), Is.True);
                Assert.That(StormRemakeSpells.Owns(id) || PendragonRules.IsRoyalRank(id) || StormRemakeStyles.OwnsEffect(id), Is.False, id.ToString());
                Assert.That(id, Is.Not.InRange(64000, 64572), "Pendragon royal ranks");
                Assert.That(StormSovereignAbilities.Owns(id), Is.True, "royal rules apply");
                Assert.That(DOL.GS.PacketHandler.PacketLib1110.UsesOwnTooltipIdentity(id), Is.True, "own tooltip, not the borrowed icon's");
            }
            for (int i = 0; i < TuathaRemakeStyles.Count; i++)
                Assert.That(DOL.GS.PacketHandler.PacketLib1110.UsesOwnTooltipIdentity(TuathaRemakeStyles.FirstEffectId + i), Is.True);
            Assert.That(TuathaRemakeStyles.FirstEffectId, Is.GreaterThan(StormRemakeStyles.FirstEffectId + StormRemakeStyles.Count));
        }

        [Test]
        public void Spells_OneAbilityKeyAcrossRanks_AndOnlyTheMaintainedOnesToggle()
        {
            for (int key = 0; key < TuathaRemakeSpells.Keys; key++)
            {
                var keys = Enumerable.Range(0, StormSovereignAbilities.Levels.Length)
                    .Select(tier => StormSovereignAbilities.AbilityKey(TuathaRemakeSpells.FirstId + tier * 16 + key)).Distinct();
                Assert.That(keys, Is.EqualTo(new[] { 200 + key }));
                bool toggle = key is TuathaRemakeSpells.WildwoodArmament;   // the courts are timed (owner 2026-11-06)
                Assert.That(StormSovereignAbilities.Toggle(TuathaRemakeSpells.FirstId + key), Is.EqualTo(toggle), $"key {key}");
            }
        }

        [Test]
        public void RegaliaOfTara_IsItsOwnSetBesideTheStormRegalia()
        {
            Assert.That(TaraRegalia.Set.Pieces, Has.Length.EqualTo(7).And.All.StartWith(TaraRegalia.Prefix));
            Assert.That(TaraRegalia.Set.Tiers.Select(t => t.Pieces), Is.EqualTo(new[] { 2, 4, 6, 7 }));
            Assert.That(TaraRegalia.Set.Tiers[2].Mods.Select(m => m.Property), Does.Contain(eProperty.EvadeChance));
            Assert.That(StormRegalia.Pieces, Is.Not.EquivalentTo(TaraRegalia.Set.Pieces));
            Assert.That(TaraRegalia.DefenseMarks(null), Is.EqualTo(1));
            foreach (int id in new[] { TaraRegaliaSpells.EyesOfTheSidhe, TaraRegaliaSpells.MantleOfMists, TaraRegaliaSpells.Briarward, TaraRegaliaSpells.BrambleSnare })
                Assert.That(MasterLevelAbilities.For(id)?.Run, Is.Not.Null, id.ToString());
        }
    }
}
