using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// The Darkness Rising champion weapons (Herald 1.126 ids 57001-57599). Every weapon has three versions in the
    /// world: the base row (Id_nb as imported) and two Weaponmaster versions with a second spell (Id_nb + "_v2",
    /// "_v3"). All carry PackageID <see cref="Package"/>, the class ids in AllowedClasses and Level 50. No Champion
    /// Level requirement (owner rule).
    /// </summary>
    public static class ChampionWeapons
    {
        public const string Package = "DR_ChampionWeapons";
        private static readonly string[] VersionSuffixes = { "_v2", "_v3" };
        private static List<DbItemTemplate> _all;
        private static readonly object _lock = new();

        public static string BaseId(string idNb)
        {
            if (string.IsNullOrEmpty(idNb))
                return idNb;
            foreach (string suffix in VersionSuffixes)
            {
                if (idNb.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return idNb.Substring(0, idNb.Length - suffix.Length);
            }
            return idNb;
        }

        public static bool IsChampionWeapon(DbItemTemplate template) =>
            template != null && string.Equals(template.PackageID, Package, StringComparison.OrdinalIgnoreCase);

        public static bool AllowsClass(DbItemTemplate template, int classId)
        {
            if (template == null || string.IsNullOrWhiteSpace(template.AllowedClasses))
                return false;
            foreach (string part in template.AllowedClasses.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part.Trim(), out int id) && id == classId)
                    return true;
            }
            return false;
        }

        public static bool IsFor(DbItemTemplate template, int classId, eRealm realm) =>
            IsChampionWeapon(template) && template.Realm == (int)realm && AllowsClass(template, classId);

        /// <summary>The class's weapons grouped by base weapon, base version first, in a stable order.</summary>
        public static List<List<DbItemTemplate>> ForClass(IEnumerable<DbItemTemplate> all, int classId, eRealm realm) =>
            all.Where(t => IsFor(t, classId, realm))
                .GroupBy(t => BaseId(t.Id_nb), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(t => t.Id_nb.Length).ThenBy(t => t.Id_nb, StringComparer.OrdinalIgnoreCase).ToList())
                .OrderBy(g => g[0].Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>What sets a version apart: the base is "standard", the others are named after their extra spell.</summary>
        public static string VersionLabel(DbItemTemplate template, Func<int, string> spellName)
        {
            if (string.Equals(BaseId(template.Id_nb), template.Id_nb, StringComparison.OrdinalIgnoreCase))
                return "standard";
            int extra = template.SpellID1 > 0 ? template.SpellID1 : template.ProcSpellID1 > 0 ? template.ProcSpellID1 : template.ProcSpellID;
            string name = extra > 0 ? spellName?.Invoke(extra) : null;
            if (string.IsNullOrWhiteSpace(name))
                name = template.Id_nb.EndsWith("_v2", StringComparison.OrdinalIgnoreCase) ? "second" : "third";
            return name.Trim();
        }

        public static IReadOnlyList<DbItemTemplate> All
        {
            get
            {
                lock (_lock)
                {
                    _all ??= GameServer.Database.SelectObjects<DbItemTemplate>(DB.Column("PackageID").IsEqualTo(Package)).ToList();
                    return _all;
                }
            }
        }

        /// <summary>Champion weapons the player carries or wears (any realm's).</summary>
        public static List<DbInventoryItem> Held(GamePlayer player)
        {
            var items = new List<DbInventoryItem>();
            items.AddRange(player.Inventory.GetItemRange(eInventorySlot.MinEquipable, eInventorySlot.MaxEquipable));
            items.AddRange(player.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack));
            return items.Where(i => i != null && IsChampionWeapon(i.Template)).ToList();
        }
    }

    /// <summary>
    /// A class guild Weaponmaster in the throne room. At level 50 he gives one champion weapon of the player's class
    /// (any version) and swaps it for another when the player hands it back. One champion weapon at a time.
    /// Keeps the champion respec of <see cref="CLWeaponNPC"/>.
    /// </summary>
    public class ChampionWeaponmaster : CLWeaponNPC
    {
        private const string PendingKey = "ChampionWeaponmaster.Pending";

        public override bool Interact(GamePlayer player)
        {
            if (!base.Interact(player))
                return false;
            TurnTo(player, 10000);

            if (player.Realm != Realm)
            {
                SayTo(player, "I arm the champions of my own realm only.");
                return true;
            }
            if (player.Level < GamePlayer.MAX_LEVEL)
            {
                SayTo(player, "Come back when you have reached your fiftieth season, and I will arm you as a champion.");
                return true;
            }

            var held = ChampionWeapons.Held(player);
            if (held.Count > 0)
            {
                SayTo(player, $"You already carry {held[0].Name}. Hand it to me if you would like a different champion weapon.");
                return true;
            }

            OfferWeapons(player);
            return true;
        }

        private void OfferWeapons(GamePlayer player)
        {
            var groups = ChampionWeapons.ForClass(ChampionWeapons.All, player.CharacterClass.ID, player.Realm);
            if (groups.Count == 0)
            {
                SayTo(player, "I have no weapon forged for your calling yet.");
                return;
            }
            player.TempProperties.RemoveProperty(PendingKey);
            string list = string.Join(", ", groups.Select(g => $"[{g[0].Name}]"));
            SayTo(player, $"Choose the weapon you will carry as a champion: {list}.");
        }

        public override bool WhisperReceive(GameLiving source, string text)
        {
            if (!base.WhisperReceive(source, text))
                return false;
            if (source is not GamePlayer player || player.Realm != Realm || player.Level < GamePlayer.MAX_LEVEL)
                return true;
            if (ChampionWeapons.Held(player).Count > 0)
                return true;

            var groups = ChampionWeapons.ForClass(ChampionWeapons.All, player.CharacterClass.ID, player.Realm);
            var chosen = groups.FirstOrDefault(g => string.Equals(g[0].Name, text, StringComparison.OrdinalIgnoreCase));
            if (chosen != null)
            {
                if (chosen.Count == 1)
                {
                    Give(player, chosen[0]);
                    return true;
                }
                player.TempProperties.SetProperty(PendingKey, ChampionWeapons.BaseId(chosen[0].Id_nb));
                string versions = string.Join(", ", chosen.Select(t => $"[{ChampionWeapons.VersionLabel(t, SpellName)}]"));
                SayTo(player, $"{chosen[0].Name} can be forged in these ways: {versions}.");
                return true;
            }

            string pending = player.TempProperties.GetProperty<string>(PendingKey);
            if (pending != null)
            {
                var group = groups.FirstOrDefault(g => string.Equals(ChampionWeapons.BaseId(g[0].Id_nb), pending, StringComparison.OrdinalIgnoreCase));
                var version = group?.FirstOrDefault(t => string.Equals(ChampionWeapons.VersionLabel(t, SpellName), text, StringComparison.OrdinalIgnoreCase));
                if (version != null)
                    Give(player, version);
            }
            return true;
        }

        private static string SpellName(int spellId) => SkillBase.GetSpellByID(spellId)?.Name;

        private void Give(GamePlayer player, DbItemTemplate template)
        {
            lock (player.Inventory.Lock)
            {
                if (ChampionWeapons.Held(player).Count > 0)
                    return;
                eInventorySlot slot = player.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                if (slot == eInventorySlot.Invalid || !player.Inventory.AddItem(slot, GameInventoryItem.Create(template)))
                {
                    SayTo(player, "You have no room to carry it. Make some space and ask me again.");
                    return;
                }
            }
            player.TempProperties.RemoveProperty(PendingKey);
            InventoryLogging.LogInventoryAction(this, player, eInventoryActionType.Other, template);
            SayTo(player, $"Carry {template.Name} with honour, champion.");
        }

        public override bool ReceiveItem(GameLiving source, DbInventoryItem item)
        {
            if (source is GamePlayer player && item != null && ChampionWeapons.IsChampionWeapon(item.Template))
            {
                if (player.Realm != Realm)
                    return false;
                if (player.Inventory.EquippedItems.Contains(item))
                {
                    SayTo(player, $"Take off your {item.Name} first.");
                    return false;
                }
                lock (player.Inventory.Lock)
                {
                    if (!player.Inventory.RemoveItem(item))
                        return false;
                }
                InventoryLogging.LogInventoryAction(player, this, eInventoryActionType.Other, item.Template);
                SayTo(player, $"I will keep {item.Name} safe.");
                OfferWeapons(player);
                return true;
            }
            return base.ReceiveItem(source, item);
        }
    }
}
