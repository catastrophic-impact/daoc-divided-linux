using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// A brain for the commanders
    /// </summary>
    public class CommanderBrain : ControlledMobBrain
	{
		public CommanderBrain(GameLiving owner) : base(owner) { }

        public bool MinionsAssisting => Body is CommanderPet commander && commander.MinionsAssisting;

        /// <summary>
        /// Determines if a given controlled brain is part of the commanders subpets
        /// </summary>
        /// <param name="brain">The brain to check</param>
        /// <returns>True if found, else false</returns>
        public bool FindPet(IControlledBrain brain)
		{
			if (Body.ControlledNpcList != null)
			{
				lock ((Body as CommanderPet).ControlledNpcListLock)
				{
					foreach (IControlledBrain icb in Body.ControlledNpcList)
					{
						if (brain == icb)
							return true;
					}
				}
			}

			return false;
		}

		public override void OnAttackedByEnemy(AttackData ad)
		{
			base.OnAttackedByEnemy(ad);

			// Notify subpets.
			if (Body.ControlledNpcList != null)
			{
				foreach (var controlledBrain in Body.ControlledNpcList)
				{
					if (controlledBrain is BdPetBrain bdPetBrain)
						bdPetBrain.OnOwnerAttacked(ad);
				}
			}
		}

		public override void OnOwnerAttacked(AttackData ad)
		{
			base.OnOwnerAttacked(ad);

			// Notify subpets.
			if (Body.ControlledNpcList != null)
			{
				foreach (var controlledBrain in Body.ControlledNpcList)
				{
					if (controlledBrain is BdPetBrain bdPetBrain)
						bdPetBrain.OnOwnerAttacked(ad);
				}
			}
		}

		/// <summary>
		/// Attack the target on command
		/// </summary>
		/// <param name="target">The target to attack</param>
		public override void Attack(GameObject target)
		{
			base.Attack(target);

			if (MinionsAssisting && Body.ControlledNpcList != null)
			{
				lock ((Body as CommanderPet).ControlledNpcListLock)
				{
					foreach (BdPetBrain icb in Body.ControlledNpcList)
						icb?.Attack(target);
				}
			}
		}

		public override void Disengage()
		{
			base.Disengage();

			if (Body.ControlledNpcList != null)
			{
				lock ((Body as CommanderPet).ControlledNpcListLock)
				{
					foreach (BdPetBrain icb in Body.ControlledNpcList)
					{
						if (icb != null)
							icb.Disengage();
					}
				}
			}
		}

		/// <summary>
		/// Defend a minion that is being attacked
		/// </summary>
		/// <param name="ad"></param>
		public void DefendMinion(GameLiving attacker)
		{
			if (AggressionState == eAggressionState.Passive || Body.IsAttacking || Body.TargetObject == attacker)
				return;

			Attack(attacker);
		}

		/// <summary>
		/// Make sure the subpets are following the commander
		/// </summary>
		/// <param name="target"></param>
		public override void Follow(GameObject target)
		{
			base.Follow(target);
			SubPetFollow();
		}

		/// <summary>
		/// Direct all the sub pets to follow the commander
		/// </summary>
		private void SubPetFollow(bool force = true)
		{
			if (Body.ControlledNpcList != null)
			{
				lock ((Body as CommanderPet).ControlledNpcListLock)
				{
					foreach (BdPetBrain icb in Body.ControlledNpcList)
					{
						if (icb == null)
							continue;

						if (force || !icb.Body.IsAttacking)
							icb.FollowOwner();
					}
				}
			}
		}

		/// <summary>
		/// Direct all the sub pets to follow the commander
		/// </summary>
		public override void Stay()
		{
			base.Stay();
			SubPetFollow();
		}

		/// <summary>
		/// Direct all the sub pets to follow the commander
		/// </summary>
		public override void ComeHere()
		{
			base.ComeHere();
			SubPetFollow();
		}

		/// <summary>
		/// Direct all the sub pets to follow the commander
		/// </summary>
		/// <param name="target"></param>
		public override void Goto(GameObject target)
		{
			base.Goto(target);
			SubPetFollow();
		}

		public override void SetAggressionState(eAggressionState state)
		{
			base.SetAggressionState(state);
			if (Body.ControlledNpcList != null)
			{
				lock ((Body as CommanderPet).ControlledNpcListLock)
				{
					foreach (BdPetBrain icb in Body.ControlledNpcList)
					{
						if (icb != null)
							icb.SetAggressionState(state);
					}
				}
			}
		}

		public override void FollowOwner()
		{
			base.FollowOwner();
			SubPetFollow(false);
		}

		/// <summary>
		/// Checks if any spells need casting
		/// </summary>
		/// <param name="type">Which type should we go through and check for?</param>
		/// <returns></returns>
		public override bool CheckSpells(eCheckSpellType type)
		{
			bool casted = false;

			if (type == eCheckSpellType.Offensive && Body is CommanderPet pet
				&& pet.PreferredSpell != CommanderPet.eCommanderPreferredSpell.None
				&& !pet.IsCasting && !pet.IsBeingInterrupted && pet.TargetObject is GameLiving living && living.IsAlive)

			{
				Spell spellDamage = pet.CommSpellDamage;
				Spell spellDamageDebuff = pet.CommSpellDamageDebuff;
				Spell spellDot = pet.CommSpellDot;
				Spell spellDebuff = pet.CommSpellDebuff;
				Spell spellOther = pet.CommSpellOther;

				// A spell still on its recast timer or out of reach was requested anyway and refused by the spell
				// handler on every think (overnight run 2026-11-09: Crawl, Extinguish Lifeforce, Pet Lifetap and
				// Exacerbate Pain failed 55-82% of casts as "spell on cooldown"). Skip to the next choice instead.
				bool Ready(Spell spell) => spell != null && IsReadyOn(Body, spell, living);

				Spell cast = null;
				switch (pet.PreferredSpell)
				{
					case CommanderPet.eCommanderPreferredSpell.Debuff:
						if (Ready(spellDebuff) && !living.HasEffect(spellDebuff))
							cast = spellDebuff;
						break;
					case CommanderPet.eCommanderPreferredSpell.Other:
						if (Ready(spellOther))
							cast = spellOther;
						break;
				}

				if (cast == null)
				{
					// Pick a damage spell
					if (Ready(spellDot) && !living.HasEffect(spellDot))
						cast = spellDot;
					else if (Ready(spellDamageDebuff) && (!living.HasEffect(spellDamageDebuff) || !Ready(spellDamage)))
						cast = spellDamageDebuff;
					else if (Ready(spellDamage))
						cast = spellDamage;
				}

				if (cast != null)
					casted = Body.CastSpell(cast, m_mobSpellLine);
			}

			if (casted)
			{
				// Check instant spells, but only cast one to prevent spamming
				if (Body.CanCastInstantHarmfulSpells && Body.TargetObject is GameLiving instantTarget)
					foreach (Spell spell in Body.InstantHarmfulSpells)
					{
						if (!IsReadyOn(Body, spell, instantTarget) ||
							(spell.Duration > 0 && instantTarget.HasEffect(spell)))
							continue;

						if (Body.CastSpell(spell, m_mobSpellLine))
							break;
					}
			}
			else
				// Only call base method if we didn't cast anything, 
				//	otherwise it tries to cast a second offensive spell
				casted = base.CheckSpells(type);

			return casted;
		}

		/// <summary>The spell is off its recast timer and the target is within its range.</summary>
		public static bool IsReadyOn(GameNPC body, Spell spell, GameLiving target) =>
			body.GetSkillDisabledDuration(spell) <= 0 &&
			body.IsWithinRadius(target, spell.CalculateEffectiveRange(body));

		public override int ModifyDamageWithTaunt(int damage)
		{
			// TODO: Move 'CommanderPet.Taunting' to the brain.
			if (Body is CommanderPet commanderPet)
			{
				int tauntScale = GS.ServerProperties.Properties.PET_BD_COMMANDER_TAUNT_VALUE;

				if (commanderPet.Taunting && tauntScale > 100)
					damage = (int)(damage * tauntScale / 100.0);
			}

			return base.ModifyDamageWithTaunt(damage);
		}
	}
}
