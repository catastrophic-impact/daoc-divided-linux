using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>DR-233: Catacombs obelisks carry a player between the obelisks of their realm.</summary>
[TestFixture]
public class UT_CatacombsObelisk
{
    private static readonly (ushort Region, string Zone, eRealm Realm)[] Stones =
    {
        (63, "Roman Aqueducts", eRealm.Albion),
        (65, "Inconnu Crypt", eRealm.Albion),
        (109, "Albion's Frontlines", eRealm.Albion),
        (433, "Albion's Frontlines", eRealm.Albion),       // a second stone whose zone has the same name
        (229, "Burial Grounds", eRealm.Midgard),
        (243, "Kobold Undercity", eRealm.Midgard),
        (92, "Veil Rift", eRealm.Hibernia),
        (500, "Neutral Hall", eRealm.None),
        (501, "", eRealm.Albion),                          // a stone outside any zone
    };

    [TestCase((ushort)1878, eRealm.Albion)]
    [TestCase((ushort)1823, eRealm.Midgard)]
    [TestCase((ushort)1863, eRealm.Hibernia)]
    [TestCase((ushort)(1863 | 0x8000), eRealm.Hibernia)]
    [TestCase((ushort)600, eRealm.None)]
    public void TheStoneTellsTheRealm(ushort model, eRealm expected)
    {
        Assert.That(CatacombsObelisk.RealmOfModel(model), Is.EqualTo(expected));
    }

    [Test]
    public void AnAlbionTravellerSeesOnlyAlbionAndNeutralStonesElsewhere()
    {
        var list = CatacombsObelisk.Destinations(eRealm.Albion, 63, Stones);
        Assert.That(list.Select(d => d.Zone), Is.EqualTo(new[] { "Albion's Frontlines", "Inconnu Crypt", "Neutral Hall" }));
        Assert.That(list.First(d => d.Zone == "Albion's Frontlines").Region, Is.EqualTo(109), "two stones, one name: the lower region");
    }

    [Test]
    public void TheStoneYouStandAtIsNotOffered()
    {
        Assert.That(CatacombsObelisk.Destinations(eRealm.Midgard, 229, Stones).Select(d => d.Zone),
            Is.EqualTo(new[] { "Kobold Undercity", "Neutral Hall" }));
    }

    [Test]
    public void ALoneStoneOffersNothing()
    {
        Assert.That(CatacombsObelisk.Destinations(eRealm.Hibernia, 92, Stones.Where(s => s.Realm == eRealm.Hibernia)), Is.Empty);
    }
}
