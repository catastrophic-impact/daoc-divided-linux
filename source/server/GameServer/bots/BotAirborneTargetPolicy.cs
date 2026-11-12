using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// DR-246 (Discord 2026-11-10): companions in Darkness Falls walked up into the air to melee the molochian tempters
    /// hovering over the doors, then ran back and forth up there. A path to a hovering target ends on the floor under it,
    /// and the line-of-sight finish then walked straight on to the target's own height. A walking bot stops on the floor
    /// instead, and a melee companion lets a flyer it can never reach go.
    /// </summary>
    public static class BotAirborneTargetPolicy
    {
        /// <summary>
        /// A walk end this far above the floor under it is out of reach: the autonomous bots' unreachable-flyer height, so
        /// a low flyer is still fought as before.
        /// </summary>
        public const float AirborneHeight = AutonomousPveTargetPolicy.UnreachableFlyerHeight;

        /// <summary>
        /// The end of a walking bot's final straight walk: the destination itself, or the floor under it when the
        /// destination hangs in the air. With no floor known the destination is kept (old behavior).
        /// </summary>
        public static Vector3 GroundedWalkEnd(Vector3 destination, Vector3? floorBeneath) =>
            floorBeneath is Vector3 floor && destination.Z - floor.Z > AirborneHeight ? floor : destination;

        /// <summary>
        /// A melee companion drops a flying target it cannot reach from its floor (the autonomous bots' existing
        /// 400-unit rule). Autonomous bots keep their own flyer watch; a companion with a usable bow shoots instead.
        /// </summary>
        public static bool CompanionDropsFlyer(bool companion, bool autonomous, bool usesRanged, GameNPC.eFlags flags, int targetZ, int botZ) =>
            companion && !autonomous && !usesRanged && AutonomousPveTargetPolicy.IsUnreachableFlyer(flags, targetZ, botZ);
    }
}
