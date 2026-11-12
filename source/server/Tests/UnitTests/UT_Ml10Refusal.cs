using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.Trials;
using DOL.Logging;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Night playtest 2026-11-04: an ML10 stone printed "Sagittarius begins." and "The stone flares with cold
    /// starlight." before "The stone stays dark..." for a leader below Master Level 9.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_Ml10Refusal
    {
        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private GameServer _previousServer;

        private sealed class InertServer : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
        }

        private sealed class TrialPlayer : GamePlayer
        {
            private TrialPlayer() : base(null, null) { }
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

        private static GamePlayer Leader(int masterLevel)
        {
            var player = (TrialPlayer)RuntimeHelpers.GetUninitializedObject(typeof(TrialPlayer));
            typeof(GamePlayer).GetField("m_dbCharacter", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(player, new DbCoreCharacter { MLLevel = masterLevel });
            return player;
        }

        [Test]
        public void EveryStoneRefusesBeforeTheRunBegins()
        {
            foreach (TrialDefinition trial in Ml10.Trials())
            {
                Assert.That(trial.Refuse, Is.Not.Null, trial.Key);
                Assert.That(trial.Refuse(Leader(5)), Is.EqualTo(Ml10.UnreadyText), trial.Key);
                Assert.That(trial.Refuse(Leader(9)), Is.Null, trial.Key);
            }
        }

        [Test]
        public void OtherTrialsHaveNoExtraRefusal()
        {
            Assert.That(Ml9.Trials().All(trial => trial.Refuse == null), Is.True);
        }
    }
}
