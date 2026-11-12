using System;
using System.Collections.Generic;
using DOL.GS.Effects;

namespace DOL.GS.Spells
{
	/// <summary>
	/// Base class for all spells that remove effects
	/// </summary>
	public abstract class RemoveSpellEffectHandler : SpellHandler
	{
		/// <summary>
		/// Stores spell effect type that will be removed
		/// RR4: now its a list of effects to remove
		/// </summary>
		protected List<string> m_spellTypesToRemove = null;

		/// <summary>
		/// Spell effect type that will be removed
		/// RR4: now its a list of effects to remove
		/// </summary>
		public virtual List<string> SpellTypesToRemove
		{
			get { return m_spellTypesToRemove; }
		}

		/// <summary>
		/// called when spell effect has to be started and applied to targets
		/// </summary>
		/// <param name="target"></param>
		public override void FinishSpellCast(GameLiving target)
		{
			m_caster.Mana -= PowerCost(target);
			base.FinishSpellCast(target);
		}

		public override void OnEffectPulse(GameSpellEffect effect)
		{
			base.OnEffectPulse(effect);
			OnDirectEffect(effect.Owner);
		}

		public override void OnDirectEffect(GameLiving target)
		{
			base.OnDirectEffect(target);
			if (target == null || !target.IsAlive)
				return;

			// RR4: we remove all the effects
			foreach (string toRemove in SpellTypesToRemove)
			{
				//GameSpellEffect effect = target.FindEffectOnTarget(toRemove);
				// A name that is not an eEffect ("Silence" in Cure Nearsight) parsed to 0, which is Bladeturn, so the cure
				// stripped the target's Bladeturn (OpenDAoC b9b32cf03). Unknown names now remove nothing.
				foreach (eEffect effectType in EffectsFor(toRemove))
					EndEffect(target, effectType);
			}
			SendEffectAnimation(target, 0, false, 1);
		}

		// Old DOL spell-type names these handlers still list, mapped to the ECS effects that carry them.
		private static readonly Dictionary<string, eEffect[]> s_effectAliases = new(StringComparer.Ordinal)
		{
			["Mesmerize"] = [eEffect.Mez],
			["StyleBleeding"] = [eEffect.Bleed],
			["SpeedDecrease"] = [eEffect.MovementSpeedDebuff, eEffect.Snare],
			["StyleSpeedDecrease"] = [eEffect.MovementSpeedDebuff, eEffect.Snare],
			["DamageSpeedDecrease"] = [eEffect.MovementSpeedDebuff, eEffect.Snare],
			["HereticSpeedDecrease"] = [eEffect.MovementSpeedDebuff, eEffect.Snare],
			["HereticDamageSpeedDecreaseLOP"] = [eEffect.MovementSpeedDebuff, eEffect.Snare],
			["VampiirSpeedDecrease"] = [eEffect.MovementSpeedDebuff, eEffect.Snare],
			["ValkyrieSpeedDecrease"] = [eEffect.MovementSpeedDebuff, eEffect.Snare],
			["WarlockSpeedDecrease"] = [eEffect.MovementSpeedDebuff, eEffect.Snare],
			["PveResurrectionIllness"] = [eEffect.ResurrectionIllness],
		};

		/// <summary>The effects a listed name removes. A name that is neither an eEffect nor a known alias removes nothing:
		/// it used to parse to 0 (Bladeturn), so Cure Nearsight, Cure All, Cure Poison and Bolstering Roar stripped the
		/// target's Bladeturn (OpenDAoC b9b32cf03 found it for Cure Nearsight's "Silence").</summary>
		public static IReadOnlyList<eEffect> EffectsFor(string name)
		{
			if (name != null && s_effectAliases.TryGetValue(name, out eEffect[] aliased))
				return aliased;
			return Enum.TryParse(name, out eEffect effectType) && Enum.IsDefined(effectType) && !int.TryParse(name, out _)
				? [effectType]
				: [];
		}

		private static void EndEffect(GameLiving target, eEffect effectType)
		{
			ECSGameEffect effect = EffectListService.GetEffectOnTarget(target, effectType);
			if (effect == null)
				return;
			if (effect is WarlockChainDotEffect warlockDot)
				warlockDot.Cured = true;
			effect.End();
		}

		// constructor
		protected RemoveSpellEffectHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
	}
}
