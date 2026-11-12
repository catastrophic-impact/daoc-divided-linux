using System;
using System.Linq;
using System.Numerics;
using DOL.Database;
using DOL.GS.Scripts;

namespace DOL.GS;

public sealed partial class AutonomousWorldBotController
{
    private OFTeleporter _frontierPorter;
    private GameMerchant _medallionMerchant;
    private long _nextPorterSearch;
    private Vector3? _frontierWaitingPoint;
    private long _nextWaitingPointSearch;
    private long _medallionNoRoomUntil;
    private long _frontierPorterWaitSince;

    /// <summary>
    /// The first leg of a porter-only journey from outside the frontier regions: on foot (or by the usual rides) through
    /// real zone connections to the realm's own frontier, where the porter and its medallion merchant stand. The overnight
    /// run of 2026-10-31 logged 714 of 1,669 fifteen-minute rescues as "Waiting for a frontier teleporter"; all but one
    /// stood in a capital, Atlantis or the Shrouded Isles, assigned to a siege in another frontier. Porters only serve
    /// bots already in a frontier region, and no crossing leads to another realm's frontier, so nothing ever moved them.
    /// </summary>
    private bool TravelToHomeFrontierForPorter(GameBot bot, CampDestination destination)
    {
        if (!AutonomousFrontierTransport.NeedsHomeFrontierFirst(bot.Realm, bot.CurrentRegionID, destination.RegionId) ||
            !AutonomousRvrStaging.TryGetBorderKeep(bot.Realm, out var keep))
            return false;
        int x = (int)keep.Position.X, y = (int)keep.Position.Y;
        if (FindNextCrossing(bot, keep.RegionId, x, y) == null) return false;
        CampDestination previous = _camp;
        _camp = new($"rvr-porter-{keep.RegionId}", keep.Name, keep.Name, keep.RegionId, x, y, (int)keep.Position.Z, 0, false, false);
        TravelAcrossRegions(bot);
        bool abandoned = _camp == null;
        _camp = previous;
        if (abandoned) return false;
        _frontierPorterWaitSince = 0;
        return true;
    }

    /// <summary>
    /// Holds a bot whose porter is not available yet, for AutonomousFrontierTransport.PorterWaitMilliseconds. Then it gives
    /// the RvR goal up through the same release an ended siege uses: a solo bot or its whole warband takes a new goal.
    /// </summary>
    private bool WaitForFrontierTeleporter(GameBot bot, CampDestination destination)
    {
        long now = GameLoop.GameLoopTime;
        if (_frontierPorterWaitSince == 0) _frontierPorterWaitSince = now;
        _rvrFrontierCommittedUntil = 0;
        _nextRvrPlanReview = Math.Min(_nextRvrPlanReview, now + 20_000);
        bot.StopMovingOnPath();
        if (AutonomousFrontierTransport.PorterWaitExpired(_frontierPorterWaitSince, now))
        {
            _frontierPorterWaitSince = 0;
            string force = bot.TempProperties.GetProperty<string>("RvrEventForce") ?? $"rvr-{bot.DatabaseID}";
            bot.TempProperties.RemoveProperty(AutonomousFrontierTransport.RequestKey);
            Log.Info($"RVR_FRONTIER_WAIT_GIVEUP bot={bot.Name} id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} " +
                     $"force={force} from={bot.CurrentRegionID} destination=\"{destination.ZoneName}\" region={destination.RegionId}");
            AutonomousRvrEventLayer.ReleaseForce(force, $"No frontier teleporter to {destination.ZoneName}; choosing other work");
            SetRvrStatus(bot, "Giving up on the frontier teleporter", destination.MonsterName,
                $"No way to {destination.ZoneName} after {AutonomousFrontierTransport.PorterWaitMilliseconds / 1000} seconds");
            return true;
        }
        SetRvrStatus(bot, "Waiting for a frontier teleporter", destination.MonsterName,
            $"{destination.ZoneName} is reached by porter, not on foot through the frontier dungeons");
        return true;
    }

    private bool TryReturnFromForeignFrontier(GameBot bot)
    {
        // PvE/services cannot walk through another realm's border gates to get
        // home. Use the same real porter/ticket boarding used by outbound RvR.
        ushort home = bot.Realm switch { eRealm.Albion => 1, eRealm.Midgard => 100, eRealm.Hibernia => 200, _ => 0 };
        if (home == 0 || bot.CurrentRegionID == home || bot.CurrentRegionID is not (1 or 100 or 200) ||
            AutonomousObjectiveAssignments.Parse(bot.PersistentRecord?.ObjectiveKind) == eAutonomousObjectiveKind.RvR ||
            GameRelic.IsPlayerCarryingRelic(bot)) return false;
        var passage = AutonomousFrontierTransport.Destination(bot.Realm, home);
        return passage != null && TryFrontierTransport(bot, new("return-home", passage.Location.Name, "frontier",
            home, passage.Location.X, passage.Location.Y, passage.Location.Z, bot.Level, false, true));
    }

    /// <summary>
    /// The first leg to another realm's frontier for a bot outside the three frontier regions (its capital after a release):
    /// its own frontier's border keep, where the porter and its medallion merchant stand. Null when the bot is already in a
    /// frontier region, the destination is no porter frontier, or its own frontier can't be reached from here either.
    /// </summary>
    private static CampDestination HomeFrontierLeg(GameBot bot, CampDestination destination)
    {
        ushort home = AutonomousFrontierTransport.HomeFrontier(bot.Realm);
        if (!AutonomousFrontierTransport.NeedsHomeFrontierFirst(bot.CurrentRegionID, destination.RegionId, home)) return null;
        var passage = AutonomousFrontierTransport.Destination(bot.Realm, home);
        if (passage == null || FindNextCrossing(bot, home, passage.Location.X, passage.Location.Y) == null) return null;
        return new("rvr-home-frontier", passage.Location.Name, "frontier",
            home, passage.Location.X, passage.Location.Y, passage.Location.Z, bot.Level, false, true);
    }

    /// <summary>Beyond this distance from the medallion merchant a bot enters the frontier on foot through its own border keep.</summary>
    private const int FrontierMedallionReach = 20_000;

    private bool TryFrontierTransport(GameBot bot, CampDestination destination)
    {
        if (bot.CurrentRegionID == destination.RegionId || bot.CurrentRegionID is not (1 or 100 or 200) ||
            destination.RegionId is not (1 or 100 or 200) || GameRelic.IsPlayerCarryingRelic(bot)) return false;
        var passage = AutonomousFrontierTransport.Destination(bot.Realm,destination.RegionId);
        if (passage == null) return false;
        bool returningToPve = passage.Medallion == "home_necklace" &&
            AutonomousObjectiveAssignments.Parse(bot.PersistentRecord?.ObjectiveKind) != eAutonomousObjectiveKind.RvR;
        var party=AutonomousFrontierTransport.BoardingParty(bot, passage);
        if(party.Any(GameRelic.IsPlayerCarryingRelic))
        {
            foreach(var member in party) member.TempProperties.RemoveProperty(AutonomousFrontierTransport.RequestKey);
            return false;
        }
        // A full backpack must not cancel every member's approach before the
        // owner can reach the real merchant and sell ordinary vendor trash.
        if (_frontierPorter?.ObjectState != GameObject.eObjectState.Active || _frontierPorter.CurrentRegion != bot.CurrentRegion)
        {
            if (GameLoop.GameLoopTime < _nextPorterSearch) return false;
            _nextPorterSearch = GameLoop.GameLoopTime + 30_000;
            _frontierPorter = AutonomousFrontierTransport.NearestPorter(bot);
            _medallionMerchant = null;
            _frontierWaitingPoint = null;
            _nextWaitingPointSearch = 0;
        }
        if (_frontierPorter == null) return false;
        // Declare the transport intent before approaching its merchant. A
        // returning PvE bot may need its own portal-keep door to BUY the ticket.
        // Boarding still requires the real purchased medallion and all safety checks.
        bot.TempProperties.SetProperty(AutonomousFrontierTransport.RequestKey,new AutonomousFrontierTransport.Request(
            _frontierPorter,passage,bot.TempProperties.GetProperty<string>("RvrEventForce") ?? $"rvr-{bot.DatabaseID}"));
        if (AutonomousFrontierTransport.Ticket(bot,passage) == null)
        {
            // A full bag with nothing to sell: stop walking back to the merchant for a while (it looped between the
            // merchant and its crossing), and let the bot's other plans and the watchdog take over.
            if (GameLoop.GameLoopTime < _medallionNoRoomUntil) return false;
            if (_medallionMerchant?.ObjectState != GameObject.eObjectState.Active ||
                !_medallionMerchant.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>().Any(item=>item.Id_nb==passage.Medallion))
                _medallionMerchant = _frontierPorter.GetNPCsInRadius(3000).OfType<GameMerchant>()
                    .Where(npc => (npc.Realm==bot.Realm || npc.Realm==eRealm.None) && npc.TradeItems?.GetAllItems().Values.OfType<DbItemTemplate>().Any(item=>item.Id_nb==passage.Medallion)==true)
                    .OrderBy(_frontierPorter.GetDistanceTo).FirstOrDefault();
            if (_medallionMerchant == null) return false;
            // Each realm sells frontier medallions only at its first border keep (Druim Ligen, Castle Sauvage, Svasud
            // Faste). A bot staged at the other border keep walks into the frontier through that keep's gate, as players
            // do, instead of crossing the realm for a medallion (run 24: Hibernians at Druim Cain stood still for 15
            // minutes "collecting a frontier medallion" 110,000 units from Araisa).
            // Another realm's frontier has no walking route since the dungeon hub stopped being a hallway
            // (AutonomousDungeonPolicy.MayRouteThrough), so a distant warband rides or walks to its porter.
            if (bot.GetDistanceTo(_medallionMerchant) > FrontierMedallionReach &&
                FindNextCrossing(bot, destination.RegionId, destination.X, destination.Y) != null) return false;
            // A ride (town teleporter or horse route) when clearly faster than walking to the merchant.
            if (TryBeginFasterStableRoute(bot, new(_medallionMerchant.X, _medallionMerchant.Y, _medallionMerchant.Z), _medallionMerchant.Name))
                return true;
            if (!ApproachSupplyMerchant(bot, _medallionMerchant))
            {
                SetRvrStatus(bot,"Collecting frontier medallion",destination.MonsterName,$"Walking to {_medallionMerchant.Name} for {passage.Medallion}");
                return true;
            }
            var template = _medallionMerchant.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>().First(item=>item.Id_nb==passage.Medallion);
            var slot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack,eInventorySlot.LastBackpack);
            if (slot == eInventorySlot.Invalid)
            {
                // A full backpack after RvR must not force a walk through enemy
                // gates. Sell one ordinary disposable item at this real merchant;
                // never delete an item or grant a free ticket/teleport.
                var trash = AutonomousBotEconomy.FindVendorTrashCandidate(bot);
                bool sold = trash != null && AutonomousBotEconomy.TrySellToVendor(bot, _medallionMerchant, trash, out _);
                if (AutonomousFrontierTransport.KeepWaitingForMedallionRoom(true, sold))
                {
                    SetRvrStatus(bot, "Making room for frontier medallion", destination.MonsterName,
                        "At the real merchant; preserving equipped items and protected upgrades");
                    return true;
                }
                _medallionNoRoomUntil = GameLoop.GameLoopTime + AutonomousFrontierTransport.MedallionNoRoomBackoffMilliseconds;
            }
            if (slot==eInventorySlot.Invalid || template.Price<0 || template.Price>0 && !AutonomousBotEconomy.TrySpend(bot.DatabaseID,template.Price))
                return false; // Existing legal dungeon itinerary remains available.
            var ticket = GameInventoryItem.Create(template);
            if (ticket == null || !bot.Inventory.AddItem(slot,ticket))
            {
                if(template.Price>0) AutonomousBotEconomy.AddMoney(bot.DatabaseID,template.Price);
                return false;
            }
            AutonomousBotEconomy.MarkInventoryChanged(bot);
            AutonomousBotStatusPersistence.Queue(bot,true);
        }
        // Ordinary warbands retain their shared departure. Committed siege
        // responders board independently; defenders receive a bounded priority slice.
        if (!_frontierWaitingPoint.HasValue && GameLoop.GameLoopTime >= _nextWaitingPointSearch)
        {
            _nextWaitingPointSearch = GameLoop.GameLoopTime + 10_000;
            if (AutonomousFrontierTransport.TryWaitingPoint(bot, _frontierPorter, out var waiting))
                _frontierWaitingPoint = waiting;
        }
        if (!_frontierWaitingPoint.HasValue)
        {
            SetRvrStatus(bot,"Finding teleporter waiting space",destination.MonsterName,"Waiting for a reachable spot clear of the teleporter");
            return true;
        }
        if (Vector3.Distance(new(bot.X,bot.Y,bot.Z),_frontierWaitingPoint.Value) > 40)
            IssuePath(bot,_frontierWaitingPoint.Value);
        else { bot.StopMovingOnPath(); bot.StopMoving(); }
        AutonomousFrontierTransport.WakeBoardingPorter(bot,
            bot.TempProperties.GetProperty<AutonomousFrontierTransport.Request>(AutonomousFrontierTransport.RequestKey));
        SetRvrStatus(bot,"Boarding frontier teleporter",destination.MonsterName,
            AutonomousFrontierTransport.HasDefenderPriority(bot, passage)
                ? $"At {_frontierPorter.Name}; priority defense departure to {passage.Location.Name}"
                : AutonomousFrontierTransport.HasCommittedSiegePassage(bot, passage)
                    ? $"At {_frontierPorter.Name}; boarding independently for the siege at the next departure to {passage.Location.Name}"
                    : $"At {_frontierPorter.Name}; waiting for the warband and the native departure to {passage.Location.Name}");
        return true;
    }
}
