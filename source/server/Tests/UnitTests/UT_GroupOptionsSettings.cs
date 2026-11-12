using DOL.GS.PacketHandler.Client.v168;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_GroupOptionsSettings
    {
        [TestCase(new byte[] { 0, 0 })]
        [TestCase(new byte[] { 1, 0 })]
        [TestCase(new byte[] { 0, 1 })]
        [TestCase(new byte[] { 1, 1 })]
        public void IsValid_ValidSettings_ReturnsTrue(byte[] settings)
        {
            Assert.That(GroupOptionsSettings.IsValid(settings), Is.True);
        }

        [TestCase(new byte[] { 2, 0 })]
        [TestCase(new byte[] { 0, 2 })]
        [TestCase(new byte[] { 1 })]
        [TestCase(new byte[] { 1, 1, 0 })]
        [TestCase(null)]
        public void IsValid_InvalidSettings_ReturnsFalse(byte[] settings)
        {
            Assert.That(GroupOptionsSettings.IsValid(settings), Is.False);
        }
    }
}
