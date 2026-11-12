using System;
using DOL.GS.PacketHandler;

namespace DOL.GS.Spells
{
    /// <summary>
    /// "Heal all over time" (official item spell 12900, the ToA Robes of the Magus, Timeless Indigo Mail and Xanxicar
    /// Remnant Cloak): every pulse restores Spell.Value percent of maximum health, power and endurance, in combat too.
    /// It shares the heal over time effect, so it does not stack with another heal over time on the same target.
    /// </summary>
    [SpellHandler(eSpellType.HealAllOverTime)]
    public class HealAllOverTimeSpellHandler : HoTSpellHandler
    {
        public override string ShortDescription =>
            $"The target regenerates {Math.Abs(Spell.Value)}% health, endurance and power every {Spell.Frequency / 1000.0} seconds.";

        public HealAllOverTimeSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        /// <summary>The amount one pulse restores of a pool of <paramref name="max"/>: Value percent, at least 1.</summary>
        public static int PulseAmount(double percent, int max)
        {
            if (max <= 0)
                return 0;

            return Math.Max(1, (int)(Math.Abs(percent) * max / 100.0));
        }

        public override void OnDirectEffect(GameLiving target)
        {
            if (target == null || target.ObjectState != GameObject.eObjectState.Active || !target.IsAlive)
                return;

            double percent = Spell.Value * CalculateBuffDebuffEffectiveness();

            if (target.Health < target.MaxHealth)
                target.Health += PulseAmount(percent, target.MaxHealth);
            if (target.Mana < target.MaxMana)
                target.Mana += PulseAmount(percent, target.MaxMana);
            if (target.Endurance < target.MaxEndurance)
                target.Endurance += PulseAmount(percent, target.MaxEndurance);

            MessageToLiving(target, Spell.Message1, eChatType.CT_Spell);
        }
    }
}
