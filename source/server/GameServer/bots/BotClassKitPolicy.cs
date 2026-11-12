using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.Spells;

namespace DOL.GS
{
    /// <summary>
    /// Bot rules for the spells of the expansion, live and royal classes (the modern client test kits).
    /// Some of their handlers need a real player's client or ground target, and their maintained stances,
    /// chants and summons use spell types the stock bot buff selector does not know.
    /// </summary>
    public static class BotClassKitPolicy
    {
        // Sovereign ability keys (StormSovereignAbilities.Make) a bot keeps up: the timed group buffs
        // (speed, absorption, to-hit and damage). The self heal is used below half health.
        // Storm remake (StormSovereignAbilities.AbilityKey): Royal Decree 100, Rally to the Crown 101, Hunter's Mark 104.
        private static readonly HashSet<int> SovereignGroupBuffs = [7, 8, 9, 100];
        private static readonly HashSet<int> SovereignSelfHeals = [10, 101];
        private static readonly HashSet<int> SovereignStealthOpeners = [17, 104];

        /// <summary>False for a spell whose handler is meant for a player at the keyboard.</summary>
        public static bool IsUsableByBot(Spell spell)
        {
            if (spell == null)
                return false;

            switch (spell.SpellType)
            {
                // Warlock chambers cast the caster to GamePlayer, and primers only prepare chamber combinations.
                case eSpellType.Chamber:
                case eSpellType.WarlockChamber:
                case eSpellType.WarlockPrimer:
                // Death Knight: a conjured weapon goes to a player's backpack, ground spells need a ground
                // target, and Revert Form only undoes a form the bot chose.
                case eSpellType.DeathKnightConjure:
                case eSpellType.DeathKnightGround:
                case eSpellType.DeathKnightRevert:
                    return false;

                case eSpellType.DeathKnightForm:
                    return IsDefaultDeathKnightForm(spell);

                case eSpellType.StormSovereignAbility:
                    // Toggles would switch themselves off on every recast, and the stealth opener needs stealth.
                    return !StormSovereignAbilities.Toggle(spell.ID) &&
                        !(StormSovereignAbilities.IsRankedAbility(spell.ID) && SovereignStealthOpeners.Contains(StormSovereignAbilities.AbilityKey(spell.ID)));
            }

            return true;
        }

        public static List<Spell> UsableByBot(List<Spell> spells) =>
            spells?.Where(IsUsableByBot).ToList();

        /// <summary>The Decrepit form keeps the servant and spell casting; Chthonic dismisses the servant.</summary>
        public static bool IsDefaultDeathKnightForm(Spell spell) =>
            spell.SpellType == eSpellType.DeathKnightForm && (DeathKnightForm)(int)spell.Value == DeathKnightForm.Decrepit;

        /// <summary>True for the kit spells this policy decides upkeep for.</summary>
        public static bool Handles(Spell spell) => spell?.SpellType switch
        {
            eSpellType.StormforgedChant or eSpellType.StormforgedWard or eSpellType.StormSovereignWolf or
                eSpellType.MaulerStance or eSpellType.DeathKnightForm or eSpellType.DeathKnightServant or
                eSpellType.DeathKnightRegenerate or eSpellType.PowerTransfer or eSpellType.BothAblativeArmor => true,
            eSpellType.StormSovereignAbility => !spell.IsHarmful,
            eSpellType.MaulerGift => ModernMauler.IsNethuniGift(spell),
            _ => false
        };

        /// <summary>Kit spells that belong in the middle of a fight, including cast-time ones.</summary>
        public static bool IsCombatSupport(Spell spell) => spell?.SpellType is
            eSpellType.DeathKnightRegenerate or eSpellType.PowerTransfer or eSpellType.BothAblativeArmor ||
            spell?.SpellType == eSpellType.StormSovereignAbility && !spell.IsHarmful || ModernMauler.IsNethuniGift(spell);

        /// <summary>
        /// A Sovereign's Decree lasts 10 seconds on a 30 second timer, so one spent walking to a pull is gone by
        /// contact. Three of them also took the three turns of the aggro window, and the pull expired untouched
        /// (2,608 Stormforged pull timeouts in the 2026-11-01 overnight run).
        /// </summary>
        public static bool WaitsForContact(Spell spell, bool inCombat) => !inCombat &&
            spell?.SpellType == eSpellType.StormSovereignAbility && StormSovereignAbilities.IsRankedAbility(spell.ID) &&
            SovereignGroupBuffs.Contains(StormSovereignAbilities.AbilityKey(spell.ID));

        /// <summary>A cast-time kit spell owns the turn. An instant one goes out and the bot still attacks.</summary>
        public static bool OwnsTurn(Spell spell) => spell?.IsInstantCast == false;

        /// <summary>Bonedancer commands: the commander must be alive and within its order's range of the target.</summary>
        public static bool CommanderCanAct(GameBot bot, Spell command, GameLiving target)
        {
            if (bot?.ControlledBrain?.Body is not CommanderPet { IsAlive: true } commander || target is not { IsAlive: true })
                return false;
            Spell order = SkillBase.GetSpellByID(command.SubSpellID);
            return order != null && (order.Range <= 0 || commander.IsWithinRadius(target, order.Range));
        }

        /// <summary>Regenerate Flesh: the Death Knight's own heal over time, below this health.</summary>
        public const int RegenerateBelowHealthPercent = 70;
        /// <summary>Gift of Nethuni: the Mauler's own healing gift, below this health.</summary>
        public const int NethuniBelowHealthPercent = 60;
        /// <summary>Gift of Power: give power only while above this much, to a member below the next value.</summary>
        public const int PowerGiftDonorPercent = 50, PowerGiftRecipientPercent = 40;
        /// <summary>Enchanted Barrier: shield a fighting member below this health.</summary>
        public const int BarrierBelowHealthPercent = 90;

        /// <summary>
        /// Who the bot should cast this kit spell on now, or null to skip it. All of them are self-cast:
        /// the group buffs fan out from the caster.
        /// </summary>
        public static GameLiving UpkeepTarget(GameBot bot, Spell spell)
        {
            if (bot == null || spell == null || !IsUsableByBot(spell) || !IsBestRank(bot, spell))
                return null;

            switch (spell.SpellType)
            {
                case eSpellType.StormSovereignWolf:
                case eSpellType.DeathKnightServant:
                    return bot.ControlledBrain == null ? bot : null;

                case eSpellType.StormforgedChant:
                case eSpellType.StormforgedWard:
                case eSpellType.DeathKnightForm:
                    return HasOwnEffect(bot, spell, sameType: true) ? null : bot;

                case eSpellType.DeathKnightRegenerate:
                    return bot.HealthPercent < RegenerateBelowHealthPercent && !HasOwnEffect(bot, spell, sameType: true) ? bot : null;

                case eSpellType.PowerTransfer:
                    if (!DeathKnight.Is(bot) || bot.ManaPercent < PowerGiftDonorPercent)
                        return null;
                    return GroupMembers(bot, spell)
                        .Where(member => member != bot && member.MaxMana > 0 && member.ManaPercent < PowerGiftRecipientPercent)
                        .OrderBy(member => member.ManaPercent).FirstOrDefault();

                case eSpellType.BothAblativeArmor:
                    // A 15-second barrier is only worth its cast on someone actually being hit.
                    return GroupMembers(bot, spell)
                        .Where(member => member.InCombat && member.HealthPercent < BarrierBelowHealthPercent &&
                            !member.effectListComponent.GetSpellEffects().Any(effect => !effect.IsEnding &&
                                effect.SpellHandler?.Spell?.SpellType == eSpellType.BothAblativeArmor))
                        .OrderBy(member => member.HealthPercent).FirstOrDefault();

                case eSpellType.MaulerStance:
                    // The two auras replace each other; keep the first one learned instead of alternating.
                    if (HasOwnEffect(bot, spell, sameType: true))
                        return null;
                    return bot.Spells.Where(s => s?.SpellType == eSpellType.MaulerStance).Min(s => s.ID) == spell.ID ? bot : null;

                case eSpellType.MaulerGift:
                    return bot.HealthPercent < NethuniBelowHealthPercent && ModernMauler.GiftReady(bot, spell) ? bot : null;

                case eSpellType.StormSovereignAbility:
                    if (!StormSovereignAbilities.IsRankedAbility(spell.ID))
                        return null;
                    int key = StormSovereignAbilities.AbilityKey(spell.ID);
                    if (SovereignSelfHeals.Contains(key))
                        return bot.HealthPercent < 50 ? bot : null;
                    if (SovereignGroupBuffs.Contains(key))
                        return HasSovereignEffect(bot, key) ? null : bot;
                    return null;
            }

            return null;
        }

        /// <summary>The bot and its living group members within the spell's range.</summary>
        private static IEnumerable<GameLiving> GroupMembers(GameBot bot, Spell spell)
        {
            IEnumerable<GameLiving> members = bot.Group?.GetMembersInTheGroup() ?? (IEnumerable<GameLiving>)[bot];
            int range = Math.Max(1, spell.Range);
            return members.Where(member => member is { IsAlive: true } && member.ObjectState == GameObject.eObjectState.Active &&
                (member == bot || member.CurrentRegion == bot.CurrentRegion && bot.IsWithinRadius(member, range)));
        }

        /// <summary>Hybrids know every rank of a line; only the highest learned rank is maintained.</summary>
        private static bool IsBestRank(GameBot bot, Spell spell)
        {
            bool sovereign = spell.SpellType == eSpellType.StormSovereignAbility;
            int key = sovereign ? StormSovereignAbilities.AbilityKey(spell.ID) : 0;
            return !bot.Spells.Any(other => other != null && other.ID != spell.ID && other.SpellType == spell.SpellType &&
                other.Level <= bot.Level && other.Level > spell.Level &&
                (!sovereign || StormSovereignAbilities.AbilityKey(other.ID) == key) &&
                (spell.SpellType != eSpellType.DeathKnightForm || IsDefaultDeathKnightForm(other)) &&
                (spell.SpellType != eSpellType.MaulerGift || other.SharedTimerGroup == spell.SharedTimerGroup));
        }

        private static bool HasOwnEffect(GameBot bot, Spell spell, bool sameType) =>
            bot.effectListComponent.GetSpellEffects().Any(effect =>
                effect is { IsEnding: false } && effect.SpellHandler?.Spell is Spell active &&
                (sameType ? active.SpellType == spell.SpellType : active.ID == spell.ID));

        private static bool HasSovereignEffect(GameBot bot, int key) =>
            bot.effectListComponent.GetSpellEffects().Any(effect =>
                effect is { IsEnding: false } && effect.SpellHandler?.Spell is Spell active &&
                StormSovereignAbilities.Owns(active.ID) && StormSovereignAbilities.AbilityKey(active.ID) == key);
    }
}
