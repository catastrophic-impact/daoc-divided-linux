using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture, NonParallelizable]
public class UT_BotWeaponStats
{
    private EpicTestServerScope _server;
    private PetTestLanguageScope _language;

    // SkillBase and GameObject need the empty test server when this fixture runs on its own.
    [SetUp] public void StartTestServer() { _server = new EpicTestServerScope(); _language = new PetTestLanguageScope(); }
    [TearDown] public void StopTestServer() { _language.Dispose(); _server.Dispose(); }

    private static DbItemTemplate LargeWeapon(int dps = 15, int speed = 45, int quality = 89) => new()
    {
        Id_nb = Guid.NewGuid().ToString(), Name = "database two-hander", Level = 1,
        Realm = (int)eRealm.Hibernia, Object_Type = (int)eObjectType.LargeWeapons,
        Item_Type = Slot.TWOHAND, Hand = 1, Type_Damage = (int)eDamageType.Crush,
        DPS_AF = dps, SPD_ABS = speed, Quality = quality, Model = 69,
        Condition = 50000, MaxCondition = 50000, Durability = 50000, MaxDurability = 50000,
        IsPickable = true, IsTradable = true, IsDropable = true, AllowedClasses = "0"
    };

    private static DbItemTemplate Select(params DbItemTemplate[] items) => BotEquipment.SelectCompanionWeapon(
        items, 1, eRealm.Hibernia, eCharacterClass.Champion, eObjectType.LargeWeapons, eInventorySlot.TwoHandWeapon);

    [Test]
    public void ChampionRejectsSpineSplitterButKeepsHealthyDatabaseChoiceWithoutMutatingEither()
    {
        var broken = LargeWeapon(1); broken.Name = "Spine Splitter";
        var healthy = LargeWeapon();
        for (int roll = 0; roll < 100; roll++)
        {
            var selected = Select(broken, healthy);
            Assert.That(selected.Name, Is.EqualTo(healthy.Name));
            Assert.That(selected.DPS_AF, Is.EqualTo(15));
            Assert.That(selected, Is.Not.SameAs(healthy));
        }
        Assert.That(broken.DPS_AF, Is.EqualTo(1));
        Assert.That(healthy.IsTradable && healthy.IsDropable, Is.True);
    }

    [TestCase(1, 45, 89)] [TestCase(14, 45, 89)] [TestCase(15, 0, 89)]
    [TestCase(15, 1, 89)] [TestCase(15, 45, 0)] [TestCase(15, 45, 1)]
    public void BadDamageSpeedOrQualityCannotPreventNormalGeneratedFallback(int dps, int speed, int quality)
    {
        using var language = new PetTestLanguageScope();
        var selected = Select(LargeWeapon(dps, speed, quality));
        Assert.That(selected.Name, Is.Not.EqualTo("database two-hander"));
        Assert.That(BotWeaponStats.IsNormalCompanionWeapon(selected), Is.True);
        Assert.That(GameInventoryItem.Create(selected).ConditionPercent, Is.EqualTo(100));
    }

    [Test]
    public void EveryClassicWeaponTypeHasNormalGeneratedStatsAtEveryLevel()
    {
        using var language = new PetTestLanguageScope();
        foreach (var type in Enumerable.Range(2, 25).Select(i => (eObjectType)i))
        for (byte level = 1; level <= 50; level++)
        {
            var realm = (int)type is >= 19 and <= 23 or 18 or 26 ? eRealm.Hibernia :
                (int)type is >= 11 and <= 17 or 25 ? eRealm.Midgard : eRealm.Albion;
            var cls = realm == eRealm.Hibernia ? eCharacterClass.Champion :
                realm == eRealm.Midgard ? eCharacterClass.Warrior : eCharacterClass.Armsman;
            var slot = BotRangedCombat.IsRangedWeaponType(type) ? eInventorySlot.DistanceWeapon :
                type is eObjectType.TwoHandedWeapon or eObjectType.PolearmWeapon or eObjectType.Staff or
                    eObjectType.Spear or eObjectType.LargeWeapons or eObjectType.CelticSpear or eObjectType.Scythe
                    ? eInventorySlot.TwoHandWeapon : eInventorySlot.RightHandWeapon;
            var template = BotEquipment.CreateCompanionItem(realm, cls, level, type, slot);
            var item = GameInventoryItem.Create(template);
            string context = $"{type}, level {level}";
            Assert.That(template.DPS_AF, Is.GreaterThanOrEqualTo(12 + 3 * level), context);
            Assert.That(BotWeaponStats.IsNormalCompanionWeapon(template), Is.True, context);
            Assert.That(item.ConditionPercent, Is.EqualTo(100), context);
            Assert.That(item.Durability, Is.GreaterThan(0), context);
            Assert.That(item.Model, Is.GreaterThan(0), context);
            Assert.That(item.Type_Damage, Is.InRange(1, 3), context);
        }
    }

    [TestCase(eObjectType.Shield)] [TestCase(eObjectType.Instrument)]
    public void ArmorFactorAndInstrumentSubtypeAreNotWeaponDps(eObjectType type)
    {
        var template = LargeWeapon(1, 0); template.Object_Type = (int)type;
        Assert.That(BotWeaponStats.NeedsCompanionDamageStats(type), Is.False);
        Assert.That(BotWeaponStats.IsNormalCompanionWeapon(template), Is.True);
    }

    [Test]
    public void NewlySelectedItemsUseMaximumConditionAndNeedRepairReserve()
    {
        var healthy = LargeWeapon(); healthy.Condition = 0; healthy.Durability = 0;
        var selected = GameInventoryItem.Create(Select(healthy));
        Assert.That(selected.ConditionPercent, Is.EqualTo(100));
        Assert.That(selected.Durability, Is.EqualTo(50000));
        healthy.MaxDurability = 0;
        Assert.That(BotWeaponStats.IsNormalCompanionWeapon(healthy), Is.False);
        healthy.MaxDurability = 50000; healthy.MaxCondition = 0;
        Assert.That(BotWeaponStats.IsNormalCompanionWeapon(healthy), Is.False);
    }

    [Test]
    public void EarnedMalformedMeleeIsNotAnUpgradeButLevelZeroStarterRemainsValid()
    {
        var broken = GameInventoryItem.Create(LargeWeapon(1));
        broken.Bonus1 = 100; // Magical utility must not hide a broken weapon.
        Assert.That(AutonomousBotEconomy.EquipmentValue(broken), Is.Zero);
        var bot = (ChampionBot)RuntimeHelpers.GetUninitializedObject(typeof(ChampionBot));
        bot.Inventory = new BotInventory(); bot.Level = 1;
        Assert.That(AutonomousBotEconomy.TryGetEquipmentUpgrade(bot, broken, out _), Is.False);
        var starter = LargeWeapon(12); starter.Level = 0;
        Assert.That(BotWeaponStats.HasFunctionalMeleeStats(starter), Is.True);
        var worn = GameInventoryItem.Create(starter); worn.Condition = 0;
        Assert.That(BotWeaponStats.HasFunctionalMeleeStats(worn), Is.False);
        Assert.That(starter.DPS_AF, Is.EqualTo(12));
    }

    [Test]
    public void RuntimeMeleeSelectionRejectsClassIncompatibleAndBrokenSlotItems()
    {
        var bot = (MeleeEligibilityBot)RuntimeHelpers.GetUninitializedObject(typeof(MeleeEligibilityBot));
        bot.Level = 7;
        DbInventoryItem legal = GameInventoryItem.Create(LargeWeapon());
        legal.Object_Type = (int)eObjectType.Axe;
        legal.LevelRequirement = 7;
        DbInventoryItem focusStaff = GameInventoryItem.Create(LargeWeapon());
        focusStaff.Object_Type = (int)eObjectType.Staff;
        DbInventoryItem broken = GameInventoryItem.Create(LargeWeapon(1));
        broken.Object_Type = (int)eObjectType.Axe;

        Assert.Multiple(() =>
        {
            Assert.That(BotWeaponStats.CanUseMelee(bot, legal), Is.True);
            Assert.That(BotWeaponStats.CanUseMelee(bot, focusStaff), Is.False);
            Assert.That(BotWeaponStats.CanUseMelee(bot, broken), Is.False);
        });
    }

    [Test]
    public void LockedHunterAndShadowbladePlansRejectFocusStavesEvenWhenBaseAbilityAllowsThem()
    {
        foreach (BotSpec plan in new BotSpec[]
                 {
                     new() { WeaponOneType = eObjectType.Spear },
                     new() { WeaponOneType = eObjectType.Axe, WeaponTwoType = eObjectType.Axe }
                 })
        {
            var bot = (BroadAbilityBot)RuntimeHelpers.GetUninitializedObject(typeof(BroadAbilityBot));
            bot.Level = 20;
            typeof(GameBot).GetProperty(nameof(GameBot.BotSpec),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(bot, plan);

            DbItemTemplate staff = LargeWeapon();
            staff.Object_Type = (int)eObjectType.Staff;
            DbItemTemplate configured = LargeWeapon();
            configured.Object_Type = (int)plan.WeaponOneType;

            Assert.That(BotWeaponStats.IsConfiguredMeleeWeapon(bot, staff), Is.False);
            Assert.That(BotWeaponStats.IsConfiguredMeleeWeapon(bot, configured), Is.True);
        }
    }

    [Test]
    public void AdvancedAlbionLifePlansUseBaseWeaponUntilTheirPromotionLevel()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GameBot.ShouldUsePlannedTwoHandedPrimary(eCharacterClass.Paladin, 1, true), Is.False);
            Assert.That(GameBot.ShouldUsePlannedTwoHandedPrimary(eCharacterClass.Paladin, 4, true), Is.False);
            Assert.That(GameBot.ShouldUsePlannedTwoHandedPrimary(eCharacterClass.Paladin, 5, true), Is.True);
            Assert.That(GameBot.ShouldUsePlannedTwoHandedPrimary(eCharacterClass.Armsman, 1, true,
                eObjectType.PolearmWeapon), Is.False);
            Assert.That(GameBot.ShouldUsePlannedTwoHandedPrimary(eCharacterClass.Armsman, 4, true,
                eObjectType.PolearmWeapon), Is.False);
            Assert.That(GameBot.ShouldUsePlannedTwoHandedPrimary(eCharacterClass.Armsman, 5, true,
                eObjectType.PolearmWeapon), Is.True);
            Assert.That(GameBot.ShouldUsePlannedTwoHandedPrimary(eCharacterClass.Armsman, 5, true,
                eObjectType.TwoHandedWeapon), Is.False);
            Assert.That(GameBot.ShouldUsePlannedTwoHandedPrimary(eCharacterClass.Armsman, 9, true,
                eObjectType.TwoHandedWeapon), Is.False);
            Assert.That(GameBot.ShouldUsePlannedTwoHandedPrimary(eCharacterClass.Armsman, 10, true,
                eObjectType.TwoHandedWeapon), Is.True);
            Assert.That(BotWeaponStats.PrimaryType(eObjectType.ThrustWeapon, eObjectType.PolearmWeapon,
                GameBot.ShouldUsePlannedTwoHandedPrimary(eCharacterClass.Armsman, 4, true,
                    eObjectType.PolearmWeapon)), Is.EqualTo(eObjectType.ThrustWeapon));
            Assert.That(BotWeaponStats.PrimaryType(eObjectType.ThrustWeapon, eObjectType.PolearmWeapon,
                GameBot.ShouldUsePlannedTwoHandedPrimary(eCharacterClass.Armsman, 5, true,
                    eObjectType.PolearmWeapon)), Is.EqualTo(eObjectType.PolearmWeapon));
            Assert.That(BotWeaponStats.PrimaryType(eObjectType.SlashingWeapon, eObjectType.TwoHandedWeapon,
                GameBot.ShouldUsePlannedTwoHandedPrimary(eCharacterClass.Armsman, 9, true,
                    eObjectType.TwoHandedWeapon)), Is.EqualTo(eObjectType.SlashingWeapon));
            Assert.That(BotWeaponStats.PrimaryType(eObjectType.SlashingWeapon, eObjectType.TwoHandedWeapon,
                GameBot.ShouldUsePlannedTwoHandedPrimary(eCharacterClass.Armsman, 10, true,
                    eObjectType.TwoHandedWeapon)), Is.EqualTo(eObjectType.TwoHandedWeapon));
            Assert.That(GameBot.ShouldUsePlannedTwoHandedPrimary(eCharacterClass.Warrior, 1, true,
                eObjectType.Sword), Is.True, "Unrelated classes retain their existing behavior");
            Assert.That(GameBot.ShouldUsePlannedTwoHandedPrimary(eCharacterClass.Paladin, 50, false), Is.False);
        });
    }

    private static int Modified(eProperty property) => property switch {
        eProperty.Strength or eProperty.Dexterity => 60, eProperty.WeaponSkill => 100, _ => 0 };
    private sealed class ChampionBot : GameBot
    {
        private ChampionBot() : base((OfflineWorldBotRecord)null) { }
        public override byte Level { get; set; }
        public override ICharacterClass CharacterClass => new ClassChampion();
        public override int GetModified(eProperty property) => Modified(property);
        public override int WeaponSpecLevel(DbInventoryItem item) => 1;
        public override double Effectiveness { get => 1; set { } }
    }
    private sealed class ChampionPlayer : GamePlayer
    {
        private ChampionPlayer() : base(null, null) { }
        public override byte Level { get; set; }
        public override ICharacterClass CharacterClass => new ClassChampion();
        public override int GetModified(eProperty property) => Modified(property);
        public override int WeaponSpecLevel(DbInventoryItem item) => 1;
        public override double Effectiveness { get => 1; set { } }
    }
    private sealed class ClassedBot : GameBot
    {
        private ClassedBot() : base((OfflineWorldBotRecord)null) { }
        public ICharacterClass Class;
        public override byte Level { get; set; }
        public override ICharacterClass CharacterClass => Class;
        public override bool HasAbilityToUseItem(DbItemTemplate item) => true;
    }

    [TestCase(typeof(ClassBard), false)]
    [TestCase(typeof(ClassDruid), false)]
    [TestCase(typeof(ClassChampion), false)]
    [TestCase(typeof(ClassBlademaster), true)]
    public void OnlyDualWieldClassesTakeAnOffhandWeapon(Type cls, bool offhand)
    {
        var bot = (ClassedBot)RuntimeHelpers.GetUninitializedObject(typeof(ClassedBot));
        bot.Level = 20;
        bot.Class = (ICharacterClass)Activator.CreateInstance(cls);
        DbItemTemplate sword = LargeWeapon();
        sword.Object_Type = (int)eObjectType.Blades;
        sword.Item_Type = Slot.LEFTHAND;
        sword.Hand = 0;
        DbInventoryItem item = GameInventoryItem.Create(sword);
        item.LevelRequirement = 1;

        Assert.That(BotWeaponStats.CanWieldOffhandWeapon(bot), Is.EqualTo(offhand));
        Assert.That(BotWeaponStats.FitsConfiguredSlot(bot, item, eInventorySlot.LeftHandWeapon), Is.EqualTo(offhand));
        Assert.That(BotWeaponStats.FitsConfiguredSlot(bot, item, eInventorySlot.RightHandWeapon), Is.True);
    }

    [Test]
    public void ChampionTrainedInLargeWeaponsFitsTheTwoHandedBuildBetterThanTheLevelOnePick()
    {
        var trained = new Dictionary<string, int>
        {
            [Specs.Large_Weapons] = 25, [Specs.Valor] = 25, [Specs.Blades] = 1,
        };
        int Fit(BotSpec plan)
        {
            BotLifetimeBuild.AlignWithInvestedWeapons(plan, line => trained.GetValueOrDefault(line));
            return AccountCompanionRules.PlanFit(
                plan.SpecLines.Select(line => new AccountCompanionRules.PlanLine(line.Spec, (int)line.SpecCap, line.levelRatio)),
                line => trained.GetValueOrDefault(line));
        }

        BotSpec oneHand = new ChampionBotSpec(eSpecType.OneHandAndShield);
        BotSpec twoHand = new ChampionBotSpec(eSpecType.TwoHanded);
        Assert.That(oneHand.Is2H, Is.False);
        Assert.That(twoHand.Is2H, Is.True);
        Assert.That(Fit(twoHand), Is.GreaterThan(Fit(oneHand)));
    }

    private static int Fit(BotSpec plan, Func<string, int> trained) =>
        AccountCompanionRules.PlanFit(
            plan.SpecLines.Select(line => new AccountCompanionRules.PlanLine(line.Spec, (int)line.SpecCap, line.levelRatio)),
            trained);

    private static System.Collections.Generic.IEnumerable<eCharacterClass> ClassesWithBotBuilds() =>
        Enum.GetValues<eCharacterClass>().Where(cls =>
            BotSpec.GetSpec(cls, BotSpec.GetSpecializationChoices(cls)[0]) != null).Distinct();

    /// <summary>
    /// Every class, every build: a companion whose plan was picked at level 1 (the class's first
    /// build) but whose owner trained another build's lines ends up on a plan that fits those
    /// lines at least as well, carrying the weapon line the owner trained most.
    /// </summary>
    [Test]
    public void EveryClassCompanionFollowsTheBuildItsOwnerTrained()
    {
        var failures = new List<string>();
        int checkedBuilds = 0;
        foreach (eCharacterClass cls in ClassesWithBotBuilds())
        {
            var choices = BotSpec.GetSpecializationChoices(cls);
            string levelOnePlan = BotLifetimeBuild.Encode(BotSpec.GetSpec(cls, choices[0]));
            foreach (eSpecType choice in choices)
            {
                for (int roll = 0; roll < 6; roll++)
                {
                    BotSpec source = BotSpec.GetSpec(cls, choice);
                    if (source == null) continue;
                    var points = source.SpecLines
                        .Where(line => !string.IsNullOrWhiteSpace(line.Spec))
                        .GroupBy(line => line.Spec)
                        .ToDictionary(group => group.Key, group => Math.Max(1, (int)(group.Max(line => line.SpecCap) * 0.6)));
                    int Trained(string line) => line != null && points.TryGetValue(line, out int value) ? value : 1;

                    BotSpec picked = GameBot.PickCompanionBuild(cls, levelOnePlan, null, Trained, out _);
                    checkedBuilds++;
                    if (picked == null) { failures.Add($"{cls}/{choice}: no plan"); continue; }
                    if (Fit(picked, Trained) < Fit(source, Trained))
                        failures.Add($"{cls}/{choice}: kept {picked.SpecType} fit {Fit(picked, Trained)} < {source.SpecType} fit {Fit(source, Trained)}");

                    string weaponLine = new[] { source.WeaponOneType, source.WeaponTwoType }
                        .Where(type => type != 0 && BotWeaponStats.IsMeleeWeapon(type))
                        .Select(SkillBase.ObjectTypeToSpec)
                        .Where(line => !string.IsNullOrEmpty(line) && Trained(line) > 1)
                        .OrderByDescending(Trained).FirstOrDefault();
                    if (weaponLine != null && !picked.SpecLines.Any(line => line.Spec == weaponLine))
                        failures.Add($"{cls}/{choice}: trained {weaponLine} but plan {picked.SpecType} has no such line");
                }
            }
        }
        Assert.That(checkedBuilds, Is.GreaterThan(100));
        Assert.That(failures.Distinct(), Is.Empty);
    }

    /// <summary>A plan with an offhand weapon only for classes that can dual wield, and the reverse.</summary>
    [Test]
    public void EveryOffhandBuildBelongsToADualWieldClass()
    {
        var failures = new List<string>();
        foreach (eCharacterClass cls in ClassesWithBotBuilds())
        {
            var characterClass = ScriptMgr.FindCharacterClass((int)cls);
            if (characterClass == null) continue;
            foreach (eSpecType choice in BotSpec.GetSpecializationChoices(cls))
                for (int roll = 0; roll < 6; roll++)
                {
                    BotSpec plan = BotSpec.GetSpec(cls, choice);
                    bool offhand = plan?.SpecType is eSpecType.DualWield or eSpecType.DualWieldAndShield or eSpecType.LeftAxe;
                    if (offhand && !characterClass.CanUseLefthandedWeapon)
                        failures.Add($"{cls}/{choice} plans {plan.SpecType} but the class cannot dual wield");
                }
        }
        Assert.That(failures.Distinct(), Is.Empty);
    }

    /// <summary>The class rule itself: exactly the live dual wielders and Maulers.</summary>
    [Test]
    public void DualWieldClassesAreTheLiveOnes()
    {
        var expected = new[]
        {
            eCharacterClass.Mercenary, eCharacterClass.Infiltrator, eCharacterClass.Blademaster,
            eCharacterClass.Nightshade, eCharacterClass.Ranger, eCharacterClass.Berserker,
            eCharacterClass.Shadowblade, eCharacterClass.Savage,
            eCharacterClass.MaulerAlb, eCharacterClass.MaulerMid, eCharacterClass.MaulerHib,
        };
        var actual = Enum.GetValues<eCharacterClass>().Distinct()
            .Where(cls => ScriptMgr.FindCharacterClass((int)cls)?.CanUseLefthandedWeapon == true)
            .ToArray();
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    private sealed class MeleeEligibilityBot : GameBot
    {
        private MeleeEligibilityBot() : base((OfflineWorldBotRecord)null) { }
        public override byte Level { get; set; }
        public override bool HasAbilityToUseItem(DbItemTemplate item) =>
            (eObjectType)item.Object_Type != eObjectType.Staff;
    }
    private sealed class BroadAbilityBot : GameBot
    {
        private BroadAbilityBot() : base((OfflineWorldBotRecord)null) { }
        public override byte Level { get; set; }
        public override bool HasAbilityToUseItem(DbItemTemplate item) => true;
    }

    [Test]
    public void NativeDamageAndWeaponSkillMatchPlayerAndBadTemplateExplainsZeroBeforeTargetModifiers()
    {
        var bot = (ChampionBot)RuntimeHelpers.GetUninitializedObject(typeof(ChampionBot));
        var player = (ChampionPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ChampionPlayer));
        bot.Level = player.Level = 1;
        // Use the actual native damage/skill methods. No world, AI turns, or damage stubs.
        var botAttack = new AttackComponent(bot);
        var playerAttack = new AttackComponent(player);
        foreach (int dps in new[] { 1, 15 })
        {
            var weapon = GameInventoryItem.Create(LargeWeapon(dps));
            weapon.SlotPosition = Slot.TWOHAND;
            double damage = botAttack.WeaponDamage(weapon, null, 1, out double cap);
            Assert.That(damage, Is.EqualTo(playerAttack.WeaponDamage(weapon, null, 1, out double playerCap)).Within(1e-9));
            Assert.That(cap, Is.EqualTo(playerCap));
            Assert.That(bot.GetWeaponSkill(weapon), Is.EqualTo(player.GetWeaponSkill(weapon)).Within(1e-9));
            Assert.That(bot.GetWeaponStat(weapon), Is.EqualTo(60));
            if (dps == 1) Assert.That((int)damage, Is.Zero);
            else Assert.That((int)damage, Is.GreaterThan(0));
        }
    }
}
