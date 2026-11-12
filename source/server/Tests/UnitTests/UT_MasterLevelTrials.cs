using System;
using System.Linq;
using DOL.GS;
using DOL.GS.Trials;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_MasterLevelTrials
    {
        [Test]
        public void Catalog_HasNoMistakes()
        {
            Assert.That(TrialCatalog.Validate(), Is.Empty);
        }

        [Test]
        public void Catalog_HasEveryStep()
        {
            var expected = Enumerable.Range(1, 9).SelectMany(ml => Enumerable.Range(1, 10).Select(step => $"{ml}.{step}"))
                .Concat(Enumerable.Range(1, 5).Select(step => $"10.{step}"));
            Assert.That(TrialCatalog.All.Select(trial => trial.Key), Is.EquivalentTo(expected));
        }

        [Test]
        public void DamageKind_FollowsDamageTypeAndWeapon()
        {
            Assert.That(TrialMob.KindOf(null, eDamageType.Heat), Is.EqualTo(TrialDamageKind.Spell));
            Assert.That(TrialMob.KindOf(null, eDamageType.Slash), Is.EqualTo(TrialDamageKind.Melee));
        }

        [Test]
        public void Scaling_ShrinksForSmallPartiesWithinLimits()
        {
            Assert.That(TrialScaling.HealthScale(TrialScope.Group, 8), Is.EqualTo(1.0));
            Assert.That(TrialScaling.HealthScale(TrialScope.Group, 4), Is.EqualTo(0.5));
            Assert.That(TrialScaling.HealthScale(TrialScope.Group, 1), Is.EqualTo(0.125));   // one player counts as one (DR-115)
            Assert.That(TrialScaling.HealthScale(TrialScope.Group, 2), Is.EqualTo(0.25));
            Assert.That(TrialScaling.HealthScale(TrialScope.Raid, 1), Is.EqualTo(TrialScaling.MinimumScale));
            Assert.That(TrialScaling.HealthScale(TrialScope.Raid, 80), Is.EqualTo(TrialScaling.MaximumScale));
        }

        [Test]
        public void Scaling_LeavesSoloTrialsAlone()
        {
            Assert.That(TrialScaling.HealthScale(TrialScope.Solo, 8), Is.EqualTo(1.0));
            Assert.That(TrialScaling.DamageScale(TrialScope.Solo, 8), Is.EqualTo(1.0));
        }

        [Test]
        public void Scaling_DamageFallsSlowerThanHealth()
        {
            Assert.That(TrialScaling.DamageScale(TrialScope.Group, 2), Is.GreaterThan(TrialScaling.HealthScale(TrialScope.Group, 2)));
        }

        [Test]
        public void Finale_NeedsTheEarlierSteps()
        {
            TrialDefinition finale = new() { ML = 1, Step = 10 };
            TrialDefinition first = new() { ML = 1, Step = 1 };
            Assert.That(TrialManager.Required(finale), Is.EqualTo(Enumerable.Range(1, 9)));
            Assert.That(TrialManager.Required(first), Is.Empty);
        }

        [Test]
        public void Finale_RequirementsCanBeSetByHand()
        {
            TrialDefinition trial = new() { ML = 1, Step = 6, RequiresSteps = new[] { 1, 4 } };
            Assert.That(TrialManager.Required(trial), Is.EqualTo(new[] { 1, 4 }));
        }

        [Test]
        public void Regions_EachZoneHasThreeRealmCopies()
        {
            foreach (TrialZone zone in Enum.GetValues<TrialZone>())
                Assert.That(TrialManager.RegionsOf(zone).Distinct().Count(), Is.EqualTo(3), zone.ToString());
        }
    }
}
