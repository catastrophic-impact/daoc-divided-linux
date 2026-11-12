using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerRules;
using DOL.Logging;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Night playtest 2026-11-04: a tester killed by Peridas still got Orion's Belt credit, XP and Atlantean Glass.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_DeadPlayerRewards
    {
        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private GameServer _previousServer;

        private sealed class InertServer : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
        }

        private sealed class RewardPlayer : GamePlayer
        {
            private RewardPlayer() : base(null, null) { }
            public bool Alive;
            public eObjectState State;
            public override bool IsAlive => Alive;
            public override eObjectState ObjectState { get => State; set => State = value; }
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

        private static RewardPlayer Player(bool alive, GameObject.eObjectState state = GameObject.eObjectState.Active)
        {
            var player = (RewardPlayer)RuntimeHelpers.GetUninitializedObject(typeof(RewardPlayer));
            player.Alive = alive;
            player.State = state;
            return player;
        }

        [Test]
        public void OnlyTheLivingEarnEncounterCreditAndGlass()
        {
            Assert.That(ArtifactEncounters.CanEarnReward(Player(true)), Is.True);
            Assert.That(ArtifactEncounters.CanEarnReward(Player(false)), Is.False, "dead");
            Assert.That(ArtifactEncounters.CanEarnReward(Player(true, GameObject.eObjectState.Inactive)), Is.False, "logged out");
            Assert.That(ArtifactEncounters.CanEarnReward(null), Is.False);
        }

        [Test]
        public void DeadPlayersGetNoKillExperience()
        {
            Assert.That(AbstractServerRules.CanEarnNpcKillReward(Player(true)), Is.True);
            Assert.That(AbstractServerRules.CanEarnNpcKillReward(Player(false)), Is.False);
        }

        [Test]
        public void AwardCreditSkipsTheDead()
        {
            // A dead player is skipped before progress is read, so no database or packets are touched.
            Assert.DoesNotThrow(() => ArtifactEncounters.AwardCredit([Player(false)], "orions_belt"));
        }
    }
}
