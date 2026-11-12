using System;
using DOL.Database;
using DOL.GS.Spells;

namespace DOL.GS.RealmAbilities
{
    /// <summary>Purchased Sovereign passives; three ranks, incremental costs 2/4/6.</summary>
    public abstract class SovereignPassive : RAPropertyEnhancer
    {
        protected SovereignPassive(DbAbility ability, int level, params eProperty[] properties)
            : base(ability, level, properties) { }
        public override int MaxLevel => 3;
        public override int CostForUpgrade(int currentLevel) => currentLevel is >= 0 and < 3 ? (currentLevel + 1) * 2 : 1000;
        public override bool CheckRequirement(GamePlayer player) => player.CharacterClass != null && PendragonRules.IsSovereign(player.CharacterClass.ID);
        protected override string ValueUnit => "%";
        protected override void SendUpdates(GameLiving target)
        {
            if (target.Health > target.MaxHealth)
                target.Health = target.MaxHealth;
            MLMod.Refresh(target);
        }
    }

    public sealed class SovereignRoyalFortitude : SovereignPassive
    {
        public SovereignRoyalFortitude(DbAbility ability, int level) : base(ability, level, eProperty.Of_Toughness) { }
        public override int GetAmountForLevel(int level) => Math.Clamp(level, 0, 3) * 3;
    }

    public sealed class SovereignStormMastery : SovereignPassive
    {
        public SovereignStormMastery(DbAbility ability, int level) : base(ability, level, eProperty.MeleeDamage, eProperty.SpellDamage) { }
        public override int GetAmountForLevel(int level) => Math.Clamp(level, 0, 3) * 2;
    }

    public sealed class SovereignCrownsResolve : SovereignPassive
    {
        public SovereignCrownsResolve(DbAbility ability, int level) : base(ability, level, MLMod.AllResists) { }
        public override int GetAmountForLevel(int level) => Math.Clamp(level, 0, 3) * 2;
    }

    /// <summary>Storm Sovereign: weapon style damage +5/10/15%.</summary>
    public sealed class SovereignThunderlord : SovereignPassive
    {
        public SovereignThunderlord(DbAbility ability, int level) : base(ability, level, eProperty.StyleDamage) { }
        public override int GetAmountForLevel(int level) => Math.Clamp(level, 0, 3) * 5;
    }

    /// <summary>Storm Sovereign: finishers refund 1/1/2 Tempest charges; charges last 30/45/60 seconds (read by StormTempest).</summary>
    public sealed class SovereignEyeOfTempest : SovereignPassive
    {
        public const string KeyName = "Sovereign_EyeOfTempest";
        public SovereignEyeOfTempest(DbAbility ability, int level) : base(ability, level, Array.Empty<eProperty>()) { }
        protected override string ValueUnit => " s";
        public override int GetAmountForLevel(int level) => level <= 0 ? 0 : 30 + 15 * Math.Min(level - 1, 2);
    }

    /// <summary>Storm Sovereign: block chance +3/6/9%; style storm shields absorb 10/20/30% more.</summary>
    public sealed class SovereignShieldwall : SovereignPassive
    {
        public const string KeyName = "Sovereign_Shieldwall";
        public SovereignShieldwall(DbAbility ability, int level) : base(ability, level, eProperty.BlockChance) { }
        public override int GetAmountForLevel(int level) => Math.Clamp(level, 0, 3) * 3;
    }

    /// <summary>Tuatha Sovereign: detonations +5/10/15% (read by TuathaMarks.Bonus).</summary>
    public sealed class TuathaMorrigansEdge : SovereignPassive
    {
        public const string KeyName = "Tuatha_MorrigansEdge";
        public TuathaMorrigansEdge(DbAbility ability, int level) : base(ability, level, Array.Empty<eProperty>()) { }
        public override int GetAmountForLevel(int level) => Math.Clamp(level, 0, 3) * 5;
    }

    /// <summary>Tuatha Sovereign: marks last 20/30/40 seconds and detonations leave 0/1/2 (read by TuathaMarks).</summary>
    public sealed class TuathaHeartOfTheCourt : SovereignPassive
    {
        public const string KeyName = "Tuatha_HeartOfTheCourt";
        public TuathaHeartOfTheCourt(DbAbility ability, int level) : base(ability, level, Array.Empty<eProperty>()) { }
        protected override string ValueUnit => " s";
        public override int GetAmountForLevel(int level) => level <= 0 ? 0 : 20 + 10 * Math.Min(level - 1, 2);
    }

    /// <summary>Pendragon Sovereign: Valor cap 6/6/7, and Valor fades out of combat every 8/12/16 seconds (read by PendragonValor).</summary>
    public sealed class PendragonCodeOfChivalry : SovereignPassive
    {
        public const string KeyName = "Pendragon_CodeOfChivalry";
        public PendragonCodeOfChivalry(DbAbility ability, int level) : base(ability, level, Array.Empty<eProperty>()) { }
        protected override string ValueUnit => " Valor";
        public override int GetAmountForLevel(int level) => level <= 0 ? 5 : level >= 3 ? 7 : 6;
    }

    /// <summary>Pendragon Sovereign: block +3/6/9%; Guard of the Crown's ward 25/50/75% stronger (read by PendragonRemakeSpells).</summary>
    public sealed class PendragonKnightsGuard : SovereignPassive
    {
        public const string KeyName = "Pendragon_KnightsGuard";
        public PendragonKnightsGuard(DbAbility ability, int level) : base(ability, level, eProperty.BlockChance) { }
        public override int GetAmountForLevel(int level) => Math.Clamp(level, 0, 3) * 3;
    }

    /// <summary>Pendragon Sovereign: Judgment heals and wards 25/50/75% stronger (read by PendragonValor.OnJudgment).</summary>
    public sealed class PendragonGrailBlessing : SovereignPassive
    {
        public const string KeyName = "Pendragon_GrailBlessing";
        public PendragonGrailBlessing(DbAbility ability, int level) : base(ability, level, Array.Empty<eProperty>()) { }
        public override int GetAmountForLevel(int level) => Math.Clamp(level, 0, 3) * 25;
    }

    /// <summary>Tuatha Sovereign: evade +3/6/9%; style thorns 10/20/30% stronger.</summary>
    public sealed class TuathaEvasionOfTheSidhe : SovereignPassive
    {
        public const string KeyName = "Tuatha_EvasionOfTheSidhe";
        public TuathaEvasionOfTheSidhe(DbAbility ability, int level) : base(ability, level, eProperty.EvadeChance) { }
        public override int GetAmountForLevel(int level) => Math.Clamp(level, 0, 3) * 3;
    }

    /// <summary>The existing Realm Abilities career automatically grants this free at RR5.</summary>
    public sealed class SovereignWrathOfTheCrown : RR5RealmAbility
    {
        public SovereignWrathOfTheCrown(DbAbility ability, int level) : base(ability, level) { }
        public override int GetReUseDelay(int level) => 300;
        public override bool CheckRequirement(GamePlayer player) =>
            player.CharacterClass != null && PendragonRules.IsSovereign(player.CharacterClass.ID) && base.CheckRequirement(player);

        public override void Execute(GameLiving living)
        {
            if (living is not GamePlayer player || !CheckRequirement(player) ||
                living.GetSkillDisabledDuration(this) > 0 || CheckPreconditions(living, DEAD | SITTING | MEZZED | STUNNED))
                return;
            Spell spell = SkillBase.GetSpellByID(StormSovereignAbilities.RR5SpellId + PendragonRules.RealmOffset(player));
            if (spell == null)
                return;
            // Starting the registered effect directly avoids adding a second spell cooldown.
            DisableSkill(living);
            living.Stealth(false);
            new StormSovereignHandler(living, spell, GlobalSpellsLines.RealmSpellsSpellLine).StartSpell(living);
        }
    }
}
