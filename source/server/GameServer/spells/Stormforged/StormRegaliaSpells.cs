using System.Collections.Generic;

namespace DOL.GS.Spells
{
    /// <summary>
    /// The Storm Regalia's item abilities (spell IDs 63740-63743, type StormSovereignAbility): two use abilities and two
    /// armor procs. Armor procs start on the attacker, so the ward targets the wearer itself.
    /// </summary>
    public static class StormRegaliaSpells
    {
        public const int CrownOfTempest = 63740;   // helm, use: fill Tempest
        public const int Wolfmantle = 63741;       // cloak, use: absorption and haste
        public const int StormguardWard = 63742;   // hauberk, struck: storm shield on the wearer
        public const int ThunderRebuke = 63743;    // sleeves, struck: lightning at the attacker

        public static bool Owns(int id) => id is >= CrownOfTempest and <= ThunderRebuke;

        public static IEnumerable<(int Id, MLAbility Ability)> Build()
        {
            yield return (CrownOfTempest, new MLAbility
            {
                Path = "StormSovereign", Level = 50, Positive = true, KeepsStealth = true, Describe = s => s.Description,
                Run = (h, _) => { StormTempest.Set(h.Caster, StormTempest.MaxCharges); h.Animate(h.Caster); }
            });
            yield return (Wolfmantle, new MLAbility
            {
                Path = "StormSovereign", Level = 50, Positive = true, KeepsStealth = true, Describe = s => s.Description,
                Mods = new[] { MLMod.Buff(eProperty.ArmorAbsorption, 20), MLMod.Buff(eProperty.MeleeSpeed, 20) },
                Run = (h, _) => { h.Hold(h.Caster); h.Animate(h.Caster); }
            });
            yield return (StormguardWard, new MLAbility
            {
                Path = "StormSovereign", Level = 50, Positive = true, KeepsStealth = true, Describe = s => s.Description,
                Run = (h, _) => h.Cast(h.Caster, 1, eSpellType.BothAblativeArmor, db =>
                {
                    db.Target = "Self"; db.Duration = 10; db.Damage = 50; db.Value = 200;
                })
            });
            yield return (ThunderRebuke, new MLAbility
            {
                Path = "StormSovereign", Level = 50, Describe = s => s.Description,
                Run = (h, t) => StormSovereignAbilities.Strike(h, t, 150)
            });
        }
    }

    /// <summary>The Regalia of Tara's item abilities (spell IDs 63750-63753): two use abilities and two armor procs.</summary>
    public static class TaraRegaliaSpells
    {
        public const int EyesOfTheSidhe = 63750;   // helm, use: every enemy within 1,000 gets 2 marks
        public const int MantleOfMists = 63751;    // cloak, use: evade and speed
        public const int Briarward = 63752;        // hauberk, struck: ward on the wearer
        public const int BrambleSnare = 63753;     // sleeves, struck: the attacker is rooted and marked

        public static bool Owns(int id) => id is >= EyesOfTheSidhe and <= BrambleSnare;

        public static IEnumerable<(int Id, MLAbility Ability)> Build()
        {
            yield return (EyesOfTheSidhe, new MLAbility
            {
                Path = "TuathaSovereign", Level = 50, Positive = true, KeepsStealth = true, Describe = s => s.Description,
                Run = (h, _) => { foreach (GameLiving enemy in h.Enemies(h.Caster, 1000)) TuathaMarks.Add(h.Caster, enemy, 2); }
            });
            yield return (MantleOfMists, new MLAbility
            {
                Path = "TuathaSovereign", Level = 50, Positive = true, KeepsStealth = true, Describe = s => s.Description,
                Mods = new[] { new MLMod(eProperty.EvadeChance, MLCategory.Ability, 30), MLMod.Speed(1.3) },
                Run = (h, _) => { h.Hold(h.Caster); h.Animate(h.Caster); }
            });
            yield return (Briarward, new MLAbility
            {
                Path = "TuathaSovereign", Level = 50, Positive = true, KeepsStealth = true, Describe = s => s.Description,
                Run = (h, _) => h.Cast(h.Caster, 1, eSpellType.BothAblativeArmor, db => { db.Target = "Self"; db.Duration = 10; db.Damage = 50; db.Value = 200; })
            });
            yield return (BrambleSnare, new MLAbility
            {
                Path = "TuathaSovereign", Level = 50, Describe = s => s.Description,
                Run = (h, t) => { h.Cast(t, 1, eSpellType.SpeedDecrease, db => { db.Value = 99; db.Duration = 6; }); TuathaMarks.Add(h.Caster, t, 1); }
            });
        }
    }

    /// <summary>The Regalia of Camelot's item abilities (spell IDs 63760-63763): two use abilities and two armor procs.</summary>
    public static class CamelotRegaliaSpells
    {
        public const int CrownOfCamelot = 63760;    // helm, use: fills your Valor
        public const int MercyOfAvalon = 63761;     // cloak, use: heals your party 20%
        public const int HolyBulwark = 63762;       // hauberk, struck: ward on the wearer, +1 Valor
        public const int SearingRebuke = 63763;     // sleeves, struck: the attacker burns and is taunted

        public static bool Owns(int id) => id is >= CrownOfCamelot and <= SearingRebuke;

        public static IEnumerable<(int Id, MLAbility Ability)> Build()
        {
            yield return (CrownOfCamelot, new MLAbility
            {
                Path = "PendragonSovereign", Level = 50, Positive = true, KeepsStealth = true, Describe = s => s.Description,
                Run = (h, _) => { PendragonValor.Set(h.Caster, PendragonValor.Max(h.Caster)); h.Animate(h.Caster); }
            });
            yield return (MercyOfAvalon, new MLAbility
            {
                Path = "PendragonSovereign", Level = 50, Positive = true, KeepsStealth = true, Describe = s => s.Description,
                Run = (h, _) => { foreach (GameLiving member in h.Group(1500)) h.Heal(member, member.MaxHealth / 5); h.Animate(h.Caster); }
            });
            yield return (HolyBulwark, new MLAbility
            {
                Path = "PendragonSovereign", Level = 50, Positive = true, KeepsStealth = true, Describe = s => s.Description,
                Run = (h, _) =>
                {
                    h.Cast(h.Caster, 1, eSpellType.BothAblativeArmor, db => { db.Target = "Self"; db.Duration = 10; db.Damage = 50; db.Value = 200; });
                    PendragonValor.Add(h.Caster, 1);
                }
            });
            yield return (SearingRebuke, new MLAbility
            {
                Path = "PendragonSovereign", Level = 50, Describe = s => s.Description,
                Run = (h, t) => { PendragonRemakeStyles.Burn(h, t); h.Cast(t, 2, eSpellType.Taunt, db => db.Value = 400 + h.Caster.Level * 10); }
            });
        }
    }
}
