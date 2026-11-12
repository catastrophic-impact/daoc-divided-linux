namespace DOL.GS
{
    public enum BotPartyRole { Damage, Tank, Support }
    public enum BotPveGroupRole { Tank, Healer, Buffer, Attacker }

    /// <summary>Group jobs, not a test for whether a class happens to know a buff.</summary>
    public static class BotPartyRoles
    {
        public static BotPartyRole For(eCharacterClass characterClass) => characterClass switch
        {
            eCharacterClass.Cleric or
            eCharacterClass.Druid or
            eCharacterClass.Healer => BotPartyRole.Support,
            eCharacterClass.Paladin or
            eCharacterClass.Armsman or eCharacterClass.Reaver or
            eCharacterClass.Warrior or eCharacterClass.Thane or
            eCharacterClass.Hero or eCharacterClass.Champion or
            eCharacterClass.Stormforged or eCharacterClass.PendragonSovereign or
            eCharacterClass.TuathaSovereign => BotPartyRole.Tank,
            _ => BotPartyRole.Damage
        };

        /// <summary>Heavy melee that can hold a party's pulls when no real tank is available (AutonomousBotGroupCoordinator.UseOffTanks).</summary>
        public static bool CanOffTank(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Mercenary or eCharacterClass.Berserker or eCharacterClass.Savage or
            eCharacterClass.Blademaster or eCharacterClass.Valewalker;

        public static bool IsHealingClass(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Cleric or eCharacterClass.Friar or eCharacterClass.Druid or
            eCharacterClass.Warden or eCharacterClass.Healer or eCharacterClass.Shaman or
            eCharacterClass.Paladin or eCharacterClass.Valkyrie;

        public static bool CanFill(eCharacterClass characterClass, BotPveGroupRole role) => role switch
        {
            BotPveGroupRole.Tank => For(characterClass) == BotPartyRole.Tank,
            BotPveGroupRole.Healer => IsHealingClass(characterClass),
            BotPveGroupRole.Buffer => For(characterClass) == BotPartyRole.Support || IsHybridSupport(characterClass),
            BotPveGroupRole.Attacker => For(characterClass) != BotPartyRole.Support || IsHybridSupport(characterClass),
            _ => false
        };

        public static string GroupRoleLabel(BotPveGroupRole role) => role switch
        {
            BotPveGroupRole.Tank => "Tank",
            BotPveGroupRole.Healer => "Healer",
            BotPveGroupRole.Buffer => "Buffer",
            _ => "Attacker"
        };

        public static bool IsHybridSupport(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Warden or eCharacterClass.Paladin or eCharacterClass.Shaman or eCharacterClass.Friar or
            eCharacterClass.Sorcerer or eCharacterClass.Mentalist or
            eCharacterClass.Bard or eCharacterClass.Minstrel or eCharacterClass.Skald or
            eCharacterClass.Valkyrie;

        /// <summary>
        /// A support class is a non-combat role only when the party has an
        /// actual combat-capable partner.  A solo healer (or a party made up
        /// entirely of healers/supports) must still be allowed through the
        /// normal attack path so it can level instead of repeatedly trying
        /// defensive upkeep with no one to fight for it.
        /// </summary>
        public static bool IsSupport(GameBot bot) => bot?.Group?.MemberCount > 1 &&
            bot.CharacterClass != null && For((eCharacterClass)bot.CharacterClass.ID) == BotPartyRole.Support &&
            HasCombatPartner(bot);

        private static bool HasCombatPartner(GameBot bot)
        {
            Group group = bot?.Group;
            if (group == null)
                return false;

            foreach (GameLiving member in group.GetMembersInTheGroup())
            {
                if (member == null || member == bot || !member.IsAlive ||
                    member.ObjectState != GameObject.eObjectState.Active)
                    continue;

                // A real player is always a combat-capable party partner from
                // the bot's point of view, even when that player is currently
                // idle.  This preserves player-led healer/buffer behavior.
                if (member is GamePlayer)
                    return true;

                if (member is GameBot other && other.CharacterClass != null &&
                    For((eCharacterClass)other.CharacterClass.ID) != BotPartyRole.Support)
                    return true;
            }

            return false;
        }

        /// <summary>A tank that also heals (the Paladin): its job in a party is to hold the monsters, not to heal.</summary>
        public static bool IsHybridTank(eCharacterClass characterClass) =>
            For(characterClass) == BotPartyRole.Tank && IsHealingClass(characterClass);

        /// <summary>Below this a hybrid tank in a party heals before it fights (BOT-AI-2026-11-07 item 5).</summary>
        public const int HybridTankHealPercent = 50;

        /// <summary>
        /// Tank first (DR-122): in a player's party a hybrid tank melees and taunts, and heals only when a member is below
        /// <see cref="HybridTankHealPercent"/>. Every other healer, a hybrid tank on its own, and one in a bot party (whose
        /// coordinator may have given it the healer's job) heal first as before. Pure.
        /// </summary>
        public static bool HealsBeforeFighting(eCharacterClass characterClass, bool playerLedParty, int partySize, int lowestMemberHealthPercent) =>
            !IsHybridTank(characterClass) || !playerLedParty || partySize < 2 || lowestMemberHealthPercent < HybridTankHealPercent;

        public static bool IsTank(GameBot bot) => bot?.CharacterClass != null &&
            For((eCharacterClass)bot.CharacterClass.ID) == BotPartyRole.Tank;

        public static string Label(eCharacterClass characterClass) => IsHybridSupport(characterClass)
            ? (For(characterClass) == BotPartyRole.Tank ? "Tank/Healer/Buffer"
                : IsHealingClass(characterClass) ? "Attacker/Healer/Buffer" : "Attacker/Buffer")
            : For(characterClass) switch
        {
            BotPartyRole.Tank => "Tank/Attacker",
            BotPartyRole.Support => IsHealingClass(characterClass) ? "Healer/Buffer" : "Song Support/Buffer",
            _ => "Damage/Attacker"
        };
    }
}
