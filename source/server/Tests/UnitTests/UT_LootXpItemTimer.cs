using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.Database;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // Loot redesign phase 6 (2026-11-06): the timed XP item drops (LootTemplate rows with a negative chance, the cooldown in
    // minutes) had two copies in LootGeneratorTemplate. These tests pin what both copies did, written before they were
    // merged, so the merge provably changed nothing. The two paths differ in one way, kept on purpose: a mob with
    // MobXLootTemplate rows reports the time left on every kill during the cooldown; one without reports it only on the
    // kill that takes 20 seconds off.
    [TestFixture, NonParallelizable]
    public class UT_LootXpItemTimer
    {
        private sealed class Mob : GameNPC
        {
            public override byte Level { get; set; }
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
        }

        private sealed class Player : GamePlayer
        {
            private Player() : base(null, null) { }
            public IPacketLib TestOut;
            public override IPacketLib Out => TestOut;
            public override eRealm Realm { get; set; }
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
        }

        // Records SendMessage text; every other packet is ignored.
        public class RecordingOut : DispatchProxy
        {
            public List<string> Messages = [];

            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name == "SendMessage" && args.Length > 0 && args[0] is string text)
                    Messages.Add(text);
                return method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
            }
        }

        private sealed class TestServer : GameServer
        {
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
            protected override IObjectDatabase DataBaseImpl => Db;
        }

        public class ReadOnlyDb : DispatchProxy
        {
            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name == "FindObjectByKey" && method.ReturnType == typeof(DbItemTemplate))
                    return Items.TryGetValue(Convert.ToString(args[0]) ?? string.Empty, out DbItemTemplate item) ? item : null;
                if (!method.Name.StartsWith("Select") && !method.Name.StartsWith("Find"))
                    throw new InvalidOperationException("loot test attempted a database write: " + method.Name);
                Type result = method.ReturnType;
                if (result.IsGenericType && typeof(IEnumerable).IsAssignableFrom(result))
                    return Activator.CreateInstance(typeof(List<>).MakeGenericType(result.GetGenericArguments()[0]));
                return null;
            }
        }

        private static readonly IObjectDatabase Db = DispatchProxy.Create<IObjectDatabase, ReadOnlyDb>();
        private static readonly Dictionary<string, DbItemTemplate> Items = new(StringComparer.OrdinalIgnoreCase);
        private static readonly FieldInfo LoopTime = typeof(GameLoop).GetField("<GameLoopTime>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);

        private sealed class Generator : LootGeneratorTemplate
        {
            public static void Use(Dictionary<string, Dictionary<string, DbLootTemplate>> loot, Dictionary<string, List<DbMobXLootTemplate>> mobx)
            {
                m_lootTemplates = loot;
                m_mobXLootTemplates = mobx;
            }
        }

        private GameServer _previous;
        private long _previousTime;
        private Player _player;
        private RecordingOut _out;

        [SetUp]
        public void SetUp()
        {
            _previous = GameServer.Instance;
            _previousTime = GameLoop.GameLoopTime;
            GameServer.LoadTestDouble((TestServer)RuntimeHelpers.GetUninitializedObject(typeof(TestServer)));
            Items.Clear();
            Items["xp_scroll"] = new DbItemTemplate { Id_nb = "xp_scroll", Name = "xp scroll", Realm = 0 };

            _player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            typeof(GameLiving).GetField("<TempProperties>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_player, new PropertyCollection());
            typeof(GameLiving).GetField(nameof(GameLiving.XpGainersLock)).SetValue(_player, new Lock());
            _player.Realm = eRealm.Albion;
            var proxy = DispatchProxy.Create<IPacketLib, RecordingOut>();
            _out = (RecordingOut)(object)proxy;
            _player.TestOut = proxy;
        }

        [TearDown]
        public void TearDown()
        {
            Generator.Use(null, null);
            LoopTime.SetValue(null, _previousTime);
            GameServer.LoadTestDouble(_previous);
        }

        private const long Minute = 60_000;
        private const long Start = 10_000_000;

        // A three minute XP item on the mob's own table, with or without MobXLootTemplate rows naming it.
        private LootList Kill(bool withMobX, long at)
        {
            LoopTime.SetValue(null, at);
            var table = new Dictionary<string, DbLootTemplate>
            {
                ["xp_scroll"] = new DbLootTemplate { TemplateName = "scroll keeper", ItemTemplateID = "xp_scroll", Chance = -3, Count = 1 },
            };
            var mobx = new Dictionary<string, List<DbMobXLootTemplate>>();
            if (withMobX)
                mobx["scroll keeper"] = [new DbMobXLootTemplate { MobName = "scroll keeper", LootTemplateName = "scroll keeper", DropCount = 1 }];
            Generator.Use(new() { ["scroll keeper"] = table }, mobx);

            var mob = (Mob)RuntimeHelpers.GetUninitializedObject(typeof(Mob));
            typeof(GameLiving).GetField("<TempProperties>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(mob, new PropertyCollection());
            typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(mob, new ArrayList());
            mob.Name = "scroll keeper";
            var generator = (LootGeneratorTemplate)RuntimeHelpers.GetUninitializedObject(typeof(LootGeneratorTemplate));
            return generator.GenerateLoot(mob, _player);
        }

        private static int Scrolls(LootList loot) => loot.GetLoot().Count(item => item.Id_nb == "xp_scroll");
        private long Timer => _player.TempProperties.GetProperty<long>("XP_Item");

        [TestCase(false)]
        [TestCase(true)]
        public void TheFirstKillDropsTheItemAndStartsTheTimer(bool withMobX)
        {
            Assert.That(Scrolls(Kill(withMobX, Start)), Is.EqualTo(1));
            Assert.That(Timer, Is.EqualTo(Start));
            Assert.That(_out.Messages, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AKillDuringTheCooldownTakesTwentySecondsOffAndSaysSo(bool withMobX)
        {
            Kill(withMobX, Start);
            Assert.That(Scrolls(Kill(withMobX, Start + Minute)), Is.EqualTo(0));
            Assert.That(Timer, Is.EqualTo(Start - 20_000));
            Assert.That(_out.Messages, Is.EqualTo(new[] { "0h 1m 40s until next XP item" }));
        }

        [TestCase(false, 0)]
        [TestCase(true, 1)]
        public void TheSameMobAgainOnlyTakesTimeOffOnce(bool withMobX, int messages)
        {
            Kill(withMobX, Start);
            Kill(withMobX, Start + Minute);
            _out.Messages.Clear();
            Assert.That(Scrolls(Kill(withMobX, Start + 2 * Minute)), Is.EqualTo(0));
            Assert.That(Timer, Is.EqualTo(Start - 20_000));
            Assert.That(_out.Messages, Has.Count.EqualTo(messages), "only a mob with MobX rows repeats the time left");
            if (messages > 0)
                Assert.That(_out.Messages[0], Is.EqualTo("0h 0m 40s until next XP item"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TheItemDropsAgainOnceTheCooldownIsOver(bool withMobX)
        {
            Kill(withMobX, Start);
            Kill(withMobX, Start + Minute);
            Assert.That(Scrolls(Kill(withMobX, Start + 3 * Minute)), Is.EqualTo(1), "three minutes less the 20 seconds have passed");
            Assert.That(Timer, Is.EqualTo(Start + 3 * Minute));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ASimulationNeverTouchesTheTimer(bool withMobX)
        {
            LootList loot = null;
            LootSimulator.Run(() => { loot = Kill(withMobX, Start); return []; }, 1);
            Assert.That(Scrolls(loot), Is.EqualTo(0));
            Assert.That(Timer, Is.EqualTo(0));
        }
    }
}
