"""Tuatha fixes for a world that already has the remake (2026-11-06): the courts last COURT_SECONDS instead of forever,
Turn of the Courts says so, and Otherworld Ascendant becomes the Morrigan (model 1904, was a plain elf) with its own text. Text comes from
tuatha_remake_edits.py, so fresh and patched worlds match.

    python tuatha_court_fix_edits.py <world.db> edits.json
"""
import json
from pathlib import Path
import sqlite3
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from storm_remake_edits import World
from tuatha_remake_edits import ASCENDANT, ASCENDANT_MODEL, COURTS, COURT_SECONDS, FIRST_SPELL, PACKAGE, SPELLS, TURN_KEY, ascendant_rows


def edits_for(db):
    w = World(db)
    for key, (name, description, _) in COURTS.items():
        row = w.one("SELECT Spell_ID FROM Spell WHERE SpellID=? AND PackageID=?", FIRST_SPELL + key, PACKAGE)
        if not row:
            raise ValueError(f"{name} ({FIRST_SPELL + key}) is missing: this world does not have the Tuatha remake")
        w.out.append({"table": "Spell", "key": row[0], "set": dict(Duration=COURT_SECONDS, Description=description)})
    turn = SPELLS[TURN_KEY][4]
    for r in w.rows("SELECT Spell_ID FROM Spell WHERE PackageID=? AND SpellID>=? AND SpellID<? AND (SpellID-?)%16=? AND Description<>?",
                    PACKAGE, FIRST_SPELL, FIRST_SPELL + 16 * 8, FIRST_SPELL, TURN_KEY, turn):
        w.out.append({"table": "Spell", "key": r[0], "set": dict(Description=turn)})
    for r in ascendant_rows(w):
        w.out.append({"table": "Spell", "key": r[0], "set": dict(Description=ASCENDANT, Value=ASCENDANT_MODEL)})
    return w.out


def main():
    db_path, out_path = Path(sys.argv[1]), Path(sys.argv[2])
    with sqlite3.connect(db_path.resolve().as_uri() + "?mode=ro", uri=True) as db:
        edits = edits_for(db)
    out_path.write_text(json.dumps(edits, indent=1))
    print(f"{len(edits)} spell updates")


if __name__ == "__main__":
    main()
