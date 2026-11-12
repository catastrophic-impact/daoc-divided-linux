using System;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS
{
    public sealed partial class AutonomousWorldBotController
    {
        // Road rest (AutonomousSoloFightReadiness): when it started (0 = not resting) and what it says.
        private long _roadRestSinceTick;
        private string _roadRestReason = string.Empty;
        // Solo break-off: running until this tick (0 = not fleeing), and the next tick a new one may start.
        private long _soloFleeUntilTick;
        private long _nextSoloFleeTick;
        private long _soloFleeLastHitTick;

        private void BeginRoadRest(GameBot bot, long now, string reason)
        {
            if (_roadRestSinceTick == 0)
                _roadRestSinceTick = now;
            _roadRestReason = reason;
            bot.StopMovingOnPath();
            bot.StopMoving();
            if (!BotRestRecovery.BlocksRest(bot))
                bot.BeginRecoveryRest();
            SetStatus(bot, "Resting on the road", GoalText(),
                $"{reason}: HP {bot.HealthPercent}% • power {bot.ManaPercent}% • endurance {bot.EndurancePercent}%");
        }

        private void EndRoadRest(GameBot bot)
        {
            if (_roadRestSinceTick == 0)
                return;
            _roadRestSinceTick = 0;
            _roadRestReason = string.Empty;
            bot.WakeRecoveryRest();
        }

        /// <summary>
        /// Keeps a solo bot resting on the road until it is full again (or the rest limit runs out), and starts a rest after a
        /// fight on the road that left it below the ordinary rest thresholds. True consumes the travel turn.
        /// </summary>
        private bool HoldRoadRest(GameBot bot, long now)
        {
            bool usesPower = bot.MaxMana > 0;
            if (_roadRestSinceTick != 0)
            {
                if (BotRestRecovery.BlocksRest(bot) || now - _roadRestSinceTick >= AutonomousSoloFightReadiness.RoadRestLimitMilliseconds ||
                    AutonomousRestPolicy.IsFullyRecovered(bot.HealthPercent, bot.ManaPercent, bot.EndurancePercent, usesPower))
                {
                    EndRoadRest(bot);
                    return false;
                }
                BeginRoadRest(bot, now, _roadRestReason);
                return true;
            }
            long lastFight = Math.Max(bot.LastAttackTick, bot.LastAttackedByEnemyTick);
            if (!AutonomousSoloFightReadiness.ShouldRestOnRoad(true, now, lastFight, bot.HealthPercent, bot.ManaPercent,
                    bot.EndurancePercent, usesPower) || BotRestRecovery.BlocksRest(bot))
                return false;
            BeginRoadRest(bot, now, "Recovering after a fight on the way");
            return true;
        }

        /// <summary>
        /// A solo bot losing a PvE fight badly breaks off and runs (AutonomousSoloFightReadiness.ShouldDisengage). Parties use
        /// TryGroupBreakOff. True consumes this turn.
        /// </summary>
        private bool TrySoloDisengage(BotBrain brain, GameBot bot, long now)
        {
            if (now < _nextSoloFleeTick || bot.Group != null || _groupDirective?.IsDynamic == true ||
                AutonomousRealmRaid.GetView(bot.Group) != null || bot.IsCrowdControlled || !bot.IsAlive)
                return false;
            GameNPC[] foes = bot.attackComponent?.AttackerTracker?.Attackers?.OfType<GameNPC>()
                .Where(npc => npc.IsAlive && npc is not GameBot && npc.Realm == eRealm.None && npc.CurrentRegionID == bot.CurrentRegionID &&
                              npc.Brain is not IControlledBrain && bot.IsWithinRadius(npc, 2_000))
                .ToArray() ?? [];
            if (foes.Length == 0)
                return false;
            bool canSelfHeal = bot.CharacterClass != null && BotPartyRoles.IsHealingClass((eCharacterClass)bot.CharacterClass.ID) &&
                               bot.ManaPercent >= 25;
            int strongest = foes.Max(npc => npc.HealthPercent);
            ConColor hardest = foes.Max(npc => ConLevels.GetConColor(bot.GetConLevel(npc)));
            if (!AutonomousSoloFightReadiness.ShouldDisengage(bot.HealthPercent, strongest, foes.Length, hardest, canSelfHeal))
                return false;

            IPathfindingMgr nav = PathfindingProvider.Instance;
            Zone zone = bot.CurrentZone;
            if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone))
                return false;
            _nextSoloFleeTick = now + AutonomousSoloFightReadiness.FleeCooldownMilliseconds;
            GameNPC[] others = bot.GetNPCsInRadius((ushort)AutonomousRouteThreatPolicy.ScanRadius)
                .Where(npc => IsExperienceMonster(npc) && npc.IsAlive && npc.Brain is StandardMobBrain { AggroRange: > 0 })
                .Take(24).ToArray();
            brain.PrepareForTankPull();
            if (!BackAwayFrom(bot, nav, zone, foes[0], foes, AutonomousRouteThreatPolicy.RangedMargin, others,
                    AutonomousSoloFightReadiness.FleeRadius))
            {
                Log.Info($"AUTONOMOUS_SOLO_DISENGAGE bot={bot.Name} id={bot.DatabaseID} class=\"{bot.ClassName}\" level={bot.Level} result=no_way_out " +
                         $"hp={bot.HealthPercent} foes={foes.Length} foe=\"{foes[0].Name}\" foeLevel={foes[0].Level} foeHp={strongest} con={hardest} " +
                         $"zone=\"{zone.Description}\"");
                return false;
            }
            _soloFleeUntilTick = now + AutonomousSoloFightReadiness.FleeMilliseconds;
            _soloFleeLastHitTick = Math.Max(now, bot.LastAttackedByEnemyTick);
            _routeCautionUntilTick = now + RouteCautionAfterDeathMilliseconds;
            _recoverBeforeNextCampTarget = _camp != null;
            Log.Info($"AUTONOMOUS_SOLO_DISENGAGE bot={bot.Name} id={bot.DatabaseID} class=\"{bot.ClassName}\" level={bot.Level} result=running " +
                     $"hp={bot.HealthPercent} foes={foes.Length} foe=\"{foes[0].Name}\" foeLevel={foes[0].Level} foeHp={strongest} con={hardest} " +
                     $"camp=\"{_camp?.Id}\" zone=\"{zone.Description}\" at={bot.X},{bot.Y},{bot.Z}");
            SetStatus(bot, $"Breaking off from {foes[0].Name}", GoalText(),
                $"Losing the fight at {bot.HealthPercent}% health: running to rest", foes[0].Name);
            return true;
        }

        /// <summary>
        /// Keeps a breaking-off bot running: it drops the fight each turn instead of turning back to it. The run ends when the
        /// time is up or nothing has hit the bot for a few seconds; a monster that is still on it then gets fought again.
        /// </summary>
        private bool ContinueSoloFlee(BotBrain brain, GameBot bot, long now)
        {
            if (_soloFleeUntilTick == 0)
                return false;
            if (bot.LastAttackedByEnemyTick > _soloFleeLastHitTick)
                _soloFleeLastHitTick = bot.LastAttackedByEnemyTick;
            bool quiet = now - _soloFleeLastHitTick >= AutonomousSoloFightReadiness.FleeQuietMilliseconds;
            if (!bot.IsAlive || now >= _soloFleeUntilTick || bot.Group != null || quiet && !bot.IsMoving)
            {
                _soloFleeUntilTick = 0;
                return false;
            }
            if (brain.HasAggro || bot.IsAttacking || bot.TargetObject != null)
                brain.PrepareForTankPull();
            if (!bot.IsMoving)
            {
                // The first point was reached with the monster still on it: one more leg away.
                IPathfindingMgr nav = PathfindingProvider.Instance;
                GameNPC chaser = bot.attackComponent?.AttackerTracker?.Attackers?.OfType<GameNPC>().FirstOrDefault(npc => npc.IsAlive);
                if (chaser == null || bot.CurrentZone == null || !nav.IsAvailable || !nav.HasNavmesh(bot.CurrentZone) ||
                    !BackAwayFrom(bot, nav, bot.CurrentZone, chaser, [chaser], AutonomousRouteThreatPolicy.RangedMargin, [],
                        AutonomousSoloFightReadiness.FleeRadius))
                {
                    _soloFleeUntilTick = 0;
                    return false;
                }
            }
            return true;
        }
    }
}
