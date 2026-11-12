"""Exercise the Mauler migration on a private in-memory copy of the current test world."""
from contextlib import closing
from pathlib import Path
import sqlite3
import unittest
from install_mauler_test import REPO,progress
from mauler_test_data import apply,definitions,validate_icons,PACKAGE

class MaulerDataTests(unittest.TestCase):
    def setUp(self):
        self.root=REPO/'artifacts/modern-client-test'
        if not (self.root/'runtime/data/opendaoc.sqlite3.db').exists():self.skipTest('needs the modern test install beside this checkout')
        self.db=sqlite3.connect(':memory:');self.db.row_factory=sqlite3.Row
        with closing(sqlite3.connect((self.root/'runtime/data/opendaoc.sqlite3.db').as_uri()+'?mode=ro',uri=True)) as src:src.backup(self.db)
    def tearDown(self):self.db.close()
    def test_migration_preserves_saves_warlock_and_existing_spells(self):
        saves=progress(self.db)
        existing=[tuple(r) for r in self.db.execute("select * from Spell where coalesce(PackageID,'')<>? order by SpellID",(PACKAGE,))]
        apply(self.db)
        self.assertEqual(saves,progress(self.db))
        self.assertEqual(existing,[tuple(r) for r in self.db.execute("select * from Spell where coalesce(PackageID,'')<>? order by SpellID",(PACKAGE,))])
        self.assertEqual(self.db.execute('pragma quick_check').fetchone()[0],'ok')
    def test_idempotent_spells_styles_and_starts(self):
        apply(self.db)
        first=[tuple(r) for r in self.db.execute('select * from Spell where PackageID=? order by SpellID',(PACKAGE,))]
        counts=[self.db.execute('select count(*) from '+t).fetchone()[0] for t in ('Style','StartupLocation','StarterEquipment','ItemTemplate')]
        apply(self.db)
        self.assertEqual(first,[tuple(r) for r in self.db.execute('select * from Spell where PackageID=? order by SpellID',(PACKAGE,))])
        self.assertEqual(counts,[self.db.execute('select count(*) from '+t).fetchone()[0] for t in ('Style','StartupLocation','StarterEquipment','ItemTemplate')])
    def test_native_slot_guard_preserves_custom_class_support(self):
        self.db.execute("update ServerProperty set Value='False' where Key='native_custom_class_slots'")
        with self.assertRaisesRegex(ValueError,'Native custom slots'):apply(self.db)
    def test_spell_collision_refuses_overwrite(self):
        apply(self.db);self.db.execute("update Spell set PackageID='foreign' where SpellID=67000")
        with self.assertRaisesRegex(ValueError,'collision'):apply(self.db)
    def test_all_three_realms_have_valid_region_zero_creation_and_same_styles(self):
        apply(self.db)
        trees=[]
        for realm,career in ((1,60),(2,61),(3,62)):
            row=self.db.execute('select Region,XPos,YPos,ZPos from StartupLocation where ClassID=? and RealmID=? and RaceID=0 and ClientRegionID=0 and MinVersion<=1130',(career,realm)).fetchone()
            self.assertIsNotNone(row);self.assertEqual(row[0],{1:1,2:100,3:200}[realm]);self.assertTrue(all(v>0 for v in row))
            tree=[tuple(r) for r in self.db.execute('select ID,Name,SpecKeyName,SpecLevelRequirement,OpeningRequirementType,OpeningRequirementValue,AttackResultRequirement from Style where ClassID=? order by ID',(career,))]
            self.assertEqual(len(tree),32);trees.append(tree)
            weapons=list(self.db.execute('select i.Object_Type,i.Item_Type from StarterEquipment e join ItemTemplate i on e.TemplateID=i.Id_nb where e.Class=? and i.Object_Type in (27,28)',(str(career)+';',)))
            self.assertEqual({tuple(r) for r in weapons},{(27,10),(27,11),(28,12)})
        self.assertEqual(trees[0],trees[1]);self.assertEqual(trees[1],trees[2])
    def test_live_overrides_are_applied_after_obsolete_library(self):
        apply(self.db)
        self.assertEqual([tuple(r) for r in self.db.execute("select l.Level,s.Value from Spell s join LineXSpell l using(SpellID) where s.Name like '%Discipline of Laresh' and l.LineName='Aura Manipulation' and s.PackageID='local-mauler-test-v1' order by l.Level")],[(12,13),(22,16),(32,20),(42,28)])
        self.assertEqual([tuple(r) for r in self.db.execute("select Value,RecastDelay from Spell where SpellID between 67225 and 67227 order by SpellID")],[(10,180),(20,180),(30,180)])
        self.assertTrue(all(tuple(r)==(30,60) for r in self.db.execute('select Duration,RecastDelay from Spell where SpellID between 67250 and 67254')))
        phoenix=self.db.execute("select ID,OpeningRequirementValue from Style where ClassID=60 and Name='Phoenix Strike'").fetchone()
        owl=self.db.execute("select ID from Style where ClassID=60 and Name='Owl Strike'").fetchone()[0]
        self.assertEqual(phoenix[1],owl)
        self.assertEqual(self.db.execute('select s.Damage from StyleXSpell x join Spell s using(SpellID) where x.ClassID=60 and x.StyleID=?',(phoenix[0],)).fetchone()[0],130)
        self.assertEqual(self.db.execute("select AttackResultRequirement from Style where ClassID=60 and Name='Dislocation'").fetchone()[0],5)
    def test_aura_variants_and_artwork_resolve(self):
        self.assertGreater(validate_icons(self.root),100);apply(self.db)
        all_ids={r[0] for r in self.db.execute('select SpellID from Spell')}
        for _,_,spell,params in definitions():
            for child in (spell.get('SubSpellID'),params.get('MaulerFarseerSpell')):
                if child:self.assertIn(child,all_ids)
            if spell.get('Pulse'):self.assertGreater(spell.get('Frequency',0),0)
        self.assertEqual(self.db.execute("select count(*) from LineXSpell where LineName like 'Mauler % Gifts'").fetchone()[0],15)
        self.assertEqual(self.db.execute("select count(*) from Spell where SpellID between 67520 and 67529 and Type='MaulerStat'").fetchone()[0],10)

    def test_tooltip_ids_fit_the_clients_signed_16_bit_index(self):
        # Tooltips above 32767 never display: the client reads the delve index as a signed short.
        apply(self.db)
        low,high=self.db.execute('select min(TooltipId),max(TooltipId) from Spell where PackageID=?',(PACKAGE,)).fetchone()
        self.assertGreaterEqual(low,31200); self.assertLessEqual(high,32767)

if __name__=='__main__':unittest.main()
