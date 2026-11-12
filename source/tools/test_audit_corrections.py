"""Read-only check of the audit corrections (audit_corrections.py) on a private copy of the modern test world."""
from contextlib import closing
from pathlib import Path
import sqlite3
import unittest
try:
    import audit_corrections as a
except ModuleNotFoundError as missing:
    # The daoc.gg class data files are local only (.git/info/exclude, owner 2026-10-25).
    if not (missing.name or '').endswith('_data'):
        raise
    raise unittest.SkipTest(f'{missing.name}.py is local only; copy it into source/tools to run these tests')

WORLD = Path(__file__).resolve().parents[2] / 'artifacts/modern-client-test/runtime/data/opendaoc.sqlite3.db'


class AuditCorrectionTests(unittest.TestCase):
    def test_corrections_apply_by_name_and_are_stable(self):
        if not WORLD.is_file():
            self.skipTest('Modern test world is not available')
        db = sqlite3.connect(':memory:')
        with closing(sqlite3.connect(WORLD.resolve().as_uri() + '?mode=ro', uri=True)) as src:
            src.backup(db)
        with db:
            first = a.apply(db)
            self.assertEqual(a.apply(db), first)
        self.assertEqual(db.execute("select Value from Spell where SpellID=74039").fetchone()[0], 25)  # Decelerate: a 25% slow
        self.assertEqual(db.execute("select CastTime from Spell where SpellID=74074").fetchone()[0], 3.2)
        self.assertEqual(db.execute("select Target, Duration, Pulse from Spell where SpellID=74028").fetchone(), ('Realm', 30, 0))
        # The Bard/Warden cures at live's 3.2 s; the Druid keeps its 2.4 s stock copies, which leave the Bard's spec line.
        self.assertEqual(db.execute("select CastTime from Spell where SpellID in (75153, 75154)").fetchall(), [(3.2,), (3.2,)])
        self.assertEqual(db.execute("select CastTime from Spell where SpellID=4983").fetchone()[0], 2.4)
        lines = lambda spell_id: {x[0] for x in db.execute('select LineName from LineXSpell where SpellID=?', (spell_id,))}
        self.assertNotIn('Regrowth Bard Spec', lines(4983) | lines(4869))
        self.assertIn('Regrowth Druid Spec', lines(4983) & lines(4869))


if __name__ == '__main__':
    unittest.main()
