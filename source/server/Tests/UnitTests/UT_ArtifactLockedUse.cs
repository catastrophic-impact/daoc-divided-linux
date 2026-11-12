using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.Logging;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Night playtest 2026-11-04: using an artifact below level 5 only printed "You attempt to use the Eerie Darkness
    /// Stone.", and a credit item only "You attempt to use the Snatcher Credit.".
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_ArtifactLockedUse
    {
        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private GameServer _previousServer;

        private sealed class InertServer : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
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

        private static ArtifactItem Stone(long experience)
        {
            var item = new ArtifactItem(new DbItemTemplate { Id_nb = "eerie_test", Name = "Eerie Darkness Stone", SpellID = 900001, SpellID1 = 900002 });
            item.Experience = experience;
            return item;
        }

        [Test]
        public void LockedUsesSayWhenTheyOpen()
        {
            ArtifactItem stone = Stone(0);
            string first = stone.LockedUseMessage(1);
            Assert.That(first, Does.Contain("unlocks at artifact level 5").And.Contain("It is level 0"));
            Assert.That(stone.LockedUseMessage(2), Does.Contain("unlocks at artifact level 10"));
        }

        [Test]
        public void OpenUsesSayNothing()
        {
            Assert.That(Stone(ArtifactLevels.XpForLevel(5)).LockedUseMessage(1), Is.Null);
            Assert.That(Stone(ArtifactLevels.XpForLevel(5)).LockedUseMessage(2), Is.Not.Null, "use 2 still waits for 10");
            Assert.That(Stone(ArtifactLevels.MaxXp).LockedUseMessage(2), Is.Null);
        }

        [Test]
        public void CreditsPointToTheScholar()
        {
            Assert.That(ArtifactItem.CreditUseText("Snatcher"), Does.Contain("Artifact Scholar").And.Contain("Snatcher"));
        }
    }
}
