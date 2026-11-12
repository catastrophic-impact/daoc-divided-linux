using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// /gc addalt: a guild member who may invite adds another character of their own account to the guild, whether
    /// or not that character is online. In a single-player world there is rarely a second online member to
    /// /gc invite one of your alts, and an account can only have one character in the game at a time.
    /// Rules: the same account only, the inviter needs the guild's Invite right, the alt must be of the guild's
    /// realm (Guild.AddPlayer refuses other realms too) and in no guild yet. The alt joins at the rank /gc invite
    /// gives (9, or 8 in a starting guild). For real players' characters only: bots are not character rows.
    /// </summary>
    public static class GuildAltJoin
    {
        public enum Refusal
        {
            None,
            NotInGuild,
            NoInvitePrivilege,
            NotFound,
            Self,
            AlreadyInThisGuild,
            InAnotherGuild,
            OtherRealm
        }

        /// <summary>The account's character with this name (case does not matter), or null.</summary>
        public static DbCoreCharacter FindAlt(IEnumerable<DbCoreCharacter> accountCharacters, string name)
        {
            if (accountCharacters == null || string.IsNullOrWhiteSpace(name))
                return null;

            string wanted = name.Trim();
            return accountCharacters.FirstOrDefault(c => c != null && string.Equals(c.Name, wanted, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Why <paramref name="alt"/> may not be added to <paramref name="guild"/>, or <see cref="Refusal.None"/>.</summary>
        /// <param name="inviterCharacterId">The inviter's character ObjectId (its InternalID).</param>
        /// <param name="inviterAccount">The inviter's account name; an alt of any other account is "not found".</param>
        public static Refusal Check(Guild guild, bool inviterMayInvite, string inviterCharacterId, string inviterAccount, DbCoreCharacter alt)
        {
            if (guild == null)
                return Refusal.NotInGuild;
            if (!inviterMayInvite)
                return Refusal.NoInvitePrivilege;
            // Another account's character is reported as not found, so the command never tells who exists.
            if (alt == null || string.IsNullOrEmpty(inviterAccount) || !string.Equals(alt.AccountName, inviterAccount, StringComparison.OrdinalIgnoreCase))
                return Refusal.NotFound;
            if (string.Equals(alt.ObjectId, inviterCharacterId, StringComparison.Ordinal))
                return Refusal.Self;
            if (string.Equals(alt.GuildID, guild.GuildID, StringComparison.Ordinal))
                return Refusal.AlreadyInThisGuild;
            if (!string.IsNullOrEmpty(alt.GuildID))
                return Refusal.InAnotherGuild;
            if (alt.Realm != (int)guild.Realm)
                return Refusal.OtherRealm;
            return Refusal.None;
        }

        /// <summary>The account's characters that /gc addalt would accept now, by name.</summary>
        public static List<DbCoreCharacter> Eligible(Guild guild, bool inviterMayInvite, string inviterCharacterId, string inviterAccount, IEnumerable<DbCoreCharacter> accountCharacters)
        {
            if (accountCharacters == null)
                return new List<DbCoreCharacter>();

            return accountCharacters
                .Where(c => Check(guild, inviterMayInvite, inviterCharacterId, inviterAccount, c) == Refusal.None)
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string RefusalText(Refusal refusal, string altName, string guildName) => refusal switch
        {
            Refusal.NotInGuild => "You are not in a guild.",
            Refusal.NoInvitePrivilege => "Your guild rank may not invite members.",
            Refusal.NotFound => $"You have no character named {altName} on this account.",
            Refusal.Self => "You are already in the guild.",
            Refusal.AlreadyInThisGuild => $"{altName} is already in {guildName}.",
            Refusal.InAnotherGuild => $"{altName} is in another guild. Log in as {altName} and /gc quit first.",
            Refusal.OtherRealm => $"{altName} is of another realm. A guild takes characters of its own realm only.",
            _ => string.Empty
        };

        /// <summary>The rank a new member gets, as with /gc invite.</summary>
        public static DbGuildRank JoinRank(Guild guild) => guild?.GetRankByID(guild.IsStartingGuild ? 8 : 9);

        /// <summary>Writes the membership onto an offline character's row (the caller saves it).</summary>
        public static void SetMembership(DbCoreCharacter alt, Guild guild, DbGuildRank rank)
        {
            alt.GuildID = guild.GuildID;
            alt.GuildRank = rank?.RankLevel ?? 9;
            alt.GuildNote = string.Empty;
        }

        /// <summary>All characters of an account.</summary>
        public static IList<DbCoreCharacter> AccountCharacters(string accountName)
        {
            if (string.IsNullOrEmpty(accountName))
                return new List<DbCoreCharacter>();

            return DOLDB<DbCoreCharacter>.SelectObjects(DB.Column("AccountName").IsEqualTo(accountName));
        }

        /// <summary>
        /// Adds an offline alt: saves the row, shows it in the guild's member list and tells the online members.
        /// It takes its guild, name and rank from the row the next time it logs in (GamePlayer.HandleGuild).
        /// </summary>
        public static bool AddOffline(Guild guild, DbCoreCharacter alt, string addedBy)
        {
            if (guild == null || alt == null)
                return false;

            DbGuildRank rank = JoinRank(guild);
            string oldGuildId = alt.GuildID;
            ushort oldRank = alt.GuildRank;
            string oldNote = alt.GuildNote;
            SetMembership(alt, guild, rank);

            if (!GameServer.Database.SaveObject(alt))
            {
                alt.GuildID = oldGuildId;
                alt.GuildRank = oldRank;
                alt.GuildNote = oldNote;
                return false;
            }

            GuildMgr.AddCharacterToGuildMemberViews(guild, alt);
            guild.SendMessageToGuildMembers($"{addedBy} added {alt.Name} to the guild ({rank?.Title ?? "new member"}).", eChatType.CT_Guild, eChatLoc.CL_SystemWindow);
            return true;
        }
    }
}
