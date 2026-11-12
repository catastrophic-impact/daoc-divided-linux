using System;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_BotTimeline
    {
        private static readonly DateTime T = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

        private static BotTimeline.Snapshot S(string goal = "Grind rats", string activity = "Walking", ushort region = 1,
            string zone = "Camelot Hills", int level = 10, int deaths = 0, bool alive = true, bool combat = false,
            string target = "", bool stalled = false) =>
            new(goal, activity, region, zone, level, deaths, alive, combat, target, stalled);

        [Test]
        public void FirstSightingIsOneEvent()
        {
            var e = BotTimeline.Compare(null, S(), T);
            Assert.That(e.Select(x => x.Kind), Is.EqualTo(new[] { "seen" }));
        }

        [Test]
        public void NothingChangedMeansNoEvents()
        {
            Assert.That(BotTimeline.Compare(S(), S(), T), Is.Empty);
        }

        [Test]
        public void DeathNamesWhatItWasFighting()
        {
            var e = BotTimeline.Compare(S(combat: true, target: "giant rat"), S(deaths: 1, alive: false), T);
            Assert.That(e.First().Kind, Is.EqualTo("death"));
            Assert.That(e.First().Text, Does.Contain("giant rat"));
        }

        [Test]
        public void LevelMoveGoalFightAndStallAreEachRecorded()
        {
            var e = BotTimeline.Compare(S(), S(goal: "Grind boars", region: 51, zone: "Avalon Isle", level: 11, combat: true,
                target: "boar", stalled: true, activity: "Pulling boar"), T);
            Assert.That(e.Select(x => x.Kind), Is.EqualTo(new[] { "level", "move", "goal", "fight", "stalled", "activity" }));
        }

        [Test]
        public void TheHistoryKeepsTheNewestEventsFirst()
        {
            const long id = -424242;
            BotTimeline.Forget(id);
            for (int i = 0; i < BotTimeline.MaxEvents + 5; i++)
                BotTimeline.Record(id, new[] { new BotTimeline.Event(T.AddSeconds(i), "activity", $"step {i}") });
            var all = BotTimeline.For(id);
            Assert.That(all.Length, Is.EqualTo(BotTimeline.MaxEvents));
            Assert.That(all[0].Text, Is.EqualTo($"step {BotTimeline.MaxEvents + 4}"));
            BotTimeline.Forget(id);
        }
    }
}
