using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using DOL.Database;
using DOL.GS;
using DOL.GS.PacketHandler.Client.v168;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture, NonParallelizable]
public sealed class UT_ModernRaceCreation
{
    private bool previous;

    [SetUp]
    public void Enable()
    {
        previous = Properties.ENABLE_MODERN_RACES;
        Properties.ENABLE_MODERN_RACES = true;
    }

    [TearDown]
    public void Restore() => Properties.ENABLE_MODERN_RACES = previous;

    private static DbCoreCharacter Character(int race, int realm, int career)
    {
        var stats = GlobalConstants.STARTING_STATS_DICT[(eRace)race];
        return new DbCoreCharacter
        {
            Name = "Racetest", AccountName = "unit-test", Level = 1,
            Race = race, Realm = realm, Class = career, Gender = 0,
            Strength = stats[eStat.STR] + 10, Constitution = stats[eStat.CON] + 10,
            Dexterity = stats[eStat.DEX] + 10, Quickness = stats[eStat.QUI],
            Intelligence = stats[eStat.INT], Piety = stats[eStat.PIE],
            Empathy = stats[eStat.EMP], Charisma = stats[eStat.CHR]
        };
    }

    public static IEnumerable<TestCaseData> EnabledClientChoices()
    {
        using var stream = typeof(ModernRaceCreationRules).Assembly.GetManifestResourceStream("DOL.GS.ModernRaceRegistrations.json")!;
        using var data = JsonDocument.Parse(stream);
        int[] disabled = { 20, 33, 39, 58, 59, 60, 61, 62 };
        foreach (var entry in data.RootElement.EnumerateArray())
        {
            int career = entry.GetProperty("Class").GetInt32();
            if (disabled.Contains(career)) continue;
            foreach (var race in entry.GetProperty("Races").EnumerateArray())
                yield return new TestCaseData(race.GetInt32(), entry.GetProperty("Realm").GetInt32(), career);
        }
    }

    [TestCaseSource(nameof(EnabledClientChoices))]
    public void EveryEnabledExpansionChoicePassesCreationValidation(int race, int realm, int career)
    {
        var character = Character(race, realm, career);
        if (career == (int)eCharacterClass.Valkyrie) character.Gender = 1;
        Assert.That(CharacterCreateRequestHandler.IsCharacterValid(character), Is.True);
    }

    [TestCase(16, 1, 2)] // Half-Ogre Armsman
    [TestCase(17, 2, 21)] // Frostalf Thane
    [TestCase(18, 3, 44)] // Shar Hero
    [TestCase(19, 1, 2)] // Albion Minotaur Armsman
    [TestCase(20, 2, 22)] // Midgard Minotaur Warrior
    [TestCase(21, 3, 44)] // Hibernia Minotaur Hero
    public void ExpansionRaceCanCreateWithNormalStats(int race, int realm, int career)
        => Assert.That(CharacterCreateRequestHandler.IsCharacterValid(Character(race, realm, career)), Is.True);

    [TestCase(16, 1, 2)]
    [TestCase(17, 2, 21)]
    [TestCase(18, 3, 44)]
    [TestCase(19, 1, 2)]
    [TestCase(20, 2, 22)]
    [TestCase(21, 3, 44)]
    public void ClassicProfileKeepsOriginalRestrictions(int race, int realm, int career)
    {
        Properties.ENABLE_MODERN_RACES = false;
        Assert.That(CharacterCreateRequestHandler.IsCharacterValid(Character(race, realm, career)), Is.False);
    }

    // Base-race pairs the stock 1.130 client offers that the classic lists left out (checked 2026-11-09).
    [TestCase(2, 1, 12)] // Avalonian Necromancer
    [TestCase(14, 2, 21)] // Valkyn Thane
    [TestCase(8, 2, 31)] // Kobold Berserker
    [TestCase(7, 2, 28)] // Dwarf Shaman
    public void ClientBaseRacePairCanCreate(int race, int realm, int career)
        => Assert.That(CharacterCreateRequestHandler.IsCharacterValid(Character(race, realm, career)), Is.True);

    [TestCase(2, 1, 12)]
    [TestCase(14, 2, 21)]
    [TestCase(8, 2, 31)]
    [TestCase(7, 2, 28)]
    public void ClientBaseRacePairStaysClassicWhenModernRacesAreOff(int race, int realm, int career)
    {
        Properties.ENABLE_MODERN_RACES = false;
        Assert.That(CharacterCreateRequestHandler.IsCharacterValid(Character(race, realm, career)), Is.False);
    }

    [Test]
    public void NormalCombinationsAndStatBudgetStillApply()
    {
        Assert.That(CharacterCreateRequestHandler.IsCharacterValid(Character(16, 1, 3)), Is.False); // Half-Ogre Scout
        Assert.That(CharacterCreateRequestHandler.IsCharacterValid(Character(19, 2, 22)), Is.False); // Wrong realm
        var character = Character(18, 3, 44);
        character.Strength += 1;
        Assert.That(CharacterCreateRequestHandler.IsCharacterValid(character), Is.False);
    }

    [Test]
    public void MinotaurRemainsMaleOnly()
    {
        var character = Character(20, 2, 22);
        character.Gender = 1;
        Assert.That(CharacterCreateRequestHandler.IsCharacterValid(character), Is.False);
    }

    [Test]
    public void EveryRaceHasAllEightStartingStats()
    {
        foreach (int race in Enumerable.Range(1, 21))
            Assert.That(GlobalConstants.STARTING_STATS_DICT[(eRace)race].Count, Is.EqualTo(8));
    }
}

