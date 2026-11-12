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
using System.Collections.Generic;
using DOL.Database;

namespace DOL.GS
{
	/// <summary>
	/// List containing all possible candidates for loot drop
	/// </summary>
	public class LootList
	{
		/// <summary>
		/// number of random items to drop
		/// </summary>
		public int DropCount { get; set; }

		/// <summary>
		/// m_dropCount items will be chosen from randomitemdrops list depending on their chance, to drop for mob loot
		/// </summary>
		private readonly List<LootEntry> m_randomItemDrops;

		/// <summary>
		/// Items in fixed drop list will ALWAYS drop for mob loot
		/// </summary>
		private readonly List<DbItemTemplate> m_fixedItemDrops;

		/// <summary>
		/// Who each fixed drop is meant for (same index as m_fixedItemDrops), or null for the kill's normal owners.
		/// </summary>
		private readonly List<GameLiving> m_fixedIntendedOwners;

		public LootList() : this(1) { }

		public LootList(int dropCount)
		{
			DropCount = dropCount;
			m_fixedItemDrops = new List<DbItemTemplate>(2);
			m_fixedIntendedOwners = new List<GameLiving>(2);
			m_randomItemDrops = new List<LootEntry>(15);
		}

		/// <summary>
		/// Adds dropCount pieces of this item to the list of fixed drops.
		/// </summary>
		/// <param name="loot"></param>
		public void AddFixed(DbItemTemplate loot, int dropCount)
		{
			AddFixed(loot, dropCount, null);
		}

		/// <summary>
		/// Adds dropCount pieces of this item to the fixed drops, offered first to intendedOwner when it drops.
		/// </summary>
		public void AddFixed(DbItemTemplate loot, int dropCount, GameLiving intendedOwner)
		{
			for (int drop = 0; drop < dropCount; drop++)
			{
				m_fixedItemDrops.Add(loot);
				m_fixedIntendedOwners.Add(intendedOwner);
			}
		}

		public void AddRandom(int chance, DbItemTemplate loot, int count = 1)
		{
			LootEntry entry = new LootEntry(chance, loot, count);
			m_randomItemDrops.Add(entry);
		}

		/// <summary>
		/// Merges two list into one big list, containing items of both and having the bigger dropCount
		/// </summary>
		/// <param name="list"></param>
		public void AddAll(LootList list)
		{
			if (list.m_randomItemDrops != null)
			{
				m_randomItemDrops.AddRange(list.m_randomItemDrops);
			}

			if (list.m_fixedItemDrops != null)
			{
				m_fixedItemDrops.AddRange(list.m_fixedItemDrops);
				m_fixedIntendedOwners.AddRange(list.m_fixedIntendedOwners);
			}

			if (list.DropCount > DropCount)
				DropCount = list.DropCount;
		}

		/// <summary>
		/// Returns a list of ItemTemplates chosen from Random and Fixed loot.
		/// </summary>
		/// <returns></returns>
		public DbItemTemplate[] GetLoot()
		{
			LootDrop[] drops = GetDrops();
			DbItemTemplate[] items = new DbItemTemplate[drops.Length];
			for (int i = 0; i < drops.Length; i++)
				items[i] = drops[i].Item;
			return items;
		}

		/// <summary>
		/// Rolls the loot once: every fixed drop with its intended owner, then DropCount random winners (no owner).
		/// </summary>
		public LootDrop[] GetDrops()
		{
			List<LootDrop> loot = new List<LootDrop>(m_fixedItemDrops.Count + DropCount);
			for (int i = 0; i < m_fixedItemDrops.Count; i++)
				loot.Add(new LootDrop(m_fixedItemDrops[i], m_fixedIntendedOwners[i]));

			if (DropCount > 0)
			{
				// Big logic change. The intended flow of the original code was to iterate over the drop list
				// DropCount times, each iteration doing the following:
				// 1.) clear lootCandidates array
				// 2.) for each loot item in m_randomItemDrops roll D100 against frequency
				// 3.) each winner gts into lootCandidates array
				// 4.) pick one random winner out of lootCandidates, add to the returned loot list
				// The bug reported was caused by the loot.Add being outside the main loop. only
				// the last winner made it into the loot array that is returned.
				// There is a secondary defect that surfaces once the loop issue is fixed. There is
				// a high chance of duplicate items dropping. This is counter to live game server
				// behavior.
				//
				// New logic iterates once accross m_randomItemDrops, rolling D100 against frequency.
				// Winner items are placed into lootCandidates. Once that loop is complete, we enter
				// into a second loop. In each iteration, we select one random winner from lootCandidates,
				// add it the to returned loot array, then remove it from lootCandidates. This avoids
				// code-induced duplicates. I imagine that ArrayList.RemoveAt() is a little expensive on cpu
				// cycles as it has to collapse the array each time an item is removed. A second option is
				// to build a matching array of booleans, set all to false, and each time a winner is copied
				// to loot, set the value in the matching array at the same index value to true. This introduces
				// the need for a nested loop to avoid true values in the matching array, and if not coded properly,
				// will hang the server as it loops an infinite number of times. The third option is to ignore
				// all of this, say that dupes are o.k., pull loot.Add inside the main loop of the old code
				// and move on.

				// new logic
				var lootCandidates = new List<LootEntry>();
				foreach (LootEntry lootEntry in m_randomItemDrops)
				{
					if (lootEntry.Chance >= Util.Random(1, 100))
						lootCandidates.Add(lootEntry);
				}
				// At this point, the candidate list is filled with items that passed
				// the %chance to drop, so we need to put DropCount of them into the return loot object.
				//
				// Initial control thought is to use the ArrayList.RemoveAt method and change the loop control
				// to include a check on lootCandidates.Count. This assumes that the removeAt function re-builds
				// the array and updates ArrayList.Count. If not, we'll be throwing exceptions like mad.

				// copy random winners to returned loot list

				if (lootCandidates.Count > 0)
				{
					for (int i = 0; (i < DropCount && lootCandidates.Count != 0); i++)
					{
						int tmpidx = Util.Random(lootCandidates.Count - 1);
						loot.Add(new LootDrop(lootCandidates[tmpidx].ItemTemplate, null));
						if (--(lootCandidates[tmpidx].Count) <= 0)
							lootCandidates.SwapRemoveAt(tmpidx);
					}

				}
			}

			return loot.ToArray();
		}
	}

	/// <summary>
	/// One item that dropped, and who it is meant for: null leaves it to the kill's normal owners.
	/// </summary>
	public readonly record struct LootDrop(DbItemTemplate Item, GameLiving IntendedOwner);

	/// <summary>
	/// Container class for entries in the randomdroplist
	/// </summary>
	internal class LootEntry
	{
		public readonly int Chance;
		public readonly DbItemTemplate ItemTemplate;
		public int Count;

		public LootEntry(int chance, DbItemTemplate item, int count)
		{
			Chance = chance;
			ItemTemplate = item;
			Count = count;
		}
	}
}