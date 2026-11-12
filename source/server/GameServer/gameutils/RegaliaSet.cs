using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.PacketHandler;
using DOL.GS.Spells;

namespace DOL.GS
{
    /// <summary>
    /// A royal armor set (Storm Regalia, Regalia of Tara): seven items whose Id_nb share a prefix, bonuses held at 2, 4, 6
    /// and 7 pieces worn, and a trainer that gives a level 50 royal any piece they do not carry.
    /// </summary>
    public sealed class RegaliaSet
    {
        public const int GrantLevel = 50;

        public string Name { get; }
        public string Prefix { get; }
        public string GrantKeyword { get; }
        public string[] Pieces { get; }
        public IReadOnlyList<(int Pieces, string Text, MLMod[] Mods)> Tiers { get; }
        private readonly Func<int, bool> _isClass;
        private readonly string _givenSpeech;
        private readonly string _appliedKey;

        public RegaliaSet(string name, string prefix, string grantKeyword, Func<int, bool> isClass, string givenSpeech,
            IReadOnlyList<(int, string, MLMod[])> tiers)
        {
            Name = name;
            Prefix = prefix;
            GrantKeyword = grantKeyword;
            _isClass = isClass;
            _givenSpeech = givenSpeech;
            Tiers = tiers;
            Pieces = new[] { "helm", "hauberk", "sleeves", "gauntlets", "leggings", "boots", "cloak" }.Select(part => prefix + part).ToArray();
            _appliedKey = "Regalia.Applied." + prefix;
        }

        public bool IsPiece(DbInventoryItem item) => item?.Id_nb?.StartsWith(Prefix, StringComparison.Ordinal) == true;

        /// <summary>Distinct pieces worn, counting <paramref name="arriving"/> and not <paramref name="leaving"/> (the hooks run mid-move).</summary>
        public int Worn(GamePlayer player, DbInventoryItem arriving = null, DbInventoryItem leaving = null)
        {
            if (player?.Inventory == null)
                return 0;
            var worn = player.Inventory.EquippedItems.Where(item => IsPiece(item) && !ReferenceEquals(item, leaving)).Select(item => item.Id_nb).ToHashSet();
            if (IsPiece(arriving))
                worn.Add(arriving.Id_nb);
            return worn.Count;
        }

        public bool HasFullSet(GamePlayer player) => Applied(player) >= Pieces.Length;

        /// <summary>Re-reads the worn pieces and swaps the held tier bonuses. Called on every equip change, login included.</summary>
        public void Refresh(GamePlayer player, DbInventoryItem arriving = null, DbInventoryItem leaving = null)
        {
            int before = Applied(player);
            int worn = Worn(player, arriving, leaving);
            if (worn == before)
                return;
            foreach (var tier in Tiers)
            {
                bool had = before >= tier.Pieces, has = worn >= tier.Pieces;
                if (had == has)
                    continue;
                foreach (MLMod mod in tier.Mods)
                    mod.Apply(player, null, has);
                if (player.Client?.ClientState == GameClient.eClientState.Playing)
                    player.Out.SendMessage($"{Name} ({tier.Pieces} pieces): {tier.Text} {(has ? "is yours" : "fades")}.", eChatType.CT_Spell, eChatLoc.CL_SystemWindow);
            }
            player.TempProperties.SetProperty(_appliedKey, worn);
            MLMod.Refresh(player);
        }

        private int Applied(GamePlayer player) => player?.TempProperties.GetProperty<int>(_appliedKey) ?? 0;

        /// <summary>Gives a level 50 royal of this set's class every piece they do not carry (bags or worn).</summary>
        public void Grant(GamePlayer player, GameNPC giver)
        {
            if (!_isClass(player.CharacterClass.ID))
                return;
            if (player.Level < GrantLevel)
            {
                player.Out.SendMessage($"{giver.GetName(0, true)} says, \"The {Name} waits for a sovereign of level {GrantLevel}.\"", eChatType.CT_Say, eChatLoc.CL_ChatWindow);
                return;
            }
            var owned = new HashSet<string>(player.Inventory.AllItems.Where(IsPiece).Select(item => item.Id_nb));
            int given = 0;
            foreach (string id in Pieces.Where(id => !owned.Contains(id)))
                if (player.ReceiveItem(giver, id))
                    given++;
            player.Out.SendMessage(given > 0
                ? $"{giver.GetName(0, true)} says, \"{_givenSpeech}\""
                : $"{giver.GetName(0, true)} says, \"You already carry the whole {Name}.\"", eChatType.CT_Say, eChatLoc.CL_ChatWindow);
        }
    }
}
