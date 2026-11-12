using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.Logging;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Night playtest 2026-11-04: Storm Wolf, Faerie Drake and Royal Lion pets sent a model+size packet every brain
    /// tick, because StormWolf.SyncOwner set Size each think and the GameNPC.Size setter always broadcast.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_NpcSizeBroadcast
    {
        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private GameServer _previousServer;

        private sealed class InertServer : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
        }

        /// <summary>Counts how often the setter asks whether it is in the world, which it does only to broadcast.</summary>
        private sealed class CountingNpc : GameNPC
        {
            public int StateReads;
            public override eObjectState ObjectState
            {
                get { StateReads++; return eObjectState.Inactive; }
                set { }
            }
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

        [Test]
        public void SettingTheSameSizeSendsNothing()
        {
            var npc = (CountingNpc)RuntimeHelpers.GetUninitializedObject(typeof(CountingNpc));
            npc.Size = 50;
            int afterFirst = npc.StateReads;
            Assert.That(afterFirst, Is.GreaterThan(0), "a new size is broadcast");

            npc.Size = 50;
            Assert.That(npc.StateReads, Is.EqualTo(afterFirst), "the same size again is not");

            npc.Size = 70;
            Assert.That(npc.StateReads, Is.GreaterThan(afterFirst));
            Assert.That(npc.Size, Is.EqualTo(70));
        }

        [Test]
        public void StormWolfSizes()
        {
            Assert.That(StormWolf.SizeFor(false), Is.EqualTo(50));
            Assert.That(StormWolf.SizeFor(true), Is.EqualTo(70));
        }
    }
}
