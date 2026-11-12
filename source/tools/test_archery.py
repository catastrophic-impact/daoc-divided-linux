"""Read-only checks of the live archery lines (archery.py) on a private copy of the modern test world."""
from contextlib import closing
from pathlib import Path
import sqlite3
import unittest
try:
    import archery as a
except ModuleNotFoundError as missing:
    # The daoc.gg class data files are local only (.git/info/exclude, owner 2026-10-25).
    if not (missing.name or '').endswith('_data'):
        raise
    raise unittest.SkipTest(f'{missing.name}.py is local only; copy it into source/tools to run these tests')

WORLD = Path(__file__).resolve().parents[2] / 'artifacts/modern-client-test/runtime/data/opendaoc.sqlite3.db'


class ArcheryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not WORLD.is_file():
            raise unittest.SkipTest('Modern test world is not available')
        cls.db = sqlite3.connect(':memory:'); cls.db.row_factory = sqlite3.Row
        with closing(sqlite3.connect(WORLD.resolve().as_uri() + '?mode=ro', uri=True)) as src:
            src.backup(cls.db)
        with cls.db:
            cls.result = a.apply(cls.db)

    def shots(self, line):
        return {r['Name']: r for r in self.db.execute('select s.*, l.Level as LineLevel from Spell s join LineXSpell l on l.SpellID=s.SpellID '
                                                     'where l.LineName=?', (line,))}

    def test_each_bow_spec_gets_a_class_line_and_live_hybrid_list(self):
        for cls, cid, spec in (('Scout', 3, 'Longbows'), ('Hunter', 25, 'Composite Bow'), ('Ranger', 50, 'Recurve Bow')):
            line = self.db.execute('select * from SpellLine where KeyName=?', (cls + ' Archery',)).fetchone()
            self.assertEqual((line['Spec'], line['ClassIDHint'], line['IsBaseLine']), (spec, cid, 0))
            self.assertEqual(self.db.execute('select Implementation from Specialization where KeyName=?', (spec,)).fetchone()[0], a.HYBRID)
            self.assertIn('Standard Shot 9', self.shots(cls + ' Archery'))

    def test_shot_types_and_live_timers(self):
        scout = self.shots('Scout Archery')
        self.assertEqual((scout['Critical Shot 9']['LifeDrainReturn'], scout['Power Shot 8']['LifeDrainReturn'],
                          scout['Point Blank Shot 3']['LifeDrainReturn'], scout['Rapid Fire 8']['LifeDrainReturn']), (1, 2, 3, 4))
        volley = scout['Volley 8']
        self.assertEqual((volley['Target'], volley['Radius'], volley['RecastDelay']), ('Area', 350, 30))
        self.assertEqual((scout['Point Blank Shot 3']['CastTime'], scout['Point Blank Shot 3']['RecastDelay']), (0, 90))
        self.assertEqual(scout['Siege Shot 8']['Type'], 'SiegeArrow')
        self.assertTrue(any(r['Type'] == 'StrengthBuff' for r in scout.values()), 'the Scout gets its live self buffs')
        self.assertFalse(any(r['Type'] == 'StrengthBuff' for r in self.shots('Ranger Archery').values()), 'Pathfinding keeps the Ranger buffs')
        self.assertEqual(scout['Standard Shot 9']['CastTime'], 4.2, "live's timer + 2 s")
        self.assertEqual((scout['Patella Shot']['Type'], scout['Patella Shot']['Value'], scout['Patella Shot']['Duration']), ('BolaShot', 40, 15))
        track = [r[0] for r in self.db.execute("select s.Value from Spell s join LineXSpell l on l.SpellID=s.SpellID where l.LineName='Scout Archery' and s.Type='TrackStealth' order by l.Level")]
        self.assertEqual(track, [15, 25, 35, 45])
        bola = scout['Bola Shot 4']
        self.assertEqual((bola['Type'], bola['Duration'], bola['CastTime'], bola['RecastDelay']), ('BolaShot', 36, 4.0, 20))
        self.assertEqual({r['Name']: r['DamageType'] for r in scout.values() if r['Type'] == 'ArrowDamageTypes'},
                         {'Blunt Arrows': 1, 'Slashing Arrows': 2, 'Thrusting Arrows': 3})

    def test_eagle_eye_follows_the_character_level(self):
        for cls in ('Scout', 'Hunter', 'Ranger'):
            line = self.db.execute('select * from SpellLine where KeyName=?', (cls + ' Archery Abilities',)).fetchone()
            self.assertEqual(line['IsBaseLine'], 1)
            eye = self.shots(cls + ' Archery Abilities')['Eagle Eye']
            self.assertEqual((eye['LineLevel'], eye['Value'], eye['Duration'], eye['Type']), (45, 25, 1800, 'ArcheryDamageBuff'))

    def test_hunter_buffs_move_to_archery(self):
        names = {r[0]: r[1] for r in self.db.execute("select s.Name, s.Value from LineXSpell l join Spell s on s.SpellID=l.SpellID where l.LineName='Hunter Archery'")}
        self.assertEqual((names.get('Invincibility'), names.get('Perfect Acuity'), names.get('Forest Phantom')), (64, 72, 195))
        self.assertEqual(self.result['beastcraft_buffs_left'], 0)
        self.assertTrue(self.db.execute("select 1 from LineXSpell where LineName='Beastcraft'").fetchone(), 'the pets and charms stay')

    def test_reapplying_is_stable_and_ids_are_in_range(self):
        with self.db:
            self.assertEqual(a.apply(self.db), self.result)
        rows = list(self.db.execute('select * from Spell where PackageID=?', (a.PACKAGE,)))
        self.assertEqual(len(rows), len(a.definitions()))
        self.assertTrue(all(a.FIRST <= r['SpellID'] <= a.LAST and r['TooltipId'] <= 32767 for r in rows))
        self.assertFalse(self.db.execute('select 1 from Spell where TooltipId between ? and ? and coalesce(PackageID,"")<>?',
                                         (a.TOOLTIP_BASE, a.TOOLTIP_BASE + a.LAST - a.FIRST, a.PACKAGE)).fetchone())


if __name__ == '__main__':
    unittest.main()
