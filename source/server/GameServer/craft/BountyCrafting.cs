using System;
using System.Collections.Generic;
using DOL.Database;
using DOL.GS.PacketHandler;
using DOL.Language;

namespace DOL.GS
{
	/// <summary>
	/// Bountycraft (live patch 1.125, costs from 1.126): trade 5 in the 1.130 client's tdl.crf.
	/// Every character has it at level 1 and it never rises; every recipe needs level 1 (the client lists skill 0),
	/// never fails, always makes the item at 100% quality and gives no skill. It is worked at a forge with stardust,
	/// a bounty mold, a "Recipe: &lt;set&gt;" item and rare alchemy components (see <see cref="BountyCraftItems"/>).
	/// </summary>
	public class BountyCrafting : AbstractCraftingSkill
	{
		public const string NameFallback = "Bountycraft";

		public BountyCrafting()
		{
			// The 1.130 client names each trade skill line by this id from its own tdl.crf; 5 is "Bountycraft".
			Icon = (byte) eCraftingSkill.BountyCrafting;
			string name = LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "Crafting.Name.Bountycraft");
			Name = string.IsNullOrWhiteSpace(name) || name.StartsWith("Crafting.Name", StringComparison.Ordinal) ? NameFallback : name;
			eSkill = eCraftingSkill.BountyCrafting;
		}

		public override string CRAFTER_TITLE_PREFIX => "Bountycrafter";

		/// <summary>Bountycraft is worked at a forge (relic towns, capitals and houses have them).</summary>
		protected override bool CheckForTools(GamePlayer player, Recipe recipe)
		{
			foreach (GameStaticItem item in player.GetItemsInRadius(CRAFT_DISTANCE))
			{
				if (IsForge(item))
					return true;
			}

			player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Crafting.CheckTool.NotHaveTools", recipe.Product.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
			player.Out.SendMessage(LanguageMgr.GetTranslation(ServerProperties.Properties.DB_LANGUAGE, "Crafting.CheckTool.FindForge"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
			return player.Client.Account.PrivLevel > 1;
		}

		/// <summary>The same forge test Weaponcraft and Armorcraft use.</summary>
		public static bool IsForge(GameStaticItem item) =>
			item != null && (string.Equals(item.Name, "forge", StringComparison.OrdinalIgnoreCase) || item.Model == 478);

		/// <summary>"Items crafted through Bountycraft will always be 100% quality and never fail."</summary>
		public override int CalculateChanceToMakeItem(GamePlayer player, int craftingLevel) => 100;

		/// <summary>Bountycraft cannot be leveled past 1.</summary>
		public override void GainCraftingSkillPoints(GamePlayer player, Recipe recipe)
		{
		}

		/// <summary>No secondary skill (metalworking etc.) is needed for bounty materials.</summary>
		public override int GetSecondaryCraftingSkillMinimumLevel(Recipe recipe) => 0;
	}

	/// <summary>Ids and rules for the Bountycraft materials the world sells.</summary>
	public static class BountyCraftItems
	{
		public const string WeaponMold = "bounty_weapon_mold";
		public const string ArmorMold = "bounty_armor_mold";
		public const string AccessoryMold = "bounty_accessory_mold";

		public static readonly HashSet<string> Molds = new(StringComparer.OrdinalIgnoreCase) { WeaponMold, ArmorMold, AccessoryMold };

		public static bool IsMold(DbItemTemplate template) => template != null && Molds.Contains(template.Id_nb);

		/// <summary>
		/// Molds "can only be bought in stacks according to your realm rank": at realm rank 1 one at a time, rank 5 five.
		/// </summary>
		public static int MaxMoldsPerPurchase(int realmLevel) => Math.Max(1, realmLevel / 10 + 1);

		/// <summary>How many of <paramref name="wanted"/> may be bought in one purchase.</summary>
		public static int AllowedCount(DbItemTemplate template, int wanted, int realmLevel) =>
			IsMold(template) ? Math.Min(wanted, MaxMoldsPerPurchase(realmLevel)) : wanted;

		/// <summary>"Box of Rare Alch. Components" (2,500 BP at the Bounty Masters): 2-5 of one of eight components.</summary>
		public const string ComponentBox = "bounty_rare_alchemy_components_box";

		/// <summary>The eight ancient and enriched alchemy components the box can hold (world ItemTemplate ids).</summary>
		public static readonly string[] BoxComponents =
		{
			"Ancient_Giant_Blood", "Ancient_Troll_Blood", "Ancient_Lich_Tooth", "Ancient_Necrotic_Brain_Fluid",
			"Ancient_Crushed_Focus_Stone", "Ancient_Mirror", "Ancient_Pure_Mercury", "Enriched_Quicksilver",
		};

		public const int BoxMinCount = 2;
		public const int BoxMaxCount = 5;

		/// <summary>One box roll: which component and how many.</summary>
		public static (string Component, int Count) RollBox(Random rng) =>
			(BoxComponents[rng.Next(BoxComponents.Length)], rng.Next(BoxMinCount, BoxMaxCount + 1));

		/// <summary>Called from GamePlayer.UseSlot. True when the item was a Bountycraft item it handled.</summary>
		public static bool TryUse(GamePlayer player, DbInventoryItem item)
		{
			if (item == null || !string.Equals(item.Id_nb, ComponentBox, StringComparison.OrdinalIgnoreCase))
				return false;

			OpenBox(player, item);
			return true;
		}

		private static void OpenBox(GamePlayer player, DbInventoryItem item)
		{
			if (!player.IsAlive || player.InCombat)
			{
				player.Out.SendMessage("You can't open that right now.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return;
			}

			(string component, int count) = RollBox(Random.Shared);
			DbItemTemplate template = GameServer.Database.FindObjectByKey<DbItemTemplate>(component);
			if (template == null)
			{
				player.Out.SendMessage("The box is empty.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return;
			}

			lock (player.Inventory.Lock)
			{
				if (player.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack) == eInventorySlot.Invalid)
				{
					player.Out.SendMessage("You need a free backpack slot to open the box.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
					return;
				}

				if (!player.Inventory.RemoveCountFromStack(item, 1))
					return;

				player.Inventory.AddTemplate(GameInventoryItem.Create(template), count, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
			}

			InventoryLogging.LogInventoryAction("(bounty box)", player, eInventoryActionType.Loot, template, count);
			player.Out.SendMessage($"You open the box and find {count} {template.Name}.", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
		}
	}
}
