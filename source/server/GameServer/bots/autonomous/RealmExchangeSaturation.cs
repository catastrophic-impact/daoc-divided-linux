using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;

namespace DOL.GS;

/// <summary>
/// Stops gamebots flooding the Realm Exchange with one kind of item. Before a bot posts, it counts the bot listings
/// already in its realm of the same object type and slot within the same five-level band; at the cap it vendors the
/// piece instead. Player listings are never counted or limited. Discord 2026-11-09 (PhantomFox): 25k listings, nearly
/// all staves; the test world held 2,180 woolen regal cloaks and 1,268 looted focus staves.
/// </summary>
public static class RealmExchangeSaturation
{
    /// <summary>Bot listings of one kind (realm, object type, slot, level band) before bots stop posting more.</summary>
    public const int MaxBotListingsPerKind = 25;
    public const int LevelBand = 5;
    private const long RefreshMilliseconds = 30_000;

    public readonly record struct Kind(ushort Lot, int ObjectType, int ItemType, int Band);

    private static readonly object Gate = new();
    private static Dictionary<Kind, int> _counts;
    private static long _builtAt = long.MinValue;

    public static Kind KindOf(ushort lot, DbInventoryItem item) =>
        new(lot, item.Object_Type, item.Item_Type, Math.Max(0, item.Level) / LevelBand);

    /// <summary>Counts live (unexpired) bot listings by kind. Pure, so tests can feed it rows.</summary>
    public static Dictionary<Kind, int> Count(IEnumerable<DbInventoryItem> listings, DateTime utcNow)
    {
        var counts = new Dictionary<Kind, int>();
        foreach (DbInventoryItem item in listings)
        {
            if (item == null || item.SellPrice <= 0 || !RealmExchangeBroker.IsExchangeOwnerLot(item.OwnerLot) ||
                !AutonomousBotEconomy.TryParseOwnerId(item.OwnerID, out _) || RealmExchangeExpiry.IsExpired(item, utcNow))
                continue;
            Kind kind = KindOf(item.OwnerLot, item);
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
        }
        return counts;
    }

    public static bool IsSaturated(IReadOnlyDictionary<Kind, int> counts, ushort lot, DbInventoryItem item) =>
        item != null && counts.GetValueOrDefault(KindOf(lot, item)) >= MaxBotListingsPerKind;

    /// <summary>Whether the bot's realm already has enough bot listings like this item.</summary>
    public static bool IsSaturated(eRealm realm, DbInventoryItem item)
    {
        if (item == null)
            return false;
        ushort lot = RealmExchangeBroker.GetOwnerLot(realm);
        if (!RealmExchangeBroker.IsExchangeOwnerLot(lot))
            return false;
        lock (Gate)
            return IsSaturated(Snapshot(), lot, item);
    }

    /// <summary>Counts a post at once, so bots between refreshes don't all post into the same gap.</summary>
    public static void NoteListed(DbInventoryItem item)
    {
        if (item == null || !RealmExchangeBroker.IsExchangeOwnerLot(item.OwnerLot))
            return;
        lock (Gate)
        {
            Dictionary<Kind, int> counts = Snapshot();
            Kind kind = KindOf(item.OwnerLot, item);
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
        }
    }

    // Caller holds Gate. One pass over the market every 30 s, not one per bot appraisal.
    private static Dictionary<Kind, int> Snapshot()
    {
        long now = Environment.TickCount64;
        if (_counts == null || now - _builtAt >= RefreshMilliseconds)
        {
            _counts = Count(MarketCache.SearchItems(new ItemQuery()).ToList(), DateTime.UtcNow);
            _builtAt = now;
        }
        return _counts;
    }

    /// <summary>For tests: forget the cached counts.</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _counts = null;
            _builtAt = long.MinValue;
        }
    }
}
