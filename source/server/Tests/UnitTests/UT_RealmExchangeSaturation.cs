using System;
using System.Linq;
using System.Reflection;
using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture, NonParallelizable]
public sealed class UT_RealmExchangeSaturation
{
    private static readonly BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly DateTime Now = DateTime.UtcNow;
    private MarketSearchEngine _previousMarket;

    [SetUp]
    public void SetUp()
    {
        _previousMarket = GetMarketEngine();
        SetMarketEngine(new MarketSearchEngine());
        RealmExchangeSaturation.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        GetMarketEngine().Dispose();
        SetMarketEngine(_previousMarket);
        RealmExchangeSaturation.Reset();
    }

    [Test]
    public void Count_GroupsBotListingsByRealmTypeSlotAndFiveLevelBand()
    {
        var listings = new[]
        {
            Listing(RealmExchangeBroker.HiberniaOwnerLot, eObjectType.Staff, 40, 1),
            Listing(RealmExchangeBroker.HiberniaOwnerLot, eObjectType.Staff, 44, 2),
            Listing(RealmExchangeBroker.HiberniaOwnerLot, eObjectType.Staff, 45, 3),
            Listing(RealmExchangeBroker.AlbionOwnerLot, eObjectType.Staff, 40, 4),
        };

        var counts = RealmExchangeSaturation.Count(listings, Now);

        Assert.That(counts[RealmExchangeSaturation.KindOf(RealmExchangeBroker.HiberniaOwnerLot, listings[0])], Is.EqualTo(2));
        Assert.That(counts[RealmExchangeSaturation.KindOf(RealmExchangeBroker.HiberniaOwnerLot, listings[2])], Is.EqualTo(1));
        Assert.That(counts[RealmExchangeSaturation.KindOf(RealmExchangeBroker.AlbionOwnerLot, listings[3])], Is.EqualTo(1));
    }

    [Test]
    public void Count_IgnoresPlayerAndExpiredBotListings()
    {
        DbInventoryItem player = Listing(RealmExchangeBroker.HiberniaOwnerLot, eObjectType.Staff, 40, 1);
        player.OwnerID = "human-character-id";
        DbInventoryItem expired = Listing(RealmExchangeBroker.HiberniaOwnerLot, eObjectType.Staff, 40, 2);
        expired.RealmExchangeListedUtc = Now.AddHours(-25).ToString("O");

        Assert.That(RealmExchangeSaturation.Count([player, expired], Now), Is.Empty);
    }

    [Test]
    public void IsSaturated_AtTheCapOnlyForThatKind()
    {
        var staves = Enumerable.Range(1, RealmExchangeSaturation.MaxBotListingsPerKind)
            .Select(id => Listing(RealmExchangeBroker.HiberniaOwnerLot, eObjectType.Staff, 42, id)).ToArray();
        var counts = RealmExchangeSaturation.Count(staves, Now);
        DbInventoryItem staff = Item(eObjectType.Staff, 41);
        DbInventoryItem ring = Item(eObjectType.Magical, 41, (int)eInventorySlot.RightRing);

        Assert.That(RealmExchangeSaturation.IsSaturated(counts, RealmExchangeBroker.HiberniaOwnerLot, staff), Is.True);
        Assert.That(RealmExchangeSaturation.IsSaturated(counts, RealmExchangeBroker.HiberniaOwnerLot, ring), Is.False);
        Assert.That(RealmExchangeSaturation.IsSaturated(counts, RealmExchangeBroker.HiberniaOwnerLot, Item(eObjectType.Staff, 30)), Is.False);
        Assert.That(RealmExchangeSaturation.IsSaturated(counts, RealmExchangeBroker.AlbionOwnerLot, staff), Is.False);
    }

    [Test]
    public void IsSaturated_ReadsTheMarketAndCountsNewPostsAtOnce()
    {
        for (int id = 1; id < RealmExchangeSaturation.MaxBotListingsPerKind; id++)
            Assert.That(MarketCache.AddItem(Listing(RealmExchangeBroker.HiberniaOwnerLot, eObjectType.Staff, 42, id)), Is.True);
        DbInventoryItem staff = Item(eObjectType.Staff, 42);

        Assert.That(RealmExchangeSaturation.IsSaturated(eRealm.Hibernia, staff), Is.False);
        RealmExchangeSaturation.NoteListed(Listing(RealmExchangeBroker.HiberniaOwnerLot, eObjectType.Staff, 43, 999));
        Assert.That(RealmExchangeSaturation.IsSaturated(eRealm.Hibernia, staff), Is.True);
        Assert.That(RealmExchangeSaturation.IsSaturated(eRealm.Midgard, staff), Is.False);
    }

    private static DbInventoryItem Listing(ushort lot, eObjectType type, int level, long botId)
    {
        DbInventoryItem item = Item(type, level);
        item.OwnerLot = lot;
        item.OwnerID = AutonomousBotEconomy.GetOwnerId(botId);
        item.SellPrice = 10_000;
        item.SlotPosition = (int)eInventorySlot.Consignment_First;
        item.RealmExchangeListedUtc = Now.ToString("O");
        return item;
    }

    private static DbInventoryItem Item(eObjectType type, int level, int slot = (int)eInventorySlot.TwoHandWeapon) => new()
    {
        Template = new DbItemTemplate
        {
            Id_nb = Guid.NewGuid().ToString(), Name = "saturation test", Object_Type = (int)type, Item_Type = slot,
            Level = level, IsTradable = true,
        },
    };

    private static MarketSearchEngine GetMarketEngine() => (MarketSearchEngine)typeof(MarketCache)
        .GetField("_searchEngine", PrivateStatic)!
        .GetValue(null)!;

    private static void SetMarketEngine(MarketSearchEngine engine) => typeof(MarketCache)
        .GetField("_searchEngine", PrivateStatic)!
        .SetValue(null, engine);
}
