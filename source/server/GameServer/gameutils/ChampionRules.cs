using System;
using System.Collections.Generic;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Offline Champion Levels: realm points earned at level 50 become champion experience (cl_rp_rate),
    /// CL1-15. Each realm has one Champion path that trains itself: reaching a Champion Level grants that
    /// level's ability (RealmChampionSpecialization, ChampionAbilities). Champion training starts when a
    /// level 50 player speaks to their realm's King in the throne room (BecomeChampion, owner 2026-11-09);
    /// until then realm points give no champion experience. Characters that already had it keep it.
    /// </summary>
    public static class ChampionRules
    {
        public static long ToChampionExperience(long realmPoints, double rate) =>
            realmPoints <= 0 || rate <= 0 ? 0 : (long)Math.Min(long.MaxValue / 2, realmPoints * rate);

        public static string PathFor(eRealm realm) => realm switch
        {
            eRealm.Albion => "ChampionAlbion",
            eRealm.Midgard => "ChampionMidgard",
            eRealm.Hibernia => "ChampionHibernia",
            _ => null
        };

        public static void GainRealmPoints(GamePlayer player, long realmPoints)
        {
            if (player.Level < GamePlayer.MAX_LEVEL || PathFor(player.Realm) == null || player.ChampionLevel >= GamePlayer.CL_MAX_LEVEL)
                return;
            long experience = ToChampionExperience(realmPoints, Properties.CL_RP_RATE);
            if (experience <= 0)
                return;
            MasterLevelRules.EnsureTraining(player);
            if (!player.Champion)
                return;
            // Quest source: the amount is already converted, so skip the old XP-based conversion.
            player.GainChampionExperience(experience, eXPSource.Quest);
        }

        public enum KingAnswer { NotLevelFifty, WrongRealm, AlreadyChampion, NowChampion }

        /// <summary>
        /// The King starts Champion training. Unlike the old KingNPC path this never clears champion levels or
        /// experience, so a character that somehow has progress keeps it.
        /// </summary>
        public static KingAnswer BecomeChampion(GamePlayer player, eRealm kingRealm)
        {
            if (player.Level < GamePlayer.MAX_LEVEL || PathFor(player.Realm) == null)
                return KingAnswer.NotLevelFifty;
            if (kingRealm != eRealm.None && player.Realm != kingRealm)
                return KingAnswer.WrongRealm;
            if (player.Champion)
                return KingAnswer.AlreadyChampion;
            player.Champion = true;
            return KingAnswer.NowChampion;
        }

        public static string KingNameFor(eRealm realm) => realm switch
        {
            eRealm.Albion => "King Constantine",
            eRealm.Midgard => "King Eirik",
            eRealm.Hibernia => "King Lug Lamfhota",
            _ => "your King"
        };

        /// <summary>Tells a level 50 player who has not started Champion training where to go.</summary>
        public static void RemindToVisitKing(GamePlayer player)
        {
            if (player == null || player.Champion || player.Level < GamePlayer.MAX_LEVEL || PathFor(player.Realm) == null)
                return;
            player.Out?.SendMessage($"Champion training has not begun. Ask the Royal Guard in your capital for an audience with {KingNameFor(player.Realm)} in the throne room to begin it.",
                eChatType.CT_Important, eChatLoc.CL_SystemWindow);
        }
    }

    /// <summary>
    /// A realm's Champion path (KeyName ChampionAlbion / ChampionMidgard / ChampionHibernia). Its spell line
    /// level is the player's Champion Level, so each CL unlocks the next ability; other realms see nothing.
    /// </summary>
    public class RealmChampionSpecialization : CareerSpecialization
    {
        public RealmChampionSpecialization(string keyname, string displayname, ushort icon, int ID)
            : base(keyname, displayname, icon, ID)
        {
        }

        public eRealm Realm => KeyName switch
        {
            "ChampionAlbion" => eRealm.Albion,
            "ChampionMidgard" => eRealm.Midgard,
            "ChampionHibernia" => eRealm.Hibernia,
            _ => eRealm.None
        };

        public bool IsFor(GameLiving living) => living is GamePlayer player && player.Realm == Realm;

        public override int GetSpecLevelForLiving(GameLiving living) => living is GamePlayer player && IsFor(player) ? player.ChampionLevel : 0;

        public override List<SpellLine> GetSpellLinesForLiving(GameLiving living) =>
            IsFor(living) ? GetSpellLinesForLiving(living, GetSpecLevelForLiving(living)) : new List<SpellLine>();

        public override IDictionary<SpellLine, List<Skill>> GetLinesSpellsForLiving(GameLiving living) =>
            IsFor(living) && GetSpecLevelForLiving(living) > 0
                ? GetLinesSpellsForLiving(living, GetSpecLevelForLiving(living))
                : new Dictionary<SpellLine, List<Skill>>();

        protected override List<Ability> GetAbilitiesForLiving(GameLiving living, int level) => new List<Ability>();
    }
}
