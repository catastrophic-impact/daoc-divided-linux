using DOL.GS;
using DOL.GS.Spells;
using DOL.GS.Trials;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The rest of the 2026-11-07 playtest list: unresistable Sovereign style effects, ML starters clear of camps,
    /// Darkness Falls solo rules, Atlantis route scans and RvR perception near the objective.
    /// </summary>
    [TestFixture]
    public class UT_Playtest1107Followups
    {
        [Test]
        public void SovereignStyleStrikesAndSubSpellsAreUnresistable()
        {
            // Strike: style id x 1000 + 990; Cast: style id x 1000 + index. Storm 63500, Tuatha 63800, Pendragon 63900.
            Assert.That(StormSovereignHandler.IsSovereignStyleSubSpell(63900 * 1000 + 990), Is.True, "Pendragon Judgment strike");
            Assert.That(StormSovereignHandler.IsSovereignStyleSubSpell(63500 * 1000 + 1), Is.True, "Storm style stun");
            Assert.That(StormSovereignHandler.IsSovereignStyleSubSpell(63860 * 1000 + 990), Is.True, "last Tuatha style");
            // Not style effects: the remake spells themselves, a sub-spell of an ordinary ML ability, stock spells.
            Assert.That(StormSovereignHandler.IsSovereignStyleSubSpell(63900), Is.False);
            Assert.That(StormSovereignHandler.IsSovereignStyleSubSpell(62400 * 1000 + 990), Is.False);
            Assert.That(StormSovereignHandler.IsSovereignStyleSubSpell(63961 * 1000 + 990), Is.False);
            Assert.That(StormSovereignHandler.IsSovereignStyleSubSpell(20301), Is.False);
            Assert.That(StormSovereignHandler.IsSovereignStyleSubSpell(0), Is.False);
        }

        [Test]
        public void OnlyWorldMonstersAreClearedAroundTrialStarters()
        {
            Assert.That(TrialManager.IsWorldMonsterType(typeof(GameNPC)), Is.True);
            Assert.That(TrialManager.IsWorldMonsterType(typeof(GameEpicNPC)), Is.True);
            Assert.That(TrialManager.IsWorldMonsterType(typeof(GameEpicBoss)), Is.True);
            foreach (var kept in new[] { typeof(TrialMob), typeof(TrialStarter), typeof(ArtifactBoss), typeof(ArtifactEncounterAnchor),
                         typeof(GameMerchant), typeof(GameTrainer), typeof(GameBot) })
                Assert.That(TrialManager.IsWorldMonsterType(kept), Is.False, kept.Name);
            Assert.That(TrialManager.DefaultClearRadius, Is.EqualTo(450));
            Assert.That(new TrialDefinition().ClearRadius, Is.EqualTo(TrialManager.DefaultClearRadius));
        }

        [Test]
        public void SoloBotsKeepToTheirOwnDarknessFallsWing()
        {
            // An Albion room 20,000 from its entrance, 10,000 closer than the bot's own: a party may cross, a solo bot may not.
            Assert.That(AutonomousDarknessFallsNavigation.CanUseOrdinaryCamp(eRealm.Midgard, eRealm.Albion, 40, 30_000, 20_000), Is.True);
            Assert.That(AutonomousDarknessFallsNavigation.CanUseOrdinaryCamp(eRealm.Midgard, eRealm.Albion, 40, 30_000, 20_000, solo: true), Is.False);
            Assert.That(AutonomousDarknessFallsNavigation.CanUseOrdinaryCamp(eRealm.Midgard, eRealm.Midgard, 25, 5_000, 5_000, solo: true), Is.True);
            Assert.That(AutonomousDarknessFallsNavigation.CanUseOrdinaryCamp(eRealm.Midgard, eRealm.None, 40, 9_000, 9_000, solo: true), Is.True, "neutral room");
            Assert.That(AutonomousDarknessFallsPolicy.SoloPvpMaximumCon, Is.EqualTo(ConColor.YELLOW));
        }

        [Test]
        public void TheCampsOwnSpeciesIsCampWorkNotARouteThreat()
        {
            Assert.That(AutonomousRouteThreatPolicy.IsCampWork("decaying marshman", ConColor.YELLOW, "Decaying Marshman"), Is.True);
            Assert.That(AutonomousRouteThreatPolicy.IsCampWork("decaying marshman", ConColor.BLUE, "decaying marshman"), Is.True);
            Assert.That(AutonomousRouteThreatPolicy.IsCampWork("decaying marshman", ConColor.ORANGE, "decaying marshman"), Is.False);
            Assert.That(AutonomousRouteThreatPolicy.IsCampWork("ra of pine", ConColor.GREEN, "decaying marshman"), Is.False);
            Assert.That(AutonomousRouteThreatPolicy.IsCampWork("ra of pine", ConColor.GREEN, null), Is.False);
        }

        [Test]
        public void AtlantisSoloBotsPullABluePairAgain()
        {
            RouteThreatAction Solo(int pack) =>
                AutonomousRouteThreatPolicy.Decide(ConColor.BLUE, false, pack, false, false, 0, true, denseWorld: true);
            Assert.That(Solo(1), Is.EqualTo(RouteThreatAction.Pull));
            Assert.That(Solo(2), Is.EqualTo(RouteThreatAction.Pull));
            Assert.That(Solo(3), Is.EqualTo(RouteThreatAction.Retarget));
        }

        [Test]
        public void DefendersNoticeEnemiesNormallyUntilTheyReachTheirKeep()
        {
            Assert.That(AutonomousWorldBotController.IsNearRvrObjective(true, 100_000, 100_000, 101_000, 102_000), Is.True);
            Assert.That(AutonomousWorldBotController.IsNearRvrObjective(true, 100_000, 100_000, 103_240, 100_000), Is.False, "3,240 away: on the march");
            Assert.That(AutonomousWorldBotController.IsNearRvrObjective(false, 100_000, 100_000, 100_000, 100_000), Is.False, "other region");
        }

        [Test]
        public void TheSiphonLineNamesEachSovereignsPower()
        {
            Assert.That(StormSovereignAbilities.SiphonText(null, 212), Is.EqualTo("Your lightning restores 212 health."));
        }
    }
}
