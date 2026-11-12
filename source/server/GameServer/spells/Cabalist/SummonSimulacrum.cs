/*
 * DAWN OF LIGHT - The first free open source DAoC server emulator
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
 *
 */
using System;
using System.Collections.Generic;
using System.Text;
using DOL.GS.Effects;
using DOL.AI.Brain;
using DOL.GS.PacketHandler;
using DOL.Events;
using DOL.GS.PropertyCalc;
using System.Collections;
using DOL.Language;

namespace DOL.GS.Spells
{
	/// <summary>
	/// Spell handler to summon a bonedancer pet.
	/// </summary>
	/// <author>IST</author>
	[SpellHandler(eSpellType.SummonSimulacrum)]
	public class SummonSimulacrum : SummonSpellHandler
	{
		public SummonSimulacrum(GameLiving caster, Spell spell, SpellLine line)
			: base(caster, spell, line) { }

		/// <summary>While a sacrificed simulacrum's power is held, no new one can be summoned (live 1.122b, DR-094).</summary>
		public override bool CheckBeginCast(GameLiving selectedTarget, bool quiet)
		{
			if (SimulacrumSacrificeSpellHandler.ActiveOn(Caster) != null)
			{
				if (!quiet)
					MessageToCaster("You cannot summon a simulacrum while you hold a sacrificed simulacrum's power!", eChatType.CT_SpellResisted);

				return false;
			}

			return base.CheckBeginCast(selectedTarget, quiet);
		}

		public override bool CheckEndCast(GameLiving selectedTarget)
		{
			if (SimulacrumSacrificeSpellHandler.ActiveOn(Caster) != null)
				return false;

			if (Caster is GamePlayer && ((GamePlayer)Caster).ControlledBrain != null)
			{
                MessageToCaster(LanguageMgr.GetTranslation((Caster as GamePlayer).Client, "Summon.CheckBeginCast.AlreadyHaveaPet"), eChatType.CT_SpellResisted);
                return false;
			}
			return base.CheckEndCast(selectedTarget);
		}
		
	}
}
