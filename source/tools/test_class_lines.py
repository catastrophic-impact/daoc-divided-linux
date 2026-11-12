"""Read-only checks of the other classes' live spells (class_lines.py) on a private copy of the modern test world."""
from contextlib import closing
from pathlib import Path
import sqlite3
import unittest
try:
    import class_lines as c
except ModuleNotFoundError as missing:
    # The daoc.gg class data files are local only (.git/info/exclude, owner 2026-10-25).
    if not (missing.name or '').endswith('_data'):
        raise
    raise unittest.SkipTest(f'{missing.name}.py is local only; copy it into source/tools to run these tests')
import hybrid_lines
import remove_sluaghbinder

WORLD = Path(__file__).resolve().parents[2] / 'artifacts/modern-client-test/runtime/data/opendaoc.sqlite3.db'


class ClassLineTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not WORLD.is_file():
            raise unittest.SkipTest('Modern test world is not available')
        cls.db = sqlite3.connect(':memory:'); cls.db.row_factory = sqlite3.Row
        with closing(sqlite3.connect(WORLD.resolve().as_uri() + '?mode=ro', uri=True)) as src:
            src.backup(cls.db)
        with cls.db:
            remove_sluaghbinder.apply(cls.db)
            hybrid_lines.apply(cls.db)
            cls.result = c.apply(cls.db)

    def rows(self):
        return list(self.db.execute('select s.*, l.LineName, l.Level as LineLevel from Spell s join LineXSpell l on l.SpellID=s.SpellID '
                                    'where s.PackageID=?', (c.PACKAGE,)))

    def test_reapplying_is_stable(self):
        with self.db:
            self.assertEqual(c.apply(self.db), self.result)
        self.assertEqual(len(self.rows()), len(c.definitions()))

    def test_ids_tooltips_groups_and_art(self):
        for row in self.rows():
            self.assertTrue(c.FIRST <= row['SpellID'] <= c.LAST)
            self.assertLessEqual(row['TooltipId'], 32767)
            # The Mauler test spells use the client's own Mauler art, so they count as stock here.
            self.assertTrue(self.db.execute("select 1 from Spell where ClientEffect=? and Icon=? and (coalesce(PackageID,'') not like 'local-%' or PackageID='local-mauler-test-v1')",
                                            (row['ClientEffect'], row['Icon'])).fetchone(), f"{row['Name']} borrows a local spell's art")
        self.assertFalse(self.db.execute('select 1 from Spell where TooltipId between ? and ? and PackageID<>?',
                                         (c.TOOLTIP_BASE, c.TOOLTIP_BASE + c.LAST - c.FIRST, c.PACKAGE)).fetchone())
        groups = {r['SpellGroup'] for r in self.rows()}
        self.assertFalse(self.db.execute(f"select 1 from Spell where SpellGroup in ({','.join('?' * len(groups))}) and PackageID<>?",
                                         (*groups, c.PACKAGE)).fetchone())

    def test_list_casters_get_new_lines_with_one_spell_per_level(self):
        new_lines = list(self.db.execute("select * from SpellLine where KeyName like '%(1.130%'"))
        self.assertTrue(new_lines)
        for line in new_lines:
            levels = [r[0] for r in self.db.execute('select Level from LineXSpell where LineName=?', (line['KeyName'],))]
            self.assertEqual(len(levels), len(set(levels)), line['KeyName'])
            sibling = self.db.execute('select * from SpellLine where KeyName=?', (line['KeyName'].split(' (1.130')[0],)).fetchone()
            self.assertEqual((sibling['Spec'], sibling['ClassIDHint'], sibling['IsBaseLine']),
                             (line['Spec'], line['ClassIDHint'], line['IsBaseLine']), line['KeyName'])

    def test_live_values(self):
        by_name = {r['Name']: r for r in self.rows()}
        snare = by_name['Blessed Deliverance']
        self.assertEqual((snare['LineName'], snare['Type'], snare['Value'], snare['Radius'], snare['Range'], snare['Duration']),
                         ('Smiting', 'SpeedDecrease', 50, 350, 0, 69))
        self.assertEqual((by_name['Holy Slumber']['Type'], by_name['Holy Slumber']['Duration']), ('Mesmerize', 60))
        self.assertEqual(by_name['Pure Light']['Value'], -45, 'a percentage heal')
        self.assertIn('(1.130)', by_name['Dazzling Clutch']['LineName'])
        self.assertEqual(self.db.execute("select Spec from SpellLine where KeyName=?", (by_name['Phantom Renewal']['LineName'],)).fetchone()[0],
                         'Mentalism', 'live Mentalism spells go to the Mentalist')
        self.assertEqual(self.db.execute("select Spec from SpellLine where KeyName=?", (by_name['Dazzling Clutch']['LineName'],)).fetchone()[0],
                         'Enchantments', 'live Enchantment spells go to the Enchanter (41), not the Eldritch (40)')
        names = {r[0] for r in self.db.execute("select s.Name from LineXSpell l join Spell s on s.SpellID=l.SpellID where l.LineName like 'Regrowth%'")}
        self.assertIn('Major Resurgence', names); self.assertNotIn('Major Resurgance', names)

    def test_second_pass_values(self):
        by_name = {r['Name']: r for r in self.rows()}
        self.assertEqual(by_name['Entangling Earth']['Value'], 35, "live's 65 is the speed left")
        ruin = by_name["Ancestor's Ruin"]
        self.assertEqual((ruin['LineName'], ruin['Type'], ruin['Damage']), ('Subterranean Incantations', 'DirectDamage', 177.6))
        self.assertEqual((by_name['Exhausting Curse']['Type'], by_name['Exhausting Curse']['Value']), ('FatigueConsumptionDebuff', -100))
        self.assertEqual((by_name['Deteriorate Arms']['Type'], by_name['Deteriorate Arms']['Value']), ('MeleeDamageDebuff', 50))
        shroud = by_name['Shroud of Uncertainty']
        self.assertEqual((shroud['Type'], shroud['Value'], shroud['Radius']), ('DamageSpeedDecrease', 10, 350))
        self.assertIn('(1.130', shroud['LineName'])
        bones = {r[0]: r[1] for r in self.db.execute("select s.Name, s.Value from LineXSpell l join Spell s on s.SpellID=l.SpellID where l.LineName='Bone Army'")}
        self.assertEqual((bones.get('Iron Bones'), bones.get('Restore Bones')), (36, 201))
        self.assertNotIn('Bones to Iron', bones)

    def test_savage_and_hunter(self):
        savagery = {r[0]: (r[1], r[2], r[3]) for r in self.db.execute(
            "select s.Name, s.Type, s.Value, s.Power from LineXSpell l join Spell s on s.SpellID=l.SpellID where l.LineName='Savagery'")}
        self.assertEqual(savagery['Ignore Attacks'], ('SavageMeleeResistanceBuff', 25, -10))
        self.assertNotIn('Ignore Blows', savagery, 'the single-resist buffs leave the line')
        self.assertEqual(savagery['Infuriating Gesture'][1], 55.6)
        self.assertEqual(savagery['Evasion of Kelgor'][1:], (20, -5))
        beast = {r[0]: (r[1], r[2]) for r in self.db.execute(
            "select s.Name, s.Value, s.AmnesiaChance from LineXSpell l join Spell s on s.SpellID=l.SpellID where l.LineName='Beastcraft' and s.Type='Charm'")}
        self.assertEqual(beast['Dominate Reptile'], (50, 8))
        self.assertEqual(beast['Dominate Insect'], (50, 3))


if __name__ == '__main__':
    unittest.main()
