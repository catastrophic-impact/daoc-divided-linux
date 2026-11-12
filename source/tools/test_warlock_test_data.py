"""Validate the migration against a private in-memory snapshot; never modify the installation."""
import sqlite3
import unittest
from contextlib import closing
from pathlib import Path
import sys
from warlock_test_data import apply, definitions, validate_icons, PACKAGE
sys.path.insert(0,str(Path(__file__).resolve().parent/'sovereign'))
from install_instant_decrees import REPO, progress

class WarlockDataTests(unittest.TestCase):
    def setUp(self):
        self.db=sqlite3.connect(':memory:')
        self.root=REPO/'artifacts/modern-client-test'
        if not (self.root/'runtime/data/opendaoc.sqlite3.db').exists():self.skipTest('needs the modern test install beside this checkout')
        with closing(sqlite3.connect((self.root/'runtime/data/opendaoc.sqlite3.db').as_uri()+'?mode=ro',uri=True)) as source:
            source.backup(self.db)
    def tearDown(self):self.db.close()
    def test_idempotent_and_preserves_saves_and_other_classes(self):
        before=progress(self.db)
        stock=[tuple(r) for r in self.db.execute("select * from Spell where coalesce(PackageID,'')<>? order by SpellID",(PACKAGE,))]
        apply(self.db)
        first=[tuple(r) for r in self.db.execute('select * from Spell where PackageID=? order by SpellID',(PACKAGE,))]
        apply(self.db)
        self.assertEqual(first,[tuple(r) for r in self.db.execute('select * from Spell where PackageID=? order by SpellID',(PACKAGE,))])
        self.assertEqual(before,progress(self.db))
        self.assertEqual(stock,[tuple(r) for r in self.db.execute("select * from Spell where coalesce(PackageID,'')<>? order by SpellID",(PACKAGE,))])
        self.assertEqual(self.db.execute('pragma quick_check').fetchone()[0],'ok')
    def test_collision_refuses_replacement(self):
        apply(self.db)
        self.db.execute("update Spell set PackageID='foreign-package' where SpellID=66000")
        with self.assertRaisesRegex(ValueError,'collision'):apply(self.db)
    def test_artwork_and_subspells_exist(self):
        rows=definitions();self.assertGreater(validate_icons(self.root,rows),100)
        ids={r['SpellID'] for _,_,r in rows}
        tips=[r['TooltipId'] for _,_,r in rows]
        self.assertEqual(len(ids),len(rows));self.assertEqual(len(tips),len(set(tips)))
        for _,_,row in rows:
            if row.get('SubSpellID'):self.assertIn(row['SubSpellID'],ids)
        apply(self.db)
        self.assertEqual(self.db.execute('select count(*) from SpellXCustomValues where KeyName=? and SpellID between 66000 and 66399',('WarlockStage',)).fetchone()[0],12)
    def test_only_warlock_is_enabled_and_primers_are_not_legacy(self):
        before=set(self.db.execute("select Value from ServerProperty where Key='disabled_classes'").fetchone()[0].split(';'))
        apply(self.db)
        disabled=self.db.execute("select Value from ServerProperty where Key='disabled_classes'").fetchone()[0].split(';')
        self.assertNotIn('59',disabled)
        # Only class 59 is enabled; every other class keeps its state (the Maulers are enabled by their own installer).
        self.assertEqual(set(disabled),before-{'59'})
        self.assertEqual(self.db.execute("select count(*) from Spell where PackageID=? and Type='WarlockPrimer'",(PACKAGE,)).fetchone()[0],17)
        self.assertEqual(self.db.execute("select count(*) from Spell where PackageID=? and Type='WarlockChamber'",(PACKAGE,)).fetchone()[0],6)

    def test_all_warlock_races_match_a_modern_start_from_region_zero(self):
        apply(self.db)
        for race in (5,6,7,19):
            row=self.db.execute('select Region,XPos,YPos,ZPos from StartupLocation where MinVersion<=1130 and RealmID in (0,2) and ClassID in (0,59) and RaceID in (0,?) and ClientRegionID=0 order by MinVersion desc limit 1',(race,)).fetchone()
            self.assertIsNotNone(row)
            self.assertTrue(all(value>0 for value in row))
        count=self.db.execute('select count(*) from StartupLocation where ClassID=59').fetchone()[0]
        apply(self.db)
        self.assertEqual(self.db.execute('select count(*) from StartupLocation where ClassID=59').fetchone()[0],count)

    def test_zero_region_character_repair_is_narrow_and_idempotent(self):
        from repair_warlock_start import repair
        repair(self.db)  # Internally verifies every character field and every other saved table.
        self.assertEqual(self.db.execute('select count(*) from DOLCharacters where Class=59 and Realm=2 and Region=0 and Xpos=0 and Ypos=0 and Zpos=0 and BindRegion=0').fetchone()[0],0)
        self.assertEqual(repair(self.db)['repaired_characters'],0)

    def test_live_additions_and_modern_icons(self):
        rows={r['Name']:(line,lv,r) for line,lv,r in definitions()}
        self.assertEqual(rows['Greater Bolt of Death'][1],48); self.assertEqual(rows['Greater Bolt of Death'][2]['IsSecondary'],1)
        self.assertEqual(rows["Spellbinder's Torrent"][2]['Damage'],365)
        self.assertEqual(rows['Hex of Hindering'][1],36); self.assertEqual(rows['Hex of Snaring'][2]['Power'],0)
        self.assertEqual(rows['Malevolent Scourge'][2]['SubSpellID'],66323)
        self.assertEqual([rows[n][2]['Duration'] for n in ('Fear Slave','Expel Slave','Intimidate Slave','Daunt Slave','Terrify Slave')],[5,9,15,22,30])
        self.assertEqual(rows['Ameliorating Incantation'][2]['Value'],10); self.assertEqual(rows['Calming Incantation'][2]['Value'],8)
        self.assertEqual(rows['Misery'][2]['Damage'],83)
        self.assertEqual(rows['Dread'][2]['Icon'],15083); self.assertEqual(rows['Doom'][2]['Icon'],15069)
        self.assertEqual(rows['Puny Misery'][2]['Icon'],15407); self.assertEqual(rows['Fear Slave'][2]['Icon'],15197)

if __name__=='__main__':unittest.main()
