using System.Collections.Generic;
using DOL.GS.Realm;

namespace DOL.GS.PlayerClass
{
	[CharacterClass((int)eCharacterClass.Necromancer, "Necromancer", "Disciple")]
	public class ClassNecromancer : ClassDisciple
	{
		public ClassNecromancer()
			: base()
		{
			m_profession = "PlayerClass.Profession.TempleofArawn";
			m_specializationMultiplier = 10;
			m_primaryStat = eStat.INT;
			m_secondaryStat = eStat.DEX;
			m_tertiaryStat = eStat.QUI;
			m_manaStat = eStat.INT;
		}

		public override bool HasAdvancedFromBaseClass()
		{
			return true;
		}

		// The 1.130 client also offers Avalonian (its creation table, checked 2026-11-09); allowed with modern races on.
		public override List<PlayerRace> EligibleRaces => ServerProperties.Properties.ENABLE_MODERN_RACES
			? new List<PlayerRace> { PlayerRace.Briton, PlayerRace.Inconnu, PlayerRace.Saracen, PlayerRace.Avalonian }
			: new List<PlayerRace> { PlayerRace.Briton, PlayerRace.Inconnu, PlayerRace.Saracen };
	}
}
