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
    /// <summary>Storm Sovereign remake: the style table, the remake spell IDs and Tempest charges.</summary>
    [TestFixture, NonParallelizable]
    public class UT_StormRemake
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

        private sealed class Corpse : GameNPC
        {
            public override byte Level { get; set; } = 50;
            public override int MaxHealth => 1000;
            public override bool IsAlive => false;
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
                StormTempest.Reset(actor);
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
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "source", "tools", "sovereign", "storm_remake_styles.json")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "storm_remake_styles.json not found above the test folder");
            string json = File.ReadAllText(Path.Combine(dir.FullName, "source", "tools", "sovereign", "storm_remake_styles.json"));
            return JsonSerializer.Deserialize<List<StyleRow>>(json);
        }

        [Test]
        public void StyleTable_HasTwentyStylesPerLinePlusStormslamAndAnEffectForEach()
        {
            var styles = Styles();
            Assert.That(styles, Has.Count.EqualTo(StormRemakeStyles.Count));
            Assert.That(styles.GroupBy(s => s.line).ToDictionary(g => g.Key, g => g.Count()),
                Is.EquivalentTo(new Dictionary<string, int> { ["Hammer"] = 20, ["Axe"] = 20, ["Shields"] = 21 }));
            Assert.That(styles.Select(s => s.index), Is.EqualTo(Enumerable.Range(0, StormRemakeStyles.Count)));
            foreach (var style in styles)
            {
                MLAbility ability = MasterLevelAbilities.For(StormRemakeStyles.FirstEffectId + style.index);
                Assert.That(ability?.Run, Is.Not.Null, style.name);
            }
        }

        [Test]
        public void StyleTable_EveryChainFollowsAnEarlierStyleOfItsOwnLine()
        {
            var styles = Styles();
            var byName = styles.ToDictionary(s => s.name);
            foreach (var style in styles.Where(s => s.open == "chain"))
            {
                Assert.That(byName.TryGetValue(style.after, out var before), Is.True, style.name);
                Assert.That(before.line, Is.EqualTo(style.line), style.name);
                Assert.That(before.level, Is.LessThan(style.level), style.name);
            }
            Assert.That(styles.Where(s => s.open != "chain").Select(s => s.after), Is.All.Null);
        }

        [Test]
        public void StyleTable_EachLineHasOffensiveAndDefensiveChainsAndASignatureFinisher()
        {
            foreach (var line in Styles().GroupBy(s => s.line))
            {
                Assert.That(line.Count(s => s.open == "anytime"), Is.GreaterThanOrEqualTo(3), line.Key);
                Assert.That(line.Count(s => s.open is "block" or "parry"), Is.GreaterThanOrEqualTo(2), line.Key);
                Assert.That(line.Count(s => s.open == "stealth"), Is.EqualTo(1), line.Key);
                Assert.That(line.Max(s => s.level), Is.EqualTo(48), line.Key);
            }
        }

        [Test]
        public void RemakeSpellIds_DoNotOverlapAnyOtherAbility()
        {
            var ids = StormRemakeSpells.Build().Select(entry => entry.Id).ToList();
            Assert.That(ids, Has.Count.EqualTo(StormSovereignAbilities.Levels.Length * StormRemakeSpells.Keys));
            foreach (int id in ids)
            {
                Assert.That(StormRemakeSpells.Owns(id), Is.True);
                Assert.That(PendragonRules.IsRoyalRank(id), Is.False, $"{id} is a royal rank");
                Assert.That(StormRemakeStyles.OwnsEffect(id), Is.False);
                Assert.That(id, Is.Not.InRange(63000, 63144), "Master Level abilities");
                Assert.That(StormSovereignAbilities.Owns(id), Is.True, "royal damage, Storm Crown and siphon rules apply");
            }
            for (int i = 0; i < StormRemakeStyles.Count; i++)
                Assert.That(StormSovereignAbilities.Owns(StormRemakeStyles.FirstEffectId + i), Is.True);
            Assert.That(StormRemakeSpells.Owns(StormRemakeSpells.FirstId + StormRemakeSpells.Keys), Is.False, "unused key slot");
        }

        [Test]
        public void RemakeSpells_OneAbilityKeyAcrossEveryRank()
        {
            for (int key = 0; key < StormRemakeSpells.Keys; key++)
            {
                var keys = Enumerable.Range(0, StormSovereignAbilities.Levels.Length)
                    .Select(tier => StormSovereignAbilities.AbilityKey(StormRemakeSpells.FirstId + tier * 16 + key)).Distinct().ToList();
                Assert.That(keys, Is.EqualTo(new[] { 100 + key }));
            }
            Assert.That(StormSovereignAbilities.AbilityKey(62400 + 3 * 32 + 7), Is.EqualTo(7), "old royal keys are unchanged");
        }

        [Test]
        public void RemakeSpellsAndStyles_ShowTheirOwnTooltipNotTheBorrowedIcon()
        {
            // Runic Armament borrowed icon 3537 and showed as the stock "Thor's Potence" in the buff bar.
            int armament = StormRemakeSpells.FirstId + 2 * 16 + StormRemakeSpells.RunicArmament;
            foreach (int id in new[] { armament, StormRemakeStyles.FirstEffectId, StormRemakeStyles.FirstEffectId + 59,
                         StormRemakeStyles.FirstEffectId * 1000 + 805, armament * 1000 + 990 })
                Assert.That(DOL.GS.PacketHandler.PacketLib1110.UsesOwnTooltipIdentity(id), Is.True, id.ToString());
        }

        [Test]
        public void RemakeSpells_OnlyRunicArmamentIsAToggle()
        {
            for (int key = 0; key < StormRemakeSpells.Keys; key++)
                Assert.That(StormSovereignAbilities.Toggle(StormRemakeSpells.FirstId + key), Is.EqualTo(key == StormRemakeSpells.RunicArmament), $"key {key}");
        }

        [Test]
        public void Tempest_BuildsToFiveAndFinishersSpendItAll()
        {
            var owner = NewPool();
            StormTempest.Add(owner, 3);
            Assert.That(StormTempest.Charges(owner), Is.EqualTo(3));
            StormTempest.Add(owner, 4);
            Assert.That(StormTempest.Charges(owner), Is.EqualTo(5), "capped at the five orbs");
            Assert.That(StormTempest.Spend(owner), Is.EqualTo(5));
            Assert.That(StormTempest.Charges(owner), Is.EqualTo(0));
            Assert.That(StormTempest.Spend(owner), Is.EqualTo(0), "nothing left to spend");
        }

        [TestCase(0, 1)]   // Thunderstroke
        [TestCase(14, 2)]  // Hidden Thunder
        [TestCase(20, 1)]  // Winterbite
        [TestCase(40, 1)]  // Royal Rebuke
        public void BuildingStyles_AddTempestWhenTheirEffectRuns(int index, int charges)
        {
            var owner = NewPool();
            var enemy = NewPool();
            var spell = new Spell(new DbSpell { SpellID = StormRemakeStyles.FirstEffectId + index, Type = "StormSovereignAbility", Target = "Enemy", Duration = 10 }, 50);
            var handler = new StormSovereignHandler(owner, spell, new SpellLine("Combat Style Effects", "Combat Style Effects", "", false));
            Assert.That(handler.Ability, Is.Not.Null);
            handler.Ability.Run(handler, enemy);
            Assert.That(StormTempest.Charges(owner), Is.EqualTo(charges));
        }

        [TestCase(0, 1)]   // Thunderstroke
        [TestCase(14, 2)]  // Hidden Thunder
        [TestCase(18, 5)]  // Gathering Storm
        [TestCase(1, 0)]   // Shatterguard builds nothing
        public void BuildingStyles_StillAddTempestWhenTheirHitKilledTheTarget(int index, int charges)
        {
            // In game a level 50 one-shot a young lynx with Thunderstroke and gained nothing: the effect needs a living target.
            var owner = NewPool();
            var corpse = new Corpse();
            _actors.Add(corpse);
            var spell = new Spell(new DbSpell { SpellID = StormRemakeStyles.FirstEffectId + index, Type = "StormSovereignAbility", Target = "Enemy", Duration = 10 }, 50);
            var handler = new StormSovereignHandler(owner, spell, new SpellLine("Combat Style Effects", "Combat Style Effects", "", false));
            handler.ApplyEffectOnTarget(corpse);
            Assert.That(StormTempest.Charges(owner), Is.EqualTo(charges));
        }

        [Test]
        public void SpendingFinishers_StillSpendWhenTheirHitKilledTheTarget()
        {
            // Playtest 2026-11-07: Thor's Judgement killed an ettin and neither arced nor spent its 3 charges.
            var owner = NewPool();
            StormTempest.Add(owner, 3);
            var corpse = new Corpse();
            _actors.Add(corpse);
            var spell = new Spell(new DbSpell { SpellID = StormRemakeStyles.FirstEffectId + 2, Type = "StormSovereignAbility", Target = "Enemy", Duration = 10 }, 50);
            var handler = new StormSovereignHandler(owner, spell, new SpellLine("Combat Style Effects", "Combat Style Effects", "", false));
            handler.ApplyEffectOnTarget(corpse);
            Assert.That(StormTempest.Charges(owner), Is.Zero, "the finisher spent its charges");
            Assert.That(StormSovereignHandler.IsSovereignStyleEffect(PendragonRemakeStyles.FirstEffectId), Is.True);
            Assert.That(StormSovereignHandler.IsSovereignStyleEffect(TuathaRemakeStyles.FirstEffectId), Is.True);
        }

        [Test]
        public void TempestPaidSpells_AreInstantAndFreeOnlyWithCharges()
        {
            var owner = NewPool();
            int royalThunder = StormRemakeSpells.FirstId + 7 * 16 + StormRemakeSpells.RoyalThunder;
            var spell = new Spell(new DbSpell { SpellID = royalThunder, Type = "StormSovereignAbility", Target = "Enemy", CastTime = 2.5, Power = 18, Range = 1500 }, 50);
            var handler = new StormSovereignHandler(owner, spell, new SpellLine("Stormforged.Remake05", "Royal Thunder", "StormforgedCareer", true));
            Assert.That(StormRemakeSpells.PaysWithTempest(royalThunder), Is.True);
            Assert.That(handler.PowerCost(owner), Is.GreaterThan(0), "no Tempest: costs power");
            StormTempest.Add(owner, 1);
            Assert.That(handler.CalculateCastingTime(), Is.Zero);
            Assert.That(handler.PowerCost(owner), Is.Zero);
            foreach (int key in new[] { StormRemakeSpells.RoyalDecree, StormRemakeSpells.RallyToTheCrown, StormRemakeSpells.RunicArmament })
                Assert.That(StormRemakeSpells.PaysWithTempest(StormRemakeSpells.FirstId + key), Is.False, $"key {key}");
        }

        [Test]
        public void Tempest_TakeSpendsOnlyWhatItNeeds()
        {
            var owner = NewPool();
            StormTempest.Add(owner, 3);
            Assert.That(StormTempest.Take(owner, 1), Is.EqualTo(1));
            Assert.That(StormTempest.Charges(owner), Is.EqualTo(2));
            Assert.That(StormTempest.Take(owner, 5), Is.EqualTo(2));
            Assert.That(StormTempest.Take(owner, 1), Is.Zero);
        }

        [Test]
        public void Stormslam_IsAnAnytimeShieldStyleThatBuildsTempest()
        {
            var slam = Styles().Single(s => s.name == "Stormslam");
            Assert.That((slam.line, slam.open, slam.index), Is.EqualTo(("Shields", "anytime", 60)));
            Assert.That(StormRemakeStyles.Builds(60), Is.EqualTo(1));
        }

        [Test]
        public void Regalia_AbilitiesAreRegisteredAndShowTheirOwnTooltips()
        {
            foreach (int id in new[] { StormRegaliaSpells.CrownOfTempest, StormRegaliaSpells.Wolfmantle, StormRegaliaSpells.StormguardWard, StormRegaliaSpells.ThunderRebuke })
            {
                Assert.That(MasterLevelAbilities.For(id)?.Run, Is.Not.Null, id.ToString());
                Assert.That(DOL.GS.PacketHandler.PacketLib1110.UsesOwnTooltipIdentity(id), Is.True, id.ToString());
            }
        }

        [Test]
        public void Regalia_CrownOfTempestFillsTheOrbs()
        {
            var owner = NewPool();
            var spell = new Spell(new DbSpell { SpellID = StormRegaliaSpells.CrownOfTempest, Type = "StormSovereignAbility", Target = "Self" }, 50);
            var handler = new StormSovereignHandler(owner, spell, new SpellLine("Item Effects", "Item Effects", "", false));
            handler.Ability.Run(handler, owner);
            Assert.That(StormTempest.Charges(owner), Is.EqualTo(StormTempest.MaxCharges));
        }

        [Test]
        public void Regalia_TiersGrowWithPiecesAndTheFullSetIsSeven()
        {
            Assert.That(StormRegalia.Pieces, Has.Length.EqualTo(7).And.All.StartWith(StormRegalia.Prefix));
            Assert.That(StormRegalia.Tiers.Select(t => t.Pieces), Is.EqualTo(new[] { 2, 4, 6, 7 }));
            Assert.That(StormRegalia.Tiers.All(t => t.Mods.Length > 0), Is.True);
            Assert.That(StormRegalia.DefenseTempest(null), Is.EqualTo(1), "no set, one charge per block or parry");
        }

        [TestCase(eRealm.Albion)]
        [TestCase(eRealm.Midgard)]
        [TestCase(eRealm.Hibernia)]
        public void GroundFields_TakeTheCastersRealmColour(eRealm realm)
        {
            // Storms, wards and runes draw their colour by realm; realm None drew the green Hibernian look for everyone.
            var owner = NewPool();
            owner.Realm = realm;
            var spell = new Spell(new DbSpell { SpellID = 62400 + 7 * 32 + 5, Type = "StormSovereignAbility", Target = "Self", Radius = 350, Duration = 30 }, 50);
            var handler = new StormSovereignHandler(owner, spell, new SpellLine("Stormforged.Sovereign05", "Eye of the Storm", "StormforgedCareer", true));
            var field = new MasterLevelField(owner, handler, new MasterLevelFieldOptions { Model = 2583 });
            Assert.That(field.Realm, Is.EqualTo(realm));
            Assert.That(field.Flags.HasFlag(GameNPC.eFlags.PEACE), Is.True, "still untargetable");
        }

        [Test]
        public void Tempest_EachChargeAddsTwentyPercent()
        {
            Assert.That(StormTempest.Bonus(0), Is.EqualTo(1.0));
            Assert.That(StormTempest.Bonus(3), Is.EqualTo(1.6).Within(1e-9));
            Assert.That(StormTempest.Bonus(5), Is.EqualTo(2.0).Within(1e-9));
            Assert.That(StormTempest.Bonus(9), Is.EqualTo(2.0).Within(1e-9));
        }

        [Test]
        public void Tempest_SetFillsAndClearEmpties()
        {
            var owner = NewPool();
            StormTempest.Set(owner, StormTempest.MaxCharges);
            Assert.That(StormTempest.Charges(owner), Is.EqualTo(5));
            StormTempest.Clear(owner);
            Assert.That(StormTempest.Charges(owner), Is.EqualTo(0));
        }
    }
}
