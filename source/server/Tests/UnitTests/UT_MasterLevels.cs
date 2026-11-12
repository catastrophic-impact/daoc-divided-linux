using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerRules;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture, NonParallelizable]
    public class UT_MasterLevels
    {
        private GameServer _previous;
        private PetTestLanguageScope _language;
        private Region _region;
        private long _time;
        private readonly List<GameLiving> _actors = new();
        private static readonly SpellLine Line = new("Stormlord", "Stormlord", "Stormlord", false);

        private sealed class Rules : NormalServerRules
        {
            public override bool IsAllowedToAttack(GameLiving attacker, GameLiving defender, bool quiet) =>
                defender != null && defender.IsAlive && defender.Realm != attacker.Realm;
        }
        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
            protected override IServerRules ServerRulesImpl => new Rules();
        }
        private sealed class Actor : GameNPC
        {
            public override bool IsAlive => true;
            public override int MaxHealth => 100;
            public override int MaxMana => 100;
            public override int MaxEndurance => 100;
            public int CriticalHealChance;
            public override int GetModified(eProperty property) => property == eProperty.CriticalHealHitChance ? CriticalHealChance : 0;
            public override double GetPseudoDoubleIncl(RandomDeckEvent deckEvent) => 1.0;
        }

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance;
            _language = new PetTestLanguageScope();
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            _time = GameLoop.GameLoopTime;
            typeof(GameLoop).GetProperty(nameof(GameLoop.GameLoopTime)).SetValue(null, 120000L);
            _region = (Region)RuntimeHelpers.GetUninitializedObject(typeof(Region));
            typeof(Region).GetField("m_regionData", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_region, new RegionData { Id = 100 });
            typeof(Region).GetField("m_zones", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_region, new List<Zone>());
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (GameLiving actor in _actors)
            {
                foreach (ECSGameEffect effect in actor.effectListComponent.GetEffects())
                {
                    effect.End();
                    ServiceObjectStore.Remove(effect);
                }
                actor.effectListComponent.BeginTick();
                ServiceObjectStore.Remove(actor.effectListComponent);
            }
            _actors.Clear();
            typeof(GameLoop).GetProperty(nameof(GameLoop.GameLoopTime)).SetValue(null, _time);
            GameServer.LoadTestDouble(_previous);
            _language.Dispose();
        }

        private Actor NewActor(eRealm realm)
        {
            var actor = new Actor { ObjectState = GameObject.eObjectState.Active, CurrentRegion = _region, X = 1000, Y = 1000, Z = 100, Realm = realm };
            _actors.Add(actor);
            return actor;
        }

        private static List<ECSGameSpellEffect> MasterLevelEffects(GameLiving living)
        {
            living.effectListComponent.BeginTick();
            return living.effectListComponent.GetSpellEffects(eEffect.MasterLevel).Where(e => !e.IsEnding).ToList();
        }

        private static void EndAll(GameLiving living)
        {
            foreach (ECSGameSpellEffect effect in living.effectListComponent.GetSpellEffects(eEffect.MasterLevel))
                effect.End();
            living.effectListComponent.BeginTick();
        }

        private static Spell Storm(string type, int value) => new(new DbSpell
        {
            SpellID = 63060, Name = type, Type = type, Target = "Self", Range = 1500, Radius = 350, Duration = 600, Frequency = 40, Value = value, Icon = 7257
        }, 1);

        [Test]
        public void LevelFiftyExperienceConvertsAtTheConfiguredRate()
        {
            Assert.That(MasterLevelRules.ToMasterExperience(1000, 32000, 1000, 1.0), Is.EqualTo(32000));
            Assert.That(MasterLevelRules.ToMasterExperience(500, 32000, 1000, 2.0), Is.EqualTo(32000));
            Assert.That(MasterLevelRules.ToMasterExperience(1000, 32000, 1000, 0), Is.Zero);
            Assert.That(MasterLevelRules.ToMasterExperience(-5, 32000, 1000, 1.0), Is.Zero);
            Assert.That(MasterLevelRules.LevelSpan, Is.GreaterThan(0));
        }

        [Test]
        public void EnemyStormsGiveAVisibleDebuffEffectThatRefreshesInsteadOfStacking()
        {
            var owner = NewActor(eRealm.Midgard);
            var enemy = NewActor(eRealm.None);
            var storm = new StormlordStorm(owner, new InebriatingFumesSpellHandler(owner, Storm("InebriatingFumes", 60), Line));
            Assert.That(storm.Affects(enemy), Is.True);
            Assert.That(storm.Affects(owner), Is.False);

            storm.Pulse(new GameLiving[] { enemy });
            Assert.That(MasterLevelEffects(enemy), Has.Count.EqualTo(1));
            storm.Pulse(new GameLiving[] { enemy });
            var effects = MasterLevelEffects(enemy);
            Assert.That(effects, Has.Count.EqualTo(1), "a second pulse refreshes the same effect");
            Assert.That(effects[0].Icon, Is.Not.Zero);
            Assert.That(effects[0].ExpireTick, Is.EqualTo(GameLoop.GameLoopTime + storm.Interval + 2000));
            Assert.That(enemy.DebuffCategory[eProperty.Dexterity], Is.EqualTo(60));
            Assert.That(enemy.DebuffCategory[eProperty.Quickness], Is.EqualTo(60));

            EndAll(enemy);
            Assert.That(enemy.DebuffCategory[eProperty.Dexterity], Is.Zero, "the debuff ends with the effect");
        }

        [Test]
        public void DazzlingArrayGivesAlliesAVisibleBuffAndIgnoresEnemies()
        {
            var owner = NewActor(eRealm.Midgard);
            var enemy = NewActor(eRealm.None);
            var storm = new StormlordStorm(owner, new DazzlingArraySpellHandler(owner, Storm("DazzlingArray", 15), Line));
            Assert.That(storm.Affects(owner), Is.True);
            Assert.That(storm.Affects(enemy), Is.False);
            storm.Pulse(new GameLiving[] { owner });
            var effects = MasterLevelEffects(owner);
            Assert.That(effects, Has.Count.EqualTo(1));
            Assert.That(effects[0].SpellHandler.HasPositiveEffect, Is.True);
            Assert.That(owner.BaseBuffBonusCategory[eProperty.MissHit], Is.EqualTo(15));
            EndAll(owner);
            Assert.That(owner.BaseBuffBonusCategory[eProperty.MissHit], Is.Zero);
        }

        [Test]
        public void DrainsTakeAPercentOfTheMaximumAndReturnHalf()
        {
            var owner = NewActor(eRealm.Midgard);
            var enemy = NewActor(eRealm.None);
            owner.Endurance = 0; enemy.Endurance = 100;
            StormlordStorm.Drain(owner, enemy, enemy.MaxEndurance, 10, power: false);
            Assert.That(enemy.Endurance, Is.EqualTo(90));
            Assert.That(owner.Endurance, Is.EqualTo(5));
            enemy.Endurance = 4;
            StormlordStorm.Drain(owner, enemy, enemy.MaxEndurance, 10, power: false);
            Assert.That(enemy.Endurance, Is.Zero, "never drains more than the target has");
        }

        [Test]
        public void MonsterPowerIsBottomlessSoSiphonOnlyFeedsTheCaster()
        {
            var owner = NewActor(eRealm.Midgard);
            var monster = NewActor(eRealm.None);
            StormlordStorm.Drain(owner, monster, monster.MaxMana, 10, power: true);
            Assert.That(monster.Mana, Is.EqualTo(5000));
        }

        [Test]
        public void ArcingPowerOnlyArcsSingleTargetDamageSpells()
        {
            Spell Make(string type, string target, int damage, int radius) =>
                new(new DbSpell { SpellID = 1, Type = type, Target = target, Damage = damage, Radius = radius }, 50);
            Assert.That(ArcingPowerEffect.Arcs(Make("DirectDamage", "Enemy", 200, 0)), Is.True);
            Assert.That(ArcingPowerEffect.Arcs(Make("DirectDamage", "Enemy", 200, 350)), Is.False);
            Assert.That(ArcingPowerEffect.Arcs(Make("DirectDamage", "Enemy", 0, 0)), Is.False);
            Assert.That(ArcingPowerEffect.Arcs(Make("Heal", "Realm", 200, 0)), Is.False);
            Assert.That(ArcingPowerEffect.Arcs(Make("ArcingPower", "Self", 0, 350)), Is.False);
        }

        [Test]
        public void EveryOtherPathHasTenAbilitiesThatCanRun()
        {
            foreach (string path in MasterLevelAbilities.Paths)
            {
                if (path == "Stormlord")
                    continue;
                for (int level = 1; level <= 10; level++)
                {
                    int id = MasterLevelAbilities.FirstId + System.Array.IndexOf(MasterLevelAbilities.Paths, path) * 10 + level - 1;
                    MLAbility ability = MasterLevelAbilities.For(id);
                    Assert.That(ability, Is.Not.Null, $"{path} ML{level}");
                    Assert.That(ability.Path, Is.EqualTo(path));
                    Assert.That(ability.Level, Is.EqualTo(level));
                    Assert.That(ability.Passive || ability.Run != null, Is.True, $"{path} ML{level} needs an action");
                    Assert.That(ability.Describe(new Spell(new DbSpell { SpellID = id, Type = "MasterLevelAbility", Target = "Self", Radius = 350, Duration = 30 }, 1)), Is.Not.Empty);
                }
            }
            Assert.That(MasterLevelAbilities.For(MasterLevelAbilities.FirstId - 1), Is.Null);
            Assert.That(MasterLevelAbilities.For(MasterLevelAbilities.FirstId + 80), Is.Null);
        }

        [Test]
        public void HeldBonusesAreAppliedOnceAndRemovedExactly()
        {
            var ally = NewActor(eRealm.Midgard);
            MLMod[] mods = { MLMod.Buff(eProperty.ArmorAbsorption, 10), MLMod.Debuff(eProperty.Dexterity, 50),
                new(eProperty.CriticalMeleeHitChance, MLCategory.Other, 10), new(eProperty.CriticalSpellHitChance, MLCategory.Ability, 10) };
            foreach (MLMod mod in mods)
                mod.Apply(ally, null, true);
            Assert.That(ally.BaseBuffBonusCategory[eProperty.ArmorAbsorption], Is.EqualTo(10));
            Assert.That(ally.DebuffCategory[eProperty.Dexterity], Is.EqualTo(50));
            Assert.That(ally.OtherBonus[eProperty.CriticalMeleeHitChance], Is.EqualTo(10));
            Assert.That(ally.AbilityBonus[eProperty.CriticalSpellHitChance], Is.EqualTo(10));
            foreach (MLMod mod in mods)
                mod.Apply(ally, null, false);
            Assert.That(ally.BaseBuffBonusCategory[eProperty.ArmorAbsorption] + ally.DebuffCategory[eProperty.Dexterity] +
                        ally.OtherBonus[eProperty.CriticalMeleeHitChance] + ally.AbilityBonus[eProperty.CriticalSpellHitChance], Is.Zero);
        }

        [Test]
        public void FriendlyFieldsShowAnEffectOnAlliesInsideWithTheirBonuses()
        {
            var owner = NewActor(eRealm.Midgard);
            var enemy = NewActor(eRealm.None);
            var spell = new Spell(new DbSpell { SpellID = 63020, Name = "Warding Crystal", Type = "MasterLevelAbility", Target = "Self", Range = 1500, Radius = 350, Duration = 600, Frequency = 40 }, 1);
            var handler = new MasterLevelAbilityHandler(owner, spell, Line);
            var field = new MasterLevelField(owner, handler, new MasterLevelFieldOptions { Friendly = true });
            Assert.That(field.Affects(owner), Is.True);
            Assert.That(field.Affects(enemy), Is.False);
            Assert.That(field.GrantsEffect(owner), Is.True);
            field.Pulse(new GameLiving[] { owner });
            field.Pulse(new GameLiving[] { owner });
            Assert.That(MasterLevelEffects(owner), Has.Count.EqualTo(1));
            Assert.That(owner.BaseBuffBonusCategory[eProperty.ArmorAbsorption], Is.EqualTo(10));
            Assert.That(owner.BaseBuffBonusCategory[eProperty.Resist_Body], Is.EqualTo(10));
            EndAll(owner);
            Assert.That(owner.BaseBuffBonusCategory[eProperty.ArmorAbsorption], Is.Zero);
        }

        [Test]
        public void FontsWithoutBonusesStillShowTheirEffectOnAllies()
        {
            var owner = NewActor(eRealm.Midgard);
            var spell = new Spell(new DbSpell { SpellID = 63032, Name = "Sphere of Rejuvenation", Type = "MasterLevelAbility", Target = "Self", Range = 1500, Radius = 500, Duration = 600, Frequency = 40, Value = 5 }, 1);
            var field = new MasterLevelField(owner, new MasterLevelAbilityHandler(owner, spell, Line), new MasterLevelFieldOptions { Friendly = true });
            field.Pulse(new GameLiving[] { owner });
            Assert.That(MasterLevelEffects(owner), Has.Count.EqualTo(1));
        }

        [Test]
        public void PerfecterFontsGiveTheFullRateInCombat()
        {
            // DR-258: Sphere of Rejuvenation (63032) and Font of Power (63034) no longer halve for allies in combat.
            foreach (int id in new[] { 63032, 63034 })
            {
                var spell = new Spell(new DbSpell { SpellID = id, Type = "MasterLevelAbility", Target = "Self", Radius = 500, Duration = 600, Frequency = 40, Value = 5 }, 1);
                Assert.That(MasterLevelAbilities.FontPercent(spell), Is.EqualTo(10));
                string text = MasterLevelAbilities.For(id).Describe(spell);
                Assert.That(text, Does.Contain("10%").And.Contain("in or out of combat"));
                Assert.That(text, Does.Not.Contain("double out of combat"));
            }
        }

        [Test]
        public void PassiveIconsArePermanent()
        {
            var owner = NewActor(eRealm.Midgard);
            var spell = new Spell(new DbSpell { SpellID = 63070, Name = "Iron Will", Type = "MasterLevelAbility", Target = "Self" }, 1);
            var handler = new MasterLevelAbilityHandler(owner, spell, Line);
            Assert.That(handler.Ability.Passive, Is.True);
            Assert.That(handler.HasPositiveEffect, Is.True);
            var effect = new MasterLevelEffect(new(owner, 0, 1, handler), permanent: true);
            Assert.That(effect.ExpireTick, Is.EqualTo(long.MaxValue));
        }

        [Test]
        public void PassivesBelongToPlayersOnly()
        {
            Assert.That(MasterLevelRules.HasPassive(NewActor(eRealm.Midgard), "Warlord", 1), Is.False);
            Assert.That(MasterLevelRules.HasPassive(null, "Warlord", 1), Is.False);
            Assert.That(MasterLevelAbilities.For("Perfecter", 4).Passive, Is.True);
        }

        [Test]
        public void ChampionLevelsRunToFifteenOnRealmPoints()
        {
            Assert.That(GamePlayer.CL_MAX_LEVEL, Is.EqualTo(15));
            Assert.That(GamePlayer.CLXPLevel, Has.Length.EqualTo(16));
            Assert.That(GamePlayer.CLXPLevel[15], Is.EqualTo(875000));
            for (int level = 1; level <= 15; level++)
                Assert.That(GamePlayer.CLXPLevel[level], Is.GreaterThan(GamePlayer.CLXPLevel[level - 1]));
            Assert.That(ChampionRules.ToChampionExperience(2500, 1.0), Is.EqualTo(2500));
            Assert.That(ChampionRules.ToChampionExperience(2500, 2.0), Is.EqualTo(5000));
            Assert.That(ChampionRules.ToChampionExperience(2500, 0), Is.Zero);
            Assert.That(ChampionRules.ToChampionExperience(-1, 1.0), Is.Zero);
        }

        [Test]
        public void EachRealmHasFifteenChampionAbilitiesOnItsOwnPath()
        {
            foreach (eRealm realm in ChampionAbilities.Realms)
            {
                string path = ChampionRules.PathFor(realm);
                var spec = new RealmChampionSpecialization(path, path, 0, 0);
                Assert.That(spec.Realm, Is.EqualTo(realm));
                for (int level = 1; level <= 15; level++)
                {
                    MLAbility ability = MasterLevelAbilities.For(ChampionAbilities.IdFor(realm, level));
                    Assert.That(ability, Is.Not.Null, $"{realm} CL{level}");
                    Assert.That(ability.Path, Is.EqualTo(path));
                    Assert.That(ability.Level, Is.EqualTo(level));
                    Assert.That(ability.Passive, Is.EqualTo(level is 1 or 3 or 5 or 7 or 9 or 11 or 13), $"{realm} CL{level}");
                    Assert.That(ability.Passive ? ability.Mods.Length > 0 : ability.Run != null, Is.True, $"{realm} CL{level}");
                }
                Assert.That(MasterLevelAbilities.For(ChampionAbilities.IdFor(realm, 6)).BreaksControl, Is.True);
            }
            Assert.That(new RealmChampionSpecialization("ChampionAlbion", "", 0, 0).IsFor(NewActor(eRealm.Albion)), Is.False, "NPCs have no Champion path");
        }

        [Test]
        public void ChampionPassivesHoldTheirBonusesOutsideBuffCaps()
        {
            var owner = NewActor(eRealm.Albion);
            var spell = new Spell(new DbSpell { SpellID = ChampionAbilities.IdFor(eRealm.Albion, 1), Name = "Knightly Vigor", Type = "MasterLevelAbility", Target = "Self", Icon = 1457 }, 1);
            var effect = new MasterLevelEffect(new(owner, 0, 1, new MasterLevelAbilityHandler(owner, spell, Line)), permanent: true);
            Assert.That(effect.Start(), Is.True);
            owner.effectListComponent.BeginTick();
            Assert.That(owner.AbilityBonus[eProperty.Strength], Is.EqualTo(15));
            Assert.That(owner.AbilityBonus[eProperty.Charisma], Is.EqualTo(15));
            Assert.That(owner.BaseBuffBonusCategory[eProperty.Strength], Is.Zero);
            EndAll(owner);
            Assert.That(owner.AbilityBonus[eProperty.Strength], Is.Zero);
        }

        [Test]
        public void MasterLevelAndChampionEffectsShowTheirOwnTooltips()
        {
            // Their icons are borrowed from stock spells (Warhorn of Heimdall uses Thor's Potence, a str/con buff).
            foreach (int id in new[] { MasterLevelAbilities.FirstId, 63079, ChampionAbilities.IdFor(eRealm.Midgard, 8), ChampionAbilities.IdFor(eRealm.Hibernia, 15) })
                Assert.That(DOL.GS.PacketHandler.PacketLib1110.UsesOwnTooltipIdentity(id), Is.True, id.ToString());
            Assert.That(DOL.GS.PacketHandler.PacketLib1110.UsesOwnTooltipIdentity(ChampionAbilities.IdFor(eRealm.Midgard, 4) * 1000 + 1), Is.True, "stock effects an ability applies");
            Assert.That(DOL.GS.PacketHandler.PacketLib1110.UsesOwnTooltipIdentity(3537), Is.False, "stock spells keep their own tooltips");
        }

        [Test]
        public void BreakFreeOnlyBypassesCastingWhileControlled()
        {
            var owner = NewActor(eRealm.Midgard);
            var spell = new Spell(new DbSpell { SpellID = ChampionAbilities.IdFor(eRealm.Midgard, 6), Name = "Berserker's Break", Type = "MasterLevelAbility", Target = "Self", Duration = 5 }, 1);
            Assert.That(MasterLevelAbilities.TryBreakFree(owner, spell, Line), Is.False, "not controlled: use the normal cast path");
            var other = new Spell(new DbSpell { SpellID = ChampionAbilities.IdFor(eRealm.Midgard, 4), Name = "Frostbite Hurl", Type = "MasterLevelAbility", Target = "Enemy" }, 1);
            owner.IsStunned = true;
            Assert.That(MasterLevelAbilities.TryBreakFree(owner, other, Line), Is.False, "only break-free abilities bypass crowd control");
            owner.IsStunned = false;
        }
        private sealed class StyleStunProbe : StyleStun
        {
            public StyleStunProbe(GameLiving caster, Spell spell) : base(caster, spell, Line) { }
            public bool IsBlocked(GameLiving target) => CheckSpellResist(target);
        }

        [Test]
        public void ChampionBreakFreeBlocksStyleStunsAndAllListedControlUntilExpiry()
        {
            var owner = NewActor(eRealm.Midgard);
            var enemy = NewActor(eRealm.Albion);
            var spell = new Spell(new DbSpell { SpellID = ChampionAbilities.IdFor(eRealm.Midgard, 6),
                Type = "MasterLevelAbility", Target = "Self", Duration = 5 }, 1);
            new MasterLevelAbilityHandler(owner, spell, Line).Hold(owner);
            var effect = MasterLevelEffects(owner).Single();
            foreach (string type in new[] { "StyleStun", "Stun", "Mesmerize", "SpeedDecrease", "Nearsight", "Confusion" })
            {
                var control = new Spell(new DbSpell { Type = type, Target = "Enemy", Duration = 10, Value = 50 }, 1);
                Assert.That(ChampionAbilities.IsControlImmune(owner, control), Is.True, type);
            }
            var stun = new Spell(new DbSpell { Type = "StyleStun", Target = "Enemy", Duration = 10 }, 1);
            Assert.That(new StyleStunProbe(enemy, stun).IsBlocked(owner), Is.True);
            Assert.That(ChampionAbilities.IsControlImmune(owner,
                new Spell(new DbSpell { Type = "DirectDamage", Target = "Enemy" }, 1)), Is.False);
            effect.ExpireTick = GameLoop.GameLoopTime;
            Assert.That(ChampionAbilities.IsControlImmune(owner, stun), Is.False, "expired effects grant no immunity");
            EndAll(owner);
            Assert.That(ChampionAbilities.IsControlImmune(owner, stun), Is.False);
        }

        [Test]
        public void ChampionHealingPassiveAddsToAlreadyCappedHealingGear()
        {
            var owner = NewActor(eRealm.Albion);
            owner.ItemBonus[eProperty.HealingEffectiveness] = 25;
            var spell = new Spell(new DbSpell { SpellID = ChampionAbilities.IdFor(eRealm.Albion, 13),
                Type = "MasterLevelAbility", Target = "Self" }, 1);
            var effect = new MasterLevelEffect(new(owner, 0, 1, new MasterLevelAbilityHandler(owner, spell, Line)), permanent: true);
            effect.Start();
            MasterLevelEffects(owner);
            var calculator = new DOL.GS.PropertyCalc.HealingEffectivenessPercentCalculator();
            Assert.That(calculator.CalcValue(owner, eProperty.HealingEffectiveness), Is.EqualTo(35));
            EndAll(owner);
            Assert.That(calculator.CalcValue(owner, eProperty.HealingEffectiveness), Is.EqualTo(25));
        }

        [Test]
        public void SameChampionBuffFromTwoCastersRefreshesWithoutStacking()
        {
            var first = NewActor(eRealm.Midgard);
            var second = NewActor(eRealm.Midgard);
            var target = NewActor(eRealm.Midgard);
            var spell = new Spell(new DbSpell { SpellID = ChampionAbilities.IdFor(eRealm.Midgard, 8),
                Type = "MasterLevelAbility", Target = "Self", Duration = 60 }, 1);
            var a = new MasterLevelAbilityHandler(first, spell, Line);
            var b = new MasterLevelAbilityHandler(second, spell, Line);
            a.Hold(target, 1000);
            MasterLevelEffects(target);
            b.Hold(target, 5000);
            Assert.That(MasterLevelEffects(target), Has.Count.EqualTo(1));
            Assert.That(target.BaseBuffBonusCategory[eProperty.MeleeDamage], Is.EqualTo(15));
            Assert.That(MasterLevelEffects(target).Single().ExpireTick, Is.EqualTo(GameLoop.GameLoopTime + 5000));
            Assert.That(a.HasConflictingEffectWith(b), Is.True);
            EndAll(target);
            Assert.That(target.BaseBuffBonusCategory[eProperty.MeleeDamage], Is.Zero);
        }

        [TestCase(0, 240)]
        [TestCase(100, 480)]
        public void ResolvedHealingKeepsTheRolledAmountBeforeOverhealAndResetsEachCast(int criticalChance, int expected)
        {
            var owner = NewActor(eRealm.Midgard);
            var target = NewActor(eRealm.Midgard);
            target.Health = 99;
            owner.CriticalHealChance = criticalChance;
            var spell = new Spell(new DbSpell { Type = "Heal", Target = "Realm", Value = 200 }, 1);
            var heal = new HealSpellHandler(owner, spell, Line);
            heal.HealTarget(target, 240, true);
            Assert.That(heal.ResolvedHealAmount, Is.EqualTo(expected));
            Assert.That(target.Health, Is.EqualTo(100));
            heal.StartSpell(null);
            Assert.That(heal.ResolvedHealAmount, Is.Zero, "an empty cast cannot reuse an earlier roll");
            Assert.That(MasterLevelTriggerEffect.IsHeal(new Spell(new DbSpell {
                Type = "Heal", Target = "Realm", Value = -25 }, 1)), Is.True, "percentage heals also qualify");
        }

        private sealed class ProgressionPlayer : GamePlayer
        {
            private ProgressionPlayer() : base(null, null) { }
            public override byte Level { get; set; }
            public override eRealm Realm { get; set; }
            public override bool MLGranted { get; set; }
            public long AwardedChampionExperience;
            public override void GainChampionExperience(long experience, eXPSource source) => AwardedChampionExperience += experience;
            public override DOL.GS.PacketHandler.IPacketLib Out => new BotDummyPacketLib();
        }

        [Test]
        public void TrainingStartsAtFiftyWithoutAnAwardOrPathSelection()
        {
            var player = (ProgressionPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ProgressionPlayer));
            typeof(GamePlayer).GetField("m_dbCharacter", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(player, new DbCoreCharacter());
            player.Realm = eRealm.Midgard;
            player.Level = 49;
            MasterLevelRules.EnsureTraining(player);
            Assert.That(player.MLGranted || player.Champion, Is.False);
            player.Level = 50;
            MasterLevelRules.EnsureTraining(player);
            Assert.That(player.MLGranted, Is.True);
            Assert.That(player.Champion, Is.False, "Champion training starts at the King, not on its own");
            Assert.That(player.MLLevel, Is.Zero);
            Assert.That(player.ChampionLevel, Is.Zero);
            MasterLevelRules.EnsureTraining(player);
            Assert.That(player.MLGranted, Is.True, "login and repeated checks are idempotent");
        }

        [Test]
        public void TheKingStartsChampionTrainingWithoutClearingProgress()
        {
            var player = (ProgressionPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ProgressionPlayer));
            typeof(GamePlayer).GetField("m_dbCharacter", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(player, new DbCoreCharacter());
            player.Realm = eRealm.Albion;
            player.Level = 49;
            Assert.That(ChampionRules.BecomeChampion(player, eRealm.Albion), Is.EqualTo(ChampionRules.KingAnswer.NotLevelFifty));
            player.Level = 50;
            Assert.That(ChampionRules.BecomeChampion(player, eRealm.Hibernia), Is.EqualTo(ChampionRules.KingAnswer.WrongRealm));
            Assert.That(player.Champion, Is.False);

            player.ChampionExperience = 1234;
            player.ChampionLevel = 2;
            Assert.That(ChampionRules.BecomeChampion(player, eRealm.Albion), Is.EqualTo(ChampionRules.KingAnswer.NowChampion));
            Assert.That(player.Champion, Is.True);
            Assert.That(player.ChampionExperience, Is.EqualTo(1234), "the old King path cleared champion experience");
            Assert.That(player.ChampionLevel, Is.EqualTo(2));
            Assert.That(ChampionRules.BecomeChampion(player, eRealm.Albion), Is.EqualTo(ChampionRules.KingAnswer.AlreadyChampion));
        }

        [Test]
        public void LevelFiftyExperienceFeedsMasterLevelsAndRealmPointsFeedChampionLevels()
        {
            var player = (ProgressionPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ProgressionPlayer));
            typeof(GamePlayer).GetField("m_dbCharacter", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(player, new DbCoreCharacter());
            player.Realm = eRealm.Midgard;
            double mlRate = DOL.GS.ServerProperties.Properties.ML_XP_RATE;
            double clRate = DOL.GS.ServerProperties.Properties.CL_RP_RATE;
            try
            {
                DOL.GS.ServerProperties.Properties.ML_XP_RATE = 1;
                DOL.GS.ServerProperties.Properties.CL_RP_RATE = 1;
                player.Level = 49;
                MasterLevelRules.GainExperience(player, MasterLevelRules.LevelSpan / 10);
                ChampionRules.GainRealmPoints(player, 2500);
                Assert.That(player.MLExperience + player.AwardedChampionExperience, Is.Zero);
                player.Level = 50;
                MasterLevelRules.GainExperience(player, MasterLevelRules.LevelSpan / 10);
                Assert.That(player.MLExperience, Is.GreaterThan(0));
                Assert.That(player.AwardedChampionExperience, Is.Zero, "normal XP does not advance CLs");
                ChampionRules.GainRealmPoints(player, 2500);
                Assert.That(player.AwardedChampionExperience, Is.Zero, "no champion experience before the King");
                Assert.That(player.MLGranted, Is.True);
                Assert.That(player.Champion, Is.False);
                Assert.That(ChampionRules.BecomeChampion(player, eRealm.Midgard), Is.EqualTo(ChampionRules.KingAnswer.NowChampion));
                ChampionRules.GainRealmPoints(player, 2500);
                Assert.That(player.AwardedChampionExperience, Is.EqualTo(2500));
            }
            finally
            {
                DOL.GS.ServerProperties.Properties.ML_XP_RATE = mlRate;
                DOL.GS.ServerProperties.Properties.CL_RP_RATE = clRate;
            }
        }

    }
}
