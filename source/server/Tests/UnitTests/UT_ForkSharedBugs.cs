using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using DOL.AI;
using DOL.AI.Brain;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.RealmAbilities;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Bugs found in the 2026-11-08 fork survey that our code shared with other forks (DR-199, DR-200).</summary>
    [TestFixture]
    public class UT_ForkSharedBugs
    {
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private sealed class TestBot : GameBot
        {
            private TestBot() : base((OfflineWorldBotRecord)null) { }
        }

        private sealed class TestPlayer : GamePlayer
        {
            private TestPlayer() : base(null, null) { }
            public IPacketLib TestOut;
            public override IPacketLib Out => TestOut;
        }

        private static T Uninitialized<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

        private static void SetBrain(GameNPC npc, ABrain brain)
        {
            typeof(GameNPC).GetField("m_brains", PrivateInstance).SetValue(npc, new System.Collections.ArrayList());
            typeof(GameNPC).GetField("m_ownBrain", PrivateInstance).SetValue(npc, brain);
        }

        [Test]
        public void PetOnlyScaresTreatAGamebotAsAPlayerNotAPet()
        {
            TestBot bot = Uninitialized<TestBot>();
            SetBrain(bot, Uninitialized<BotBrain>());
            GameNPC pet = Uninitialized<GameNPC>();
            SetBrain(pet, Uninitialized<ControlledMobBrain>());

            Assert.That(bot.Brain, Is.InstanceOf<IControlledBrain>(), "The trap: a gamebot's brain is a controlled brain");
            Assert.That(ModernNaturesMadness.IsEnemyPet(null, bot), Is.False, "Nature's Madness must not scare a gamebot");
            Assert.That(ModernNaturesMadness.IsEnemyPet(null, pet), Is.True, "It still scares a real pet");
            Assert.That(WarlockPetFearHandler.IsEnemyPet(null, bot), Is.False,
                "Warlock pet fear, area pet scares and Malefic Horror must not take a gamebot for a pet");
        }

        [Test]
        public void MoneyAwardsAndPurchasesOnManyThreadsLoseNoCoin()
        {
            TestPlayer player = Uninitialized<TestPlayer>();
            player.TestOut = DispatchProxy.Create<IPacketLib, UT_RealmExchangeEconomy.NoOpPacketLib>();
            player.AddMoney(1_000_000);

            int bought = 0;
            Parallel.For(0, 20_000, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
            {
                if (i % 2 == 0)
                    player.AddMoney(123);
                else if (player.RemoveMoney(77))
                    System.Threading.Interlocked.Increment(ref bought);
            });

            Assert.That(bought, Is.EqualTo(10_000));
            Assert.That(player.GetCurrentMoney(), Is.EqualTo(1_000_000 + 10_000L * 123 - 10_000L * 77));
        }

        [Test]
        public void RemoveMoneyRefusesMoreThanThePlayerHas()
        {
            TestPlayer player = Uninitialized<TestPlayer>();
            player.TestOut = DispatchProxy.Create<IPacketLib, UT_RealmExchangeEconomy.NoOpPacketLib>();
            player.AddMoney(500);
            Assert.That(player.RemoveMoney(501), Is.False);
            Assert.That(player.RemoveMoney(500), Is.True);
            Assert.That(player.GetCurrentMoney(), Is.Zero);
        }
    }
}
