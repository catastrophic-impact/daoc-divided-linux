"""Read-only check of the DoT timing step (dot_timing.py) on a private copy of the modern test world."""
from contextlib import closing
from pathlib import Path
import sqlite3
import unittest
try:
    import dot_timing as t
except ModuleNotFoundError as missing:
    # The daoc.gg class data files are local only (.git/info/exclude, owner 2026-10-25).
    if not (missing.name or '').endswith('_data'):
        raise
    raise unittest.SkipTest(f'{missing.name}.py is local only; copy it into source/tools to run these tests')

WORLD = Path(__file__).resolve().parents[2] / 'artifacts/modern-client-test/runtime/data/opendaoc.sqlite3.db'


class DotTimingTests(unittest.TestCase):
    def test_dots_take_live_timing_and_reapplying_changes_nothing(self):
        if not WORLD.is_file():
            self.skipTest('Modern test world is not available')
        db = sqlite3.connect(':memory:')
        with closing(sqlite3.connect(WORLD.resolve().as_uri() + '?mode=ro', uri=True)) as src:
            src.backup(db)
        with db:
            first = t.apply(db)
            second = t.apply(db)
        # An installed world already has live timing, so its DoTs count as already converted.
        self.assertGreater(first['dot_timing_live'] + first['dot_timing_halved'] + first['dot_timing_already'], 100)
        self.assertEqual((second['dot_timing_live'], second['dot_timing_halved']), (0, 0))
        mist = db.execute("select Duration, Frequency, Damage from Spell where Name='Corrosive Mist' and coalesce(PackageID,'') not like 'local-%'").fetchone()
        self.assertEqual(mist[:2], (10, 25))
        poison = db.execute("select Duration, Frequency from Spell where SpellID=30000").fetchone()
        self.assertEqual(poison, (20, 40), 'item poisons keep their timing')


if __name__ == '__main__':
    unittest.main()
