using System.Collections.Generic;
using System.Linq;
using DaocServer.Admin;

namespace DOL.GS.Admin
{
    /// <summary>Admin operations for battleground bracket bots (daoc-divided-linux addition; see GameServer/bots/bracket).</summary>
    public static class BracketAdmin
    {
        public static List<BracketBgInfo> Status() =>
            BracketBotService.Status().Select(s => new BracketBgInfo(
                s.Name, s.Region, s.PlayersInside,
                BracketBotPlanner.Realms.Select(r => s.Roster[r]).ToList(),
                BracketBotPlanner.Realms.Select(r => s.Online[r]).ToList())).ToList();
    }
}
