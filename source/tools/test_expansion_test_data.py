"""Exercise the Bainshee/Vampiir/Heretic migration on a private in-memory copy of the current test world."""
from contextlib import closing
import re
import sqlite3
import unittest
from install_expansion_test import REPO, progress
from expansion_test_data import HYBRID, KITS, apply, definitions, validate_icons

BUILT = (39, 58, 33, 66)
KIT_SPELLS = "PackageID in ('local-bainshee-test-v1','local-vampiir-test-v1','local-heretic-test-v1','local-deathknight-test-v1')"


class ExpansionDataTests(unittest.TestCase):
    def setUp(self):
        self.root = REPO / 'artifacts/modern-client-test'
        if not (self.root / 'runtime/data/opendaoc.sqlite3.db').exists():
            self.skipTest('needs the modern test install beside this checkout')
        self.db = sqlite3.connect(':memory:')
        self.db.row_factory = sqlite3.Row
        with closing(sqlite3.connect((self.root / 'runtime/data/opendaoc.sqlite3.db').as_uri() + '?mode=ro', uri=True)) as src:
            src.backup(self.db)

    def tearDown(self):
        self.db.close()

    def test_idempotent_preserves_saves_and_other_spells(self):
        before = progress(self.db)
        stock = [tuple(r) for r in self.db.execute("select * from Spell where coalesce(PackageID,'') not like 'local-%-test-v1' order by SpellID")]
        apply(self.db, BUILT)
        snapshot = lambda: ([tuple(r) for r in self.db.execute(f'select * from Spell where {KIT_SPELLS} order by SpellID')],
                            [tuple(r) for r in self.db.execute('select * from Style where ClassId in (58,33) order by StyleID')],
                            [tuple(r) for r in self.db.execute('select * from StyleXSpell where ClassID in (58,33) order by StyleXSpell_ID')],
                            [tuple(r)[1:] for r in self.db.execute("select * from SpecXAbility where Spec in ('VampiirCareer','HereticCareer') order by Spec,SpecLevel,AbilityKey")],
                            [tuple(r) for r in self.db.execute("select * from Mob where PackageID='local-deathknight-test-v1' order by Mob_ID")],
                            [tuple(r) for r in self.db.execute("select * from ItemTemplate where PackageID='local-deathknight-test-v1' order by Id_nb")],
                            [tuple(r)[1:] for r in self.db.execute("select * from SpellLine where ClassIDHint=66 order by KeyName")])
        first = snapshot()
        apply(self.db, BUILT)
        self.assertEqual(first, snapshot())
        self.assertEqual(before, progress(self.db))
        self.assertEqual(stock, [tuple(r) for r in self.db.execute("select * from Spell where coalesce(PackageID,'') not like 'local-%-test-v1' order by SpellID")])
        self.assertEqual(self.db.execute('pragma quick_check').fetchone()[0], 'ok')

    def test_class_is_enabled_with_a_start_and_tooltips_fit_the_client(self):
        apply(self.db, BUILT)
        disabled = self.db.execute("select Value from ServerProperty where Key='disabled_classes'").fetchone()[0].split(';')
        for cls in BUILT:
            info = KITS[cls]
            self.assertNotIn(str(cls), disabled)
            self.assertTrue(self.db.execute('select 1 from StartupLocation where ClassID=? and ClientRegionID=0 and RaceID=0 and Region>0', (cls,)).fetchone())
            self.assertEqual(self.db.execute('select Value from ServerProperty where Key=?', (info['property'],)).fetchone()[0], 'True')
            low, high = self.db.execute('select min(TooltipId),max(TooltipId) from Spell where PackageID=?', (info['package'],)).fetchone()
            self.assertGreaterEqual(low, info['tooltips']); self.assertLessEqual(high, 32767)

    def test_art_and_subspells_resolve(self):
        rows = definitions(BUILT)
        self.assertGreater(validate_icons(self.root, rows), 50)
        ids = {r['SpellID'] for _, _, _, r in rows}
        for _, _, _, row in rows:
            if row.get('SubSpellID'):
                self.assertIn(row['SubSpellID'], ids, row['Name'])

    def test_bainshee_follows_the_live_changes(self):
        rows = {(r['Name'], line): (lv, r) for _, line, lv, r in definitions((39,))}
        names = {name for name, _ in rows}
        for removed in ('Moan of Bidding', 'Disconcerting Yell', 'Instill Concern', "Wraith's Shield", 'Inhibiting Cry'):
            self.assertNotIn(removed, names)
        self.assertEqual(rows[('Impeding Screech', 'Spectral Guard')][0], 7, '1.121 root at 6, moved off the cone nuke at 6')
        self.assertEqual(rows[('Spectral Cords', 'Spectral Force')][0], 8, '1.128 baseline root')
        self.assertEqual(rows[('Alarming Screech', 'Spectral Force')][1]['Type'], 'CureMezz', '1.129 mez cure in the baseline')
        self.assertEqual(rows[('Audible Barrier', 'Spectral Guard')][0], 20)
        self.assertEqual(rows[('Disturbing Voices', 'Spectral Guard')][0], 46)
        self.assertEqual(rows[('Deafening Cascade', 'Ethereal Shriek')][1]['Damage'], 315)
        self.assertEqual(rows[('Deafening Cascade', 'Ethereal Shriek')][1]['DamageType'], 11, 'Cold since 1.121')
        self.assertEqual(rows[('Reverberating Scream', 'Ethereal Shriek')][1]['Value'], 50, '1.128c')
        self.assertEqual(rows[('Terrify Minion', 'Phantasmal Wail')][0], 49)
        self.assertEqual(rows[('Resounding Aura', 'Phantasmal Wail')][1]['Damage'], 375)
        self.assertEqual(rows[("Bawler's Obliteration", 'Spectral Guard')][1]['Target'], 'Cone')


    def test_vampiir_follows_the_live_changes(self):
        rows = {(r['Name'], line): (lv, r) for _, line, lv, r in definitions((58,))}
        names = {name for name, _ in rows}
        for removed in ('Enervating Embrace', 'Suppress', 'Neutralize', "Vampiir's Sprint"):
            self.assertNotIn(removed, names)
        self.assertEqual(rows[('Vengeance of the Raven', 'Shadow Mastery')][0], 21, '1.121 moved the fumble to Shadow Mastery')
        self.assertEqual(rows[('Wrath of the Raven', 'Shadow Mastery')][1]['Value'], 30, '1.128')
        self.assertEqual(rows[('Darkened Haste', 'Vampiiric Embrace')][0], 45, '1.121 moved the haste to Vampiiric Embrace')
        self.assertEqual(rows[('Flight of the Bat', 'Shadow Mastery')][1]['Type'], 'VampiirFlight')
        self.assertEqual(rows[('Flight of Darkness', 'Shadow Mastery')][1]['Duration'], 45)
        self.assertEqual(rows[("Vampiir's Inferno", 'Vampiiric Embrace')], (49, rows[("Vampiir's Inferno", 'Vampiiric Embrace')][1]))
        self.assertEqual(rows[("Vampiir's Inferno", 'Vampiiric Embrace')][1]['Range'], 750, '1.129b')
        self.assertEqual(rows[('Arctic Claw', 'Dementia')][1]['Power'], 37, '28 power raised 33% in 1.127')
        self.assertEqual(rows[('Hardened Skin', 'Dementia')][1]['RecastDelay'], 45, '1.129')
        self.assertEqual(rows[("Shadow's Spite", 'Vampiiric Embrace')][1]['Value'], 46)
        # The bolt ability casts kit base + ability level, so its ranks must sit at the start of the range.
        bolts = [r for _, line, _, r in definitions((58,)) if r['Type'] == 'VampiirBolt']
        self.assertEqual([r['SpellID'] for r in bolts], list(range(69000, 69008)))
        self.assertTrue(all(r['RecastDelay'] == 45 for r in bolts))

    def test_heretic_follows_the_live_changes(self):
        rows = {(r['Name'], line): (lv, r) for _, line, lv, r in definitions((33,))}
        names = {name for name, _ in rows}
        for removed in ('Diabolic Thorns', 'Diabolic Gate', 'Accursed Misery', 'Speed Decrease', 'Lacerate Movement'):
            self.assertNotIn(removed, names)
        RS, ES, BASE = KITS[33]['lines']
        self.assertEqual(rows[('Dark Magma', RS)][1]['DamageType'], 14)
        self.assertEqual(rows[('Incinerating Embers', RS)][1]['Type'], 'DirectDamage', '1.123 shout')
        self.assertEqual(rows[('Bursting Embers', RS)][0], 38)
        self.assertEqual(rows[('Lava Avalanche', RS)][1]['Range'], 1650)
        self.assertEqual(rows[('Lava Avalanche', RS)][1]['Value'], 40, '1.128')
        self.assertEqual(rows[('Fiery Stranglehold', RS)][1]['RecastDelay'], 30, '1.126')
        self.assertEqual(rows[('Torrential Blaze', RS)][1]['CastTime'], 2.5)
        self.assertEqual(rows[('Buffer of Warmth', ES)][0], 9, '1.121 moved to the specialization')
        self.assertEqual(rows[('Infernal Tear', ES)][0], 37)
        self.assertEqual(rows[("Hellflower's Bloom", ES)][1]['Value'], 6, '1.125')
        self.assertEqual(rows[('Molten Barricade', ES)][1]['Value'], 400, '1.125')
        self.assertEqual(rows[('Buffer of Warmth', ES)][1]['Value'], 10, '1.125')
        self.assertEqual(rows[("Arawn's Inferno", RS)][1]['Range'], 1650, '1.125')
        self.assertEqual(rows[("Arawn's Singe", RS)][1]['ClientEffect'], 14045, 'client has no effect 14044')
        self.assertEqual(rows[('Torrential Blaze', RS)][1]['Range'], 1600)
        cocoon = rows[('Destructive Cocoon', RS)][1]
        self.assertEqual((cocoon['Type'], cocoon['Value'], cocoon['Duration'], cocoon['_params']),
                         ('HereticCocoon', 200, 60, dict(CocoonStart=25, CocoonStep=5, CocoonMax=50)), '1.125')
        self.assertEqual(rows[('Aura of Deflection', BASE)][1]['Value'], 150, '1.122b')

    def test_styles_and_careers_are_installed(self):
        apply(self.db, BUILT)
        styles = {r['Name']: r for r in self.db.execute('select * from Style where ClassId=58')}
        self.assertEqual(len(styles), 22)
        self.assertEqual((styles['Knee Cap']['ID'], styles['Knee Cap']['OpeningRequirementType'], styles['Knee Cap']['OpeningRequirementValue']),
                         (579, 2, 0), '1.123 back positional')
        self.assertEqual(styles["Hornet's Sting"]['OpeningRequirementValue'], styles["Wasp's Sting"]['ID'])
        heretic = {r['Name']: r for r in self.db.execute('select * from Style where ClassId=33')}
        self.assertEqual(heretic['Shadowfire']['ID'], 583)
        self.assertEqual(heretic['Leviathan']['OpeningRequirementType'], 2)
        drain = self.db.execute("select sp.LifeDrainReturn from StyleXSpell x join Spell sp on sp.SpellID=x.SpellID "
                                "join Style s on s.StyleID=x.StyleID where s.ClassId=33 and s.Name='Indigosnake'").fetchone()[0]
        self.assertEqual(drain, 30, '1.126')
        for style in self.db.execute('select StyleID, OpeningRequirementType, OpeningRequirementValue from Style where ClassId in (58,33)'):
            if style['OpeningRequirementType'] == 0 and style['OpeningRequirementValue']:
                self.assertTrue(self.db.execute('select 1 from Style where ClassId in (58,33) and ID=?', (style['OpeningRequirementValue'],)).fetchone())
        for spec, key in [('VampiirCareer', 'Vampiir Bolt'), ('VampiirCareer', 'HibArmor'), ('HereticCareer', 'AlbArmor')]:
            self.assertTrue(self.db.execute('select 1 from SpecXAbility where Spec=? and AbilityKey=?', (spec, key)).fetchone())
        impl = self.db.execute("select Implementation from Ability where KeyName='Vampiir Strength'").fetchone()[0]
        self.assertEqual(impl, 'DOL.GS.SkillHandler.VampiirStrength')


    def test_list_classes_have_one_spell_per_level_per_line(self):
        # The client casts a list spell by line index and level; a second spell at the same level is unreachable.
        for cls in BUILT:
            if cls in HYBRID:
                continue
            seen = {}
            for _, line, level, row in definitions((cls,)):
                if line:
                    self.assertNotIn((line, level), seen, f"{row['Name']} collides with {seen.get((line, level))}")
                    seen[(line, level)] = row['Name']

    def test_every_spell_type_has_a_concrete_server_handler(self):
        handlers = {}
        for source in (REPO / 'source/server/GameServer').rglob('*.cs'):
            text = source.read_text(encoding='utf-8', errors='ignore')
            for match in re.finditer(r'\[SpellHandler\(eSpellType\.(\w+)\)\]\s*(?:\[[^\]]*\]\s*)*((?:\w+\s+)*?)class\s', text):
                handlers.setdefault(match.group(1), set()).add('abstract' not in match.group(2))
        for _, _, _, row in definitions(BUILT):
            self.assertIn(True, handlers.get(row['Type'], set()), f"{row['Name']}: {row['Type']} has no concrete handler")

    def test_live_realm_abilities_and_cocoon_values_are_installed(self):
        apply(self.db, BUILT)
        ras = lambda cls: {r[0] for r in self.db.execute('select AbilityKey from ClassXRealmAbility_Atlas where CharClass=?', (cls,))}
        self.assertTrue({'Mastery of Focus', 'AtlasOF_MasteryOfMagery', 'Dual Threat', 'Mark of Prey'} <= ras(58))
        self.assertIn('AtlasOF_StaticTempest', ras(33))
        self.assertNotIn('AtlasOF_Ichor', ras(33), '1.125')
        self.assertTrue({'Fanaticism', 'Sonic Barrier'} <= ras(33) | ras(39))
        params = dict(self.db.execute("select x.KeyName, x.Value from SpellXCustomValues x join Spell s on s.SpellID=x.SpellID "
                                      "where s.Name='Afflicting Cocoon'").fetchall())
        self.assertEqual(params, {'CocoonStart': '5', 'CocoonStep': '1', 'CocoonMax': '10'})


    def test_death_knight_follows_the_live_redesign(self):
        rows = {(r['Name'], line): (lv, r) for _, line, lv, r in definitions((66,))}
        names = {name for name, _ in rows}
        DSB, DSS, PWB, PWS, SVB, SVS = KITS[66]['lines']
        for removed in ('Bringer of Death', 'Swords of Arawn', 'Summon Death Shredder'):
            self.assertNotIn(removed, names)
        self.assertEqual(rows[('Decrepit Form', DSB)][0], 1)
        self.assertEqual(rows[('Chthonic Form', PWB)][0], 8)
        self.assertEqual(rows[('Spirit Form', SVB)][0], 30)
        self.assertEqual(rows[('Speed of the Dead', DSS)][1]['RecastDelay'], 300, '1.127')
        self.assertEqual(rows[('Crippling Exhaustion', SVS)][0], 3, 'live spell data: Death Servant 3')
        self.assertEqual(rows[('Power Blast', SVS)][1]['Power'], 16, 'live spell data')
        self.assertEqual(rows[('Death Bladeturn', DSS)][0], 24, 'live spell data')
        for name, line, level in [('Power Lock', SVS, 45), ("Arawn's Legion", SVS, 50), ('Wake Servant', SVB, 26),
                                  ('Imbue the Abyss', SVB, 48), ('Mystic Ground', SVB, 44), ('Intimacy with Death', SVS, 48)]:
            self.assertEqual(rows[(name, line)][0], level, name)
        self.assertNotIn('Nails of Arawn', names, 'not in the live spell list')
        self.assertEqual(rows[('Mystic Ground', SVB)][1]['Power'], 70, '1.130A')
        self.assertEqual(rows[('Ice Bracelet', PWS)][1]['Value'], 70, '1.128C')
        self.assertEqual(rows[('Freezing Howl', PWS)][1]['Damage'], 43, '1.126')
        self.assertEqual(rows[('Way of Magic', PWS)][1]['Target'], 'Self', '1.129B')
        self.assertEqual(rows[('Ignore Depletion', PWS)][1]['Duration'], 20, '1.129B')
        self.assertEqual(rows[('Plague Surge', SVS)][1]['Damage'], 199, '1.128C')
        self.assertEqual(rows[('Spirit Bladeturn', SVS)][0], 26, '1.128C')
        self.assertEqual(rows[('Unholy Ground', DSB)][1]['Duration'], 15, '1.126')
        grounds = {r['SharedTimerGroup'] for _, _, _, r in definitions((66,)) if r['Type'] == 'DeathKnightGround'}
        self.assertEqual(len(grounds), 1, 'every ground spell shares one reuse timer')
        darkness = [r for _, _, _, r in definitions((66,)) if r['Name'] == 'Darkness'][0]
        self.assertEqual(darkness['SpellID'], 71000, 'ModernDeathKnightDarkness.SpellId')

    def test_death_knight_world_rows(self):
        apply(self.db, BUILT)
        self.assertEqual(self.db.execute("select count(*) from Mob where ClassType='DOL.GS.Trainer.DeathKnightTrainer'").fetchone()[0],
                         self.db.execute("select count(*) from Mob where ClassType='DOL.GS.Trainer.NecromancerTrainer'").fetchone()[0])
        self.assertEqual(self.db.execute('select count(*) from SpellLine where ClassIDHint=66').fetchone()[0], 6)
        self.assertEqual(self.db.execute("select count(*) from ClassXRealmAbility_Atlas where CharClass=66 and AbilityKey='DeathKnight Call of Darkness'").fetchone()[0], 1)
        self.assertFalse(self.db.execute("select 1 from ClassXRealmAbility_Atlas where CharClass=66 and AbilityKey like '%Parry%'").fetchone(), '1.124D')
        self.assertTrue(self.db.execute("select 1 from ItemTemplate where Id_nb='deathknight_nethersbane'").fetchone())
        self.assertEqual(self.db.execute('select count(*) from NpcTemplate where TemplateId between 66001 and 66005').fetchone()[0], 5)
        spirit = dict(self.db.execute('select TemplateId, Name from NpcTemplate where TemplateId between 66011 and 66015').fetchall())
        self.assertEqual(spirit, {66011: 'spirit aegis', 66012: 'phantom', 66013: 'umbral hulk', 66014: 'image of Arawn', 66015: 'arch demon'},
                         'Spirit Form templates (DeathKnight.SpiritTemplateOffset)')
        self.assertTrue(self.db.execute('select 1 from NpcTemplate where TemplateId=66006').fetchone(), "Arawn's legion")
        self.assertTrue(all(r[0] for r in self.db.execute('select Spells from NpcTemplate where TemplateId between 66001 and 66005')),
                        'every servant casts its live abilities')
        self.assertTrue(self.db.execute("select ProcSpellID from ItemTemplate where Id_nb='deathknight_nethersbane'").fetchone()[0])
        self.assertEqual(self.db.execute("select Bonus1Type, Bonus1 from ItemTemplate where Id_nb='deathknight_icebrand_50'").fetchone()[:], (4, 20),
                         'live Icebrand bonuses')
        self.assertEqual(self.db.execute("select Object_Type, Hand from ItemTemplate where Id_nb='deathknight_icebrand_50'").fetchone()[:], (6, 1),
                         'Icebrand is a two-handed sword')
        self.assertTrue(all(r[0] == 65535 for r in self.db.execute("select Duration from Spell where Type='DeathKnightServant'")),
                        'servants are permanent like stock pets')
        starter = {r[0] for r in self.db.execute("select TemplateID from StarterEquipment where Class='66;'")}
        self.assertIn('woolen_cloth_vest', starter)
        self.assertTrue(self.db.execute("select 1 from SpecXAbility where Spec='DeathKnightCareer' and AbilityKey='Weaponry: Two Handed'").fetchone())
        self.assertTrue(all(r[0] == 1001 for r in self.db.execute('select WeaponTypeRequirement from Style where ClassId=66')))
        classic = [r[0] for r in self.db.execute("select KeyName from SpellLine where Spec='Deathsight' and ClassIDHint=0")]
        self.assertEqual(sorted(classic), ['Deathsight', 'Deathsight Spec'], 'classic Necromancer lines untouched')


if __name__ == '__main__':
    unittest.main()
