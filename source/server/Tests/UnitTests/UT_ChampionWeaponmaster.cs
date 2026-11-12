using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_ChampionWeaponmaster
    {
        private static DbItemTemplate Weapon(string id, string name, int realm, string classes, int spell1 = 0, int proc = 0, string package = ChampionWeapons.Package) =>
            new DbItemTemplate { Id_nb = id, Name = name, Realm = realm, AllowedClasses = classes, SpellID1 = spell1, ProcSpellID = proc, PackageID = package };

        [Test]
        public void VersionsShareTheirBaseWeapon()
        {
            Assert.That(ChampionWeapons.BaseId("Armsman_Dextera_Blade_v2"), Is.EqualTo("Armsman_Dextera_Blade"));
            Assert.That(ChampionWeapons.BaseId("Armsman_Dextera_Blade_V3"), Is.EqualTo("Armsman_Dextera_Blade"));
            Assert.That(ChampionWeapons.BaseId("Armsman_Dextera_Blade"), Is.EqualTo("Armsman_Dextera_Blade"));
        }

        [Test]
        public void OnlyTheClassAndRealmSeeTheirWeapons()
        {
            var all = new List<DbItemTemplate>
            {
                Weapon("Ansuz_Bonedancer_Staff", "Ansuz Bonedancer Staff", 2, "27;29;30;59"),
                Weapon("Ansuz_Bonedancer_Staff_v2", "Ansuz Bonedancer Staff", 2, "27;29;30;59", spell1: 811094),
                Weapon("Ansuz_Bonedancer_Staff_v3", "Ansuz Bonedancer Staff", 2, "27;29;30;59", spell1: 811093),
                Weapon("Thurisaz_Thane_Axe", "Thurisaz Thane Axe", 2, "21"),
                Weapon("AnsuzBonedancerStaff", "Ansuz Bonedancer Staff", 0, "0", package: "dolpubdb"),
                Weapon("Upsilon_Cabalist_Staff", "Upsilon Cabalist Staff", 1, "13"),
            };

            var bonedancer = ChampionWeapons.ForClass(all, 30, eRealm.Midgard);
            Assert.That(bonedancer, Has.Count.EqualTo(1));
            Assert.That(bonedancer[0].Select(t => t.Id_nb), Is.EqualTo(new[] { "Ansuz_Bonedancer_Staff", "Ansuz_Bonedancer_Staff_v2", "Ansuz_Bonedancer_Staff_v3" }));
            Assert.That(ChampionWeapons.ForClass(all, 21, eRealm.Midgard).Single()[0].Id_nb, Is.EqualTo("Thurisaz_Thane_Axe"));
            Assert.That(ChampionWeapons.ForClass(all, 30, eRealm.Albion), Is.Empty, "other realm");
            Assert.That(ChampionWeapons.ForClass(all, 13, eRealm.Albion).Single()[0].Id_nb, Is.EqualTo("Upsilon_Cabalist_Staff"));
            Assert.That(ChampionWeapons.AllowsClass(all[4], 30), Is.False, "old dolpubdb copies stay out");
            Assert.That(ChampionWeapons.IsChampionWeapon(all[4]), Is.False);
        }

        [Test]
        public void VersionsAreLabelledByTheirExtraSpell()
        {
            var names = new Dictionary<int, string> { [811094] = "Boon of Kings", [811079] = "Slash Resist Decrease" };
            string Name(int id) => names.TryGetValue(id, out string n) ? n : null;
            Assert.That(ChampionWeapons.VersionLabel(Weapon("A_Blade", "A", 1, "2"), Name), Is.EqualTo("standard"));
            Assert.That(ChampionWeapons.VersionLabel(Weapon("A_Blade_v2", "A", 1, "2", spell1: 811094), Name), Is.EqualTo("Boon of Kings"));
            Assert.That(ChampionWeapons.VersionLabel(Weapon("A_Blade_v3", "A", 1, "2", proc: 811079), Name), Is.EqualTo("Slash Resist Decrease"));
            Assert.That(ChampionWeapons.VersionLabel(Weapon("A_Blade_v3", "A", 1, "2", spell1: 999), Name), Is.EqualTo("third"));
        }

        [Test]
        public void EachRealmHasItsThroneRoom()
        {
            Assert.That(ThroneRoomTeleporter.ThroneRegionFor(eRealm.Albion), Is.EqualTo(394));
            Assert.That(ThroneRoomTeleporter.ThroneRegionFor(eRealm.Midgard), Is.EqualTo(360));
            Assert.That(ThroneRoomTeleporter.ThroneRegionFor(eRealm.Hibernia), Is.EqualTo(395));
            Assert.That(ThroneRoomTeleporter.ThroneRegionFor(eRealm.None), Is.EqualTo(0));
        }
    }
}
