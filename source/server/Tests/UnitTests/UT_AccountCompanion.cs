using System.Collections.Generic;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_AccountCompanion
    {
        private static readonly long[] Xp = { 0, 50, 250, 850, 2300, 6350, 15950, 37950, 88950, 203950, 459950 };
        private static long XpFor(int level) => level < Xp.Length ? Xp[level] : long.MaxValue;

        /// <summary>DR-273: auto-spend stays off by default, and the join message says how to turn it on.</summary>
        [Test]
        public void JoinMessageExplainsAutoSpend()
        {
            string off = AccountCompanionRules.JoinMessage("Gunnar", 20, "Berserker", false, 12);
            Assert.That(off, Does.Contain("Gunnar has 12 specialization points to spend"));
            Assert.That(off, Does.Contain("/companion autospend Gunnar on"));
            Assert.That(AccountCompanionRules.JoinMessage("Gunnar", 20, "Berserker", false, 0), Does.Not.Contain("points to spend"));
            string on = AccountCompanionRules.JoinMessage("Gunnar", 20, "Berserker", true, 12);
            Assert.That(on, Does.Contain("spends its own specialization points"));
            Assert.That(on, Does.Not.Contain("/companion autospend"));
        }

        [Test]
        public void EarnedPoints_MatchesPlayerFormula()
        {
            // Levels 1-5 give 1+2+3+4+5 points, minus the one-point start offset.
            Assert.That(AccountCompanionRules.EarnedSpecPoints(5, false, 15), Is.EqualTo(14));
            // Level 6 with a 1.5 multiplier class adds 15 * 6 / 10 = 9.
            Assert.That(AccountCompanionRules.EarnedSpecPoints(6, false, 15), Is.EqualTo(23));
        }

        [Test]
        public void SecondStage_AddsHalfLevelPointsBelow50Only()
        {
            int whole = AccountCompanionRules.EarnedSpecPoints(45, false, 20);
            Assert.That(AccountCompanionRules.EarnedSpecPoints(45, true, 20), Is.EqualTo(whole + 20 * 45 / 20));
            Assert.That(AccountCompanionRules.EarnedSpecPoints(50, true, 20), Is.EqualTo(AccountCompanionRules.EarnedSpecPoints(50, false, 20)));
        }

        [Test]
        public void Autotrain_RefundsTheFreeLevels()
        {
            // Level 20: up to level 5 in an autotrained line is free.
            Assert.That(AccountCompanionRules.EffectiveLineCost(5, true, 20), Is.EqualTo(0));
            Assert.That(AccountCompanionRules.EffectiveLineCost(6, true, 20), Is.EqualTo(6));
            Assert.That(AccountCompanionRules.EffectiveLineCost(6, false, 20), Is.EqualTo(20));
        }

        [Test]
        public void Spend_FollowsPlanRatiosAndNeverOverspends()
        {
            var current = new Dictionary<string, int> { ["Sword"] = 1, ["Shields"] = 1, ["Parry"] = 7 };
            var plan = new[]
            {
                new AccountCompanionRules.PlanLine("Sword", 50, 1.0f),
                new AccountCompanionRules.PlanLine("Shields", 42, 0.5f),
            };
            Dictionary<string, int> trained = AccountCompanionRules.Spend(current, new List<string>(), plan, 10, 25, out int leftover);

            int spent = 0;
            foreach ((string key, int level) in trained)
                spent += AccountCompanionRules.LineCost(level) - AccountCompanionRules.LineCost(current[key]);
            Assert.That(spent + leftover, Is.EqualTo(25));
            Assert.That(trained["Sword"], Is.GreaterThan(trained["Shields"]));
            Assert.That(trained["Sword"], Is.LessThanOrEqualTo(10), "a line never passes the character level");
            Assert.That(trained["Parry"], Is.EqualTo(7), "a hand-trained line outside the plan is never touched");
        }

        [Test]
        public void NextLevelCost_IsTheNewLevelMinusFreeAutotrainLevels()
        {
            Assert.That(AccountCompanionRules.NextLevelCost(5, false, 20), Is.EqualTo(6));
            Assert.That(AccountCompanionRules.NextLevelCost(4, true, 20), Is.EqualTo(0), "level 5 is free at character level 20");
            Assert.That(AccountCompanionRules.NextLevelCost(5, true, 20), Is.EqualTo(6));
        }

        [Test]
        public void TrainLine_StopsWhenPointsRunOut()
        {
            // 5 -> 6 costs 6 and 6 -> 7 costs 7; 7 -> 8 would need 8 more than the 20 points.
            Assert.That(AccountCompanionRules.TrainLine(5, 5, false, 20, 20, out int cost), Is.EqualTo(7));
            Assert.That(cost, Is.EqualTo(13));
        }

        [Test]
        public void TrainLine_NeverPassesTheCharacterLevel()
        {
            Assert.That(AccountCompanionRules.TrainLine(19, 10, false, 20, 1000, out int cost), Is.EqualTo(20));
            Assert.That(cost, Is.EqualTo(20));
            Assert.That(AccountCompanionRules.TrainLine(20, 1, false, 20, 1000, out cost), Is.EqualTo(20));
            Assert.That(cost, Is.EqualTo(0));
        }

        [Test]
        public void TrainLine_TakesFreeAutotrainLevelsWithNoPoints()
        {
            Assert.That(AccountCompanionRules.TrainLine(1, 10, true, 20, 0, out int cost), Is.EqualTo(5));
            Assert.That(cost, Is.EqualTo(0));
        }

        [Test]
        public void Respec_SendsALineTo1AndAnAutotrainLineToItsFreeLevels()
        {
            Assert.That(AccountCompanionRules.RespecLevel(false, 40), Is.EqualTo(1));
            Assert.That(AccountCompanionRules.RespecLevel(true, 40), Is.EqualTo(10));
            Assert.That(AccountCompanionRules.RespecLevel(true, 7), Is.EqualTo(1), "below level 8 an autotrain line goes to 1 too");
        }

        [Test]
        public void Respec_GivesBackEveryPaidPoint()
        {
            Assert.That(AccountCompanionRules.RespecRefund(20, false, 40), Is.EqualTo(AccountCompanionRules.LineCost(20)));
            // Levels 1-10 of an autotrained line were free at character level 40, so only 11-20 come back.
            Assert.That(AccountCompanionRules.RespecRefund(20, true, 40),
                Is.EqualTo(AccountCompanionRules.LineCost(20) - AccountCompanionRules.LineCost(10)));
            Assert.That(AccountCompanionRules.RespecRefund(6, true, 40), Is.EqualTo(0), "a line inside its free levels has nothing to give back");
        }

        [Test]
        public void Respec_ThenTrainAgain_CostsWhatTheRespecGaveBack()
        {
            int refund = AccountCompanionRules.RespecRefund(20, false, 40);
            Assert.That(AccountCompanionRules.TrainLine(1, 19, false, 40, refund, out int cost), Is.EqualTo(20));
            Assert.That(cost, Is.EqualTo(refund));
        }

        [Test]
        public void TrainLinks_NameTheLineAndTheLevelCount()
        {
            Assert.That(DOL.GS.Commands.AccountCompanionMenu.TryParseTrainLink("Two Handed +5", out string line, out int steps), Is.True);
            Assert.That(line, Is.EqualTo("Two Handed"));
            Assert.That(steps, Is.EqualTo(5));
            Assert.That(DOL.GS.Commands.AccountCompanionMenu.TryParseTrainLink("Mending +3", out _, out _), Is.False, "the page offers +1, +5 and +10 only");
            Assert.That(DOL.GS.Commands.AccountCompanionMenu.TryParseTrainLink("Back", out _, out _), Is.False);
            Assert.That(DOL.GS.Commands.AccountCompanionMenu.TryParseTrainLink("Reset Two Handed", out _, out _), Is.False);
        }

        [Test]
        public void ResetLinks_NameALineOrEveryLine()
        {
            Assert.That(DOL.GS.Commands.AccountCompanionMenu.TryParseResetLink("Reset Two Handed", out string line), Is.True);
            Assert.That(line, Is.EqualTo("Two Handed"));
            Assert.That(DOL.GS.Commands.AccountCompanionMenu.TryParseResetLink("Reset all lines", out line), Is.True);
            Assert.That(line, Is.EqualTo("all"));
            Assert.That(DOL.GS.Commands.AccountCompanionMenu.TryParseResetLink("Mending +1", out _), Is.False);
        }

        [Test]
        public void Spend_WithNoPointsChangesNothing()
        {
            var current = new Dictionary<string, int> { ["Sword"] = 4 };
            var plan = new[] { new AccountCompanionRules.PlanLine("Sword", 50, 1.0f) };
            Dictionary<string, int> trained = AccountCompanionRules.Spend(current, null, plan, 10, 0, out int leftover);
            Assert.That(trained["Sword"], Is.EqualTo(4));
            Assert.That(leftover, Is.EqualTo(0));
        }

        [Test]
        public void StatGains_MatchPlayerLevelUp()
        {
            Assert.That(AccountCompanionRules.StatGains(1, 5), Is.EqualTo((0, 0, 0)));
            // Levels 6 and 7: primary twice, secondary at 6, tertiary at 6.
            Assert.That(AccountCompanionRules.StatGains(5, 7), Is.EqualTo((2, 1, 1)));
            Assert.That(AccountCompanionRules.StatGains(1, 12), Is.EqualTo((7, 4, 3)));
        }

        [Test]
        public void LevelForExperience_RaisesThroughEveryThreshold()
        {
            Assert.That(AccountCompanionRules.LevelForExperience(1, 49, XpFor), Is.EqualTo(1));
            Assert.That(AccountCompanionRules.LevelForExperience(1, 300, XpFor), Is.EqualTo(3));
            Assert.That(AccountCompanionRules.LevelForExperience(50, long.MaxValue, XpFor), Is.EqualTo(50));
        }

        [Test]
        public void MergeSerializedSpecs_KeepsOtherEntriesAndOrder()
        {
            string merged = AccountCompanionRules.MergeSerializedSpecs("Sword|5;Crafting|3;Shields|2",
                new[] { ("Sword", 9), ("Parry", 4) });
            Assert.That(merged, Is.EqualTo("Sword|9;Crafting|3;Shields|2;Parry|4"));
        }

        [Test]
        public void Check_RefusesInTheRightOrder()
        {
            Assert.That(AccountCompanionRules.Check(true, 2, 2, true, true, false, false), Is.EqualTo(AccountCompanionRules.SummonRefusal.Yourself));
            Assert.That(AccountCompanionRules.Check(false, 2, 1, true, true, false, false), Is.EqualTo(AccountCompanionRules.SummonRefusal.OtherRealm));
            Assert.That(AccountCompanionRules.Check(false, 2, 2, false, true, false, false), Is.EqualTo(AccountCompanionRules.SummonRefusal.NeedsPromotion));
            Assert.That(AccountCompanionRules.Check(false, 2, 2, true, false, false, false), Is.EqualTo(AccountCompanionRules.SummonRefusal.UnsupportedClass));
            Assert.That(AccountCompanionRules.Check(false, 2, 2, true, true, true, false), Is.EqualTo(AccountCompanionRules.SummonRefusal.AlreadySummoned));
            Assert.That(AccountCompanionRules.Check(false, 2, 2, true, true, false, true), Is.EqualTo(AccountCompanionRules.SummonRefusal.GroupFull));
            Assert.That(AccountCompanionRules.Check(false, 2, 2, true, true, false, false), Is.EqualTo(AccountCompanionRules.SummonRefusal.None));
        }

        [Test]
        public void CharacterModel_DecodesToNpcModelAndSize()
        {
            // A real Norse female healer: 0x50A1 is model 161 with creation bits above it.
            Assert.That(AccountCompanionRules.NpcModel(20641), Is.EqualTo(161));
            Assert.That(AccountCompanionRules.NpcSize(20641), Is.EqualTo(50));
            Assert.That(AccountCompanionRules.NpcModel(153 | 0x800), Is.EqualTo(153));
            Assert.That(AccountCompanionRules.NpcSize(153 | 0x800), Is.EqualTo(45));
            Assert.That(AccountCompanionRules.NpcSize(153 | 0x1800), Is.EqualTo(55));
        }
    }
}
