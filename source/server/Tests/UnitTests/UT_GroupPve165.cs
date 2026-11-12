using DOL.AI.Brain;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>Group PvE fixes (2026-11-06 list): don't break mez, pulls, main assist, chain pulls, group make-up.</summary>
    [TestFixture]
    public sealed class UT_GroupPve165
    {
        [Test]
        public void TanksNeverProtectAgainstAMesmerizedMonster() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.IsProtectionThreat(true, false), Is.True, "a monster hitting a member is taken off it");
            Assert.That(BotBrain.IsProtectionThreat(true, true), Is.False, "a mezzed monster still points at its last victim");
            Assert.That(BotBrain.IsProtectionThreat(false, false), Is.False);
        });
    }
}
