using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using static DOL.GS.GameClient;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_LoginDeniedCode
    {
        [TearDown]
        public void TurnAccountNoticeOff()
        {
            Properties.LOGIN_ACCOUNT_NOTICE = false;
        }

        // "Unknown error." is 0x15 up to 1.127 and 99 on 1.130, where 0x15 is the account notice.
        [TestCase(eClientVersion.Version1127, 0x15)]
        [TestCase(eClientVersion.Version1129, 0x15)]
        [TestCase(eClientVersion.Version1130, 99)]
        public void Unknown_IsUnknownErrorForTheClient(eClientVersion version, int expected)
        {
            Assert.That(PacketLib168.LoginDeniedCode(eLoginError.Unknown, version), Is.EqualTo(expected));
        }

        [TestCase(eClientVersion.Version1127, false, 0x15)]
        [TestCase(eClientVersion.Version1127, true, 0x15)]
        [TestCase(eClientVersion.Version1130, false, 99)]
        [TestCase(eClientVersion.Version1130, true, 0x16)]
        public void AccountNotice_ReachesOnly1130AndOnlyWhenTurnedOn(eClientVersion version, bool accountNotice, int expected)
        {
            Properties.LOGIN_ACCOUNT_NOTICE = accountNotice;

            Assert.That(PacketLib168.LoginDeniedCode(eLoginError.AccountNotice, version), Is.EqualTo(expected));
        }

        [TestCase(eClientVersion.Version1127)]
        [TestCase(eClientVersion.Version1130)]
        public void OtherCodes_AreSentAsIs(eClientVersion version)
        {
            Assert.That(PacketLib168.LoginDeniedCode(eLoginError.BannedAccount, version), Is.EqualTo(0x14));
            Assert.That(PacketLib168.LoginDeniedCode(eLoginError.IncorrectClientVersion, version), Is.EqualTo(0x05));
        }
    }
}
