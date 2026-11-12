using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_StormTempest
    {
        [Test]
        public void Slots_LightOneOrbPerChargeFromTheFirstSlot()
        {
            Assert.That(StormTempest.Slots(0, 0x66), Is.EqualTo(new byte[] { 0, 0, 0, 0, 0 }));
            Assert.That(StormTempest.Slots(3, 0x66), Is.EqualTo(new byte[] { 0x66, 0x66, 0x66, 0, 0 }));
            Assert.That(StormTempest.Slots(5, 0x33), Is.EqualTo(new byte[] { 0x33, 0x33, 0x33, 0x33, 0x33 }));
        }

        [Test]
        public void Slots_AlwaysFiveAndClamped()
        {
            Assert.That(StormTempest.Slots(9, 0x11), Has.Length.EqualTo(5).And.All.EqualTo((byte)0x11));
            Assert.That(StormTempest.Slots(-2, 0x11), Is.All.EqualTo((byte)0));
        }

        [TestCase("0x66", (byte)0x66)]
        [TestCase("0X0a", (byte)0x0A)]
        [TestCase("102", (byte)102)]
        [TestCase("3", (byte)3)]
        public void TryParseCode_AcceptsHexAndDecimal(string text, byte expected)
        {
            Assert.That(StormTempest.TryParseCode(text, out byte code), Is.True);
            Assert.That(code, Is.EqualTo(expected));
        }

        [TestCase("")]
        [TestCase("0x")]
        [TestCase("256")]
        [TestCase("blue")]
        public void TryParseCode_RejectsBadInput(string text)
        {
            Assert.That(StormTempest.TryParseCode(text, out _), Is.False);
        }
    }
}
