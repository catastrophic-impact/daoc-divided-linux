using System;
using DOL.AI.Brain;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_SummonTemplateBrain
    {
        [Test]
        public void AWorldMonsterClassIsNotAPetBrain()
        {
            Assert.That(SummonSpellHandler.ResolvePetBrainType("DOL.GS.GameNPC"), Is.Null);
            Assert.That(SummonSpellHandler.ResolvePetBrainType(""), Is.Null);
            Assert.That(SummonSpellHandler.ResolvePetBrainType("No.Such.Type"), Is.Null);
        }

        [Test]
        public void APetBrainClassIsUsed()
        {
            Type t = SummonSpellHandler.ResolvePetBrainType(typeof(ControlledMobBrain).FullName);
            Assert.That(t, Is.EqualTo(typeof(ControlledMobBrain)).Or.Null);
            if (t != null)
                Assert.That(typeof(IControlledBrain).IsAssignableFrom(t), Is.True);
        }
    }
}
