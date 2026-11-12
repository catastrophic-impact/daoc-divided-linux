using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture, NonParallelizable]
    public class UT_RecoveryAndBuffUpkeep
    {
        private GameServer _previous;
        private PetTestLanguageScope _language;
        private bool _enabled;
        private int _health, _power, _endurance, _sitting, _refresh;
        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        }
        private sealed class Human : GamePlayer
        {
            private Human() : base(null, null) { }
            public bool Combat, Attacking, Casting, Moving, Controlled;
            public override bool IsAlive => true;
            public override bool InCombat => Combat;
            public override bool IsAttacking => Attacking;
            public override bool IsCasting => Casting;
            public override bool IsMoving => Moving;
            public override bool IsCrowdControlled => Controlled;
            public override bool IsSitting { get; set; }
            public override int MaxHealth => 1000;
            public override int MaxMana => 600;
            public override int MaxEndurance => 100;
        }

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance; _language = new PetTestLanguageScope();
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            _enabled = Properties.FASTER_OUT_OF_COMBAT_RECOVERY;
            _health = Properties.REST_HEALTH_SECONDS; _power = Properties.REST_POWER_SECONDS;
            _endurance = Properties.REST_ENDURANCE_SECONDS; _sitting = Properties.REST_SITTING_MULTIPLIER;
            _refresh = Properties.BUFF_UPKEEP_REFRESH_SECONDS;
            Properties.FASTER_OUT_OF_COMBAT_RECOVERY = true;
            Properties.REST_HEALTH_SECONDS = 60; Properties.REST_POWER_SECONDS = 30;
            Properties.REST_ENDURANCE_SECONDS = 20; Properties.REST_SITTING_MULTIPLIER = 2;
            Properties.BUFF_UPKEEP_REFRESH_SECONDS = 15;
        }
        [TearDown]
        public void Cleanup()
        {
            Properties.FASTER_OUT_OF_COMBAT_RECOVERY = _enabled;
            Properties.REST_HEALTH_SECONDS = _health; Properties.REST_POWER_SECONDS = _power;
            Properties.REST_ENDURANCE_SECONDS = _endurance; Properties.REST_SITTING_MULTIPLIER = _sitting;
            Properties.BUFF_UPKEEP_REFRESH_SECONDS = _refresh;
            GameServer.LoadTestDouble(_previous); _language.Dispose();
        }
        private static Human Player() => (Human)RuntimeHelpers.GetUninitializedObject(typeof(Human));

        [Test]
        public void StationaryRecoveryUsesSeparateResourceFloors()
        {
            Human player = Player();
            Assert.That(OutOfCombatRecovery.Amount(player, eProperty.HealthRegenerationAmount, 15), Is.EqualTo(100));
            Assert.That(OutOfCombatRecovery.Amount(player, eProperty.PowerRegenerationAmount, 12), Is.EqualTo(120));
            Assert.That(OutOfCombatRecovery.Amount(player, eProperty.EnduranceRegenerationAmount, 1), Is.EqualTo(5));
        }
        [Test]
        public void BonusesAndRateSettingsStackOnTheFloor()
        {
            // Issue #10: items, mythirian regen, Champion passives and *_regen_amount_modifier used to vanish behind the floor.
            Human player = Player();
            Assert.That(OutOfCombatRecovery.Amount(player, eProperty.PowerRegenerationAmount, 12, 0, 1), Is.EqualTo(120));
            Assert.That(OutOfCombatRecovery.Amount(player, eProperty.PowerRegenerationAmount, 30, 20, 1), Is.EqualTo(140));
            Assert.That(OutOfCombatRecovery.Amount(player, eProperty.PowerRegenerationAmount, 18, 0, 1.5), Is.EqualTo(180));
            Assert.That(OutOfCombatRecovery.Amount(player, eProperty.PowerRegenerationAmount, 45, 20, 1.5), Is.EqualTo(210));
            Assert.That(OutOfCombatRecovery.Amount(player, eProperty.EnduranceRegenerationAmount, 9, 8, 1), Is.EqualTo(13));
            Assert.That(OutOfCombatRecovery.Amount(player, eProperty.EnduranceRegenerationAmount, 2, 1, 2), Is.EqualTo(12));
            Assert.That(OutOfCombatRecovery.Amount(player, eProperty.HealthRegenerationAmount, 25, 10, 1), Is.EqualTo(110));
            // A debuff lowers the floor, never below the native amount.
            Assert.That(OutOfCombatRecovery.Amount(player, eProperty.PowerRegenerationAmount, 1, -200, 1), Is.EqualTo(1));
            player.IsSitting = true;
            Assert.That(OutOfCombatRecovery.Amount(player, eProperty.EnduranceRegenerationAmount, 12, 8, 1), Is.EqualTo(18));
            player.IsSitting = false; player.Combat = true;
            Assert.That(OutOfCombatRecovery.Amount(player, eProperty.PowerRegenerationAmount, 45, 20, 1.5), Is.EqualTo(45));
        }
        [TestCase("combat")]
        [TestCase("attacking")]
        [TestCase("casting")]
        [TestCase("cc")]
        public void ActiveEncounterConditionsKeepNativeRegeneration(string condition)
        {
            Human player = Player();
            player.Combat = condition == "combat"; player.Attacking = condition == "attacking";
            player.Casting = condition == "casting"; player.Controlled = condition == "cc";
            Assert.That(OutOfCombatRecovery.Amount(player, eProperty.HealthRegenerationAmount, 15), Is.EqualTo(15));
        }
        [Test]
        public void SittingDoublesRateDespiteShorterNativeTick()
        {
            Human player = Player(); player.IsSitting = true;
            Assert.That(OutOfCombatRecovery.Amount(player, eProperty.HealthRegenerationAmount, 15), Is.EqualTo(100));
            Assert.That(OutOfCombatRecovery.Floor(15, 1000, 6000, 60, false), Is.EqualTo(100));
            // Same amount every three seconds sitting, versus every six seconds standing.
        }
        [Test]
        public void SuppressionStrongerBonusesAndMovingEnduranceArePreserved()
        {
            Human player = Player();
            Assert.That(OutOfCombatRecovery.Amount(player, eProperty.HealthRegenerationAmount, 0), Is.Zero);
            Assert.That(OutOfCombatRecovery.Amount(player, eProperty.HealthRegenerationAmount, 200), Is.EqualTo(200));
            player.Moving = true;
            Assert.That(OutOfCombatRecovery.Amount(player, eProperty.EnduranceRegenerationAmount, 0), Is.Zero);
            Properties.FASTER_OUT_OF_COMBAT_RECOVERY = false;
            Assert.That(OutOfCombatRecovery.Amount(player, eProperty.PowerRegenerationAmount, 12), Is.EqualTo(12));
        }

        private static Spell Buff(int id = 1, int duration = 600, int concentration = 0) =>
            new(new DbSpell { SpellID = id, Type = "StrengthBuff", Target = "Realm", Duration = duration, Concentration = concentration, Value = 50 }, 1);

        [Test]
        public void OnlyOwnActiveExactRankNearExpiryCanRefresh()
        {
            Spell spell = Buff();
            Assert.That(BotBuffUpkeep.CanRefresh(spell, spell, true, true, false, 24000, 10000), Is.True);
            Assert.That(BotBuffUpkeep.CanRefresh(spell, spell, false, true, false, 24000, 10000), Is.False);
            Assert.That(BotBuffUpkeep.CanRefresh(spell, Buff(2), true, true, false, 24000, 10000), Is.False);
            Assert.That(BotBuffUpkeep.CanRefresh(spell, spell, true, true, false, 26000, 10000), Is.False);
            Assert.That(BotBuffUpkeep.CanRefresh(spell, spell, true, true, true, 24000, 10000), Is.False);
            Assert.That(BotBuffUpkeep.CanRefresh(spell, spell, true, true, false, 10000, 10000), Is.False);
        }
        [Test]
        public void ShortAndConcentrationBuffsAreNotRepeatedlyRefreshed()
        {
            Spell shortBuff = Buff(duration: 30), conc = Buff(concentration: 1);
            Assert.That(BotBuffUpkeep.CanRefresh(shortBuff, shortBuff, true, true, false, 11000, 10000), Is.False);
            Assert.That(BotBuffUpkeep.CanRefresh(conc, conc, true, true, false, 11000, 10000), Is.False);
            Properties.BUFF_UPKEEP_REFRESH_SECONDS = 0;
            Spell spell = Buff();
            Assert.That(BotBuffUpkeep.CanRefresh(spell, spell, true, true, false, 11000, 10000), Is.False);
        }
    }
}
