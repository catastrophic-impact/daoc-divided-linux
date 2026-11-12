using System.Linq;
using System.Reflection;
using DOL.GS;
using DOL.GS.Effects;
using DOL.GS.PropertyCalc;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Rules from the official patch notes that the 2026-11-08 game-rules audit found missing (DR-184, 189, 190).</summary>
[TestFixture]
public class UT_RulesAuditMajors
{
    [TestCase(eCharacterClass.Armsman, 40, 0)]
    [TestCase(eCharacterClass.Armsman, 41, 2)]
    [TestCase(eCharacterClass.Warrior, 45, 8)]
    [TestCase(eCharacterClass.Hero, 49, 14)]
    [TestCase(eCharacterClass.Hero, 50, 15)]
    [TestCase(eCharacterClass.Warrior, 60, 15)]
    [TestCase(eCharacterClass.Paladin, 50, 0)]
    [TestCase(eCharacterClass.Wizard, 50, 0)]
    public void PureTanksGainSpellDamageResistanceFrom41To50(eCharacterClass characterClass, int level, int expected)
    {
        Assert.That(TankMagicResistance.Percent(characterClass, level), Is.EqualTo(expected));
    }

    [Test]
    public void TankResistanceNeverDropsAsLevelsRise()
    {
        int previous = 0;
        for (int level = 1; level <= 50; level++)
        {
            int now = TankMagicResistance.Percent(eCharacterClass.Armsman, level);
            Assert.That(now, Is.GreaterThanOrEqualTo(previous), $"level {level}");
            Assert.That(now - previous, Is.LessThanOrEqualTo(2), $"level {level}");
            previous = now;
        }
    }

    [Test]
    public void BlockCapBetweenPlayersDefaultsToSixtyPercent()
    {
        FieldInfo field = typeof(Properties).GetField(nameof(Properties.BLOCK_CAP));
        ServerPropertyAttribute attribute = field.GetCustomAttribute<ServerPropertyAttribute>();
        Assert.That(System.Convert.ToDouble(attribute.DefaultValue), Is.EqualTo(0.60).Within(0.0001));
    }

    [Test]
    public void StatDebuffsNoLongerCarryASpellIdInterruptList()
    {
        Assert.That(typeof(StatDebuffECSEffect).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Any(method => method.Name == "TryDebuffInterrupt"), Is.False,
            "live: Reaver pulsing debuffs (1.72) and Bonedancer instant body debuffs (1.87) do not interrupt");
    }
}
