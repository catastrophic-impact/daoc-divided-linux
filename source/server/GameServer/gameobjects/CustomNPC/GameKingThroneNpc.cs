/*
 * DAWN OF LIGHT - The first free open source DAoC server emulator
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
 *
 */
using System;
using DOL.GS.PacketHandler;
using DOL.Database;
using DOL.Language;

namespace DOL.GS
{
	// This class has to be completed and may be inherited for scripting purpose (like quests)
	/// <summary>
	/// The realm's King in the throne room. A level 50 player starts Champion training here (ChampionRules.BecomeChampion,
	/// owner 2026-11-09); champion levels earned are kept, never cleared. He also levels and respecs champions and points
	/// them to the Weaponmasters.
	/// </summary>
	public class KingNPC : GameNPC
	{
		public KingNPC()
			: base()
		{
		}

		public override bool Interact(GamePlayer player)
		{
			if (!base.Interact(player))
				return false;

			TurnTo(player, 5000);
			if (player.Realm != Realm)
				return true;

			if (!player.Champion)
			{
				if (player.Level >= GamePlayer.MAX_LEVEL)
					SayTo(player, "Your deeds have reached my ears. Will you take up the life of the [Champions] of this realm? Your battles against our enemies will then raise you through the champion levels.");
				else
					SayTo(player, "Serve the realm well. When you have reached your fiftieth season, come before me again.");
				return true;
			}

			bool cllevel = false;
			while (player.ChampionLevel < player.ChampionMaxLevel && player.ChampionExperience >= player.ChampionExperienceForNextLevel)
			{
				player.ChampionLevelUp();
				cllevel = true;
			}
			if (cllevel)
				player.Out.SendMessage("You reached champion level " + player.ChampionLevel + "!", eChatType.CT_System, eChatLoc.CL_PopupWindow);

			string respec = player.ChampionLevel >= 5 ? " I can also [respecialize] your champion skills if you so desire." : string.Empty;
			SayTo(player, "Welcome, champion. My Weaponmasters in this hall will arm you." + respec);
			return true;
		}

		public override bool WhisperReceive(GameLiving source, string str)
		{
			if (!base.WhisperReceive(source, str))
				return false;

			if (source is not GamePlayer player)
				return false;

			string text = str?.Trim().ToLowerInvariant();
			if (text == "champions" || text == "champion")
			{
				switch (ChampionRules.BecomeChampion(player, Realm))
				{
					case ChampionRules.KingAnswer.NowChampion:
						player.Out.SendUpdatePlayer();
						player.SaveIntoDatabase();
						SayTo(player, "Rise, champion of the realm. Realm points you earn from now on raise your champion levels. My Weaponmasters in this hall will arm you.");
						player.Out.SendMessage("Champion training has begun. Realm points from PvP/RvR now advance your Champion Levels.", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
						return true;
					case ChampionRules.KingAnswer.AlreadyChampion:
						SayTo(player, "You are already a champion of this realm.");
						return true;
					default:
						return true;
				}
			}

			if (text == "respecialize" && player.Realm == Realm && player.Champion && player.ChampionLevel >= 5)
			{
				player.RespecChampionSkills();
				player.SaveIntoDatabase();
				player.Out.SendMessage("I have reset your champion skills!", eChatType.CT_Important, eChatLoc.CL_PopupWindow);
			}

			return true;
		}
	}

	// This class has to be completed and may be inherited for scripting purpose
	public class CLWeaponNPC : GameNPC
	{
		public CLWeaponNPC()
			: base()
		{
		}
		/// <summary>
		/// Talk to trainer
		/// </summary>
		/// <param name="source"></param>
		/// <param name="text"></param>
		/// <returns></returns>
		public override bool WhisperReceive(GameLiving source, string text)
		{
			if (!base.WhisperReceive(source, text)) return false;
			GamePlayer player = source as GamePlayer;
			if (player == null) return false;

			switch (text)
			{
				//level respec for players
				case "respecialize":
					if (player.Champion && player.ChampionLevel >= 5)
					{
						player.RespecChampionSkills();
						player.Out.SendMessage("I have reset your Champion skills!", eChatType.CT_Important, eChatLoc.CL_SystemWindow);

					}
					break;
			}

			//Now we turn the npc into the direction of the person
			TurnTo(player, 10000);
			return true;
		}

		/// <summary>
		/// For Recieving CL Respec Stone.
		/// </summary>
		/// <param name="source"></param>
		/// <param name="item"></param>
		/// <returns></returns>
		public override bool ReceiveItem(GameLiving source, DbInventoryItem item)
		{
			if (source == null || item == null) return false;

			GamePlayer player = source as GamePlayer;
			if (player != null)
			{
				switch (item.Id_nb)
				{
					case "respec_cl":
						{
							player.Inventory.RemoveCountFromStack(item, 1);
                            InventoryLogging.LogInventoryAction(player, this, eInventoryActionType.Other, item.Template);
							player.RespecAmountChampionSkill++;
							player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "CLWeaponNPC.ReceiveItem.RespecCL"), eChatType.CT_System, eChatLoc.CL_PopupWindow);
							return true;
						}
				}
			}

			return base.ReceiveItem(source, item);
		}
	}
}
