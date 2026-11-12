using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Night playtest 2026-11-04: the scholar offered a Midgard Warrior nine Crocodile's Tooth versions (Albion and
    /// Hibernian weapon types and left-hand-only ones); only the hammer was usable.
    /// </summary>
    [TestFixture]
    public class UT_ArtifactScholarVersions
    {
        private const int Warrior = (int) eCharacterClass.Warrior;

        private static DbItemTemplate Version(eObjectType type, eInventorySlot slot = eInventorySlot.RightHandWeapon, int realm = 0, string classes = "")
        {
            return new DbItemTemplate { Object_Type = (int) type, Item_Type = (int) slot, Realm = realm, AllowedClasses = classes };
        }

        [Test]
        public void OtherRealmsWeaponTypesAreNotOffered()
        {
            Assert.That(ArtifactCatalog.IsVersionFor(eRealm.Midgard, Warrior, false, Version(eObjectType.Hammer)), Is.True);
            Assert.That(ArtifactCatalog.IsVersionFor(eRealm.Midgard, Warrior, false, Version(eObjectType.CrushingWeapon)), Is.False);
            Assert.That(ArtifactCatalog.IsVersionFor(eRealm.Midgard, Warrior, false, Version(eObjectType.Blades)), Is.False);
            Assert.That(ArtifactCatalog.IsVersionFor(eRealm.Albion, (int) eCharacterClass.Armsman, false, Version(eObjectType.ThrustWeapon)), Is.True);
            Assert.That(ArtifactCatalog.IsVersionFor(eRealm.Hibernia, (int) eCharacterClass.Hero, false, Version(eObjectType.Piercing)), Is.True);
        }

        [Test]
        public void LeftHandOnlyWeaponsNeedADualWielder()
        {
            DbItemTemplate leftAxe = Version(eObjectType.LeftAxe, eInventorySlot.LeftHandWeapon);
            Assert.That(ArtifactCatalog.IsVersionFor(eRealm.Midgard, Warrior, false, leftAxe), Is.False, "Warrior");
            Assert.That(ArtifactCatalog.IsVersionFor(eRealm.Midgard, (int) eCharacterClass.Berserker, true, leftAxe), Is.True, "Berserker");
            Assert.That(ArtifactCatalog.IsVersionFor(eRealm.Midgard, Warrior, false, Version(eObjectType.Shield, eInventorySlot.LeftHandWeapon)), Is.True, "shields are fine");
        }

        [Test]
        public void ArmourOfAnotherRealmIsNotOffered()
        {
            Assert.That(ArtifactCatalog.IsVersionFor(eRealm.Hibernia, (int) eCharacterClass.Hero, false, Version(eObjectType.Scale, eInventorySlot.TorsoArmor)), Is.True);
            Assert.That(ArtifactCatalog.IsVersionFor(eRealm.Hibernia, (int) eCharacterClass.Hero, false, Version(eObjectType.Chain, eInventorySlot.TorsoArmor)), Is.False);
            Assert.That(ArtifactCatalog.IsVersionFor(eRealm.Midgard, Warrior, false, Version(eObjectType.Chain, eInventorySlot.TorsoArmor)), Is.True);
            Assert.That(ArtifactCatalog.IsVersionFor(eRealm.Albion, (int) eCharacterClass.Armsman, false, Version(eObjectType.Reinforced, eInventorySlot.TorsoArmor)), Is.False);
        }

        [Test]
        public void RealmAndClassRestrictionsStillApply()
        {
            Assert.That(ArtifactCatalog.IsVersionFor(eRealm.Midgard, Warrior, false, Version(eObjectType.Magical, eInventorySlot.Neck, realm: (int) eRealm.Albion)), Is.False);
            Assert.That(ArtifactCatalog.IsVersionFor(eRealm.Midgard, Warrior, false, Version(eObjectType.Magical, eInventorySlot.Neck, classes: "23;24")), Is.False);
            Assert.That(ArtifactCatalog.IsVersionFor(eRealm.Midgard, Warrior, false, Version(eObjectType.Magical, eInventorySlot.Neck, classes: $"{Warrior};24")), Is.True);
            Assert.That(ArtifactCatalog.IsVersionFor(eRealm.Midgard, Warrior, false, Version(eObjectType.Staff, eInventorySlot.TwoHandWeapon)), Is.True,
                "shared types pass here; the skill check decides");
        }
    }
}
