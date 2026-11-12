using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>A piece one gamebot paid another to make. The buyer's coin is held in the row until delivery pays the
/// crafter or expiry refunds the buyer, so a restart loses neither the coin nor the order.</summary>
[DataTable(TableName = "offline_bot_craft_orders")]
public sealed class OfflineBotCraftOrderRecord : DataObject, ILivingServerRow
{
    public DataObject CopyForSave() => (OfflineBotCraftOrderRecord)MemberwiseClone();

    [DataElement(AllowDbNull = false, Unique = true, Varchar = 64)] public string OrderId { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false, Index = true)] public long BuyerBotId { get; set; }
    [DataElement(AllowDbNull = false, Index = true)] public long CrafterBotId { get; set; }
    [DataElement(AllowDbNull = false)] public int Realm { get; set; }
    [DataElement(AllowDbNull = false)] public int CraftingSkill { get; set; }
    [DataElement(AllowDbNull = false, Varchar = 255)] public string ProductId { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false, Varchar = 255)] public string ProductName { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int RecipeLevel { get; set; }
    /// <summary>The buyer's coin, held by the order.</summary>
    [DataElement(AllowDbNull = false)] public long Copper { get; set; }
    /// <summary><see cref="AutonomousBotCraftOrders.OpenState"/> until the piece is made, then
    /// <see cref="AutonomousBotCraftOrders.MadeState"/> until it is delivered.</summary>
    [DataElement(AllowDbNull = false, Varchar = 16)] public string State { get; set; } = AutonomousBotCraftOrders.OpenState;
    /// <summary>The made piece in the crafter's bag, while it waits for delivery.</summary>
    [DataElement(AllowDbNull = false, Varchar = 255)] public string ItemObjectId { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string CreatedUtc { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string ExpiresUtc { get; set; } = string.Empty;
}

/// <summary>
/// Orders between gamebots (bot_craft_orders). At a task break a bot with coin that a crafted piece would upgrade pays
/// a crafter of its realm whose skill makes the recipe orange or easier. The coin leaves the buyer's purse at once and
/// is held by the order. The crafter makes the piece first on its next craft trip, buying the materials with its own
/// coin as for any other work, and at its next task break hands it straight into the buyer's backpack and takes the
/// coin. An order not delivered in <see cref="Lifetime"/> refunds the buyer; a piece already made stays with its maker.
/// Only autonomous gamebots order or fill orders; companion bots and players are never involved.
/// </summary>
public static class AutonomousBotCraftOrders
{
    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousBotCraftOrders));

    [ServerProperty("autonomous", "bot_craft_orders", "Gamebots pay crafter gamebots of their realm to make armor and weapon upgrades.", true)]
    public static bool Enabled = true;

    public const string OpenState = "open";
    public const string MadeState = "made";
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(3);
    /// <summary>A crafter makes orders only at a task break (45-120 minute tasks), so it is asked only when its current
    /// task ends within this window.</summary>
    public static readonly TimeSpan BreakWindow = TimeSpan.FromMinutes(45);
    /// <summary>Chance, at a task break, that a bot with no order looks for one.</summary>
    public const double CommissionChance = 0.25;
    /// <summary>A crafter fills orders it makes at this chance or better: orange, yellow or easier.</summary>
    public const int MinimumMakeChance = 84;
    public const int MaximumOpenPerCrafter = 2;
    /// <summary>A buyer spends at most this share of its coin on one order.</summary>
    public const double BuyerShareOfCoin = 0.5;
    /// <summary>The crafter's fee on top of the materials: a share of their cost, at least <see cref="MinimumFee"/>.</summary>
    public const double FeeShare = 0.25;
    public const long MinimumFee = 100;
    public const long ExpiryPassMilliseconds = 60_000;

    internal static readonly LivingServerTable<string, OfflineBotCraftOrderRecord> Orders =
        new("craft order", row => row.OrderId, StringComparer.Ordinal);

    /// <summary>Made pieces waiting for delivery, by item id: never worn, sold or listed by their maker.</summary>
    private static readonly ConcurrentDictionary<string, string> Held = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    private static long _nextExpiryPass;

    // The coin and roster this works with; tests replace them (ResetForTests puts them back).
    public static Func<long, long, bool> SpendCoin = AutonomousBotEconomy.TrySpend;
    public static Func<long, long, bool> PayCoin = AutonomousBotEconomy.AddMoney;
    public static Func<long, GameBot> FindLive = DefaultFindLive;
    public static Func<eRealm, IEnumerable<GameBot>> CraftersOf = DefaultCraftersOf;

    private static GameBot DefaultFindLive(long botId) => AutonomousBotRegistry.TryGet(botId, out GameBot bot) ? bot : null;

    private static IEnumerable<GameBot> DefaultCraftersOf(eRealm realm) =>
        AutonomousBotRegistry.Snapshot().Where(bot => bot.Realm == realm && AutonomousBotCrafting.IsTrained(bot));

    #region State

    /// <summary>Fills the in-memory state from the saved rows (start-up, and tests).</summary>
    public static void Load(IEnumerable<OfflineBotCraftOrderRecord> rows)
    {
        OfflineBotCraftOrderRecord[] all = rows.ToArray();
        Orders.Load(all);
        Held.Clear();
        foreach (OfflineBotCraftOrderRecord row in all.Where(row => row.State == MadeState && !string.IsNullOrEmpty(row.ItemObjectId)))
            Held[row.ItemObjectId] = row.OrderId;
    }

    public static int Count => Orders.Count;

    public static OfflineBotCraftOrderRecord[] OrdersOf(long botId) =>
        Orders.Where(row => row.BuyerBotId == botId || row.CrafterBotId == botId);

    public static bool IsHeldForOrder(DbInventoryItem item) =>
        item != null && !string.IsNullOrEmpty(item.ObjectId) && Held.ContainsKey(item.ObjectId);

    private static int OpenFor(long crafterId) => Orders.Where(row => row.CrafterBotId == crafterId).Length;

    /// <summary>A crafter with an order still to make goes on a craft trip at its next task break.</summary>
    public static bool HasOpenWork(GameBot crafter) =>
        Enabled && crafter != null && Orders.Where(row => row.CrafterBotId == crafter.DatabaseID && row.State == OpenState).Length > 0;

    #endregion

    #region Rules

    /// <summary>What a piece costs its buyer: the materials at the merchant's price and the crafter's fee.</summary>
    public static long Quote(Recipe recipe) =>
        recipe.CostToCraft + Math.Max(MinimumFee, (long)Math.Ceiling(recipe.CostToCraft * FeeShare));

    /// <summary>A crafter can take the order: it knows the craft and makes the recipe at <see cref="MinimumMakeChance"/>.</summary>
    public static bool CanFill(AbstractCraftingSkill craft, string skills, Recipe recipe)
    {
        if (craft == null || recipe == null)
            return false;
        int skill = AutonomousBotCrafting.SkillValue(skills, craft.eSkill);
        return skill > 0 && craft.ChanceToMakeAt(skill, recipe.Level) >= MinimumMakeChance &&
               AutonomousBotCrafting.MeetsSecondaryRequirement(craft, recipe, skills);
    }

    /// <summary>
    /// When the crafter next reaches a task break where it can craft: now while on a craft trip or with no task, the end
    /// of its task otherwise. Never while grouped (group bots get no break) or on a break that is not a craft trip.
    /// </summary>
    public static DateTime NextCraftBreak(GameBot crafter)
    {
        OfflineWorldBotRecord record = crafter?.PersistentRecord;
        if (record == null || crafter.Group != null)
            return DateTime.MaxValue;
        if (AutonomousObjectiveAssignments.IsBetweenPveTasks(record))
            return AutonomousObjectiveAssignments.HasBetweenTaskFlag(record, 'C') ? DateTime.MinValue : DateTime.MaxValue;
        return DateTime.TryParse(record.ObjectiveExpiresUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime end)
            ? end.ToUniversalTime()
            : DateTime.MinValue;
    }

    /// <summary>The crafter of the buyer's realm to ask: able to fill it and at a craft break within
    /// <see cref="BreakWindow"/>; least busy first, then the soonest break, then the most skilled.</summary>
    public static GameBot PickCrafter(IEnumerable<GameBot> crafters, Recipe recipe, GameBot buyer)
    {
        AbstractCraftingSkill craft = CraftingMgr.getSkillbyEnum(recipe.RequiredCraftingSkill);
        if (craft == null || buyer == null)
            return null;
        DateTime latest = DateTime.UtcNow + BreakWindow;
        return crafters
            .Where(crafter => crafter != null && crafter.DatabaseID > 0 && crafter.DatabaseID != buyer.DatabaseID &&
                              crafter.Realm == buyer.Realm && crafter.IsAutonomousWorldBot && !crafter.IsTemporaryGroupHelper &&
                              crafter.Level >= AutonomousBotCrafting.MinimumBotLevel &&
                              CanFill(craft, crafter.PersistentRecord?.SerializedCraftingSkills, recipe) &&
                              OpenFor(crafter.DatabaseID) < MaximumOpenPerCrafter && NextCraftBreak(crafter) <= latest)
            .OrderBy(crafter => OpenFor(crafter.DatabaseID))
            .ThenBy(crafter => NextCraftBreak(crafter))
            .ThenByDescending(crafter => AutonomousBotCrafting.SkillValue(crafter.PersistentRecord.SerializedCraftingSkills, craft.eSkill))
            .ThenBy(crafter => crafter.DatabaseID)
            .FirstOrDefault();
    }

    /// <summary>
    /// The piece to order: of the buyer's realm and level window, within its share of coin, an upgrade to what it wears
    /// and with a crafter to make it. The biggest upgrade first, the cheaper among equals.
    /// </summary>
    public static Recipe ChooseCommission(IEnumerable<Recipe> recipes, eRealm realm, int buyerLevel, long coin,
        Func<Recipe, int> upgradeGain, Func<Recipe, bool> hasCrafter)
    {
        long budget = (long)(coin * BuyerShareOfCoin);
        return recipes
            .Where(recipe => recipe?.Product != null && !recipe.Product.IsStackable &&
                             (recipe.Product.Realm == 0 || recipe.Product.Realm == (int)realm) &&
                             FitsCommissionLevel(recipe.Product.Level, buyerLevel) && Quote(recipe) <= budget)
            .Select(recipe => (Recipe: recipe, Gain: upgradeGain(recipe)))
            .Where(candidate => candidate.Gain > 0)
            .OrderByDescending(candidate => candidate.Gain)
            .ThenBy(candidate => Quote(candidate.Recipe))
            .Select(candidate => candidate.Recipe)
            .FirstOrDefault(hasCrafter);
    }

    /// <summary>
    /// Night 2026-11-04: 2 craft orders against 88 the night before. Almost no bot was under level 20 any more, the crafters'
    /// skill ended at a median of 8 (top 28), and an order had to fit the buyer's 10-level window, so no crafter could make
    /// anything a buyer would take. Gamebots start without armor and earn it, so a lower piece is still a real upgrade to
    /// many of them (the order also needs <see cref="UpgradeGain"/> above 0): an order may be up to this many levels below
    /// its buyer.
    /// </summary>
    public const int CommissionLevelWindow = 20;

    public static bool FitsCommissionLevel(int productLevel, int buyerLevel) =>
        productLevel <= buyerLevel && productLevel + CommissionLevelWindow >= buyerLevel;

    /// <summary>How much the product would raise what the buyer wears in its slot, by the equipment upgrade rules; 0 if
    /// it is no upgrade.</summary>
    public static int UpgradeGain(GameBot buyer, Recipe recipe)
    {
        try
        {
            DbInventoryItem piece = GameInventoryItem.Create(recipe.Product);
            if (recipe.IsForUniqueProduct)
                piece.Quality = Math.Max(piece.Quality, 94);
            if (!AutonomousBotEconomy.TryGetEquipmentUpgrade(buyer, piece, out eInventorySlot slot))
                return 0;
            DbInventoryItem worn = buyer.Inventory.GetItem(slot);
            return Math.Max(1, AutonomousBotEconomy.EquipmentValue(piece) - AutonomousBotEconomy.EquipmentValue(worn));
        }
        catch
        {
            return 0;
        }
    }

    #endregion

    #region Placing, making and delivering

    /// <summary>At a task break: deliver what this bot made for others, then perhaps order an upgrade for itself. At the
    /// end of a break (<paramref name="commission"/> false) it only delivers what the craft trip made.</summary>
    public static void AtTaskBreak(GameBot bot, bool commission = true)
    {
        if (!Enabled || bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.DatabaseID <= 0)
            return;
        try
        {
            Deliver(bot);
            if (commission && Random.Shared.NextDouble() < CommissionChance)
                TryCommission(bot);
        }
        catch (Exception exception)
        {
            Log.Warn($"Craft orders: task break of {bot.Name} failed", exception);
        }
    }

    /// <summary>Orders the best upgrade a crafter of the realm can make, if the buyer has the coin. Returns the order.</summary>
    public static OfflineBotCraftOrderRecord TryCommission(GameBot buyer)
    {
        if (!Enabled || !AutonomousBotCrafting.Enabled || buyer?.IsAutonomousWorldBot != true || buyer.IsTemporaryGroupHelper ||
            buyer.DatabaseID <= 0 || buyer.Inventory == null || Orders.Where(row => row.BuyerBotId == buyer.DatabaseID).Length > 0)
            return null;
        GameBot[] crafters = CraftersOf(buyer.Realm).ToArray();
        if (crafters.Length == 0)
            return null;
        long coin = AutonomousBotEconomy.GetMoney(buyer.DatabaseID);
        IEnumerable<Recipe> recipes = AutonomousBotCrafting.Professions.SelectMany(AutonomousBotCrafting.RecipesOf);
        Recipe recipe = ChooseCommission(recipes, buyer.Realm, buyer.Level, coin, candidate => UpgradeGain(buyer, candidate),
            candidate => AutonomousBotCrafting.OnSale(candidate, buyer.Realm) && PickCrafter(crafters, candidate, buyer) != null);
        GameBot crafter = recipe == null ? null : PickCrafter(crafters, recipe, buyer);
        return crafter == null ? null : Place(buyer, crafter, recipe);
    }

    /// <summary>Takes the price from the buyer's purse into a new order for the crafter. Null when the buyer cannot pay.</summary>
    public static OfflineBotCraftOrderRecord Place(GameBot buyer, GameBot crafter, Recipe recipe)
    {
        long price = Quote(recipe);
        DateTime now = DateTime.UtcNow;
        OfflineBotCraftOrderRecord row;
        lock (Gate)
        {
            if (!SpendCoin(buyer.DatabaseID, price))
                return null;
            row = new OfflineBotCraftOrderRecord
            {
                OrderId = Guid.NewGuid().ToString("N"),
                BuyerBotId = buyer.DatabaseID,
                CrafterBotId = crafter.DatabaseID,
                Realm = (int)buyer.Realm,
                CraftingSkill = (int)recipe.RequiredCraftingSkill,
                ProductId = recipe.Product.Id_nb,
                ProductName = recipe.Product.Name ?? string.Empty,
                RecipeLevel = recipe.Level,
                Copper = price,
                State = OpenState,
                CreatedUtc = now.ToString("O"),
                ExpiresUtc = now.Add(Lifetime).ToString("O"),
            };
            Orders.TryAdd(row.OrderId, row);
        }
        buyer.MarkAutonomousStateDirty();
        Log.Info($"AUTONOMOUS_CRAFT_ORDER_PLACED order={row.OrderId} buyer=\"{buyer.Name}\" buyerId={buyer.DatabaseID} level={buyer.Level} " +
                 $"crafter=\"{crafter.Name}\" crafterId={crafter.DatabaseID} craft={recipe.RequiredCraftingSkill} recipeLevel={recipe.Level} " +
                 $"item=\"{row.ProductName}\" itemId=\"{row.ProductId}\" copper={price}");
        return row;
    }

    /// <summary>The order this crafter makes on its trip, oldest first, if it knows the recipe and can pay the materials.</summary>
    public static AutonomousBotCrafting.Session PlanOrder(GameBot crafter, long coin)
    {
        if (!Enabled || crafter == null)
            return null;
        string skills = crafter.PersistentRecord?.SerializedCraftingSkills;
        foreach (OfflineBotCraftOrderRecord row in Orders.Where(row => row.CrafterBotId == crafter.DatabaseID && row.State == OpenState)
                     .OrderBy(row => row.CreatedUtc, StringComparer.Ordinal))
        {
            var skill = (eCraftingSkill)row.CraftingSkill;
            AbstractCraftingSkill craft = CraftingMgr.getSkillbyEnum(skill);
            Recipe recipe = AutonomousBotCrafting.RecipesOf(skill)
                .FirstOrDefault(candidate => string.Equals(candidate.Product.Id_nb, row.ProductId, StringComparison.OrdinalIgnoreCase));
            // An order the crafter cannot make now stays open and is refunded when it expires.
            if (recipe == null || craft == null || craft.ChanceToMakeAt(AutonomousBotCrafting.SkillValue(skills, skill), recipe.Level) <= 0 ||
                recipe.CostToCraft > coin || !AutonomousBotCrafting.OnSale(recipe, crafter.Realm))
                continue;
            return new AutonomousBotCrafting.Session(recipe, skill, 1, row.OrderId);
        }
        return null;
    }

    /// <summary>The ordered piece is made: it waits in the crafter's bag for delivery.</summary>
    public static void OnMade(GameBot crafter, string orderId, DbInventoryItem item)
    {
        lock (Gate)
        {
            if (item == null || !Orders.TryGet(orderId, out OfflineBotCraftOrderRecord row) || row.State != OpenState)
                return;
            Orders.Change(orderId, () => row, order =>
            {
                order.State = MadeState;
                order.ItemObjectId = item.ObjectId;
            });
            Held[item.ObjectId] = orderId;
        }
        Log.Info($"AUTONOMOUS_CRAFT_ORDER_MADE order={orderId} crafter=\"{crafter.Name}\" crafterId={crafter.DatabaseID} item=\"{item.Name}\" quality={item.Quality}");
    }

    /// <summary>
    /// The crafter's task break: each made piece goes straight from its bag into the buyer's backpack and the order's coin
    /// to the crafter. A buyer who is not in the world or has a full bag is tried again at the next break. A piece the
    /// crafter no longer has refunds the buyer. Returns how many were delivered.
    /// </summary>
    public static int Deliver(GameBot crafter)
    {
        if (crafter?.Inventory == null || crafter.DatabaseID <= 0)
            return 0;
        int delivered = 0;
        foreach (OfflineBotCraftOrderRecord row in Orders.Where(row => row.CrafterBotId == crafter.DatabaseID && row.State == MadeState))
        {
            DbInventoryItem piece;
            GameBot buyer;
            lock (Gate)
            {
                if (!Orders.TryGet(row.OrderId, out OfflineBotCraftOrderRecord current) || !ReferenceEquals(current, row))
                    continue;
                piece = crafter.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack)
                    .FirstOrDefault(item => item != null && item.ObjectId == row.ItemObjectId);
                if (piece == null)
                {
                    Close(row, refund: true, "the piece is no longer in the crafter's bag");
                    continue;
                }
                buyer = FindLive(row.BuyerBotId);
                if (buyer?.Inventory == null || buyer.Realm != crafter.Realm || !Hand(crafter, buyer, piece))
                    continue;
                Orders.Remove(row.OrderId);
                Held.TryRemove(row.ItemObjectId, out _);
            }
            bool paid = PayCoin(crafter.DatabaseID, row.Copper);
            delivered++;
            foreach (GameBot bot in new[] { crafter, buyer })
            {
                AutonomousBotEconomy.MarkInventoryChanged(bot);
                bot.MarkAutonomousStateDirty();
                AutonomousBotStatusPersistence.Queue(bot, true);
            }
            bool worn = AutonomousBotEconomy.TryEquipOwnedUpgrade(buyer, piece, "craft-order");
            Log.Info($"AUTONOMOUS_CRAFT_ORDER_DELIVERED order={row.OrderId} crafter=\"{crafter.Name}\" crafterId={crafter.DatabaseID} " +
                     $"buyer=\"{buyer.Name}\" buyerId={buyer.DatabaseID} item=\"{piece.Name}\" copper={row.Copper} paid={(paid ? 1 : 0)} worn={(worn ? 1 : 0)}");
        }
        return delivered;
    }

    /// <summary>Moves the real item between the two bags, as the Realm Exchange moves a sold item; nothing is copied.</summary>
    private static bool Hand(GameBot crafter, GameBot buyer, DbInventoryItem piece)
    {
        lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
        {
            eInventorySlot slot = buyer.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
            int originalSlot = piece.SlotPosition;
            if (slot == eInventorySlot.Invalid || !crafter.Inventory.RemoveItemWithoutDbDeletion(piece))
                return false;
            if (buyer.Inventory.AddItem(slot, piece))
                return true;
            crafter.Inventory.AddItem((eInventorySlot)originalSlot, piece);
            return false;
        }
    }

    /// <summary>Ends an order under <see cref="Gate"/>; with <paramref name="refund"/> the buyer gets its coin back.</summary>
    private static void Close(OfflineBotCraftOrderRecord row, bool refund, string reason)
    {
        if (!Orders.Remove(row.OrderId))
            return;
        if (!string.IsNullOrEmpty(row.ItemObjectId))
            Held.TryRemove(row.ItemObjectId, out _);
        bool refunded = refund && PayCoin(row.BuyerBotId, row.Copper);
        Log.Info($"AUTONOMOUS_CRAFT_ORDER_REFUNDED order={row.OrderId} buyerId={row.BuyerBotId} crafterId={row.CrafterBotId} " +
                 $"item=\"{row.ProductName}\" state={row.State} copper={row.Copper} refunded={(refunded ? 1 : 0)} reason=\"{reason}\"");
        if (refund && !refunded)
            Log.Warn($"Craft orders: could not refund {row.Copper} copper to bot {row.BuyerBotId} (order {row.OrderId})");
    }

    /// <summary>Refunds every order past its time. A made piece stays with its maker as its own work. Returns how many.</summary>
    public static int ExpireDue(DateTime utcNow)
    {
        int expired = 0;
        foreach (OfflineBotCraftOrderRecord row in Orders.Where(row => IsDue(row.ExpiresUtc, utcNow)))
        {
            lock (Gate)
            {
                if (!Orders.TryGet(row.OrderId, out OfflineBotCraftOrderRecord current) || !ReferenceEquals(current, row))
                    continue;
                Close(row, refund: true, "expired");
            }
            expired++;
        }
        return expired;
    }

    /// <summary>Unreadable times count as due, so a damaged row cannot hold a buyer's coin for ever.</summary>
    public static bool IsDue(string expiresUtc, DateTime utcNow) =>
        !DateTime.TryParse(expiresUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime due) ||
        due.ToUniversalTime() <= utcNow;

    /// <summary>The Living Server's flush timer (off the game loop): the expiry pass, once a minute.</summary>
    public static void Pulse()
    {
        long now = Environment.TickCount64;
        if (now < Volatile.Read(ref _nextExpiryPass))
            return;
        Volatile.Write(ref _nextExpiryPass, now + ExpiryPassMilliseconds);
        try
        {
            ExpireDue(DateTime.UtcNow);
        }
        catch (Exception exception)
        {
            Log.Warn("Craft orders: expiry pass failed", exception);
        }
    }

    #endregion

    /// <summary>Tests only: empty state and the real coin and roster again.</summary>
    public static void ResetForTests()
    {
        Load([]);
        SpendCoin = AutonomousBotEconomy.TrySpend;
        PayCoin = AutonomousBotEconomy.AddMoney;
        FindLive = DefaultFindLive;
        CraftersOf = DefaultCraftersOf;
    }
}
