using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.Spells;
using DOL.Logging;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Night playtest 2026-11-04 text bugs: heals showed the full heal instead of the hit points restored, pet styles
    /// and lifetaps read as the owner's own, empty spell messages printed blank lines, and "You steal oaken treant for
    /// 55 endurance!".
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_SpellMessageText
    {
        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private GameServer _previousServer;

        private sealed class InertServer : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
        }

        private sealed class TextPlayer : GamePlayer
        {
            private TextPlayer() : base(null, null) { }
            public int Sent;
            public override DOL.GS.PacketHandler.IPacketLib Out => throw new System.InvalidOperationException("sent: " + ++Sent);
        }

        [OneTimeSetUp]
        public void InitializeLogging() => LoggerManager.InitializeWithExplicitLibrary(null, LogLibrary.None);

        [SetUp]
        public void SetUp()
        {
            _previousServer = GameServer.Instance;
            GameServer.LoadTestDouble((InertServer)RuntimeHelpers.GetUninitializedObject(typeof(InertServer)));
        }

        [TearDown]
        public void TearDown() => GameServer.LoadTestDouble(_previousServer);

        [TestCase(460, 823, 460, 0, TestName = "capped heal shows what it restored")]
        [TestCase(823, 823, 823, 0, TestName = "full heal unchanged")]
        [TestCase(900, 800, 800, 100, TestName = "critical extra is what the base heal did not cover")]
        [TestCase(750, 800, 750, 0, TestName = "capped critical heal shows no extra")]
        [TestCase(0, 800, 0, 0, TestName = "nothing restored")]
        public void HealsReportHitPointsRestored(int restored, int preCritical, int expectedBase, int expectedCritical)
        {
            (int shown, int critical) = HealSpellHandler.SplitRestored(restored, preCritical);
            Assert.That(shown, Is.EqualTo(expectedBase));
            Assert.That(critical, Is.EqualTo(expectedCritical));
        }

        [Test]
        public void PetLinesNameThePet()
        {
            var player = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(TextPlayer));
            var pet = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
            pet.Name = "bone golem";

            Assert.That(SpellHandler.CasterSentence(player, $"steal {SpellHandler.Points(40, "hit")}.", "x"), Is.EqualTo("You steal 40 hit points."));
            Assert.That(SpellHandler.CasterSentence(pet, "x", $"steals {SpellHandler.Points(1, "hit")}."), Is.EqualTo("Your bone golem steals 1 hit point."));
        }

        [Test]
        public void EnduranceDrainReadsRight()
        {
            var player = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(TextPlayer));
            Assert.That(EnduranceDrainSpellHandler.DrainText(player, "the oaken treant", 55), Is.EqualTo("You drain 55 endurance from the oaken treant!"));
            Assert.That(EnduranceDrainSpellHandler.DrainedText("Rtwovamp", 55), Is.EqualTo("Rtwovamp drains 55 endurance from you!"));
        }

        [Test]
        public void EmptyMessagesAreNotSent()
        {
            var player = (TextPlayer)RuntimeHelpers.GetUninitializedObject(typeof(TextPlayer));
            Assert.DoesNotThrow(() => player.MessageToSelf("", DOL.GS.PacketHandler.eChatType.CT_Spell));
            Assert.DoesNotThrow(() => player.MessageToSelf(null, DOL.GS.PacketHandler.eChatType.CT_Spell));
            Assert.Throws<System.InvalidOperationException>(() => player.MessageToSelf("text", DOL.GS.PacketHandler.eChatType.CT_Spell));
        }

        [Test]
        public void PetStyleLineHasItsTranslation()
        {
            string line = null;
            for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null && line == null; dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, "GameServer", "language", "EN", "GameObjects", "GamePlayer.txt");
                if (File.Exists(path))
                    line = File.ReadAllLines(path).FirstOrDefault(l => l.StartsWith("StyleProcessor.ExecuteStyle.PerformsPerfectly:"));
            }

            Assert.That(line, Is.Not.Null);
            string text = line[(line.IndexOf(':') + 1)..].Trim();
            Assert.That(string.Format(text, "bone golem", "Plague", "(+26, GR: 0.572)"), Is.EqualTo("Your bone golem performs its Plague perfectly. (+26, GR: 0.572)"));
        }
    }
}
