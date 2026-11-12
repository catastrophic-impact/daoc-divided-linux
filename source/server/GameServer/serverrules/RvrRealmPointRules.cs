using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.Keeps;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;

namespace DOL.GS.ServerRules
{
    /// <summary>
    /// Live realm point rules the server lacked (rules audit 2026-11-08):
    ///   DR-176  1.81    healing a realm mate who is in RvR combat pays about 5% of the hit points restored
    ///   DR-177  1.81    a mesmerize counts as 5% and a stun as 10% of the target's health in the kill's damage pool
    ///   DR-178  1.81    repairing a keep door or outpost piece pays about 10% of the amount repaired
    ///   DR-181  1.122b  an outnumbered realm earns +10% to +100% on kills, counted in New Frontiers every 15 minutes
    ///   DR-182  1.121   realm points from kills at an outpost under siege fill a personal pool (50% keep, 20% tower)
    ///                   that is paid when the outpost is taken or the siege ends
    /// Every part has a server property; 0 (or False) turns it off. None of it applies in the battlegrounds.
    /// </summary>
    public static class RvrRealmPointRules
    {
        public const int PopulationRecountMs = 15 * 60 * 1000;
        public const int SiegeMemoryMs = 10 * 60 * 1000;
        public const int SiegeAwardRadius = 8000;
        public const int SiegeSweepMs = 15 * 1000;
        private const string HealCarryProperty = "rvr.heal.rp.carry";

        public static bool InBattleground(GameObject where) =>
            where == null || where.CurrentZone?.IsBG == true || GameServer.KeepManager?.GetBattleground(where.CurrentRegionID) != null;

        /// <summary>A real player or a persistent gamebot: the two kinds of living that earn and are worth realm points.</summary>
        public static bool CountsAsPlayer(GameLiving living) =>
            living is GamePlayer || living is GameBot { IsAutonomousWorldBot: true } || living is GameBot { IsAccountCompanion: true };

        private static void Grant(GameLiving earner, long realmPoints)
        {
            if (realmPoints <= 0)
                return;

            if (earner is GamePlayer player)
                player.GainRealmPoints(realmPoints, true);
            else if (earner is GameBot bot)
                bot.GainRealmPoints(realmPoints, true);
        }

        #region Healing (DR-176)

        /// <summary>Realm points for <paramref name="restored"/> hit points at <paramref name="percent"/>, and the hit points left over.</summary>
        public static int HealRealmPoints(int restored, int percent, out int carry)
        {
            carry = Math.Max(0, restored);

            if (percent <= 0 || restored <= 0)
                return 0;

            int points = (int) ((long) restored * percent / 100);
            carry = restored - points * 100 / percent;
            return points;
        }

        /// <summary>
        /// Called by the heal handler with the hit points really restored. Only single target and group heals count
        /// (not spread heals, heals over time or pulses), only on someone else, and only while that someone is in
        /// combat with the enemy realms.
        /// </summary>
        public static void OnHeal(GameLiving caster, GameLiving target, Spell spell, int restored)
        {
            if (caster == null || target == null || spell == null || restored <= 0 || caster == target)
                return;

            if (spell.SpellType != eSpellType.Heal || spell.Pulse != 0 || spell.Frequency != 0)
                return;

            if (!CountsAsPlayer(caster) || !CountsAsPlayer(target) || caster.Realm != target.Realm)
                return;

            if (!target.InCombatPvP || InBattleground(target))
                return;

            // Small heals add up: what does not make a whole point yet is kept for the next heal.
            int pending = caster.TempProperties.GetProperty(HealCarryProperty, 0) + restored;
            int points = HealRealmPoints(pending, Properties.HEAL_RP_PERCENT, out int carry);
            caster.TempProperties.SetProperty(HealCarryProperty, carry);

            if (points <= 0)
                return;

            Grant(caster, points);
            (caster as GamePlayer)?.Statistics.AddToRealmPointsEarnedFromHitPointsHealed((uint) points);
        }

        #endregion

        #region Crowd control (DR-177)

        /// <summary>The damage a mesmerize or stun counts as in the target's damage pool.</summary>
        public static double CrowdControlCredit(int targetMaxHealth, bool stun)
        {
            int percent = stun ? Properties.STUN_RP_POOL_PERCENT : Properties.MEZ_RP_POOL_PERCENT;
            return percent <= 0 || targetMaxHealth <= 0 ? 0 : targetMaxHealth * percent / 100.0;
        }

        /// <summary>Called when a mesmerize or stun takes hold of an enemy player or gamebot.</summary>
        public static void OnCrowdControl(GameLiving caster, GameLiving target, bool stun)
        {
            if (caster == null || target == null || caster.Realm == target.Realm || caster.Realm == eRealm.None)
                return;

            if (!CountsAsPlayer(target) || InBattleground(target))
                return;

            // A target still immune from the last one is not held: no credit.
            if (target.effectListComponent?.ContainsEffectForEffectType(stun ? eEffect.StunImmunity : eEffect.MezImmunity) == true)
                return;

            double credit = CrowdControlCredit(target.MaxHealth, stun);

            if (credit > 0)
                target.AddXPGainer(caster, credit);
        }

        #endregion

        #region Repairs (DR-178)

        public static int RepairRealmPoints(int repaired) =>
            repaired <= 0 || Properties.REPAIR_RP_PERCENT <= 0
                ? 0
                : (int) Math.Min(Math.Max(0, Properties.REPAIR_RP_CAP), (long) repaired * Properties.REPAIR_RP_PERCENT / 100);

        public static void OnRepair(GamePlayer player, GameObject repairedObject, int repaired)
        {
            if (player == null || InBattleground(repairedObject))
                return;

            Grant(player, RepairRealmPoints(repaired));
        }

        #endregion

        #region Outnumbered realm bonus (DR-181)

        private static readonly int[] _populationBonus = new int[4];
        private static long _nextPopulationCount;

        /// <summary>The 1.122b table: the realm's New Frontiers population as a share of the largest realm's.</summary>
        public static int PopulationBonusPercent(int own, int largest)
        {
            if (largest <= 0 || own >= largest)
                return 0;

            int share = (int) Math.Ceiling(own * 100.0 / largest);
            return share switch
            {
                > 90 => 0,
                > 80 => 10,
                > 70 => 25,
                > 60 => 40,
                > 50 => 55,
                > 40 => 70,
                > 30 => 85,
                _ => 100,
            };
        }

        public static int PopulationBonusPercent(eRealm realm)
        {
            if (!Properties.UNDERPOPULATED_RP_BONUS || realm is < eRealm.Albion or > eRealm.Hibernia)
                return 0;

            long now = GameLoop.GameLoopTime;

            if (now >= _nextPopulationCount)
            {
                _nextPopulationCount = now + PopulationRecountMs;
                RecountPopulation();
            }

            return _populationBonus[(int) realm];
        }

        private static void RecountPopulation()
        {
            int[] count = new int[4];
            ushort frontier = (ushort) DefaultKeepManager.DEFAULT_FRONTIERS_REGION;

            foreach (GamePlayer player in ClientService.Instance.GetPlayers())
            {
                if (player.CurrentRegionID == frontier && player.Realm is >= eRealm.Albion and <= eRealm.Hibernia)
                    count[(int) player.Realm]++;
            }

            for (eRealm realm = eRealm.Albion; realm <= eRealm.Hibernia; realm++)
                count[(int) realm] += WorldMgr.GetNPCsByType(typeof(GameBot), realm, frontier).Count(npc => npc is GameBot { IsAutonomousWorldBot: true });

            int largest = count.Max();

            for (int realm = 1; realm <= 3; realm++)
                _populationBonus[realm] = PopulationBonusPercent(count[realm], largest);
        }

        #endregion

        #region Siege contribution (DR-182)

        private sealed class Pool
        {
            public double Points;
            public long LastTick;
        }

        private static readonly Dictionary<AbstractGameKeep, Dictionary<GameLiving, Pool>> _sieges = new();
        private static readonly object _siegeLock = new();
        private static long _nextSiegeSweep;

        /// <summary>The share of kill realm points that goes into the pool at this kind of outpost.</summary>
        public static int SiegeSharePercent(bool tower) =>
            Math.Max(0, tower ? Properties.SIEGE_CONTRIBUTION_TOWER_PERCENT : Properties.SIEGE_CONTRIBUTION_KEEP_PERCENT);

        /// <summary>A pool pays at most ten times what the earner is worth as a kill.</summary>
        public static long SiegePayout(double pool, int earnerKillWorth) =>
            (long) Math.Min(Math.Max(0, pool), Math.Max(1, earnerKillWorth) * 10L);

        private static AbstractGameKeep OutpostUnderSiegeAt(GameLiving living)
        {
            IList<IArea> areas = living.CurrentAreas;

            if (areas == null)
                return null;

            foreach (IArea area in areas)
            {
                if (area is KeepArea { Keep: { InCombat: true, IsPortalKeep: false } keep })
                    return keep;
            }

            return null;
        }

        private static void AddSiegeContribution(GameLiving earner, int realmPoints)
        {
            AbstractGameKeep keep = OutpostUnderSiegeAt(earner);

            if (keep == null)
                return;

            int share = SiegeSharePercent(keep is GameKeepTower);

            if (share <= 0)
                return;

            lock (_siegeLock)
            {
                if (!_sieges.TryGetValue(keep, out Dictionary<GameLiving, Pool> pools))
                    _sieges[keep] = pools = new();

                if (!pools.TryGetValue(earner, out Pool pool))
                    pools[earner] = pool = new();

                pool.Points += realmPoints * share / 100.0;
                pool.LastTick = GameLoop.GameLoopTime;
            }
        }

        /// <summary>The outpost changed hands or its siege ended: everyone who still has a pool and is near is paid.</summary>
        private static void PaySiege(AbstractGameKeep keep, string what)
        {
            Dictionary<GameLiving, Pool> pools;

            lock (_siegeLock)
            {
                if (!_sieges.Remove(keep, out pools))
                    return;
            }

            foreach ((GameLiving earner, Pool pool) in pools)
            {
                if (earner.ObjectState != GameObject.eObjectState.Active || earner.CurrentRegionID != keep.CurrentRegion.ID ||
                    !earner.IsWithinRadius(new Point3D(keep.X, keep.Y, earner.Z), SiegeAwardRadius))
                    continue;

                long payout = SiegePayout(pool.Points, earner.RealmPointsValue);

                if (payout <= 0)
                    continue;

                (earner as GamePlayer)?.Out.SendMessage($"Your part in the siege of {keep.Name} ({what}) earns you a bonus.",
                    eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                Grant(earner, payout);
            }
        }

        /// <summary>Called by AbstractGameKeep.Reset when the lord has fallen.</summary>
        public static void OnKeepTaken(AbstractGameKeep keep)
        {
            if (keep != null)
                PaySiege(keep, "taken");
        }

        /// <summary>Game loop: pays the sieges that have ended and forgets contributions older than ten minutes.</summary>
        public static void Tick()
        {
            long now = GameLoop.GameLoopTime;

            if (now < _nextSiegeSweep)
                return;

            _nextSiegeSweep = now + SiegeSweepMs;
            List<AbstractGameKeep> defended = null;

            lock (_siegeLock)
            {
                if (_sieges.Count == 0)
                    return;

                foreach ((AbstractGameKeep keep, Dictionary<GameLiving, Pool> pools) in _sieges)
                {
                    if (!keep.InCombat)
                    {
                        (defended ??= new()).Add(keep);
                        continue;
                    }

                    foreach (GameLiving stale in pools.Where(pair => now - pair.Value.LastTick > SiegeMemoryMs).Select(pair => pair.Key).ToList())
                        pools.Remove(stale);
                }
            }

            if (defended == null)
                return;

            foreach (AbstractGameKeep keep in defended)
                PaySiege(keep, "defended");
        }

        #endregion

        /// <summary>
        /// Called with the realm points a kill is about to pay (player or gamebot victim): adds the outnumbered realm
        /// bonus and notes the result in the earner's siege pool. Returns what to pay.
        /// </summary>
        public static int OnKillRealmPoints(GameLiving earner, int realmPoints)
        {
            if (earner == null || realmPoints <= 0 || InBattleground(earner))
                return realmPoints;

            int percent = PopulationBonusPercent(earner.Realm);

            if (percent > 0)
            {
                int bonus = realmPoints * percent / 100;
                realmPoints += bonus;

                if (bonus > 0)
                    (earner as GamePlayer)?.Out.SendMessage($"Your realm is outnumbered in the frontier: {bonus} bonus realm points (+{percent}%).",
                        eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            }

            AddSiegeContribution(earner, realmPoints);
            return realmPoints;
        }
    }
}
