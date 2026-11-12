using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Gamebot crafting, second pass: recipe choice for skill and use, orders between bots, the crafters'
    /// extra Realm Exchange posts and crafted sales in the sale record.</summary>
    [TestFixture, NonParallelizable]
    public class UT_AutonomousBotCraftingOrders
    {
        private sealed class TestBot : GameBot
        {
            private TestBot() : base((OfflineWorldBotRecord)null) { }
            public override byte Level { get; set; }
            public override int EffectiveLevel => Level;
            public override eRealm Realm { get; set; }
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public override ICharacterClass CharacterClass => new ClassEnchanter();
            public override void RefreshItemBonuses() { }
        }

        private sealed class TestServer : GameServer
        {
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
            protected override IObjectDatabase DataBaseImpl => TemplateDatabase;
        }

        private static readonly Dictionary<string, DbItemTemplate> Templates = new(StringComparer.OrdinalIgnoreCase);
        private static readonly IObjectDatabase TemplateDatabase = DispatchProxy.Create<IObjectDatabase, TemplateReads>();

        public class TemplateReads : DispatchProxy
        {
            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name == "FindObjectByKey" && args[0] is string key)
                    return Templates.GetValueOrDefault(key);
                return Default(method);
            }
        }

        /// <summary>A database that keeps what is saved, for the sale ledger.</summary>
        public class SavedRows : DispatchProxy
        {
            public readonly List<DataObject> Saved = new();

            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name == "SaveObject")
                {
                    if (args[0] is IEnumerable<DataObject> rows)
                        Saved.AddRange(rows);
                    else if (args[0] is DataObject row)
                        Saved.Add(row);
                }
                return Default(method);
            }
        }

        private static object Default(MethodInfo method)
        {
            Type result = method.ReturnType;
            if (result == typeof(bool))
                return true;
            if (result.IsGenericType && typeof(IEnumerable).IsAssignableFrom(result))
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(result.GetGenericArguments()[0]));
            return result.IsValueType && result != typeof(void) ? Activator.CreateInstance(result) : null;
        }

        private GameServer _previousServer;
        private MarketSearchEngine _previousMarket;
        private MarketSearchEngine _testMarket;
        private readonly List<DbInventoryItem> _marketItems = new();
        private readonly Dictionary<long, long> _purses = new();
        private readonly Dictionary<long, GameBot> _live = new();
        private static long _nextId = 950000;

        private static FieldInfo MarketField => typeof(MarketCache).GetField("_searchEngine", BindingFlags.Static | BindingFlags.NonPublic);

        private static AbstractCraftingSkill[] CraftSkills => (AbstractCraftingSkill[])typeof(CraftingMgr)
            .GetField("m_craftingskills", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        private static AbstractCraftingSkill Weaponcraft => CraftingMgr.getSkillbyEnum(eCraftingSkill.WeaponCrafting);

        [OneTimeSetUp]
        public void Logging() => DOL.Logging.LoggerManager.InitializeWithExplicitLibrary(null, DOL.Logging.LogLibrary.None);

        [SetUp]
        public void SetUp()
        {
            _previousServer = GameServer.Instance;
            GameServer.LoadTestDouble((TestServer)RuntimeHelpers.GetUninitializedObject(typeof(TestServer)));
            _previousMarket = (MarketSearchEngine)MarketField.GetValue(null);
            _testMarket = new MarketSearchEngine();
            MarketField.SetValue(null, _testMarket);
            Templates.Clear();
            _purses.Clear();
            _live.Clear();
            // The real craft, without the translations its constructor reads.
            var craft = (WeaponCrafting)RuntimeHelpers.GetUninitializedObject(typeof(WeaponCrafting));
            craft.eSkill = eCraftingSkill.WeaponCrafting;
            CraftSkills[(int)eCraftingSkill.WeaponCrafting - 1] = craft;
            AutonomousBotCraftOrders.ResetForTests();
            AutonomousBotCraftOrders.SpendCoin = (bot, copper) =>
            {
                if (_purses.GetValueOrDefault(bot) < copper)
                    return false;
                _purses[bot] -= copper;
                return true;
            };
            AutonomousBotCraftOrders.PayCoin = (bot, copper) =>
            {
                _purses[bot] = _purses.GetValueOrDefault(bot) + copper;
                return true;
            };
            AutonomousBotCraftOrders.FindLive = id => _live.GetValueOrDefault(id);
            AutonomousBotCraftOrders.CraftersOf = realm => _live.Values.Where(bot => bot.Realm == realm);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (DbInventoryItem item in _marketItems)
                MarketCache.RemoveItem(item);
            _marketItems.Clear();
            _testMarket.Dispose();
            MarketField.SetValue(null, _previousMarket);
            AutonomousBotCraftOrders.ResetForTests();
            AutonomousBotCrafting.SetCatalogForTests([], eCraftingSkill.WeaponCrafting);
            CraftSkills[(int)eCraftingSkill.WeaponCrafting - 1] = null;
            GameServer.LoadTestDouble(_previousServer);
        }

        private static void Field(Type type, object target, string name, object value) =>
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private TestBot Bot(string name, string skills = "", int level = 20)
        {
            TestBot bot = (TestBot)RuntimeHelpers.GetUninitializedObject(typeof(TestBot));
            Field(typeof(GameLiving), bot, "<TempProperties>k__BackingField", new PropertyCollection());
            Field(typeof(GameNPC), bot, "m_brains", new ArrayList());
            bot.Level = (byte)level;
            bot.Realm = eRealm.Albion;
            bot.Name = name;
            bot.DatabaseID = Interlocked.Increment(ref _nextId);
            bot.InternalID = AutonomousBotEconomy.GetOwnerId(bot.DatabaseID);
            bot.Inventory = new BotInventory(bot.InternalID);
            typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot)).SetValue(bot, true);
            typeof(GameBot).GetProperty(nameof(GameBot.PersistentRecord)).SetValue(bot,
                new OfflineWorldBotRecord { BotId = bot.DatabaseID, SerializedCraftingSkills = skills });
            Field(typeof(GameNPC), bot, "m_ownBrain", new BotBrain { Body = bot });
            Field(typeof(GameBot), bot, "_experienceLock", new Lock());
            _live[bot.DatabaseID] = bot;
            return bot;
        }

        private static DbItemTemplate Item(string id, int price = 10, int pack = 1, int maxCount = 1, int model = 1) =>
            Templates[id] = new DbItemTemplate { Id_nb = id, Name = id, Level = 1, Quality = 95, MaxCount = maxCount, PackSize = pack,
                MaxCondition = 10000, MaxDurability = 10000, Price = price, Model = model, IsTradable = true, IsDropable = true };

        private static Recipe MakeRecipe(int level, long price = 10, int productLevel = 1, string id = null, bool unique = true,
            eObjectType type = eObjectType.SlashingWeapon)
        {
            DbItemTemplate product = Item(id ?? $"product_{level}_{Guid.NewGuid():N}", 100);
            product.Level = productLevel;
            product.Object_Type = (int)type;
            DbItemTemplate material = Item("bars_" + price, (int)price, 1, 100, 519);
            return new Recipe(product, [new Ingredient(2, material)], eCraftingSkill.WeaponCrafting, level, !unique);
        }

        private static DbInventoryItem OwnPiece(GameBot maker, string name)
        {
            DbInventoryItem piece = GameInventoryItem.Create(Item(name));
            piece.IsCrafted = true;
            piece.Creator = maker.Name;
            Assert.That(maker.Inventory.AddItem(eInventorySlot.FirstEmptyBackpack, piece), Is.True);
            return piece;
        }

        #region 1. Recipe choice for skill gain

        [Test]
        public void TheHardestRecipeThatStillTeachesAboutTheBestIsChosen()
        {
            // Skill 100: 101 and 110 are orange (84% to make, 55% to gain), 130 is red (68%, 65%): within 5%.
            Recipe lowOrange = MakeRecipe(101, price: 5);
            Recipe orange = MakeRecipe(110);
            Recipe red = MakeRecipe(130, price: 20);
            Recipe yellow = MakeRecipe(95, price: 1);

            AutonomousBotCrafting.Choice choice = AutonomousBotCrafting.ChooseRecipe([yellow, lowOrange, orange, red],
                Weaponcraft, "1|100;6|200", eRealm.Albion, 1_000, _ => true);

            Assert.That(choice.Recipe, Is.SameAs(red), "Not the cheapest orange, the hardest that teaches about as well");
            Assert.That(choice.Make, Is.EqualTo(68));
            Assert.That(choice.Gain, Is.EqualTo(65));
        }

        [Test]
        public void ARecipeThatTeachesNothingIsNeverChosen()
        {
            Recipe grey = MakeRecipe(40);
            Recipe impossible = MakeRecipe(160);
            Assert.That(AutonomousBotCrafting.ChooseRecipe([grey, impossible], Weaponcraft, "1|100;6|200", eRealm.Albion, 1_000, _ => true),
                Is.Null, "A grey recipe gives no point and a purple one cannot be made");

            Recipe green = MakeRecipe(60);
            Assert.That(AutonomousBotCrafting.ChooseRecipe([grey, green], Weaponcraft, "1|100;6|200", eRealm.Albion, 1_000, _ => true).Recipe,
                Is.SameAs(green), "A green recipe still teaches, at 15%");
        }

        [Test]
        public void AYellowRecipeIsNotATieWithAnOrangeOne()
        {
            Recipe yellow = MakeRecipe(100);
            Recipe orange = MakeRecipe(105, price: 50);
            Assert.That(AutonomousBotCrafting.ChooseRecipe([yellow, orange], Weaponcraft, "1|100;6|200", eRealm.Albion, 1_000, _ => true).Recipe,
                Is.SameAs(orange));
        }

        [Test]
        public void TheHardestRecipeTheBotCannotAffordGivesWayToOneItCan()
        {
            Recipe dearRed = MakeRecipe(130, price: 500);
            Recipe orange = MakeRecipe(110, price: 10);
            Assert.That(AutonomousBotCrafting.ChooseRecipe([dearRed, orange], Weaponcraft, "1|100;6|200", eRealm.Albion, 100, _ => true).Recipe,
                Is.SameAs(orange));
        }

        #endregion

        #region 2. Useful gear

        [Test]
        public void AmongRecipesThatTeachAlikeAPieceSomeoneCanUseComesFirst()
        {
            Recipe useless = MakeRecipe(130, id: "useless");
            Recipe useful = MakeRecipe(112, price: 40, id: "useful");

            AutonomousBotCrafting.Choice choice = AutonomousBotCrafting.ChooseRecipe([useless, useful], Weaponcraft, "1|100;6|200",
                eRealm.Albion, 1_000, _ => true, recipe => recipe == useful);

            Assert.That(choice.Recipe, Is.SameAs(useful));
        }

        [Test]
        public void UseNeverBuysAWorseChanceOfAPoint()
        {
            Recipe useless = MakeRecipe(110);
            Recipe usefulButBlue = MakeRecipe(80);
            Assert.That(AutonomousBotCrafting.ChooseRecipe([useless, usefulButBlue], Weaponcraft, "1|100;6|200", eRealm.Albion, 1_000,
                _ => true, recipe => recipe == usefulButBlue).Recipe, Is.SameAs(useless), "Blue teaches at 30%, orange at 55%");
        }

        [TestCase(20, 20, true)]
        [TestCase(10, 20, true)]
        [TestCase(9, 20, false)]
        [TestCase(21, 20, false)]
        public void APieceFitsAWearerOfItsLevelWindow(int productLevel, int wearerLevel, bool fits) =>
            Assert.That(AutonomousBotCrafting.FitsLevel(productLevel, wearerLevel), Is.EqualTo(fits));

        [Test]
        public void OnlyArmorAndWeaponsCountAsUseful()
        {
            TestBot bot = Bot("Wearer");
            DbItemTemplate potion = Item("potion");
            potion.Level = 20;
            potion.Object_Type = (int)eObjectType.Magical;
            Assert.That(AutonomousBotCrafting.CouldUse(bot, potion), Is.False);
            Assert.That(AutonomousBotCrafting.UsefulTo(bot)(new Recipe(potion, [new Ingredient(1, Item("ore"))], eCraftingSkill.WeaponCrafting, 1, true)),
                Is.False);
        }

        #endregion

        #region 3. Orders between bots

        [Test]
        public void APiecesPriceIsItsMaterialsAndAFee()
        {
            Assert.That(AutonomousBotCraftOrders.Quote(MakeRecipe(10, price: 10)), Is.EqualTo(20 + AutonomousBotCraftOrders.MinimumFee));
            Assert.That(AutonomousBotCraftOrders.Quote(MakeRecipe(10, price: 5_000)), Is.EqualTo(10_000 + 2_500));
        }

        [Test]
        public void ACrafterFillsOnlyWhatItMakesAtOrangeOrBetter()
        {
            Recipe recipe = MakeRecipe(110);
            Assert.That(AutonomousBotCraftOrders.CanFill(Weaponcraft, "1|100;6|200", recipe), Is.True, "Orange: 84%");
            Assert.That(AutonomousBotCraftOrders.CanFill(Weaponcraft, "1|85;6|200", recipe), Is.False, "Red: 68%");
            Assert.That(AutonomousBotCraftOrders.CanFill(Weaponcraft, "2|300", MakeRecipe(1)), Is.False, "Not a weaponcrafter");
        }

        [Test]
        public void TheLeastBusyAbleCrafterOfTheRealmIsAsked()
        {
            TestBot buyer = Bot("Buyer");
            TestBot weak = Bot("Weak", "1|50;6|200");
            TestBot strong = Bot("Strong", "1|300;6|300");
            TestBot able = Bot("Able", "1|120;6|200");
            TestBot enemy = Bot("Enemy", "1|500;6|500");
            enemy.Realm = eRealm.Midgard;
            Recipe recipe = MakeRecipe(110);

            GameBot first = AutonomousBotCraftOrders.PickCrafter([buyer, weak, strong, able, enemy], recipe, buyer);
            Assert.That(first, Is.SameAs(strong), "The most skilled among the idle");

            _purses[buyer.DatabaseID] = 1_000_000;
            AutonomousBotCraftOrders.Place(buyer, strong, recipe);
            Assert.That(AutonomousBotCraftOrders.PickCrafter([weak, strong, able, enemy], recipe, buyer), Is.SameAs(able),
                "A crafter with an order waits behind one without");
        }

        [Test]
        public void OnlyACrafterWhoseTaskEndsSoonIsAsked()
        {
            TestBot buyer = Bot("Buyer");
            TestBot late = Bot("Late", "1|300;6|300");
            TestBot soon = Bot("Soon", "1|120;6|200");
            TestBot later = Bot("Later", "1|200;6|200");
            TestBot resting = Bot("Resting", "1|500;6|500");
            late.PersistentRecord.ObjectiveExpiresUtc = DateTime.UtcNow.AddMinutes(100).ToString("O");
            soon.PersistentRecord.ObjectiveExpiresUtc = DateTime.UtcNow.AddMinutes(10).ToString("O");
            later.PersistentRecord.ObjectiveExpiresUtc = DateTime.UtcNow.AddMinutes(30).ToString("O");
            resting.PersistentRecord.ObjectiveAssignmentId = "between-pve-services-ID-" + resting.DatabaseID + "-1";
            Recipe recipe = MakeRecipe(110);

            Assert.That(AutonomousBotCraftOrders.PickCrafter([late, soon, later, resting], recipe, buyer), Is.SameAs(soon),
                "The soonest break; not one 100 minutes away, nor one on a break without a craft trip");

            resting.PersistentRecord.ObjectiveAssignmentId = "between-pve-services-CI-" + resting.DatabaseID + "-1";
            Assert.That(AutonomousBotCraftOrders.PickCrafter([late, soon, later, resting], recipe, buyer), Is.SameAs(resting),
                "A crafter on its craft trip now");
            Assert.That(AutonomousBotCraftOrders.PickCrafter([late], recipe, buyer), Is.Null);
        }

        [Test]
        public void TheBiggestAffordableUpgradeWithACrafterIsOrdered()
        {
            Recipe small = MakeRecipe(10, price: 10, productLevel: 18);
            Recipe big = MakeRecipe(20, price: 10, productLevel: 20);
            Recipe tooDear = MakeRecipe(30, price: 10_000, productLevel: 20);
            Recipe tooHigh = MakeRecipe(40, price: 10, productLevel: 25);
            Recipe noCrafter = MakeRecipe(50, price: 10, productLevel: 19);
            var gains = new Dictionary<Recipe, int> { [small] = 20, [big] = 50, [tooDear] = 500, [tooHigh] = 500, [noCrafter] = 400 };

            Recipe chosen = AutonomousBotCraftOrders.ChooseCommission([small, big, tooDear, tooHigh, noCrafter], eRealm.Albion, 20, 1_000,
                recipe => gains[recipe], recipe => recipe != noCrafter);

            Assert.That(chosen, Is.SameAs(big));
            Assert.That(AutonomousBotCraftOrders.ChooseCommission([small], eRealm.Albion, 20, 1_000, _ => 0, _ => true), Is.Null,
                "No upgrade, no order");
        }

        [Test]
        public void ALowPieceIsStillOrderedAsAnUpgradeForALevel20Buyer()
        {
            // Night 2026-11-04: crafters topped out at skill 28, so only low pieces could be made.
            Recipe low = MakeRecipe(10, price: 10, productLevel: 3);
            Assert.That(AutonomousBotCraftOrders.ChooseCommission([low], eRealm.Albion, 22, 1_000, _ => 30, _ => true), Is.SameAs(low));
            Assert.That(AutonomousBotCraftOrders.ChooseCommission([low], eRealm.Albion, 24, 1_000, _ => 30, _ => true), Is.Null,
                "More than the commission window below the buyer");
            Assert.That(AutonomousBotCraftOrders.FitsCommissionLevel(25, 20), Is.False, "Never above the buyer");
        }

        [Test]
        public void PlacingAnOrderTakesTheBuyersCoinIntoTheOrder()
        {
            TestBot buyer = Bot("Buyer");
            TestBot smith = Bot("Smith", "1|120;6|200");
            Recipe recipe = MakeRecipe(110, price: 100);
            long price = AutonomousBotCraftOrders.Quote(recipe);
            _purses[buyer.DatabaseID] = price - 1;

            Assert.That(AutonomousBotCraftOrders.Place(buyer, smith, recipe), Is.Null, "Too little coin");
            Assert.That(AutonomousBotCraftOrders.Count, Is.Zero);

            _purses[buyer.DatabaseID] = price + 5;
            OfflineBotCraftOrderRecord order = AutonomousBotCraftOrders.Place(buyer, smith, recipe);

            Assert.That(order, Is.Not.Null);
            Assert.That(_purses[buyer.DatabaseID], Is.EqualTo(5));
            Assert.That(order.Copper, Is.EqualTo(price));
            Assert.That(order.State, Is.EqualTo(AutonomousBotCraftOrders.OpenState));
            Assert.That(AutonomousBotCraftOrders.HasOpenWork(smith), Is.True);
            Assert.That(AutonomousBotCraftOrders.HasOpenWork(buyer), Is.False);
        }

        [Test]
        public void AnOrderIsMadeFirstWithRealMaterialsAndDeliveredIntoTheBuyersBag()
        {
            TestBot buyer = Bot("Buyer");
            TestBot smith = Bot("Smith", "1|120;6|200");
            Recipe recipe = MakeRecipe(110, price: 100, id: "ordered_sword");
            Recipe other = MakeRecipe(125, price: 1);
            AutonomousBotCrafting.SetCatalogForTests([recipe, other], eCraftingSkill.WeaponCrafting, allOnSale: true);
            _purses[buyer.DatabaseID] = 10_000;
            OfflineBotCraftOrderRecord order = AutonomousBotCraftOrders.Place(buyer, smith, recipe);

            AutonomousBotCrafting.Session session = AutonomousBotCraftOrders.PlanOrder(smith, 1_000);
            Assert.That(session.Recipe, Is.SameAs(recipe));
            Assert.That(session.Copies, Is.EqualTo(1));
            Assert.That(session.OrderId, Is.EqualTo(order.OrderId));
            Assert.That(AutonomousBotCraftOrders.PlanOrder(smith, 199), Is.Null, "The crafter pays for the materials");

            DbItemTemplate bars = recipe.Ingredients[0].Material;
            DbInventoryItem piece = null;
            for (int i = 0; i < 50 && piece == null; i++)
            {
                smith.Inventory.AddTemplate(GameInventoryItem.Create(bars), 2, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                piece = AutonomousBotCrafting.Attempt(smith, session, out _);
            }
            Assert.That(piece, Is.Not.Null);
            Assert.That(order.State, Is.EqualTo(AutonomousBotCraftOrders.MadeState));
            Assert.That(AutonomousBotCraftOrders.IsHeldForOrder(piece), Is.True);
            Assert.That(AutonomousBotCrafting.UnsoldWork(smith), Is.Zero, "The buyer's piece is not stock to sell");
            Assert.That(AutonomousBotEconomy.TryGetEquipmentUpgrade(smith, piece, out _), Is.False, "Never worn by its maker");
            Assert.That(AutonomousBotCraftOrders.HasOpenWork(smith), Is.False);

            Assert.That(AutonomousBotCraftOrders.Deliver(smith), Is.EqualTo(1));

            Assert.That(smith.Inventory.AllItems, Does.Not.Contain(piece));
            Assert.That(buyer.Inventory.AllItems, Does.Contain(piece), "The real item, not a copy");
            Assert.That(piece.OwnerID, Is.EqualTo(buyer.InternalID));
            Assert.That(_purses[smith.DatabaseID], Is.EqualTo(order.Copper), "The order's coin goes to the crafter");
            Assert.That(AutonomousBotCraftOrders.Count, Is.Zero);
            Assert.That(AutonomousBotCraftOrders.IsHeldForOrder(piece), Is.False);
        }

        [Test]
        public void ABuyerOutOfTheWorldWaitsForTheNextBreak()
        {
            TestBot buyer = Bot("Buyer");
            TestBot smith = Bot("Smith", "1|120;6|200");
            Recipe recipe = MakeRecipe(110);
            _purses[buyer.DatabaseID] = 10_000;
            OfflineBotCraftOrderRecord order = AutonomousBotCraftOrders.Place(buyer, smith, recipe);
            DbInventoryItem piece = OwnPiece(smith, "made sword");
            AutonomousBotCraftOrders.OnMade(smith, order.OrderId, piece);
            _live.Remove(buyer.DatabaseID);

            Assert.That(AutonomousBotCraftOrders.Deliver(smith), Is.Zero);
            Assert.That(smith.Inventory.AllItems, Does.Contain(piece));
            Assert.That(AutonomousBotCraftOrders.Count, Is.EqualTo(1));
        }

        [Test]
        public void AnExpiredOrderRefundsTheBuyerAndLeavesThePieceWithItsMaker()
        {
            TestBot buyer = Bot("Buyer");
            TestBot smith = Bot("Smith", "1|120;6|200");
            _purses[buyer.DatabaseID] = 10_000;
            OfflineBotCraftOrderRecord order = AutonomousBotCraftOrders.Place(buyer, smith, MakeRecipe(110));
            DbInventoryItem piece = OwnPiece(smith, "made sword");
            AutonomousBotCraftOrders.OnMade(smith, order.OrderId, piece);

            Assert.That(AutonomousBotCraftOrders.ExpireDue(DateTime.UtcNow), Is.Zero, "Not yet");
            Assert.That(AutonomousBotCraftOrders.ExpireDue(DateTime.UtcNow + AutonomousBotCraftOrders.Lifetime + TimeSpan.FromMinutes(1)), Is.EqualTo(1));

            Assert.That(_purses[buyer.DatabaseID], Is.EqualTo(10_000), "Every copper back");
            Assert.That(_purses.GetValueOrDefault(smith.DatabaseID), Is.Zero);
            Assert.That(AutonomousBotCraftOrders.Count, Is.Zero);
            Assert.That(AutonomousBotCraftOrders.IsHeldForOrder(piece), Is.False);
            Assert.That(AutonomousBotCrafting.UnsoldWork(smith), Is.EqualTo(1), "Now the maker's own work to sell");
        }

        [Test]
        public void APieceTheCrafterLostRefundsTheBuyer()
        {
            TestBot buyer = Bot("Buyer");
            TestBot smith = Bot("Smith", "1|120;6|200");
            _purses[buyer.DatabaseID] = 10_000;
            OfflineBotCraftOrderRecord order = AutonomousBotCraftOrders.Place(buyer, smith, MakeRecipe(110));
            DbInventoryItem piece = OwnPiece(smith, "made sword");
            AutonomousBotCraftOrders.OnMade(smith, order.OrderId, piece);
            smith.Inventory.RemoveItem(piece);

            Assert.That(AutonomousBotCraftOrders.Deliver(smith), Is.Zero);
            Assert.That(_purses[buyer.DatabaseID], Is.EqualTo(10_000));
            Assert.That(AutonomousBotCraftOrders.Count, Is.Zero);
        }

        [Test]
        public void SavedOrdersComeBackAfterARestart()
        {
            var open = new OfflineBotCraftOrderRecord { OrderId = "a", BuyerBotId = 1, CrafterBotId = 2, Copper = 300, ExpiresUtc = DateTime.UtcNow.AddHours(1).ToString("O") };
            var made = new OfflineBotCraftOrderRecord { OrderId = "b", BuyerBotId = 3, CrafterBotId = 2, Copper = 400, State = AutonomousBotCraftOrders.MadeState,
                ItemObjectId = "item-b", ExpiresUtc = DateTime.UtcNow.AddHours(1).ToString("O") };

            AutonomousBotCraftOrders.Load([open, made]);

            Assert.That(AutonomousBotCraftOrders.Count, Is.EqualTo(2));
            Assert.That(AutonomousBotCraftOrders.OrdersOf(2), Has.Length.EqualTo(2));
            DbInventoryItem piece = GameInventoryItem.Create(Item("held"));
            typeof(DataObject).GetProperty(nameof(DataObject.ObjectId)).SetValue(piece, "item-b");
            Assert.That(AutonomousBotCraftOrders.IsHeldForOrder(piece), Is.True, "A made piece stays held across the restart");
            Assert.That(AutonomousBotCraftOrders.IsDue("not a time", DateTime.UtcNow), Is.True, "A damaged row is refunded, not kept");
        }

        [Test]
        public void TheSwitchStopsOrders()
        {
            TestBot buyer = Bot("Buyer");
            TestBot smith = Bot("Smith", "1|120;6|200");
            _purses[buyer.DatabaseID] = 10_000;
            AutonomousBotCraftOrders.Place(buyer, smith, MakeRecipe(110));
            AutonomousBotCraftOrders.Enabled = false;
            try
            {
                Assert.That(AutonomousBotCraftOrders.HasOpenWork(smith), Is.False);
                Assert.That(AutonomousBotCraftOrders.PlanOrder(smith, 10_000), Is.Null);
                Assert.That(AutonomousBotCraftOrders.TryCommission(buyer), Is.Null);
            }
            finally
            {
                AutonomousBotCraftOrders.Enabled = true;
            }
        }

        #endregion

        #region 4. Listing cap

        private void List(GameBot bot, int count)
        {
            for (int i = 0; i < count; i++)
            {
                DbInventoryItem listed = GameInventoryItem.Create(Item($"listing {bot.DatabaseID} {i}"));
                listed.OwnerID = bot.InternalID;
                listed.OwnerLot = RealmExchangeBroker.AlbionOwnerLot;
                listed.SlotPosition = (int)eInventorySlot.Consignment_First + i;
                listed.SellPrice = 100;
                Assert.That(MarketCache.AddItem(listed), Is.True);
                _marketItems.Add(listed);
            }
        }

        [Test]
        public void OwnWorkHasItsOwnExtraPosts()
        {
            TestBot smith = Bot("Smith", "1|50");
            List(smith, AutonomousBotEconomy.MaxBotRealmExchangeListings);

            Assert.That(AutonomousBotEconomy.FindFreeListingSlot(smith.InternalID), Is.EqualTo(-1), "Loot stops at the cap");
            Assert.That(AutonomousBotEconomy.FindFreeListingSlot(smith.InternalID, true), Is.GreaterThanOrEqualTo(0));
            DbInventoryItem own = OwnPiece(smith, "own sword");
            DbInventoryItem loot = GameInventoryItem.Create(Item("found sword"));
            Assert.That(AutonomousBotEconomy.CanListNow(smith, own), Is.True);
            Assert.That(AutonomousBotEconomy.CanListNow(smith, loot), Is.False);

            TestBot full = Bot("Full", "1|50");
            List(full, AutonomousBotEconomy.MaxBotRealmExchangeListings + AutonomousBotEconomy.OwnWorkExtraListings);
            Assert.That(AutonomousBotEconomy.FindFreeListingSlot(full.InternalID, true), Is.EqualTo(-1), "The extra posts have an end too");
        }

        [Test]
        public void AtTheCapACrafterStillPostsItsOwnWork()
        {
            TestBot smith = Bot("Smith", "1|50");
            smith.PersistentRecord.ObjectiveAssignmentId = "between-pve-services-CI-1-1";
            List(smith, AutonomousBotEconomy.MaxBotRealmExchangeListings);
            DbInventoryItem loot = GameInventoryItem.Create(Item("dear loot", 100_000));
            loot.Level = 30;
            loot.Quality = 100;
            Assert.That(smith.Inventory.AddItem(eInventorySlot.FirstEmptyBackpack, loot), Is.True);
            DbInventoryItem own = OwnPiece(smith, "own sword");

            Assert.That(AutonomousBotEconomy.FindValuableListingCandidate(smith)?.Item, Is.SameAs(own));
        }

        #endregion

        #region 5. Crafted sales

        [Test]
        public void ASaleRecordsWhetherTheItemWasCrafted()
        {
            var database = DispatchProxy.Create<IObjectDatabase, SavedRows>();
            var sale = new RealmExchangeSaleLedger.Sale(1, "Bronze sword", 1, 500, "offlinebot:1", "Smith", "offlinebot:2", "Buyer",
                DateTime.UtcNow, true);

            Assert.That(RealmExchangeSaleLedger.CommitSale(database, sale, new OfflineWorldBotRecord()), Is.True);

            RealmExchangeSaleRecord row = ((SavedRows)(object)database).Saved.OfType<RealmExchangeSaleRecord>().Single();
            Assert.That(row.IsCrafted, Is.True);
            Assert.That(RealmExchangeSaleLedger.SaleLine(sale), Does.StartWith("AUTONOMOUS_EXCHANGE_SOLD ").And.EndWith(" crafted=1"));
            Assert.That(RealmExchangeSaleLedger.SaleLine(sale with { Crafted = false }), Does.EndWith(" crafted=0"));
        }

        [Test]
        public void AnOldSaleReadsAsNotCrafted() =>
            Assert.That(new RealmExchangeSaleLedger.Sale(1, "Loot", 1, 5, "a", "A", "b", "B", DateTime.UtcNow).Crafted, Is.False);

        #endregion
    }
}
