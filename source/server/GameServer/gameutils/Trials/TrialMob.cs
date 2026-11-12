using DOL.GS.PacketHandler;

namespace DOL.GS.Trials
{
    /// <summary>A monster or clickable objective that belongs to one trial run and leaves with it.</summary>
    public class TrialMob : GameNPC
    {
        public TrialRun Run { get; }
        public TrialMobSpec Spec { get; }
        public bool Immune { get; set; }
        public bool Finished { get; private set; }

        private readonly int _maxHealth;

        public TrialMob(TrialRun run, TrialMobSpec spec, int maxHealth)
        {
            Run = run;
            Spec = spec;
            Immune = spec.Immune;
            _maxHealth = maxHealth;
        }

        public override int MaxHealth => _maxHealth;

        /// <summary>
        /// Only the trial's required mobs pay a kill. Optional extras (Ghillan's clones, respawning hatchlings, adds)
        /// spawn without end and were farmed for full XP, coin and Atlantean Glass.
        /// </summary>
        public bool Rewards => Spec.Required;

        public override long ExperienceValue => Rewards ? base.ExperienceValue : 0;

        public override long GetExperienceValueForLevel(int level) => Rewards ? base.GetExperienceValueForLevel(level) : 0;

        public override bool CanDropLoot => Rewards && base.CanDropLoot;

        public override void TakeDamage(GameObject source, eDamageType damageType, int damageAmount, int criticalAmount)
        {
            if (Spec.Objective || Immune || !Run.MayHurt(source) || !Spec.Hurts.HasFlag(KindOf(source, damageType)))
                return;

            base.TakeDamage(source, damageType, damageAmount, criticalAmount);

            if (Spec.Reflect > 0 && source is GameLiving attacker && attacker.IsAlive)
                attacker.TakeDamage(this, damageType, (int)(damageAmount * Spec.Reflect), 0);
        }

        public static TrialDamageKind KindOf(GameObject source, eDamageType damageType)
        {
            if (damageType is not (eDamageType.Slash or eDamageType.Crush or eDamageType.Thrust))
                return TrialDamageKind.Spell;
            return source is GameLiving { ActiveWeaponSlot: eActiveWeaponSlot.Distance } ? TrialDamageKind.Ranged : TrialDamageKind.Melee;
        }

        public override void Die(GameObject killer)
        {
            Finished = true;
            Run.MobFinished(this);
            base.Die(killer);
        }

        /// <summary>Finishes an objective as a click would, without the click.</summary>
        public void Complete()
        {
            Finished = true;
            Run.MobFinished(this);
        }

        public override bool Interact(GamePlayer player)
        {
            if (!Spec.Objective)
                return base.Interact(player);
            if (Finished || !Run.IsParticipant(player))
                return false;
            if (!IsWithinRadius(player, WorldMgr.INTERACT_DISTANCE))
            {
                player.Out.SendMessage("You are too far away.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return false;
            }
            if (!string.IsNullOrEmpty(Spec.ObjectiveText))
                player.Out.SendMessage(Spec.ObjectiveText, eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            Complete();
            return true;
        }
    }
}
