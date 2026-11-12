using System.Collections.Generic;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests.Gameserver
{
    [TestFixture]
    public class UT_BracketBotPlanner
    {
        private static Dictionary<eRealm, int> Present(int alb, int mid, int hib) =>
            new() { [eRealm.Albion] = alb, [eRealm.Midgard] = mid, [eRealm.Hibernia] = hib };

        [Test]
        public void NeedsAnother_BuildsUpOneAtATimeUntilTheRealmHasTheWantedNumber()
        {
            Assert.That(BracketBotPlanner.NeedsAnother(12, 0), Is.True);
            Assert.That(BracketBotPlanner.NeedsAnother(12, 11), Is.True);
            Assert.That(BracketBotPlanner.NeedsAnother(12, 12), Is.False);
            Assert.That(BracketBotPlanner.NeedsAnother(12, 15), Is.False);
            Assert.That(BracketBotPlanner.NeedsAnother(0, 0), Is.False);
        }

        [Test]
        public void ClassAllowed_AtMostTwoOfOneClassPerRealm()
        {
            Assert.That(BracketBotPlanner.MaxPerClass, Is.EqualTo(2));
            Assert.That(BracketBotPlanner.ClassAllowed(0), Is.True);
            Assert.That(BracketBotPlanner.ClassAllowed(1), Is.True);
            Assert.That(BracketBotPlanner.ClassAllowed(2), Is.False);
        }

        [Test]
        public void Fill_EmptyBattleground_BringsPerRealmToEveryRealm()
        {
            Dictionary<eRealm, int> plan = BracketBotPlanner.Plan(Present(0, 0, 0), 12, fillIn: true, keepEven: true);
            Assert.That(plan[eRealm.Albion], Is.EqualTo(12));
            Assert.That(plan[eRealm.Midgard], Is.EqualTo(12));
            Assert.That(plan[eRealm.Hibernia], Is.EqualTo(12));
        }

        [Test]
        public void Fill_CountsPlayersAndOrdinaryBotsAlreadyThere()
        {
            Dictionary<eRealm, int> plan = BracketBotPlanner.Plan(Present(5, 0, 12), 12, fillIn: true, keepEven: false);
            Assert.That(plan[eRealm.Albion], Is.EqualTo(7));
            Assert.That(plan[eRealm.Midgard], Is.EqualTo(12));
            Assert.That(plan[eRealm.Hibernia], Is.EqualTo(0));
        }

        [Test]
        public void Fill_KeepEven_RaisesEveryRealmTowardTheBiggest()
        {
            Dictionary<eRealm, int> plan = BracketBotPlanner.Plan(Present(5, 0, 20), 12, fillIn: true, keepEven: true);
            Assert.That(plan[eRealm.Albion], Is.EqualTo(15));
            Assert.That(plan[eRealm.Midgard], Is.EqualTo(20));
            Assert.That(plan[eRealm.Hibernia], Is.EqualTo(0));
        }

        [Test]
        public void Extra_AlwaysAddsThePerRealmNumber()
        {
            Dictionary<eRealm, int> plan = BracketBotPlanner.Plan(Present(30, 2, 0), 12, fillIn: false, keepEven: true);
            Assert.That(plan.Values, Is.All.EqualTo(12));
        }

        [Test]
        public void PerRealm_NeverBelowTheWaveMinimumOfEight()
        {
            Dictionary<eRealm, int> fill = BracketBotPlanner.Plan(Present(0, 0, 0), 3, fillIn: true, keepEven: true);
            Dictionary<eRealm, int> extra = BracketBotPlanner.Plan(Present(0, 0, 0), 0, fillIn: false, keepEven: false);
            Assert.That(fill.Values, Is.All.EqualTo(8));
            Assert.That(extra.Values, Is.All.EqualTo(8));
            Assert.That(BracketBotProperties.MinimumPerRealm, Is.EqualTo(AutonomousBattlegroundWave.MinimumWave));
        }

        [Test]
        public void FillMode_ParsesFromTheSetting()
        {
            string saved = BracketBotProperties.FILL_MODE;
            try
            {
                BracketBotProperties.FILL_MODE = "extra";
                Assert.That(BracketBotProperties.FillIn, Is.False);
                BracketBotProperties.FILL_MODE = "fill";
                Assert.That(BracketBotProperties.FillIn, Is.True);
                BracketBotProperties.FILL_MODE = null;
                Assert.That(BracketBotProperties.FillIn, Is.True, "unknown values fall back to fill");
            }
            finally { BracketBotProperties.FILL_MODE = saved; }
        }
    }
}
