using System;
using System.Numerics;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    public enum RouteThreatAction { Ignore, Pull, Detour, Retarget }

    /// <summary>
    /// Route threat awareness for gamebots walking through open zones. Bots used to react to
    /// aggro only: walking to a distant camp they strolled into aggressive monsters on the way
    /// and casters died in melee. On 2026-10-03..05, 55-71% of solo attempts that had a death
    /// died before reaching their camp. Now a travelling bot (solo, or a group leader) looks a
    /// short way down its real route every few seconds and, for the first aggressive monster
    /// whose aggro range crosses it: pulls it on its own terms when it is an even fight (casters
    /// open from spell range instead of being jumped), goes around it when it is too strong or
    /// part of a pack, and as a last resort gives up the camp. Dungeons keep their own corridor
    /// logic (GuardDungeonTravel).
    /// </summary>
    public static class AutonomousRouteThreatPolicy
    {
        [ServerProperty("autonomous", "bot_route_threat_awareness",
            "Gamebots look ahead along their route in open zones and pull, avoid or route around aggressive monsters before walking into them.", true)]
        public static bool Enabled = true;

        /// <summary>
        /// Whether a walk is scanned for PvE monsters: every walk of a bot on a PvE objective, and of a bot on an RvR
        /// objective only its march to a keep (night 2026-11-04: levels 40-49 on frontier tours walked into purple camps).
        /// </summary>
        public static bool ScansFor(bool rvrObjective, bool rvrTravel) => !rvrObjective || rvrTravel;

        public const int ScanMilliseconds = 2_500;
        public const float LookAhead = 1_500f;
        public const int ScanRadius = 1_800;
        public const int MeleeMargin = 80;
        public const int RangedMargin = 220;
        public const int DetourClearance = 120;
        /// <summary>Monsters this close to the destination are the camp itself; camp work handles them.</summary>
        public const float CampSkipRadius = 700f;
        public const int MaxDetoursPerThreat = 2;
        public const long RetargetCooldownMilliseconds = 10 * 60_000;
        // From Nikse77/OfflineDAoC bbe074d3 (2026-10-29): four parties of level 20-36 were wiped in Midgard's island region 151
        // on their way to low camps by giant cicadas (45-48), vanvettig djur (44-46) and forest skrats (50): the policy pulled a
        // red pack of two with a party average far below it, and walked on ("Ignore") into purple monsters once the ten-minute
        // retarget cooldown was running. A party never pulls a monster this many levels above its average, and a monster it must
        // not pull and cannot go around makes it give up the camp even inside the cooldown (at most once a minute).
        public const int GroupMaximumLevelsAbove = 8;
        // Playtest 2026-10-31, 40 minutes on 3,000 bots: solo single oranges killed 7% within 150 seconds (12% in Atlantis),
        // not the 34-54% of the 2026-10-18 logs, and groups lost nobody in 95 yellow and orange pulls. A party of four bots
        // beat level 60 reds in 15-33 seconds. Atlantis is too thick with roaming monsters to walk around them all.
        /// <summary>From this level a lone bot takes a single orange rather than give up its camp.</summary>
        public const int HighLevelSoloMinimum = 45;
        public const int StrongPartyMinimumMembers = 4;
        public const int StrongPartyMinimumLevel = 45;
        public const int StrongPartyMaximumLevelsAbove = 12;
        public const int StrongPartyEasyRedPack = 2;
        // Overnight run 2026-10-31: a level 50's purple starts 11 levels up (ConLevels), so "under 12 above" left a single level,
        // 61, and none of the 11 purple decisions of level 45+ parties fell in it: they were 13 to 16 above. 170 of 171 such parties
        // were strong, so that was never the gate. A way around also came first, which took 9 of the 11. Yet a Hero and three bots
        // killed a level 63 pair in 46 seconds with no deaths, and a party of five beat a 64 and 60 pair, while a dense 62 and 65
        // camp wiped that same party. So a strong party pulls one or two purples up to 14 above, rather than walking around them,
        // and three or more stay off limits. The mob lessons have the last word (provenDeadly).
        public const int StrongPartyPurplePack = 2;
        public const int StrongPartyPurpleLevelsAbove = 15;
        /// <summary>How far around a purple a party counts its camp: the 1,800-unit scan around the leader stops short of a monster far down the route.</summary>
        public const int CampCountRadius = 1_100;
        /// <summary>A monster this hurt is nearly dead: running now only throws the fight away.</summary>
        public const int BreakOffFoeHealthPercent = 40;
        public const int BreakOffMinimumParty = 3;

        /// <summary>A party that can fight well above its level: four or more at level with a healer. Pure.</summary>
        public static bool IsStrongParty(int members, bool hasHealer, int averageLevel) =>
            members >= StrongPartyMinimumMembers && hasHealer && averageLevel >= StrongPartyMinimumLevel;

        /// <summary>
        /// A purple a strong party takes on: one or two, none far above it, and not one the party's lessons say keeps killing
        /// parties. <paramref name="hardestLevelsAbove"/> is the highest of the pack, not the one on the route. Pure.
        /// </summary>
        public static bool IsStrongPartyPurple(ConColor con, bool strongParty, int packSize, int hardestLevelsAbove, bool provenDeadly) =>
            strongParty && !provenDeadly && con == ConColor.PURPLE && packSize <= StrongPartyPurplePack &&
            hardestLevelsAbove < StrongPartyPurpleLevelsAbove;

        /// <summary>
        /// Half the party down, or the healer gone against an orange or worse, and the monster far from finished. A lost
        /// healer alone is no reason to run from an easy monster (playtest 2026-10-31: six of seven ran from one sentry). Pure.
        /// </summary>
        public static bool ShouldBreakOff(int members, int alive, bool hadHealer, bool healerAlive, int strongestFoeHealthPercent, ConColor hardestFoe) =>
            members >= BreakOffMinimumParty && strongestFoeHealthPercent >= BreakOffFoeHealthPercent && alive > 0 &&
            ((members - alive) * 2 >= members + 1 || hadHealer && !healerAlive && hardestFoe >= ConColor.ORANGE);
        public const long ForcedRetargetCooldownMilliseconds = 60_000;

        /// <summary>
        /// What to do about the first threat on the route. An even fight is pulled; a strong
        /// monster or a pack is avoided when a way around exists (at most twice per monster);
        /// otherwise a manageable one is pulled anyway, and only a hopeless one makes the bot
        /// give up the camp (at most once per 10 minutes, else it carries on as before).
        /// Solo bots (2026-10-18 play logs, 6,579 route pulls in a day): 34-54% of solo bots that
        /// pulled an orange on the way were dead within 150 seconds, and 21-27% after a yellow,
        /// against 7-18% for green and blue. So a solo bot goes around a yellow when it can, and
        /// gives up the camp rather than fight an orange (a single orange is still fought when it
        /// may not give up again yet). A <paramref name="cautious"/> solo bot (one that died in the
        /// last half hour) treats a yellow like an orange.
        /// A solo bot facing a purple or red (or a boss) it cannot go around gives up the camp even inside the
        /// ten-minute retarget cooldown when <paramref name="mayAbandonHopeless"/>: it used to walk on into it
        /// ("Ignore"), and on 2026-10-28 879 solo bots died to a purple or red before reaching their camp.
        /// </summary>
        /// <summary>A blocker of the camp's own species at yellow or lower is the camp's work, not a route threat.</summary>
        public static bool IsCampWork(string blockerName, ConColor con, string campMonster) =>
            !string.IsNullOrEmpty(campMonster) && con <= ConColor.YELLOW &&
            string.Equals(blockerName, campMonster, StringComparison.OrdinalIgnoreCase);

        public static RouteThreatAction Decide(ConColor con, bool grouped, int packSize, bool bossLike,
            bool detourAvailable, int detoursSoFar, bool mayRetarget, bool cautious = false, bool mayAbandonHopeless = false,
            int levelsAboveReference = 0, bool mayForceRetarget = false, bool strongParty = false, bool highLevelSolo = false,
            bool denseWorld = false, int hardestLevelsAbove = int.MinValue, bool provenDeadly = false)
        {
            // A grey monster is no threat (and in DAoC rarely aggroes); lower cons are fought in
            // bigger packs. Run 7 (2026-10-06): 14% of give-ups were greys 6+ levels below the
            // bot and many more were green or blue packs of three to five.
            if (con <= ConColor.GREY && !bossLike)
                return RouteThreatAction.Ignore;
            bool canDetour = detourAvailable && detoursSoFar < MaxDetoursPerThreat;
            if (!grouped && !bossLike)
            {
                ConColor felt = cautious && con == ConColor.YELLOW ? ConColor.ORANGE : con;
                // Atlantis: solo blue packs killed more often than single oranges (14% against 12%).
                // Playtest 2026-11-07: with 1, three blue statuettes ended 48% of Atlantis trips; 2 again, now that
                // Atlantis camps reach 500 like the rest of the world.
                int easySolo = felt <= ConColor.GREEN ? 3 : felt <= ConColor.BLUE ? 2 : 0;
                int fightableSolo = felt <= ConColor.GREEN ? 5 : felt <= ConColor.BLUE ? denseWorld ? 2 : 3 : felt <= ConColor.ORANGE ? 1 : 0;
                if (packSize <= easySolo)
                    return RouteThreatAction.Pull;
                if (canDetour)
                    return RouteThreatAction.Detour;
                // A yellow pair with no way around too: on 2026-10-31 solo give-ups took 37% of bot time for no kills, and 337 of
                // the 1,223 were yellow packs, while a single yellow killed only 7%. The mob lessons turn back a pair that kills.
                if (felt == ConColor.YELLOW && packSize <= 2)
                    return RouteThreatAction.Pull;
                if (felt == ConColor.ORANGE && highLevelSolo && packSize <= 1)
                    return RouteThreatAction.Pull;
                if (felt == ConColor.ORANGE && mayRetarget)
                    return RouteThreatAction.Retarget;
                if (packSize <= fightableSolo)
                    return RouteThreatAction.Pull;
                return mayRetarget || felt >= ConColor.RED && mayAbandonHopeless
                    ? RouteThreatAction.Retarget : RouteThreatAction.Ignore;
            }
            int easyPack = grouped ? con <= ConColor.BLUE ? 5 : 3
                : con <= ConColor.GREEN ? 3 : con <= ConColor.BLUE ? 2 : 1;
            int manageablePack = grouped ? con <= ConColor.BLUE ? 6 : 4
                : con <= ConColor.GREEN ? 5 : con <= ConColor.BLUE ? 3 : 2;
            bool easy = !bossLike && (grouped
                ? con <= ConColor.ORANGE && packSize <= easyPack || strongParty && con == ConColor.RED && packSize <= StrongPartyEasyRedPack ||
                  IsStrongPartyPurple(con, strongParty, packSize, Math.Max(levelsAboveReference, hardestLevelsAbove), provenDeadly)
                : con <= ConColor.YELLOW && packSize <= easyPack);
            if (easy)
                return RouteThreatAction.Pull;
            if (canDetour)
                return RouteThreatAction.Detour;
            int levelsAllowed = strongParty ? StrongPartyMaximumLevelsAbove : GroupMaximumLevelsAbove;
            bool manageable = !bossLike && (grouped
                ? con <= ConColor.RED && packSize <= manageablePack && levelsAboveReference < levelsAllowed
                : con <= ConColor.ORANGE && packSize <= manageablePack);
            if (manageable)
                return RouteThreatAction.Pull;
            return mayRetarget || !grouped && bossLike && mayAbandonHopeless ||
                   grouped && mayForceRetarget && IsHopeless(con, bossLike, levelsAboveReference)
                ? RouteThreatAction.Retarget : RouteThreatAction.Ignore;
        }

        /// <summary>A monster a party must not walk into: a boss, a purple one, or one far above the party's average.</summary>
        public static bool IsHopeless(ConColor con, bool bossLike, int levelsAboveReference) =>
            bossLike || con >= ConColor.PURPLE || levelsAboveReference >= GroupMaximumLevelsAbove;

        /// <summary>
        /// A monster already fighting someone else still counts as a route threat when it cons orange or harder: it turns on
        /// the next bot as soon as its fight ends. The route scan used to skip every monster in combat, and on the 2026-11-02
        /// overnight run 878 of 1,515 bots under level 10 killed by a purple before reaching camp died to a monster of the
        /// same name that had killed another bot within three minutes and 1,500 units. Pure.
        /// </summary>
        public static bool WatchesBusyMonster(ConColor seen) => seen >= ConColor.ORANGE;

        /// <summary>
        /// A busy monster is never pulled (that joins someone else's fight against a hard monster): go around it, else give up
        /// the camp. With no camp to give up the bot walks on, as it did before. Pure.
        /// </summary>
        public static RouteThreatAction ForBusyMonster(RouteThreatAction action, bool canDetour, bool hasCamp) =>
            action != RouteThreatAction.Pull ? action
            : canDetour ? RouteThreatAction.Detour
            : hasCamp ? RouteThreatAction.Retarget : RouteThreatAction.Ignore;

        /// <summary>
        /// How far past a monster's aggro range a bot that gave up its camp because of it backs off. A bot used to stop
        /// where it gave up and stand there for the 30-second replan wait, often inside the monster's aggro: on 2026-10-29
        /// 935 of 1,594 solo deaths to a red or purple before reaching camp were to the monster the bot had just given
        /// up its camp for, most within 45 seconds.
        /// </summary>
        public const int RetreatClearance = 250;

        public static float RetreatRadius(int aggroRange, int margin) => aggroRange + margin + RetreatClearance;

        /// <summary>
        /// The centre a bot backs away from and how far, for a whole pack: past the aggro of every member, not only the
        /// first one found. On 2026-10-29 (backing away from the first monster only) 803 of 1,199 deaths to the monster
        /// just given up for were to packs, the bot caught by the members it had not backed away from.
        /// </summary>
        public static (Vector2 Center, float Radius) PackRetreat(ReadOnlySpan<(Vector2 Position, int AggroRange)> pack, int margin)
        {
            Vector2 center = Vector2.Zero;
            foreach (var member in pack)
                center += member.Position;
            center /= Math.Max(1, pack.Length);
            float reach = 0;
            foreach (var member in pack)
                reach = Math.Max(reach, Vector2.Distance(member.Position, center) + member.AggroRange);
            return (center, reach + margin + RetreatClearance);
        }

        /// <summary>Horizontal distance from a point to the segment a-b.</summary>
        public static float DistanceToSegment(Vector2 a, Vector2 b, Vector2 point)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.LengthSquared();
            float t = lengthSquared < 0.01f ? 0 : Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0, 1);
            return Vector2.Distance(a + ab * t, point);
        }

        /// <summary>
        /// Where to back off to from a monster: points at <paramref name="radius"/> from it, straight away from it
        /// first, then turning up to 90 degrees either side (never past the monster).
        /// </summary>
        public static Vector2[] RetreatCandidates(Vector2 bot, Vector2 threat, float radius)
        {
            Vector2 away = bot - threat;
            float angle = away.LengthSquared() > 1f ? MathF.Atan2(away.Y, away.X) : 0f;
            float[] turns = [0f, MathF.PI / 4, -MathF.PI / 4, MathF.PI / 2, -MathF.PI / 2];
            var points = new Vector2[turns.Length];
            for (int i = 0; i < turns.Length; i++)
                points[i] = threat + new Vector2(MathF.Cos(angle + turns[i]), MathF.Sin(angle + turns[i])) * radius;
            return points;
        }

        /// <summary>
        /// The point on the route (within the look-ahead) nearest to a position, and the route's
        /// horizontal direction there.
        /// </summary>
        public static bool NearestOnRoute(ReadOnlySpan<Vector3> route, Vector3 position, float lookAhead,
            out Vector3 point, out Vector2 direction)
        {
            point = default;
            direction = default;
            float best = float.MaxValue, travelled = 0;
            for (int i = 1; i < route.Length && travelled < lookAhead; i++)
            {
                Vector3 start = route[i - 1], delta = route[i] - start;
                float length = delta.Length();
                if (length < 0.01f) continue;
                float available = Math.Min(length, lookAhead - travelled);
                float t = Math.Clamp(Vector3.Dot(position - start, delta) / (length * length), 0, available / length);
                Vector3 nearest = start + delta * t;
                float d = Vector3.DistanceSquared(nearest, position);
                if (d < best)
                {
                    best = d;
                    point = nearest;
                    Vector2 flat = new(delta.X, delta.Y);
                    direction = flat.LengthSquared() > 0.01f ? Vector2.Normalize(flat) : Vector2.UnitY;
                }
                travelled += length;
            }
            return best < float.MaxValue;
        }

        /// <summary>
        /// The two sidestep points that keep a bot outside a monster's aggro range: beside the
        /// monster, perpendicular to the route, at aggro range plus margin plus clearance.
        /// </summary>
        public static (Vector3 Left, Vector3 Right) DetourPoints(Vector3 threat, Vector2 routeDirection,
            float aggroRange, int margin, float floorZ) =>
            (DetourPoint(threat, routeDirection, aggroRange, margin, floorZ, 1, 0),
             DetourPoint(threat, routeDirection, aggroRange, margin, floorZ, -1, 0));

        /// <summary>
        /// A sidestep point on one side of the monster (side +1 or -1), shifted along the route by
        /// <paramref name="alongFraction"/> times the sidestep distance (negative = before it).
        /// </summary>
        public static Vector3 DetourPoint(Vector3 threat, Vector2 routeDirection, float aggroRange, int margin,
            float floorZ, int side, float alongFraction)
        {
            Vector2 perpendicular = new(-routeDirection.Y, routeDirection.X);
            float distance = aggroRange + margin + DetourClearance;
            return new(threat.X + (perpendicular.X * side + routeDirection.X * alongFraction) * distance,
                       threat.Y + (perpendicular.Y * side + routeDirection.Y * alongFraction) * distance, floorZ);
        }

        /// <summary>The fractions along the route tried for each side, most direct first.</summary>
        public static readonly float[] DetourAlongFractions = [0f, -0.5f, 0.5f, -1f];

        /// <summary>
        /// Pure rule: both straight legs of a detour (start to sidestep, sidestep to the route
        /// past the monster) stay at least <paramref name="radius"/> from it horizontally.
        /// </summary>
        public static bool LegsClear(Vector3 start, Vector3 sidestep, Vector3 rejoin, Vector3 threat, float radius) =>
            SegmentDistance2D(start, sidestep, threat) >= radius && SegmentDistance2D(sidestep, rejoin, threat) >= radius;

        public static float SegmentDistance2D(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector2 a2 = new(a.X, a.Y), b2 = new(b.X, b.Y), p2 = new(p.X, p.Y), ab = b2 - a2;
            float lengthSquared = ab.LengthSquared();
            float t = lengthSquared < 0.01f ? 0 : Math.Clamp(Vector2.Dot(p2 - a2, ab) / lengthSquared, 0, 1);
            return Vector2.Distance(a2 + ab * t, p2);
        }
    }
}
