using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Bots for the expansion, live and royal classes (modern client test kits).</summary>
[TestFixture, NonParallelizable]
public sealed class UT_BotModernClasses
{
    private static readonly eCharacterClass[] ModernClasses =
    [
        eCharacterClass.Heretic, eCharacterClass.MaulerAlb, eCharacterClass.DeathKnight, eCharacterClass.PendragonSovereign,
        eCharacterClass.Valkyrie, eCharacterClass.Warlock, eCharacterClass.MaulerMid, eCharacterClass.Stormforged,
        eCharacterClass.Bainshee, eCharacterClass.Vampiir, eCharacterClass.MaulerHib, eCharacterClass.TuathaSovereign,
    ];

    private bool[] _saved;
    private bool _savedBotCustom;

    private static ref bool Switch(int index)
    {
        switch (index)
        {
            case 0: return ref Properties.ENABLE_HERETIC_TEST;
            case 1: return ref Properties.ENABLE_DEATH_KNIGHT_TEST;
            case 2: return ref Properties.ENABLE_VALKYRIE_TEST;
            case 3: return ref Properties.ENABLE_WARLOCK_TEST;
            case 4: return ref Properties.ENABLE_BAINSHEE_TEST;
            case 5: return ref Properties.ENABLE_VAMPIIR_TEST;
            case 6: return ref Properties.ENABLE_MAULER_TEST;
            case 7: return ref Properties.ENABLE_STORMFORGED;
            case 8: return ref Properties.ENABLE_PENDRAGON;
            default: return ref Properties.ENABLE_TUATHA;
        }
    }

    private static void SetAll(bool value)
    {
        for (int i = 0; i < 10; i++)
            Switch(i) = value;
        Properties.BOTS_USE_CUSTOM_CLASSES = value;
    }

    [SetUp]
    public void Save()
    {
        _saved = Enumerable.Range(0, 10).Select(i => Switch(i)).ToArray();
        _savedBotCustom = Properties.BOTS_USE_CUSTOM_CLASSES;
    }

    [TearDown]
    public void Restore()
    {
        for (int i = 0; i < 10; i++)
            Switch(i) = _saved[i];
        Properties.BOTS_USE_CUSTOM_CLASSES = _savedBotCustom;
    }

    private static readonly eCharacterClass[] CustomClasses =
        [eCharacterClass.Stormforged, eCharacterClass.PendragonSovereign, eCharacterClass.TuathaSovereign];

    [Test]
    public void BotsLeaveTheCustomClassesToPlayers_UnlessTheBotSwitchIsOn()
    {
        SetAll(true);
        Properties.BOTS_USE_CUSTOM_CLASSES = false;     // the default (owner, DR-dev-0.1g)
        var realms = new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia };
        var botClasses = realms.SelectMany(r => AutonomousBotIdentityGenerator.GetEraClasses(r)).ToArray();
        var playerPicks = realms.SelectMany(r => AutonomousBotIdentityGenerator.GetEraClasses(r, forPlayer: true)).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(botClasses, Has.None.Matches<eCharacterClass>(CustomClasses.Contains), "gamebots never roll them");
            Assert.That(botClasses, Has.Length.EqualTo(48), "the other nine newer classes stay");
            Assert.That(playerPicks, Is.SupersetOf(CustomClasses), "a /spawn helper the player picks can be one");
            Assert.Throws<ArgumentException>(() =>
                AutonomousBotIdentityGenerator.GenerateForClass(eRealm.Midgard, eGender.Male, eCharacterClass.Stormforged));
            Assert.That(AutonomousBotIdentityGenerator.GenerateForClass(eRealm.Midgard, eGender.Male, eCharacterClass.Stormforged,
                forPlayer: true).CharacterClass, Is.EqualTo(eCharacterClass.Stormforged));
            Assert.That(AutonomousBotIdentityGenerator.IsClassAvailable(eCharacterClass.Stormforged), Is.True,
                "loot and players still see the class");
        });
        var random = new Random(7);
        var names = new HashSet<string>();
        for (int i = 0; i < 600; i++)
            Assert.That(CustomClasses, Does.Not.Contain(AutonomousBotIdentityGenerator.Generate(
                realms[i % 3], i % 2 == 0 ? eGender.Male : eGender.Female, names, random).CharacterClass));
        Properties.BOTS_USE_CUSTOM_CLASSES = true;
        Assert.That(realms.SelectMany(r => AutonomousBotIdentityGenerator.GetEraClasses(r)), Is.SupersetOf(CustomClasses));
    }

    [Test]
    public void EverySpecializationChoiceBuildsItsOwnPlan([ValueSource(nameof(ModernClasses))] eCharacterClass characterClass)
    {
        IReadOnlyList<eSpecType> choices = BotSpec.GetSpecializationChoices(characterClass);
        foreach (eSpecType choice in choices)
        {
            for (int roll = 0; roll < 20; roll++)
            {
                BotSpec spec = BotSpec.GetSpec(characterClass, choice);
                Assert.That(spec, Is.Not.Null, $"{characterClass} has no bot plan");
                Assert.That(spec.SpecLines, Is.Not.Empty);
                Assert.That(spec.SpecLines.Select(line => line.Spec), Is.Unique);
                Assert.That(spec.SpecLines, Has.All.Matches<BotSpecLine>(line => line.SpecCap is >= 1 and <= 50));
                if (choice != eSpecType.None)
                    Assert.That(spec.SpecType, Is.EqualTo(choice), $"{characterClass} {choice}");
            }
        }
    }

    [Test]
    public void SovereignsTrainWeaponsShieldsAndParryButNotStealth(
        [Values(eCharacterClass.Stormforged, eCharacterClass.PendragonSovereign, eCharacterClass.TuathaSovereign)]
        eCharacterClass characterClass)
    {
        for (int roll = 0; roll < 20; roll++)
        {
            BotSpec spec = BotSpec.GetSpec(characterClass, BotSpec.ChooseRandomSpecialization(characterClass));
            Assert.That(spec.SpecLines.Select(line => line.Spec), Has.None.EqualTo(Specs.Stealth));
            Assert.That(spec.SpecLines.Select(line => line.Spec), Does.Contain(Specs.Parry));
            if (spec.Is2H)
                Assert.That(spec.WeaponTwoType, Is.Not.EqualTo((eObjectType)0));
        }
    }

    [Test]
    public void MaulerPlansUseStaffOrFistWraps()
    {
        BotSpec staff = new MaulerBotSpec(eSpecType.TwoHanded);
        BotSpec fists = new MaulerBotSpec(eSpecType.DualWield);
        Assert.Multiple(() =>
        {
            Assert.That(staff.Is2H, Is.True);
            Assert.That(staff.WeaponTwoType, Is.EqualTo(eObjectType.MaulerStaff));
            Assert.That(staff.SpecLines[0].Spec, Is.EqualTo(Specs.Mauler_Staff));
            Assert.That(fists.WeaponOneType, Is.EqualTo(eObjectType.FistWraps));
            Assert.That(fists.SpecLines[0].Spec, Is.EqualTo(Specs.Fist_Wraps));
        });
    }

    [Test]
    public void SwitchesOff_KeepTheClassicSiCatalog()
    {
        SetAll(false);
        var classes = new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }
            .SelectMany(AutonomousBotIdentityGenerator.GetEraClasses).ToArray();
        Assert.That(classes, Has.None.Matches<eCharacterClass>(ModernClasses.Contains));
        Assert.That(classes, Has.Length.EqualTo(39));
        Assert.Throws<ArgumentException>(() =>
            AutonomousBotIdentityGenerator.GenerateForClass(eRealm.Midgard, eGender.Female, eCharacterClass.Valkyrie));
    }

    [Test]
    public void SwitchesOn_OfferEveryNewClassInItsRealm()
    {
        SetAll(true);
        var albion = AutonomousBotIdentityGenerator.GetEraClasses(eRealm.Albion);
        var midgard = AutonomousBotIdentityGenerator.GetEraClasses(eRealm.Midgard);
        var hibernia = AutonomousBotIdentityGenerator.GetEraClasses(eRealm.Hibernia);
        Assert.Multiple(() =>
        {
            Assert.That(albion, Is.SupersetOf(new[] { eCharacterClass.Heretic, eCharacterClass.MaulerAlb, eCharacterClass.DeathKnight, eCharacterClass.PendragonSovereign }));
            Assert.That(midgard, Is.SupersetOf(new[] { eCharacterClass.Valkyrie, eCharacterClass.Warlock, eCharacterClass.MaulerMid, eCharacterClass.Stormforged }));
            Assert.That(hibernia, Is.SupersetOf(new[] { eCharacterClass.Bainshee, eCharacterClass.Vampiir, eCharacterClass.MaulerHib, eCharacterClass.TuathaSovereign }));
            Assert.That(albion.Count + midgard.Count + hibernia.Count, Is.EqualTo(51));
        });
    }

    [Test]
    public void OneSwitchAddsOnlyItsOwnClasses()
    {
        SetAll(false);
        Properties.ENABLE_MAULER_TEST = true;
        Assert.That(AutonomousBotIdentityGenerator.GetEraClasses(eRealm.Midgard), Does.Contain(eCharacterClass.MaulerMid));
        Assert.That(AutonomousBotIdentityGenerator.GetEraClasses(eRealm.Midgard), Does.Not.Contain(eCharacterClass.Valkyrie));
        Assert.That(AutonomousBotIdentityGenerator.GetEraClasses(eRealm.Albion), Does.Contain(eCharacterClass.MaulerAlb));
    }

    [Test]
    public void GeneratedIdentitiesUseLegalClassicRaces([ValueSource(nameof(ModernClasses))] eCharacterClass characterClass)
    {
        SetAll(true);
        eRealm realm = Array.IndexOf(ModernClasses, characterClass) switch
        {
            < 4 => eRealm.Albion,
            < 8 => eRealm.Midgard,
            _ => eRealm.Hibernia
        };
        var random = new Random((int)characterClass);
        for (int i = 0; i < 30; i++)
        {
            var identity = AutonomousBotIdentityGenerator.GenerateForClass(realm, i % 2 == 0 ? eGender.Male : eGender.Female,
                characterClass, random: random);
            Assert.That(identity.CharacterClass, Is.EqualTo(characterClass));
            Assert.That((int)identity.Race, Is.InRange(1, 15), "bots keep to the classic races");
            Assert.That(AutonomousBotIdentityGenerator.GetEligibleRaces(characterClass), Does.Contain(identity.Race));
        }
    }

    [Test]
    public void ModernClassesHaveRealDisplayNames([ValueSource(nameof(ModernClasses))] eCharacterClass characterClass) =>
        Assert.That(BotManager.GetClassNameById((byte)characterClass), Is.Not.EqualTo("Fighter"));

    [Test]
    public void SovereignsTankAndValkyriesHeal()
    {
        Assert.That(BotPartyRoles.For(eCharacterClass.Stormforged), Is.EqualTo(BotPartyRole.Tank));
        Assert.That(BotPartyRoles.For(eCharacterClass.PendragonSovereign), Is.EqualTo(BotPartyRole.Tank));
        Assert.That(BotPartyRoles.For(eCharacterClass.TuathaSovereign), Is.EqualTo(BotPartyRole.Tank));
        Assert.That(BotPartyRoles.IsHealingClass(eCharacterClass.Valkyrie), Is.True);
    }

    private static Spell Spell(eSpellType type, int id = 1, string target = "Self", int value = 0) =>
        new(new DbSpell { SpellID = id, Type = type.ToString(), Target = target, Value = value }, 1);

    [TestCase(eSpellType.WarlockChamber)]
    [TestCase(eSpellType.Chamber)]
    [TestCase(eSpellType.WarlockPrimer)]
    [TestCase(eSpellType.DeathKnightConjure)]
    [TestCase(eSpellType.DeathKnightGround)]
    [TestCase(eSpellType.DeathKnightRevert)]
    public void PlayerOnlyKitSpellsAreRemoved(eSpellType type) =>
        Assert.That(BotClassKitPolicy.IsUsableByBot(Spell(type)), Is.False);

    [Test]
    public void OrdinarySpellsStay() =>
        Assert.That(BotClassKitPolicy.IsUsableByBot(Spell(eSpellType.DirectDamage, target: "Enemy")), Is.True);

    [Test]
    public void OnlyTheDecrepitFormIsKept()
    {
        Assert.That(BotClassKitPolicy.IsUsableByBot(Spell(eSpellType.DeathKnightForm, value: 1)), Is.True);
        Assert.That(BotClassKitPolicy.IsUsableByBot(Spell(eSpellType.DeathKnightForm, value: 2)), Is.False);
        Assert.That(BotClassKitPolicy.IsUsableByBot(Spell(eSpellType.DeathKnightForm, value: 3)), Is.False);
    }

    [Test]
    public void SovereignTogglesAndStealthOpenerAreRemoved()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BotClassKitPolicy.IsUsableByBot(Spell(eSpellType.StormSovereignAbility, 62400 + 6)), Is.False, "toggle");
            Assert.That(BotClassKitPolicy.IsUsableByBot(Spell(eSpellType.StormSovereignAbility, 62400 + 17)), Is.False, "stealth opener");
            Assert.That(BotClassKitPolicy.IsUsableByBot(Spell(eSpellType.StormSovereignAbility, 62400 + 7)), Is.True, "group buff");
            // The Pendragon copy of the same toggle.
            Assert.That(BotClassKitPolicy.IsUsableByBot(Spell(eSpellType.StormSovereignAbility, 62400 + 1600 + 13)), Is.False);
            Assert.That(BotClassKitPolicy.IsUsableByBot(Spell(eSpellType.StormSovereignAbility, 62400, "Enemy")), Is.True, "strike");
        });
    }

    [Test]
    public void MeleeHealersWaitOutTheirOwnSwingOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BotBrain.HealWaitsForOwnSwing(instant: false, uninterruptible: false, selfInterrupted: true, interruptedByOther: false), Is.True);
            Assert.That(BotBrain.HealWaitsForOwnSwing(false, false, true, interruptedByOther: true), Is.False, "being hit: the heal path already skips");
            Assert.That(BotBrain.HealWaitsForOwnSwing(instant: true, false, true, false), Is.False);
            Assert.That(BotBrain.HealWaitsForOwnSwing(false, uninterruptible: true, true, false), Is.False);
            Assert.That(BotBrain.HealWaitsForOwnSwing(false, false, selfInterrupted: false, false), Is.False);
        });
    }

    [Test]
    public void VampiirAndShamanCastBetweenSwings()
    {
        Assert.That(ShamanBotCombatPolicy.CastsBetweenSwings(eCharacterClass.Vampiir), Is.True);
        Assert.That(ShamanBotCombatPolicy.CastsBetweenSwings(eCharacterClass.Shaman), Is.True);
        Assert.That(ShamanBotCombatPolicy.CastsBetweenSwings(eCharacterClass.Champion), Is.True);
        Assert.That(ShamanBotCombatPolicy.CastsBetweenSwings(eCharacterClass.Reaver), Is.True);
        Assert.That(ShamanBotCombatPolicy.CastsBetweenSwings(eCharacterClass.Valkyrie), Is.True);
        Assert.That(ShamanBotCombatPolicy.CastsBetweenSwings(eCharacterClass.MaulerMid), Is.True);
        Assert.That(ShamanBotCombatPolicy.CastsBetweenSwings(eCharacterClass.Heretic), Is.True, "channels Arawn's Singe in melee reach");
        Assert.That(ShamanBotCombatPolicy.RotationPriority(new Spell(new DbSpell { Type = "RampingDamageFocus", Target = "Enemy" }, 1)), Is.EqualTo(0),
            "the Heretic's focus damage comes first");
        Assert.That(ShamanBotCombatPolicy.CastsBetweenSwings(eCharacterClass.Warrior), Is.False, "pure melee has no attack spells");
        Assert.That(ShamanBotCombatPolicy.IsHybrid(eCharacterClass.Vampiir), Is.False, "root rules stay the Shaman's");
    }

    [TestCase(eSpellType.DirectDamage)]
    [TestCase(eSpellType.DamageOverTime)]
    [TestCase(eSpellType.Lifedrain)]
    [TestCase(eSpellType.Stun)]
    [TestCase(eSpellType.Disease)]
    [TestCase(eSpellType.StormSovereignAbility)]
    public void InstantAttacksAreFiredForEveryClass(eSpellType type) =>
        // Regression: Reaver Soul Wench, Valkyrie Odin's Heel and other instants were never cast after the
        // Bonedancer command case landed inside this list and made every type use the commander check.
        Assert.That(BotBrain.IsInstantAttackType(type), Is.True);

    [Test]
    public void BonedancerCommandsAreNotInTheSharedInstantList() =>
        Assert.That(BotBrain.IsInstantAttackType(eSpellType.CommanderCommand), Is.False, "they have their own commander test");

    [Test]
    public void StaffAndSpearBuildsStartWithTheirTwoHander()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BotWeaponStats.PrimaryType(eObjectType.FistWraps, eObjectType.MaulerStaff, true), Is.EqualTo(eObjectType.MaulerStaff));
            Assert.That(BotWeaponStats.PrimaryType(eObjectType.Sword, eObjectType.Spear, true), Is.EqualTo(eObjectType.Spear));
            Assert.That(BotWeaponStats.PrimaryType(eObjectType.Sword, eObjectType.Spear, false), Is.EqualTo(eObjectType.Sword));
        });
    }

    [Test]
    public void PointBlankSpellsReachTheirRadius()
    {
        Assert.That(BotBrain.OffensiveReach(range: 0, radius: 300, effectiveRange: 0), Is.EqualTo(300), "Shrill Aura");
        Assert.That(BotBrain.OffensiveReach(range: 1500, radius: 350, effectiveRange: 1500), Is.EqualTo(1500), "ranged area nuke");
        Assert.That(BotBrain.OffensiveReach(range: 0, radius: 0, effectiveRange: 0), Is.EqualTo(1));
    }

    [Test]
    public void SovereignDecreesWaitForTheFight()
    {
        Spell decree = Spell(eSpellType.StormSovereignAbility, id: 62631);
        Spell royalDecree = Spell(eSpellType.StormSovereignAbility, id: 62631 + PendragonRules.SpellOffset);
        Spell tuathaDecree = Spell(eSpellType.StormSovereignAbility, id: 62633 + PendragonRules.TuathaOffset);
        Spell renewal = Spell(eSpellType.StormSovereignAbility, id: 62634);
        Assert.Multiple(() =>
        {
            Assert.That(BotClassKitPolicy.WaitsForContact(decree, inCombat: false), Is.True);
            Assert.That(BotClassKitPolicy.WaitsForContact(royalDecree, inCombat: false), Is.True);
            Assert.That(BotClassKitPolicy.WaitsForContact(tuathaDecree, inCombat: false), Is.True);
            Assert.That(BotClassKitPolicy.WaitsForContact(decree, inCombat: true), Is.False, "fighting");
            Assert.That(BotClassKitPolicy.WaitsForContact(renewal, inCombat: false), Is.False, "the self heal does not wait");
            Assert.That(BotClassKitPolicy.WaitsForContact(Spell(eSpellType.BothAblativeArmor), inCombat: false), Is.False);
        });
    }

    [Test]
    public void OnlyCastTimeKitSpellsOwnTheTurn()
    {
        Spell channel = new(new DbSpell { SpellID = 1, Type = nameof(eSpellType.DeathKnightRegenerate), Target = "Self", CastTime = 2.5 }, 1);
        Assert.Multiple(() =>
        {
            Assert.That(BotClassKitPolicy.OwnsTurn(channel), Is.True);
            Assert.That(BotClassKitPolicy.OwnsTurn(Spell(eSpellType.StormSovereignAbility, id: 62631)), Is.False, "instant Decree");
        });
    }

    [Test]
    public void CombatSupportKitSpells()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BotClassKitPolicy.IsCombatSupport(Spell(eSpellType.DeathKnightRegenerate)), Is.True);
            Assert.That(BotClassKitPolicy.IsCombatSupport(Spell(eSpellType.PowerTransfer, target: "Realm")), Is.True);
            Assert.That(BotClassKitPolicy.IsCombatSupport(Spell(eSpellType.BothAblativeArmor, target: "Realm")), Is.True);
            Assert.That(BotClassKitPolicy.IsCombatSupport(Spell(eSpellType.StrengthBuff)), Is.False);
            Assert.That(BotClassKitPolicy.Handles(Spell(eSpellType.DeathKnightRegenerate)), Is.True);
            Assert.That(BotClassKitPolicy.CommanderCanAct(null, Spell(eSpellType.CommanderCommand, target: "Enemy"), null), Is.False);
            // Ranged casters only fire instant damage spells; a commander order is an attack.
            Assert.That(BotCasterPriority.AllowInstant(Spell(eSpellType.CommanderCommand, target: "Enemy"), rangedCaster: true, meleePressure: false), Is.True);
        });
    }

    [Test]
    public void KitUpkeepCoversStancesChantsAndSummonsButNotHarmfulAbilities()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BotClassKitPolicy.Handles(Spell(eSpellType.MaulerStance)), Is.True);
            Assert.That(BotClassKitPolicy.Handles(Spell(eSpellType.StormforgedChant)), Is.True);
            Assert.That(BotClassKitPolicy.Handles(Spell(eSpellType.StormSovereignWolf)), Is.True);
            Assert.That(BotClassKitPolicy.Handles(Spell(eSpellType.DeathKnightServant)), Is.True);
            Assert.That(BotClassKitPolicy.Handles(Spell(eSpellType.StormSovereignAbility, 62407)), Is.True);
            Assert.That(BotClassKitPolicy.Handles(Spell(eSpellType.StormSovereignAbility, 62400, "Enemy")), Is.False);
            Assert.That(BotClassKitPolicy.Handles(Spell(eSpellType.StrengthBuff)), Is.False);
        });
    }
}
