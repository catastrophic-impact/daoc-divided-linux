using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Discord report 2026-11-05 (Abermenai): a dead gamebot did not wait for a Heretic's rez. The Heretic (Revive at
    /// level 10) and the Valkyrie (Valkyrie Mending) resurrect in the world's spell data and count as rez classes.
    /// </summary>
    [TestFixture]
    public class UT_RezClasses
    {
        [TestCase(eCharacterClass.Cleric)]
        [TestCase(eCharacterClass.Friar)]
        [TestCase(eCharacterClass.Heretic)]
        [TestCase(eCharacterClass.Healer)]
        [TestCase(eCharacterClass.Shaman)]
        [TestCase(eCharacterClass.Valkyrie)]
        [TestCase(eCharacterClass.Druid)]
        [TestCase(eCharacterClass.Warden)]
        [TestCase(eCharacterClass.Bard)]
        public void ClassesThatResurrectAreRezClasses(eCharacterClass characterClass) =>
            Assert.That(AutonomousLivingServer.IsRezClassId((int)characterClass), Is.True);

        [TestCase(eCharacterClass.Armsman)]
        [TestCase(eCharacterClass.Wizard)]
        [TestCase(eCharacterClass.Berserker)]
        [TestCase(eCharacterClass.Enchanter)]
        public void OtherClassesAreNot(eCharacterClass characterClass) =>
            Assert.That(AutonomousLivingServer.IsRezClassId((int)characterClass), Is.False);

        [Test]
        public void NoPlayerCannotRez() => Assert.That(AutonomousLivingServer.CanRez(null), Is.False);
    }
}
