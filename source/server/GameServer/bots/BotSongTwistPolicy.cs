using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>One native song source at a time under 1.65 rules (several under live chant rules); never extend child effects or skip recast timers.</summary>
    public static class BotSongTwistPolicy
    {
        public readonly record struct Song(int Id, int CastMilliseconds, int CooldownMilliseconds, long RemainingMilliseconds);

        public static bool IsManagedSong(GameLiving living, Spell spell)
        {
            if (living is not GameBot bot || bot.CharacterClass == null ||
                spell == null || !spell.IsPulsing || spell.IsHarmful || spell.IsFocus)
                return false;
            eCharacterClass characterClass = (eCharacterClass)bot.CharacterClass.ID;
            if (BotBrain.IsClassicSongClass(characterClass))
                return BotBrain.IsMaintainableClassBuff(spell);
            // Tank chants use a separate selector: never send Paladin/Warden
            // through the performer code that drops songs for solo melee.
            return spell.IsInstantCast && !spell.NeedInstrument && (characterClass switch
            {
                eCharacterClass.Paladin => spell.SpellType is eSpellType.EnduranceRegenBuff or
                    eSpellType.SpecArmorFactorBuff or eSpellType.DamageAdd or eSpellType.CombatHeal,
                eCharacterClass.Warden => spell.SpellType is eSpellType.SpeedEnhancement or
                    eSpellType.Bladeturn or eSpellType.DamageAdd,
                _ => false
            });
        }

        /// <summary>The one chant a solo Paladin holds: damage in a fight, the combat heal when hurt, endurance otherwise.</summary>
        public static eSpellType SoloPaladinAnchor(bool fighting, int healthPercent) =>
            !fighting ? eSpellType.EnduranceRegenBuff : healthPercent < 70 ? eSpellType.CombatHeal : eSpellType.DamageAdd;

        public static bool IsMobileSong(GameLiving living, Spell spell)
        {
            return IsManagedSong(living, spell) &&
                (spell.IsInstantCast || spell.NeedInstrument || spell.MoveCast);
        }

        // Only temporary player companions use this travel pool. Bards have
        // a real endurance song; Skalds and Minstrels do not, so their useful
        // second travel song is health regeneration. Power and combat chants
        // must not take the speed anchor while the owner is moving.
        public static bool IsCompanionTravelSong(eCharacterClass characterClass, eSpellType type) =>
            characterClass switch
            {
                eCharacterClass.Bard => type is eSpellType.SpeedEnhancement or eSpellType.EnduranceRegenBuff,
                eCharacterClass.Skald or eCharacterClass.Minstrel =>
                    type is eSpellType.SpeedEnhancement or eSpellType.HealthRegenBuff,
                _ => false
            };

        // Only one helpful pulse source is legal. Generic buff selection must
        // not independently replace a tank's anchor with a resist/endurance
        // pulse; those are deliberately not part of the chosen core rotation.
        public static bool IsReservedPulse(GameLiving living, Spell spell) =>
            IsManagedSong(living, spell) ||
            living is GameBot bot && bot.CharacterClass != null &&
            (eCharacterClass)bot.CharacterClass.ID is eCharacterClass.Paladin or eCharacterClass.Warden &&
            spell != null && spell.IsPulsing && !spell.IsHarmful && !spell.IsFocus &&
            BotBrain.IsMaintainableClassBuff(spell);

        public static bool HasMobileSongCast(GameLiving living)
        {
            if (living is not GameBot bot || bot.CharacterClass == null)
                return false;

            CastingComponent casting = living.castingComponent;
            if (casting == null) return false;
            Spell active = casting.SpellHandler?.Spell;
            Spell queued = casting.QueuedSpellHandler?.Spell;
            bool pending = casting.TryPeekPendingSpell(out Spell requested);
            // Never use a song to authorize movement during an ordinary heal,
            // hostile spell, or unknown ability request queued alongside it.
            return (active != null || queued != null || pending) &&
                (active == null || IsMobileSong(living, active)) &&
                (queued == null || IsMobileSong(living, queued)) &&
                (!pending || IsMobileSong(living, requested));
        }

        public static eSpellType WardenAnchor(bool traveling, bool combat, bool grouped, bool hasBladeturn)
        {
            if (traveling && !combat) return eSpellType.SpeedEnhancement;
            if (hasBladeturn && (combat || grouped)) return eSpellType.Bladeturn;
            return eSpellType.DamageAdd;
        }

        /// <summary>
        /// Under live chant rules (allow_multiple_chants) every chant can stay up, so a selector skips one
        /// whose type is already running at this rank or higher instead of twisting back to it,
        /// and skips every new chant once the caster is at its pulse cap.
        /// Under 1.65 rules the one-song rotation is unchanged.
        /// </summary>
        public static bool KeepsRunning(GameLiving living, Spell spell) =>
            ServerProperties.Properties.ALLOW_MULTIPLE_CHANTS && spell != null && (IsPulseCapped(living, spell) ||
            KeepsSameChant(living, spell));

        // At the live pulse cap a new chant can't start, so selectors skip it instead of retrying.
        private static bool IsPulseCapped(GameLiving living, Spell spell) =>
            living is GameBot { CharacterClass: not null } bot && !EffectListComponent.CanStartPulse(spell,
                bot.effectListComponent.GetPulseEffects().Where(effect => !effect.IsEnding && !effect.IsEnded &&
                    effect.SpellHandler?.Spell != null).Select(effect => effect.SpellHandler.Spell),
                EffectListComponent.MaxPulses(bot.CharacterClass.ID));

        private static bool KeepsSameChant(GameLiving living, Spell spell) =>
            living?.effectListComponent.GetPulseEffects().Any(effect => !effect.IsEnding && !effect.IsEnded &&
                effect.SpellHandler?.Spell is Spell running && KeepsRunning(running, spell)) == true;

        public static bool KeepsRunning(Spell running, Spell candidate) =>
            running.SpellType == candidate.SpellType && (running.ID == candidate.ID || running.Level >= candidate.Level);

        // ---- Holding one song like a player (owner 2026-10-30: "the twisting ... is too much sound diarrhea")
        // Performers no longer twist a second song in and out; each situation has one song, held until the situation
        // changes, and never changed sooner than HoldMilliseconds after the last change. Normal and True 1.65 Mode.
        public const int HoldMilliseconds = 10_000;
        public const int TravelAfterMovingMilliseconds = 3_000;  // short steps (a pull, a reposition) are not travel
        public const int StayTravelingMilliseconds = 5_000;      // short stops keep the speed song

        /// <summary>
        /// The one song a Bard, Minstrel or Skald holds: speed while travelling; in a fight endurance (then power, then
        /// health); standing around, whatever the group is short of (power, health, endurance), else the song already
        /// playing. Null when none of the wanted songs is known.
        /// </summary>
        public static eSpellType? PerformerHold(bool traveling, bool combat, bool powerLow, bool healthLow, bool enduranceLow,
            ICollection<eSpellType> available, eSpellType? active)
        {
            if (traveling && !combat && available.Contains(eSpellType.SpeedEnhancement))
                return eSpellType.SpeedEnhancement;
            var wanted = new List<eSpellType>();
            if (combat)
                wanted.AddRange([eSpellType.EnduranceRegenBuff, eSpellType.PowerRegenBuff, eSpellType.HealthRegenBuff]);
            else
            {
                if (powerLow) wanted.Add(eSpellType.PowerRegenBuff);
                if (healthLow) wanted.Add(eSpellType.HealthRegenBuff);
                if (enduranceLow) wanted.Add(eSpellType.EnduranceRegenBuff);
                // nobody short of anything: keep the song already playing (any but speed, which is for travel)
                if (wanted.Count == 0 && active is { } current && current != eSpellType.SpeedEnhancement && available.Contains(current))
                    return current;
                wanted.AddRange([eSpellType.PowerRegenBuff, eSpellType.HealthRegenBuff, eSpellType.EnduranceRegenBuff]);
            }
            foreach (eSpellType type in wanted)
                if (available.Contains(type))
                    return type;
            // owner 2026-10-30: a bot (gamebot or companion) that hasn't learned speed or endurance yet uses what it has
            // until then: any other song it knows, speed last.
            return available.Where(type => type != eSpellType.SpeedEnhancement).OrderBy(type => type).Cast<eSpellType?>().FirstOrDefault()
                ?? (available.Contains(eSpellType.SpeedEnhancement) ? eSpellType.SpeedEnhancement : null);
        }

        public const int SkaldRestChantHealthPercent = 50;

        /// <summary>
        /// The chant a Skald holds in a fight. Night playtest 2026-11-04: Skalds died at 92% power with their chants unused,
        /// because every performer stopped its song in combat, and the one-song hold swapped between travel and regen out of
        /// it. A Skald fights with its damage add (Chant of the Brawl and up) going, or its Song of Rest when someone in the
        /// group is below half health; travel speed never in a fight.
        /// </summary>
        public static eSpellType? SkaldCombatHold(ICollection<eSpellType> available, int lowestHealthPercent)
        {
            if (lowestHealthPercent < SkaldRestChantHealthPercent && available.Contains(eSpellType.HealthRegenBuff))
                return eSpellType.HealthRegenBuff;
            if (available.Contains(eSpellType.DamageAdd))
                return eSpellType.DamageAdd;
            return available.Contains(eSpellType.HealthRegenBuff) ? eSpellType.HealthRegenBuff : null;
        }

        /// <summary>The one chant a Paladin holds (owner 2026-10-30: endurance in a fight), grouped or solo.</summary>
        public static eSpellType PaladinHold(ICollection<eSpellType> available) =>
            new[] { eSpellType.EnduranceRegenBuff, eSpellType.DamageAdd, eSpellType.SpecArmorFactorBuff, eSpellType.CombatHeal }
                .FirstOrDefault(available.Contains, eSpellType.EnduranceRegenBuff);

        public static int Choose(int primaryId, int activeId, IReadOnlyList<Song> songs)
        {
            Song primary = songs.FirstOrDefault(song => song.Id == primaryId);
            if (primary.Id == 0) return 0;
            // Return to the anchor immediately after the secondary takes. In
            // particular Skald must not leave speed while its 8s reuse is up.
            if (activeId != primaryId)
                return primary.CooldownMilliseconds <= 0 ? primaryId : 0;
            if (primary.CooldownMilliseconds > 0) return 0;

            foreach (Song secondary in songs.Where(song => song.Id != primaryId && song.CooldownMilliseconds <= 0)
                         .OrderBy(song => song.RemainingMilliseconds))
            {
                int roundTrip = Math.Max(0, secondary.CastMilliseconds) + Math.Max(0, primary.CastMilliseconds) + 1000;
                if (primary.RemainingMilliseconds > roundTrip && secondary.RemainingMilliseconds <= roundTrip)
                    return secondary.Id;
            }
            return 0;
        }
    }
}
