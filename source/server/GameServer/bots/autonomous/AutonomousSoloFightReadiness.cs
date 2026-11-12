namespace DOL.GS
{
    /// <summary>
    /// When a solo gamebot is fit to start a fight on the road, when it rests there first, and when it breaks off a fight it
    /// is losing. Night playtest 2026-11-04: 355 of 427 Midgard bot deaths came before the bot reached its camp, 79 right after
    /// "Clearing the route"; route pulls never looked at health, endurance or power (Dagununrun, Shadowblade 26, pulled four snow
    /// imps in 90 seconds and died with 28% endurance), bots rested only at camp, and a solo bot fought every pull to the death
    /// (Albion: 722 of 864 deaths solo; a Theurgist 25 kept summoning at 5% health with two druids on her). Parties keep their
    /// own pull gate (AutonomousBotGroupCoordinator.ReadyForGroupPull) and break-off rule
    /// (AutonomousRouteThreatPolicy.ShouldBreakOff: a lost healer only breaks off against orange or worse). Pure.
    /// </summary>
    public static class AutonomousSoloFightReadiness
    {
        public const int RoutePullHealthPercent = 75;
        public const int RoutePullPowerPercent = 50;
        public const int RoutePullEndurancePercent = 40;
        /// <summary>A fight this recent makes a hurt solo bot rest on the road before it walks on.</summary>
        public const long RoadRestAfterFightMilliseconds = 60_000;
        /// <summary>The longest a road rest lasts (fast bot rest refills 10% a second; this only stops a rest that never ends).</summary>
        public const long RoadRestLimitMilliseconds = 90_000;

        /// <summary>A solo bot opens a route pull only with the health, power and endurance to finish it.</summary>
        public static bool ReadyForRoutePull(int health, int power, int endurance, bool usesPower) =>
            health >= RoutePullHealthPercent && (!usesPower || power >= RoutePullPowerPercent) &&
            endurance >= RoutePullEndurancePercent;

        /// <summary>
        /// A solo bot that fought a moment ago and is still below the ordinary rest thresholds rests where it stands before it
        /// walks on. A bot that has not fought walks on as before: rest is not a travel prerequisite.
        /// </summary>
        public static bool ShouldRestOnRoad(bool solo, long now, long lastFightTick, int health, int power, int endurance, bool usesPower) =>
            solo && lastFightTick > 0 && now - lastFightTick < RoadRestAfterFightMilliseconds &&
            AutonomousRestPolicy.NeedsRecovery((byte)health, (byte)power, (byte)endurance, usesPower);

        public const int DisengageHealthPercent = 30;
        /// <summary>The monster must have at least this much more health (in percent) than the bot: losing, not trading evenly.</summary>
        public const int DisengageFoeHealthLead = 25;
        /// <summary>A monster this hurt is nearly dead: running now only throws the fight away.</summary>
        public const int DisengageFoeMinimumHealthPercent = 40;
        public const int DisengageAddsHealthPercent = 45;
        public const int DisengageAddsFoeHealthPercent = 60;
        public const long FleeMilliseconds = 15_000;
        public const long FleeCooldownMilliseconds = 60_000;
        public const int FleeRadius = 1_800;
        /// <summary>A flee ends early once no attacker has landed a blow for this long.</summary>
        public const long FleeQuietMilliseconds = 4_000;

        /// <summary>
        /// A solo bot breaks off when it is losing badly: below 30% health with the monster well above that and far from dead,
        /// or below 45% with two or more on it and the strongest still above 60%. A grey monster is never run from, and a bot
        /// that can still heal itself only runs from adds.
        /// </summary>
        public static bool ShouldDisengage(int health, int strongestFoeHealth, int attackers, ConColor hardest, bool canSelfHeal)
        {
            if (attackers <= 0 || hardest <= ConColor.GREY || health <= 0)
                return false;
            bool losingOneOnOne = !canSelfHeal && health < DisengageHealthPercent &&
                                  strongestFoeHealth >= DisengageFoeMinimumHealthPercent &&
                                  strongestFoeHealth - health >= DisengageFoeHealthLead;
            bool swampedByAdds = attackers >= 2 && health < DisengageAddsHealthPercent &&
                                 strongestFoeHealth >= DisengageAddsFoeHealthPercent;
            return losingOneOnOne || swampedByAdds;
        }
    }
}
