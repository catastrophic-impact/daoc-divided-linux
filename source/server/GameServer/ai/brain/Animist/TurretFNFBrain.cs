using System.Collections.Generic;
using DOL.GS;
using DOL.GS.ServerProperties;

namespace DOL.AI.Brain
{
    public class TurretFNFBrain : TurretBrain
    {
        private List<GameLiving> _filteredAggroList = new();

        public TurretFNFBrain(GameLiving owner) : base(owner) { }

        protected override bool CheckLosBeforeCastingOffensiveSpells => Properties.CHECK_LOS_BEFORE_AGGRO_FNF;

        public override void Think()
        {
            CheckProximityAggro();

            if (!CheckSpells(eCheckSpellType.Offensive))
                CheckSpells(eCheckSpellType.Defensive);
        }

        public override bool CheckProximityAggro()
        {
            // AI shrooms assist one deliberate fight; even a single-target
            // spell would pull an entire camp if every turret scanned/randomly
            // selected fresh mobs. Human-owned manual FnFs keep native behavior.
            if (AnimistSingleTargetPolicy.AppliesTo(Owner))
                return false;

            // FnF turrets need to add all players and NPCs to their aggro list to be able to switch target randomly and effectively.
            CheckPlayerAggro();
            CheckNpcAggro();
            return HasAggro;
        }

        protected override void CheckPlayerAggro()
        {
            // Copy paste of 'base.CheckPlayerAggro()' except we add all players in range.
            foreach (GamePlayer player in Body.GetPlayersInRadius((ushort) AggroRange))
            {
                if (!CanConsiderProximityTarget(player))
                    continue;

                if (player.IsStealthed || player.Steed != null)
                    continue;

                if (player.effectListComponent.ContainsEffectForEffectType(eEffect.Shade))
                    continue;

                if (Properties.CHECK_LOS_BEFORE_AGGRO_FNF)
                    SendAggroLosCheck(player, player);
                else
                    AddToAggroList(player);
            }
        }

        protected override void CheckNpcAggro()
        {
            // Copy paste of 'base.CheckNPCAggro()' except we add all NPCs in range.
            foreach (GameNPC npc in Body.GetNPCsInRadius((ushort) AggroRange))
            {
                if (!CanConsiderProximityTarget(npc))
                    continue;

                if (npc is GameTaxi or GameTrainingDummy)
                    continue;

                if (Properties.CHECK_LOS_BEFORE_AGGRO_FNF)
                {
                    if (npc.Brain is ControlledMobBrain theirControlledNpcBrain && theirControlledNpcBrain.GetPlayerOwner() is GamePlayer theirOwner)
                    {
                        SendAggroLosCheck(theirOwner, npc);
                        continue;
                    }
                    else if (GetPlayerOwner() is GamePlayer ourOwner)
                    {
                        SendAggroLosCheck(ourOwner, npc);
                        continue;
                    }
                }

                AddToAggroList(npc);
            }
        }

        protected bool CanConsiderProximityTarget(GameLiving target)
        {
            if (Body is TurretFnfPet && Owner is GameBot { IsAutonomousWorldBot: true } bot && BotAnimistPolicy.AppliesTo(bot))
                return AggroLevel > 0 && target != null && !bot.IsObjectGreyCon(target) &&
                    GameServer.ServerRules.IsAllowedToAttack(Body, target, true);
            // The private Stormforged field storm is useful against low-level enemies too.
            // Stock FnFs retain their owner-relative grey-con restriction.
            if (Body is TurretFnfPet { NPCTemplate.TemplateId: 60171002 } && Owner is GamePlayer)
                return AggroLevel > 0 && target != null && target.IsAlive &&
                    GameServer.ServerRules.IsAllowedToAttack(Body, target, true);
            return CanAggroTarget(target);
        }

        protected override bool CanAddToAggroListFromMultipleLosChecks => true;

        protected override bool ShouldBeIgnoredFromAggroList(GameLiving living)
        {
            // We always return true because we don't care about what `CleanUpAggroListAndGetHighestModifiedThreat` returns.
            // This is just an opportunity to build a filtered aggro list, to be used by `CalculateNextAttackTarget`.
            if (LivingHasEffect(living, ((TurretPet) Body).TurretSpell) ||
                living.effectListComponent.ContainsEffectForEffectType(eEffect.SnareImmunity) ||
                base.ShouldBeIgnoredFromAggroList(living))
            {
                return true;
            }

            _filteredAggroList.Add(living);
            return true;
        }

        protected override GameLiving CleanUpAggroListAndGetHighestModifiedThreat()
        {
            _filteredAggroList.Clear();
            return base.CleanUpAggroListAndGetHighestModifiedThreat();
        }

        protected override GameLiving CalculateNextAttackTarget()
        {
            if (AnimistSingleTargetPolicy.AppliesTo(Owner))
                return base.CalculateNextAttackTarget();

            CleanUpAggroListAndGetHighestModifiedThreat();

            // The aggro list keeps attackers that have since moved beyond this rooted turret's reach; casting at them
            // was refused as "too far" (sandbox 2026-11-09: one Forest's Incarnation 100x at 4502 > 1000).
            Spell spell = (Body as TurretPet).TurretSpell;
            int reach = spell.IsPBAoE ? spell.Radius : spell.CalculateEffectiveRange(Body);
            _filteredAggroList.RemoveAll(living => !Body.IsWithinRadius(living, reach));

            // Prioritize targets that don't already have our effect and aren't immune to it.
            // If there's none, allow them to be attacked again but only if our spell does damage.
            if (_filteredAggroList.Count > 0)
                return _filteredAggroList[Util.Random(_filteredAggroList.Count - 1)];
            else if (spell.Damage > 0)
            {
                List<GameLiving> tempAggroList = GameLoop.GetListForTick<GameLiving>();
                foreach (GameLiving living in AggroList.Keys) // Wasteful, but we don't expect this to be called often.
                    if (Body.IsWithinRadius(living, reach))
                        tempAggroList.Add(living);

                if (tempAggroList.Count != 0)
                    return tempAggroList[Util.Random(tempAggroList.Count - 1)];
            }

            return null;
        }

        public override void UpdatePetWindow() { }
        public override void OnAttackedByEnemy(AttackData ad) { }
    }
}
