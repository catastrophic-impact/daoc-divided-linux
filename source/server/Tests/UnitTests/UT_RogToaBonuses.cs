using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Random loot rolls ToA bonuses when `atlas_rog / rog_toa_item_chance` is above 0 (GitHub report: no ToA stats on any ROG).
    /// </summary>
    [TestFixture]
    public class UT_RogToaBonuses
    {
        private static readonly eProperty[] ToaLines =
        [
            eProperty.MeleeDamage, eProperty.MeleeSpeed, eProperty.StyleDamage, eProperty.Fatigue, eProperty.OffhandDamageAndChance,
            eProperty.CastingSpeed, eProperty.SpellDamage, eProperty.SpellRange, eProperty.SpellDuration, eProperty.ResistPierce,
            eProperty.BuffEffectiveness, eProperty.DebuffEffectiveness, eProperty.HealingEffectiveness, eProperty.PowerPool,
            eProperty.ArcherySpeed, eProperty.ArcheryRange, eProperty.RangedDamage, eProperty.ArrowRecovery,
        ];

        private static readonly eProperty[] TenPercentLines =
        [
            eProperty.MeleeDamage, eProperty.MeleeSpeed, eProperty.StyleDamage, eProperty.OffhandDamageAndChance, eProperty.CastingSpeed,
            eProperty.SpellDamage, eProperty.SpellRange, eProperty.ResistPierce, eProperty.ArcherySpeed, eProperty.ArcheryRange, eProperty.RangedDamage,
        ];

        private static readonly eProperty[] MeleeLines = [eProperty.MeleeDamage, eProperty.MeleeSpeed, eProperty.StyleDamage, eProperty.Fatigue];
        private static readonly eProperty[] SpellLines =
        [
            eProperty.CastingSpeed, eProperty.SpellDamage, eProperty.SpellRange, eProperty.SpellDuration, eProperty.ResistPierce,
            eProperty.BuffEffectiveness, eProperty.DebuffEffectiveness, eProperty.PowerPool,
        ];
        private static readonly eProperty[] ArcheryLines = [eProperty.ArcherySpeed, eProperty.ArcheryRange, eProperty.RangedDamage, eProperty.ArrowRecovery];

        private readonly Dictionary<FieldInfo, object> _rogDefaults = new();

        [OneTimeSetUp]
        public void InitializeItemFactory()
        {
            foreach (FieldInfo field in typeof(Properties).GetFields(BindingFlags.Public | BindingFlags.Static)
                         .Where(field => field.Name.StartsWith("ROG_")))
            {
                _rogDefaults[field] = field.GetValue(null);
                field.SetValue(null, field.GetCustomAttribute<ServerPropertyAttribute>().DefaultValue);
            }
            var prefixes = (IDictionary)typeof(GeneratedUniqueItem).GetField("hPropertyToMagicPrefix",
                BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            if (prefixes.Count == 0) GeneratedUniqueItem.InitializeHashtables();
        }

        [OneTimeTearDown]
        public void RestoreItemDefaults()
        {
            foreach (var entry in _rogDefaults) entry.Key.SetValue(null, entry.Value);
        }

        [TearDown]
        public void ResetChance() { Properties.ROG_TOA_ITEM_CHANCE = 0; }

        private static IEnumerable<(eProperty Property, int Value)> Bonuses(GeneratedUniqueItem item)
        {
            (int type, int value)[] lines =
            [
                (item.Bonus1Type, item.Bonus1), (item.Bonus2Type, item.Bonus2), (item.Bonus3Type, item.Bonus3), (item.Bonus4Type, item.Bonus4),
                (item.Bonus5Type, item.Bonus5), (item.Bonus6Type, item.Bonus6), (item.Bonus7Type, item.Bonus7), (item.Bonus8Type, item.Bonus8),
                (item.Bonus9Type, item.Bonus9), (item.Bonus10Type, item.Bonus10), (item.ExtraBonusType, item.ExtraBonus),
            ];
            return lines.Where(l => l.type != 0 && l.value != 0).Select(l => ((eProperty)l.type, l.value));
        }

        private static List<(eProperty Property, int Value)> Roll(eRealm realm, eCharacterClass charClass, byte level, int count)
        {
            var all = new List<(eProperty, int)>();
            for (int i = 0; i < count; i++)
                all.AddRange(Bonuses(new GeneratedUniqueItem(realm, charClass, level)));
            return all;
        }

        [Test]
        public void ChanceZero_NoToaLines()
        {
            Properties.ROG_TOA_ITEM_CHANCE = 0;
            var rolled = Roll(eRealm.Albion, eCharacterClass.Wizard, 50, 1500).Concat(Roll(eRealm.Midgard, eCharacterClass.Berserker, 50, 1500));
            Assert.That(rolled.Where(b => ToaLines.Contains(b.Property) || (b.Property >= eProperty.StatCapBonus_First && b.Property <= eProperty.AcuCapBonus)),
                Is.Empty);
        }

        [Test]
        public void ChanceSet_DefaultConstructorRollsToa()
        {
            Properties.ROG_TOA_ITEM_CHANCE = 30;
            Assert.That(GeneratedUniqueItem.ToaBonusesEnabled, Is.True);
            var rolled = Roll(eRealm.Albion, eCharacterClass.Wizard, 50, 500);
            Assert.That(rolled.Count(b => ToaLines.Contains(b.Property)), Is.GreaterThan(0));
        }

        [Test]
        public void LowLevelItems_NoToa()
        {
            Properties.ROG_TOA_ITEM_CHANCE = 100;
            var rolled = Roll(eRealm.Albion, eCharacterClass.Armsman, GeneratedUniqueItem.ToaMinimumItemLevel - 1, 1000);
            Assert.That(rolled.Where(b => ToaLines.Contains(b.Property)), Is.Empty);
        }

        [Test]
        public void PureCaster_NoMeleeOrArcheryLines()
        {
            Properties.ROG_TOA_ITEM_CHANCE = 100;
            var rolled = Roll(eRealm.Albion, eCharacterClass.Wizard, 50, 1500);
            Assert.That(rolled.Where(b => MeleeLines.Contains(b.Property) || ArcheryLines.Contains(b.Property)
                                           || b.Property == eProperty.OffhandDamageAndChance || b.Property == eProperty.HealingEffectiveness), Is.Empty);
            Assert.That(rolled.Any(b => b.Property == eProperty.SpellDamage), Is.True);
        }

        [Test]
        public void NoPowerMelee_NoSpellLines()
        {
            Properties.ROG_TOA_ITEM_CHANCE = 100;
            var rolled = Roll(eRealm.Albion, eCharacterClass.Armsman, 50, 1500);
            Assert.That(rolled.Where(b => SpellLines.Contains(b.Property) || ArcheryLines.Contains(b.Property)
                                           || b.Property == eProperty.HealingEffectiveness || b.Property == eProperty.OffhandDamageAndChance), Is.Empty);
            Assert.That(rolled.Any(b => b.Property == eProperty.MeleeDamage), Is.True);
        }

        [Test]
        public void EveryToaLineCanDrop_AndAmountsStayUnderCaps()
        {
            Properties.ROG_TOA_ITEM_CHANCE = 100;
            var rolled = new List<(eProperty Property, int Value)>();
            rolled.AddRange(Roll(eRealm.Albion, eCharacterClass.Wizard, 50, 800));
            rolled.AddRange(Roll(eRealm.Albion, eCharacterClass.Cleric, 50, 800));
            rolled.AddRange(Roll(eRealm.Albion, eCharacterClass.Scout, 50, 800));
            rolled.AddRange(Roll(eRealm.Hibernia, eCharacterClass.Blademaster, 50, 800));
            rolled.AddRange(Roll(eRealm.Hibernia, eCharacterClass.Druid, 50, 800));
            rolled.AddRange(Roll(eRealm.Midgard, eCharacterClass.Hunter, 50, 800));

            var missing = ToaLines.Where(p => !rolled.Any(b => b.Property == p)).ToList();
            Assert.That(missing, Is.Empty, "ToA lines that never dropped: " + string.Join(", ", missing));

            foreach (var bonus in rolled.Where(b => ToaLines.Contains(b.Property)))
            {
                int max = TenPercentLines.Contains(bonus.Property) ? 5 : 10;
                Assert.That(bonus.Value, Is.InRange(1, max), bonus.Property.ToString());
            }
        }

        [Test]
        public void NoPowerOrAcuityCapLinesForClassesWithoutPower()
        {
            Properties.ROG_TOA_ITEM_CHANCE = 100;
            foreach ((eRealm realm, eCharacterClass charClass) in new[]
                     {
                         (eRealm.Albion, eCharacterClass.Armsman), (eRealm.Hibernia, eCharacterClass.Hero),
                         (eRealm.Midgard, eCharacterClass.Warrior), (eRealm.Midgard, eCharacterClass.Hunter),
                     })
            {
                // Hunters have power (Dexterity casters), so only their Acuity lines are out.
                bool hasPower = charClass == eCharacterClass.Hunter;
                var rolled = Roll(realm, charClass, 50, 600);
                Assert.That(rolled.Where(b => b.Property is eProperty.AcuCapBonus or eProperty.Acuity
                                              || (!hasPower && b.Property is eProperty.PowerPoolCapBonus or eProperty.PowerPool or eProperty.MaxMana)),
                    Is.Empty, charClass.ToString());
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TheUtilityFloorNeverRaisesALinePastItsCap(bool toa)
        {
            // Mob loot asks for a floor of level - 15..20. An item whose other lines are zero-utility ToA bonuses used to
            // pour the whole floor into one stat (+72 Quickness on a level 65 drop).
            Properties.ROG_TOA_ITEM_CHANCE = toa ? 100 : 0;
            foreach (byte level in new byte[] { 51, 60, 65 })
            {
                for (int i = 0; i < 700; i++)
                {
                    var item = new GeneratedUniqueItem(eRealm.Midgard, eCharacterClass.Berserker, level, level - 15);
                    foreach (var bonus in Bonuses(item))
                    {
                        bool capped = (bonus.Property >= eProperty.Stat_First && bonus.Property <= eProperty.Stat_Last)
                                      || bonus.Property is eProperty.Acuity or eProperty.MaxHealth
                                      || (bonus.Property >= eProperty.Resist_First && bonus.Property <= eProperty.Resist_Last);
                        if (!capped)
                            continue;
                        int cap = GeneratedUniqueItem.LineCap((int)bonus.Property, level, 1.5);
                        Assert.That(bonus.Value, Is.LessThanOrEqualTo(cap), $"{bonus.Property} on a level {level} item");
                    }
                }
            }
        }
    }
}
