using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Living Server 7adb58b15: the server console runs commands as an admin account with no player.</summary>
    [TestFixture]
    public class UT_ConsoleCommandClient
    {
        [Test]
        public void TheConsoleRunsCommandsAsAnAdminWithoutAPlayer()
        {
            GameClient console = ConsoleCommandClient.Create();
            Assert.That(console.Account, Is.Not.Null);
            Assert.That(console.Account.PrivLevel, Is.EqualTo((uint)ePrivLevel.Admin));
            Assert.That(console.Account.Name, Is.EqualTo(ConsoleCommandClient.AccountName));
            Assert.That(console.Player, Is.Null);
        }
    }
}
