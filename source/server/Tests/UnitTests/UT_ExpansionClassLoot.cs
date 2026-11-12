using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>ROG loot for the expansion classes uses their own weapons, armor, shields and lines.</summary>
    [TestFixture]
    public class UT_ExpansionClassLoot
    {
        [Test]
        public void ExpansionClassesGetTheirOwnWeaponsAndArmor()
        {
            for (int i = 0; i < 50; i++)
            {
                Assert.That(GeneratedUniqueItem.GetAlbionWeapon(eCharacterClass.MaulerAlb), Is.AnyOf(eObjectType.FistWraps, eObjectType.MaulerStaff));
                Assert.That(GeneratedUniqueItem.GetMidgardWeapon(eCharacterClass.Valkyrie), Is.AnyOf(eObjectType.Sword, eObjectType.Spear, eObjectType.Staff, eObjectType.Shield));
                Assert.That(GeneratedUniqueItem.GetAlbionWeapon(eCharacterClass.Heretic), Is.AnyOf(eObjectType.CrushingWeapon, eObjectType.Flexible, eObjectType.Staff, eObjectType.Shield));
                Assert.That(GeneratedUniqueItem.GetHiberniaWeapon(eCharacterClass.Vampiir), Is.EqualTo(eObjectType.Piercing));
            }
            Assert.That(GeneratedUniqueItem.GetMidgardArmorType(eCharacterClass.Valkyrie, 50), Is.EqualTo(eObjectType.Chain));
            Assert.That(GeneratedUniqueItem.GetHiberniaArmorType(eCharacterClass.MaulerHib, 50), Is.EqualTo(eObjectType.Leather));
            Assert.That(GeneratedUniqueItem.GetHiberniaArmorType(eCharacterClass.Bainshee, 50), Is.EqualTo(eObjectType.Cloth));
            Assert.That(GeneratedUniqueItem.GetMidgardWeapon(eCharacterClass.Warlock), Is.EqualTo(eObjectType.Staff));
        }

        [Test]
        public void SkillsAndStatsFollowEachClassesLines()
        {
            Assert.That(ExpansionClassLoot.SkillValid(eCharacterClass.Warlock, eProperty.Skill_Cursing), Is.True);
            Assert.That(ExpansionClassLoot.SkillValid(eCharacterClass.Warlock, eProperty.Skill_Sword), Is.False);
            Assert.That(ExpansionClassLoot.SkillValid(eCharacterClass.MaulerMid, eProperty.Skill_Power_Strikes), Is.True);
            Assert.That(ExpansionClassLoot.SkillValid(eCharacterClass.Vampiir, eProperty.Skill_Dementia), Is.True);
            Assert.That(ExpansionClassLoot.StatTemplate(eCharacterClass.Bainshee), Is.EqualTo(eCharacterClass.Eldritch));
            Assert.That(ExpansionClassLoot.StatTemplate(eCharacterClass.Armsman), Is.EqualTo(eCharacterClass.Armsman), "classic classes unchanged");
            Assert.That(ExpansionClassLoot.TryShieldSize(eCharacterClass.Valkyrie, out int size) && size == 3, Is.True);
            Assert.That(ClassItemRules.ItemClass(eCharacterClass.DeathKnight), Is.EqualTo(eCharacterClass.Necromancer));
        }

        [Test]
        public void HibernianTuathaDropsUseItsOwnWeaponsAndScale()
        {
            for (int i = 0; i < 50; i++)
                Assert.That(GeneratedUniqueItem.GetHiberniaWeapon(eCharacterClass.TuathaSovereign), Is.AnyOf(eObjectType.Scythe, eObjectType.Blades,
                    eObjectType.Shield));   // 2026-11-06 remake: Scythe, Blades and Shield
            Assert.That(GeneratedUniqueItem.GetHiberniaArmorType(eCharacterClass.TuathaSovereign, 50), Is.EqualTo(eObjectType.Scale));
        }

        [Test]
        public void OutOfClassPoolAddsNewClassesOnlyWhileSwitchedOn()
        {
            bool previous = DOL.GS.ServerProperties.Properties.ENABLE_TUATHA;
            bool mauler = DOL.GS.ServerProperties.Properties.ENABLE_MAULER_TEST;
            try
            {
                DOL.GS.ServerProperties.Properties.ENABLE_TUATHA = false;
                DOL.GS.ServerProperties.Properties.ENABLE_MAULER_TEST = false;
                Assert.That(ROGMobGenerator.RealmLootClasses(eRealm.Hibernia), Has.Count.EqualTo(13));
                DOL.GS.ServerProperties.Properties.ENABLE_TUATHA = true;
                DOL.GS.ServerProperties.Properties.ENABLE_MAULER_TEST = true;
                Assert.That(ROGMobGenerator.RealmLootClasses(eRealm.Hibernia), Does.Contain(eCharacterClass.TuathaSovereign).And.Contain(eCharacterClass.MaulerHib));
                Assert.That(ROGMobGenerator.RealmLootClasses(eRealm.Albion), Does.Contain(eCharacterClass.MaulerAlb).And.Not.Contain(eCharacterClass.MaulerHib));
            }
            finally
            {
                DOL.GS.ServerProperties.Properties.ENABLE_TUATHA = previous;
                DOL.GS.ServerProperties.Properties.ENABLE_MAULER_TEST = mauler;
            }
        }
    }
}
