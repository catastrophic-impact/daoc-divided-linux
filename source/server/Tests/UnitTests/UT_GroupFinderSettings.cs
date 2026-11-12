using DOL.GS.PacketHandler.Client.v168;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_GroupFinderSettings
    {
        [TestCase(new byte[] { 0, 0, 0, 0 })]
        [TestCase(new byte[] { 1, 0, 1, 4 })]
        [TestCase(new byte[] { 1, 1, 3, 0 })]
        public void IsValid_ValidSettings_ReturnsTrue(byte[] settings)
        {
            Assert.That(GroupFinderSettings.IsValid(settings), Is.True);
        }

        [TestCase(new byte[] { 2, 0, 1, 2 })]
        [TestCase(new byte[] { 1, 2, 1, 2 })]
        [TestCase(new byte[] { 1, 0, 5, 0 })]
        [TestCase(new byte[] { 1, 0, 0, 5 })]
        [TestCase(new byte[] { 1, 0, 1 })]
        [TestCase(new byte[] { 1, 0, 1, 2, 0 })]
        [TestCase(null)]
        public void IsValid_InvalidSettings_ReturnsFalse(byte[] settings)
        {
            Assert.That(GroupFinderSettings.IsValid(settings), Is.False);
        }
    }
}
