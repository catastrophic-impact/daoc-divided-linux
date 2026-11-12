using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.Spells;

namespace DOL.GS
{
    /// <summary>
    /// The live archery shots a Scout, Hunter or Ranger bot fires in place of the classic bow attack
    /// (owner 2026-11-01). The classic bow stays as the fallback for a bot with no shot it can use.
    /// </summary>
    public static class BotArcheryShots
    {
        // Paid shots stop here, so the bot still has endurance to run or fight in melee. Standard Shots are free.
        public const int EnduranceReservePercent = 25;

        /// <summary>
        /// Left out: Critical Shot needs stealth, which a bot drops on engaging. Volley needs a ground target.
        /// Rapid Fire and Acid Shot splash, which would pull adds and break crowd control.
        /// </summary>
        public static bool IsBotShot(Spell spell) => spell != null &&
            spell.SpellType == eSpellType.Archery && spell.Target == eSpellTarget.ENEMY && spell.Radius == 0 &&
            spell.LifeDrainReturn != (int)Archery.eShotType.Critical;

        public static bool IsSoloShot(Spell spell) => spell.Name.Contains("Solo Shot");

        /// <summary>How long an archer that was hit in melee fights in melee before it tries its bow again.</summary>
        public const long MeleePressureFallbackMilliseconds = 6_000;

        /// <summary>
        /// A shot is not started while a monster beside the archer is interrupting it: night playtest 2026-11-04, Power Shot
        /// was interrupted 26 times of 36 and a Hunter started a draw four times in nine seconds with an ant on it. The bot
        /// fights in melee instead (BotBrain.RequestArcherMeleeFallback). Pure.
        /// </summary>
        public static bool HoldShotUnderMeleePressure(bool beingInterruptedByOther, bool attackerInMeleeRange) =>
            beingInterruptedByOther && attackerInMeleeRange;

        private static double DamagePerSecond(Spell spell) => spell.Damage / Math.Max(1, spell.CastTime);

        /// <summary>
        /// Point Blank and Power Shot whenever they are ready, otherwise the hardest hitting shot for its cast time.
        /// Long Shot is weak for its cast, so that rule only picks it beyond every other shot's range.
        /// </summary>
        public static Spell Choose(IEnumerable<Spell> shots, int distance, bool grouped, int endurancePercent,
            Func<Spell, int> range, Func<Spell, bool> ready)
        {
            List<Spell> usable = shots.Where(shot => distance <= range(shot) && ready(shot) && !(grouped && IsSoloShot(shot)) &&
                (shot.Power == 0 || endurancePercent >= EnduranceReservePercent)).ToList();
            return Strongest(usable, Archery.eShotType.PointBlank) ?? Strongest(usable, Archery.eShotType.Power) ??
                usable.OrderByDescending(DamagePerSecond).FirstOrDefault();
        }

        private static Spell Strongest(IEnumerable<Spell> shots, Archery.eShotType type) =>
            shots.Where(shot => shot.LifeDrainReturn == (int)type).OrderByDescending(shot => shot.Level).FirstOrDefault();
    }
}
