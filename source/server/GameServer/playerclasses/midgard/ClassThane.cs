using System.Collections.Generic;
using DOL.GS.Realm;

namespace DOL.GS.PlayerClass
{
	[CharacterClass((int)eCharacterClass.Thane, "Thane", "Viking")]
	public class ClassThane : ClassViking
	{
		public ClassThane()
			: base()
		{
			m_profession = "PlayerClass.Profession.HouseofThor";
			m_specializationMultiplier = 20;
			m_primaryStat = eStat.STR;
			m_secondaryStat = eStat.PIE;
			m_tertiaryStat = eStat.CON;
			m_manaStat = eStat.PIE;
			m_wsbase = 360;
			m_baseHP = 720;
		}

		public override eClassType ClassType
		{
			get { return eClassType.Hybrid; }
		}

		public override bool HasAdvancedFromBaseClass()
		{
			return true;
		}

		// The 1.130 client also offers Valkyn (its creation table, checked 2026-11-09); allowed with modern races on.
		public override List<PlayerRace> EligibleRaces => ServerProperties.Properties.ENABLE_MODERN_RACES
			? new List<PlayerRace> { PlayerRace.Dwarf, PlayerRace.Norseman, PlayerRace.Troll, PlayerRace.Valkyn }
			: new List<PlayerRace> { PlayerRace.Dwarf, PlayerRace.Norseman, PlayerRace.Troll };
	}
}
