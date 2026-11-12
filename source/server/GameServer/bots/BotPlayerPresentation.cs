using System.Collections.Generic;
using DOL.GS.PacketHandler.Client.v168;
using DOL.GS.ServerProperties;

namespace DOL.GS;

/// <summary>
/// Bots drawn the way the client draws players (owner 2026-10-30): a GameBot is an NPC on the server, but a 1.130 client
/// gets the player create and position packets for it, so its name plate, face and movement are a player's. Only those
/// packets change: every server rule still sees the NPC. The client keeps players by session id as well as object id, so
/// each bot in the world holds one from a range real clients don't reach. Older clients and the new client (which reads no
/// other player's position packet) get the NPC packets, as does a bot in a region with no id left.
/// </summary>
public static class BotPlayerPresentation
{
    public const ushort FirstSession = 256;
    public const ushort LastSession = 1400;
    /// <summary>How often a moving bot's position goes out. A real client sends its own about this often while running.</summary>
    public const int MovingUpdateInterval = 300;
    /// <summary>
    /// How often a standing bot's position goes out. A real client keeps sending its own, so the game client lets go of a
    /// "player" that goes quiet: on the 5 second NPC interval a standing bot faded out and back (owner 2026-10-30).
    /// </summary>
    public const int StandingUpdateInterval = 1000;

    private sealed class RegionSessions
    {
        public readonly GameBot[] Bots = new GameBot[LastSession - FirstSession + 1];
        public int Next;
    }

    private static readonly Dictionary<ushort, RegionSessions> Regions = new();

    /// <summary>The session id this client knows the bot by, when the bot is shown to it as a player.</summary>
    public static bool TryGetSession(GameClient client, GameBot bot, out ushort session)
    {
        session = 0;
        if (client == null || bot == null || !Properties.BOT_SHOW_AS_PLAYERS || client.Version < GameClient.eClientVersion.Version1130)
            return false;
        session = bot.PresentedSession;
        if (session != 0)
            return true;
        if (bot.ObjectState is not GameObject.eObjectState.Active)
            return false;
        lock (Regions)
        {
            if (bot.PresentedSession != 0)
                session = bot.PresentedSession;
            else
                session = Allocate(bot);
        }
        return session != 0;
    }

    /// <summary>The bot holding a session id in a region, or null.</summary>
    public static GameBot FindBySession(ushort region, ushort session)
    {
        if (session < FirstSession || session > LastSession)
            return null;
        lock (Regions)
            return Regions.TryGetValue(region, out RegionSessions sessions) ? sessions.Bots[session - FirstSession] : null;
    }

    /// <summary>The bot left the world (for good or to move): its session id can go to another bot.</summary>
    public static void Release(GameBot bot)
    {
        lock (Regions)
        {
            if (bot.PresentedSession != 0 && Regions.TryGetValue(bot.PresentedRegion, out RegionSessions sessions) &&
                sessions.Bots[bot.PresentedSession - FirstSession] == bot)
                sessions.Bots[bot.PresentedSession - FirstSession] = null;
            bot.PresentedSession = 0;
        }
    }

    private static ushort Allocate(GameBot bot)
    {
        ushort region = bot.CurrentRegionID;
        if (!Regions.TryGetValue(region, out RegionSessions sessions))
            Regions[region] = sessions = new RegionSessions();
        for (int tried = 0; tried < sessions.Bots.Length; tried++)
        {
            int slot = sessions.Next;
            sessions.Next = (slot + 1) % sessions.Bots.Length;
            if (sessions.Bots[slot] != null)
                continue;
            sessions.Bots[slot] = bot;
            bot.PresentedRegion = region;
            bot.PresentedSession = (ushort)(FirstSession + slot);
            return bot.PresentedSession;
        }
        return 0;
    }

    /// <summary>
    /// The gap between a bot's position packets. A bot that just stopped gets one more at the moving rate: the client runs
    /// a player on until told otherwise. Pure.
    /// </summary>
    public static int UpdateInterval(bool moving, bool wasMoving) => moving || wasMoving ? MovingUpdateInterval : StandingUpdateInterval;

    /// <summary>The player state a bot's position packet carries. Dead comes first: a dead bot on a route is not riding. Pure.</summary>
    public static byte StateFlags(bool dead, bool riding, bool flying, bool underwater)
    {
        PlayerPositionUpdateHandler.StateFlags flags = default;
        if (dead)
            flags = PlayerPositionUpdateHandler.StateFlags.DEAD;
        else if (riding)
            flags = PlayerPositionUpdateHandler.StateFlags.RIDING;
        else if (flying)
            flags = PlayerPositionUpdateHandler.StateFlags.FLYING;
        else if (underwater)
            flags = PlayerPositionUpdateHandler.StateFlags.SWIMMING;
        return (byte)flags;
    }

    /// <summary>How far below the bot a water polygon may lie and still be the surface it floats on.</summary>
    private const float WaterSurfaceDepth = 32f;
    private const float WaterSurfaceAbove = 8f;
    private static readonly EDtPolyFlags[] SwimOnly = [EDtPolyFlags.Swim, EDtPolyFlags.Disabled];

    /// <summary>
    /// Whether a bot shown as a player swims. The zone water level alone misses the surface: it is 0 in the
    /// battlegrounds and sits below the sea in Atlantis (7970 against a surface near 8010), so a bot on top of the
    /// water was drawn standing in the air and the client dropped it and snapped it back, over and over (Discord
    /// 2026-11-04). The navmesh marks water from the client's own river data, so a bot on a swim polygon swims.
    /// Worked out again only when the bot moves.
    /// </summary>
    public static bool Swimming(GameBot bot)
    {
        if (bot.IsUnderwater)
            return true;
        Zone zone = bot.CurrentZone;
        if (zone == null)
            return false;
        var at = (zone.ID, bot.X, bot.Y, bot.Z);
        if (bot.SwimCheckedAt == at)
            return bot.SwimmingForClients;
        bool swimming = OnWaterSurface(PathfindingProvider.Instance, zone, new System.Numerics.Vector3(bot.X, bot.Y, bot.Z));
        bot.SwimmingForClients = swimming;
        bot.SwimCheckedAt = at;
        return swimming;
    }

    /// <summary>
    /// Whether the navmesh has a water polygon straight under this point, close enough to be the surface it floats on.
    /// A little above counts too: a bot's height is whole units and the surface polygon is not.
    /// </summary>
    public static bool OnWaterSurface(IPathfindingMgr nav, Zone zone, System.Numerics.Vector3 position) =>
        nav.IsAvailable && nav.HasNavmesh(zone) &&
        nav.GetClosestPoint(zone, position with { Z = position.Z - (WaterSurfaceDepth - WaterSurfaceAbove) / 2 }, 2, 2,
            (WaterSurfaceDepth + WaterSurfaceAbove) / 2, SwimOnly).HasValue;

    /// <summary>
    /// The seven face bytes of the player create packet. A bot has no saved face, so its name picks one, always the same.
    /// Only the first four of each choice: every race has at least those at character creation. Pure.
    /// </summary>
    public static byte[] Face(string name)
    {
        uint hash = 2166136261;
        foreach (char letter in name ?? string.Empty)
            hash = (hash ^ letter) * 16777619;
        // Eye size, lip size, mood, eye colour, hair colour, face (high nibble), hair style (high nibble).
        return [0, 0, 0, (byte)(hash & 3), (byte)((hash >> 2) & 3), (byte)(((hash >> 4) & 3) << 4), (byte)(((hash >> 6) & 3) << 4)];
    }
}
