using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// DR-209: every class trainer gives a level 50 player of its class the live epic armour and the Cloak of the
    /// Loyal &lt;class&gt; (World Workshop make_class_epic_sets_edits.py, package "DR-209 live epic sets"). Only pieces
    /// the player does not already carry (bags, worn or vault-free inventory) are given.
    /// </summary>
    public static class ClassEpicGear
    {
        public const int GrantLevel = 50;
        public const string GrantKeyword = "Epic Armor";
        public const string Package = "DR-209 live epic sets";

        private static readonly ConcurrentDictionary<int, string[]> _byClass = new();

        /// <summary>The Id_nb of every piece for this class, read once per class from the item table.</summary>
        public static string[] Pieces(int classId) => _byClass.GetOrAdd(classId, id =>
            GameServer.Database.SelectObjects<DbItemTemplate>(DB.Column("PackageID").IsEqualTo(Package)
                    .And(DB.Column("AllowedClasses").IsEqualTo(id.ToString())))
                .OrderBy(t => t.Item_Type).Select(t => t.Id_nb).ToArray());

        public static bool Offers(GamePlayer player) =>
            player != null && player.Level >= GrantLevel && Pieces(player.CharacterClass.ID).Length > 0;

        public static void Offer(GamePlayer player, GameNPC trainer)
        {
            if (Offers(player))
                player.Out.SendMessage($"{trainer.GetName(0, true)} says, \"You have served the realm well. Ask me for your [{GrantKeyword}] " +
                    "and I will give you your class's armor and cloak.\"", eChatType.CT_Say, eChatLoc.CL_PopupWindow);
        }

        /// <summary>Gives every piece the player does not carry. Returns how many were given.</summary>
        public static int Grant(GamePlayer player, GameNPC trainer)
        {
            if (player.Level < GrantLevel)
            {
                player.Out.SendMessage($"{trainer.GetName(0, true)} says, \"Come back when you reach level {GrantLevel}.\"", eChatType.CT_Say, eChatLoc.CL_ChatWindow);
                return 0;
            }

            string[] pieces = Pieces(player.CharacterClass.ID);
            if (pieces.Length == 0)
                return 0;

            var owned = new HashSet<string>(player.Inventory.AllItems.Select(item => item.Id_nb), StringComparer.OrdinalIgnoreCase);
            int given = 0;
            foreach (string id in pieces.Where(id => !owned.Contains(id)))
            {
                if (player.ReceiveItem(trainer, id))
                    given++;
            }

            player.Out.SendMessage(given > 0
                ? $"{trainer.GetName(0, true)} says, \"Wear it with pride. The realm is in your debt.\""
                : $"{trainer.GetName(0, true)} says, \"You already carry all of your {GrantKeyword.ToLower()}.\"", eChatType.CT_Say, eChatLoc.CL_ChatWindow);
            return given;
        }
    }
}
