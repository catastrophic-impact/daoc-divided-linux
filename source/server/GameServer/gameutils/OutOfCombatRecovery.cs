using System;
using DOL.AI.Brain;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    public static class OutOfCombatRecovery
    {
        /// <summary>
        /// Outside combat a player gets at least the refill floor (<c>rest_*_seconds</c>). Issue #10 (owner 2026-11-06): the floor
        /// used to replace the computed rate, so items, mythirian regen, Champion passives, buffs and the
        /// <c>*_regen_amount_modifier</c> settings did nothing outside combat. Now <paramref name="bonus"/> (every source on top of
        /// the level base, debuffs taken off) is added to the floor and the sum is scaled by <paramref name="modifier"/>.
        /// </summary>
        public static int Amount(GameLiving living, eProperty resource, int native, double bonus = 0, double modifier = 1)
        {
            if (!Properties.FASTER_OUT_OF_COMBAT_RECOVERY || living is not GamePlayer ||
                !living.IsAlive || living.InCombat || living.IsAttacking || living.IsCasting || living.IsCrowdControlled ||
                PetFighting(living.ControlledBrain?.Body))
                return native;

            int seconds, maximum, interval;
            bool resting = living.IsSitting;
            switch (resource)
            {
                case eProperty.HealthRegenerationAmount:
                    if (native <= 0) return native; // Disease and bleeding remain authoritative.
                    seconds = Properties.REST_HEALTH_SECONDS; maximum = living.MaxHealth;
                    interval = ClassicRestRegeneration.HealthAndPowerInterval(living.IsSitting, false);
                    break;
                case eProperty.PowerRegenerationAmount:
                    if (living is GamePlayer player && (eCharacterClass?)player.CharacterClass?.ID is
                        eCharacterClass.Vampiir or eCharacterClass.MaulerAlb or eCharacterClass.MaulerMid or eCharacterClass.MaulerHib)
                        return native;
                    seconds = Properties.REST_POWER_SECONDS; maximum = living.MaxMana;
                    interval = ClassicRestRegeneration.HealthAndPowerInterval(living.IsSitting, false);
                    break;
                case eProperty.EnduranceRegenerationAmount:
                    if (living.IsMoving) return native;
                    seconds = Properties.REST_ENDURANCE_SECONDS; maximum = living.MaxEndurance; interval = 1000;
                    break;
                default: return native;
            }
            return Floor(native, maximum, interval, seconds, resting, bonus, modifier);
        }

        private static bool PetFighting(GameNPC pet)
        {
            if (pet == null || !pet.IsAlive) return false;
            if (pet.InCombat || pet.IsAttacking || pet.IsCasting && pet.castingComponent?.SpellHandler?.Spell?.IsHarmful == true ||
                pet.Brain is StandardMobBrain { HasAggro: true }) return true;
            if (pet.ControlledNpcList != null)
                foreach (IControlledBrain child in pet.ControlledNpcList)
                    if (child?.Body is GameNPC sub && (sub.InCombat || sub.IsAttacking)) return true;
            return false;
        }

        public static int Floor(int native, int maximum, int intervalMs, int seconds, bool sitting, double bonus = 0, double modifier = 1)
        {
            if (seconds <= 0 || maximum <= 0) return native;
            double stance = sitting ? Math.Max(1, Properties.REST_SITTING_MULTIPLIER) : 1;
            double floor = maximum * (double)intervalMs / (seconds * 1000.0) * stance;
            double stacked = Math.Max(0, (floor + bonus) * Math.Max(0, modifier));
            return Math.Max(native, (int)Math.Min(int.MaxValue, Math.Ceiling(stacked)));
        }
    }
}
