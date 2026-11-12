"""Read-only checks of the live spell values (spell_drift.py) on a private copy of the modern test world."""
from contextlib import closing
from pathlib import Path
import sqlite3
import unittest
try:
    import spell_drift as d
except ModuleNotFoundError as missing:
    # The daoc.gg class data files are local only (.git/info/exclude, owner 2026-10-25).
    if not (missing.name or '').endswith('_data'):
        raise
    raise unittest.SkipTest(f'{missing.name}.py is local only; copy it into source/tools to run these tests')

WORLD = Path(__file__).resolve().parents[2] / 'artifacts/modern-client-test/runtime/data/opendaoc.sqlite3.db'


class SpellDriftTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not WORLD.is_file():
            raise unittest.SkipTest('Modern test world is not available')
        cls.db = sqlite3.connect(':memory:')
        with closing(sqlite3.connect(WORLD.resolve().as_uri() + '?mode=ro', uri=True)) as src:
            src.backup(cls.db)
        with cls.db:
            cls.result = d.apply(cls.db)

    def test_every_value_reaches_live_and_reapplying_is_stable(self):
        self.assertEqual(self.result, dict(drift_values_live=len(d.DRIFT) + len(d.DRIFT_V3), drift_values_left=0,
                                           drift_levels_live=sum((s, line) not in d.RETIRED for s, line, _, _ in d.LEVELS),
                                           drift_levels_left=0))
        with self.db:
            self.assertEqual(d.apply(self.db), self.result)
        for spell_id, column, _, new in d.DRIFT[:50]:
            self.assertEqual(self.db.execute(f'select "{column}" from Spell where SpellID=?', (spell_id,)).fetchone()[0], new)

    def test_only_stock_spells_and_same_unit_fields(self):
        ids = {r[0] for r in d.DRIFT}
        self.assertTrue(all(i < 59000 for i in ids))
        self.assertFalse(self.db.execute(f"select 1 from Spell where SpellID in ({','.join(map(str, ids))}) and "
                                         "(coalesce(PackageID,'') like 'local-%' or Type in ('Bomber','PetSpell'))").fetchone())
        self.assertLessEqual({r[1] for r in d.DRIFT}, d.COLUMNS)

    def test_a_value_changed_since_is_left_alone(self):
        spell_id, column, old, new = d.DRIFT[0]
        db = sqlite3.connect(':memory:')
        db.execute(f'create table Spell (SpellID integer, "{column}" real)')
        db.execute('create table LineXSpell (SpellID integer, LineName text, Level integer)')
        db.execute('insert into Spell values (?,?)', (spell_id, old + 1))
        self.assertEqual(d.apply(db)['drift_values_left'], len(d.DRIFT) + len(d.DRIFT_V3))
        self.assertEqual(db.execute(f'select "{column}" from Spell').fetchone()[0], old + 1)


if __name__ == '__main__':
    unittest.main()
