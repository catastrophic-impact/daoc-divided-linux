using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// A Catacombs obelisk (DR-233). On live the obelisks are the Catacombs travel network. The data we hold has one
    /// obelisk per zone and no travel rows, so this is a simple version: click an obelisk and it lists the other
    /// obelisks of your realm; say or click a zone name to go there. No discovery step.
    /// The realm comes from the obelisk's model: each realm has its own stone.
    /// </summary>
    public class CatacombsObelisk : GameNPC
    {
        public const ushort AlbionModel = 1878, MidgardModel = 1823, HiberniaModel = 1863;
        private const int ArrivalOffset = 64;

        private static readonly ConcurrentDictionary<ushort, CatacombsObelisk> _byRegion = new();

        /// <summary>Which realm an obelisk serves; None for a stone of no realm (everyone may use it).</summary>
        public static eRealm RealmOfModel(ushort model)
        {
            return (model & 0x7FFF) switch
            {
                AlbionModel => eRealm.Albion,
                MidgardModel => eRealm.Midgard,
                HiberniaModel => eRealm.Hibernia,
                _ => eRealm.None
            };
        }

        /// <summary>
        /// Where a traveller of a realm can go from one obelisk: every other obelisk of that realm (or of no realm),
        /// by zone name. A zone with two names in the list keeps the first.
        /// </summary>
        public static List<(ushort Region, string Zone)> Destinations(eRealm traveller, ushort fromRegion,
            IEnumerable<(ushort Region, string Zone, eRealm Realm)> obelisks)
        {
            return obelisks
                .Where(o => o.Region != fromRegion && !string.IsNullOrWhiteSpace(o.Zone) && (o.Realm == eRealm.None || o.Realm == traveller))
                .GroupBy(o => o.Zone, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(o => o.Region).First())
                .OrderBy(o => o.Zone, StringComparer.OrdinalIgnoreCase)
                .Select(o => (o.Region, o.Zone))
                .ToList();
        }

        public eRealm ObeliskRealm => RealmOfModel(Model);

        private string ZoneName => CurrentZone?.Description;

        public override bool AddToWorld()
        {
            if (!base.AddToWorld())
                return false;

            _byRegion[CurrentRegionID] = this;
            return true;
        }

        public override bool RemoveFromWorld()
        {
            _byRegion.TryRemove(new KeyValuePair<ushort, CatacombsObelisk>(CurrentRegionID, this));
            return base.RemoveFromWorld();
        }

        private List<(ushort Region, string Zone)> DestinationsFor(GamePlayer player)
        {
            return Destinations(player.Realm, CurrentRegionID,
                _byRegion.Values.Select(o => (o.CurrentRegionID, o.ZoneName, o.ObeliskRealm)));
        }

        private bool MayUse(GamePlayer player)
        {
            eRealm realm = ObeliskRealm;
            return realm == eRealm.None || realm == player.Realm || player.Client.Account.PrivLevel > 1;
        }

        public override bool Interact(GamePlayer player)
        {
            if (!base.Interact(player) || player == null)
                return false;

            if (!MayUse(player))
            {
                player.Out.SendMessage("The obelisk's runes stay dark for you.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return true;
            }

            List<(ushort Region, string Zone)> destinations = DestinationsFor(player);

            if (destinations.Count == 0)
            {
                player.Out.SendMessage("The obelisk's runes glow, but they show no other place.", eChatType.CT_System, eChatLoc.CL_PopupWindow);
                return true;
            }

            player.Out.SendMessage("The obelisk's runes glow. They can carry you to: " +
                string.Join(", ", destinations.Select(d => $"[{d.Zone}]")) + ".", eChatType.CT_System, eChatLoc.CL_PopupWindow);
            return true;
        }

        public override bool WhisperReceive(GameLiving source, string text)
        {
            if (!base.WhisperReceive(source, text) || source is not GamePlayer player || string.IsNullOrWhiteSpace(text))
                return false;

            if (!MayUse(player))
                return false;

            (ushort Region, string Zone) destination = DestinationsFor(player)
                .FirstOrDefault(d => d.Zone.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase));

            if (destination.Zone == null || !_byRegion.TryGetValue(destination.Region, out CatacombsObelisk target))
                return false;

            if (player.InCombat)
            {
                player.Out.SendMessage("You cannot use the obelisk while in combat.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return true;
            }

            player.Out.SendMessage($"The runes flare and carry you to {destination.Zone}.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
            player.MoveTo(target.CurrentRegionID, target.X + ArrivalOffset, target.Y + ArrivalOffset, target.Z, target.Heading);
            return true;
        }
    }
}
