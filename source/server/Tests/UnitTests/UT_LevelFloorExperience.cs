using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Night playtest 2026-11-04: `/player level 50` left Experience at 0, so the first kill
    /// "topped up" 169,999,999,950 XP and poured it into Master Levels and artifacts.
    /// </summary>
    [TestFixture]
    public class UT_LevelFloorExperience
    {
        [Test]
        public void TopUpToTheFloorIsAppliedButNotEarned()
        {
            long floor = GamePlayer.GetExperienceAmountForLevel(49);
            long applied = GamePlayer.ApplyExperienceFloor(0, 50, floor, out long earned);
            Assert.That(applied, Is.EqualTo(floor), "stored experience still reaches the floor");
            Assert.That(earned, Is.EqualTo(50), "only the kill's 50 XP counts");
        }

        [Test]
        public void NormalGainIsUnchanged()
        {
            long floor = 1000;
            long applied = GamePlayer.ApplyExperienceFloor(1500, 300, floor, out long earned);
            Assert.That(applied, Is.EqualTo(300));
            Assert.That(earned, Is.EqualTo(300));
        }

        [Test]
        public void LossStopsAtTheFloorAndIsNotAGain()
        {
            long applied = GamePlayer.ApplyExperienceFloor(1100, -500, 1000, out long earned);
            Assert.That(applied, Is.EqualTo(-100));
            Assert.That(earned, Is.EqualTo(-100));

            applied = GamePlayer.ApplyExperienceFloor(200, -500, 1000, out earned);
            Assert.That(applied, Is.EqualTo(800), "a below-floor save is still lifted");
            Assert.That(earned, Is.Zero, "but a loss never shows as gained experience");
        }

        private sealed class LevelPlayer : GamePlayer
        {
            private LevelPlayer() : base(null, null) { }
            public override byte Level { get; set; }
            public override DOL.GS.PacketHandler.IPacketLib Out => new BotDummyPacketLib();
        }

        [TestCase((byte)1, (byte)50)]
        [TestCase((byte)30, (byte)45)]
        [TestCase((byte)50, (byte)20)]
        public void SettingALevelDirectlyPutsExperienceOnItsFloor(byte from, byte to)
        {
            var player = (LevelPlayer)RuntimeHelpers.GetUninitializedObject(typeof(LevelPlayer));
            typeof(GamePlayer).GetField("m_dbCharacter", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(player, new DOL.Database.DbCoreCharacter());
            player.Level = from;
            player.Experience = 0;
            player.IsLevelSecondStage = true;

            player.SetLevelDirect(to);

            Assert.That(player.Level, Is.EqualTo(to));
            Assert.That(player.Experience, Is.EqualTo(player.GetExperienceNeededForLevel(to)));
            Assert.That(player.IsLevelSecondStage, Is.False);
            long applied = GamePlayer.ApplyExperienceFloor(player.Experience, 50, player.ExperienceForCurrentLevel, out long earned);
            Assert.That(applied, Is.EqualTo(50));
            Assert.That(earned, Is.EqualTo(50), "the next kill pays only what it is worth");
        }
    }
}
