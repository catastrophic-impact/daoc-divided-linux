"""Plan: make the Catacombs obelisks work (DR-233). Read-only generator.

Every "Obelisk" NPC in a Catacombs region becomes a DOL.GS.CatacombsObelisk (needs a server with that class) and
gets Flags 16 (peace): the imported rows carry 20 or 28, which hide the name and, with 28, stop players targeting
the stone at all. "Obelisk of Draining" (the Darkspire encounter objects) is left alone.

usage: python -I make_catacombs_obelisks_edits.py <world.sqlite3.db> <out.edits.json>
"""
import json
import sqlite3
import sys

CLASS = 'DOL.GS.CatacombsObelisk'
MODELS = {1878: 'Albion', 1823: 'Midgard', 1863: 'Hibernia'}


def main():
    world, out = sys.argv[1], sys.argv[2]
    db = sqlite3.connect(f'file:{world}?mode=ro', uri=True)
    db.row_factory = sqlite3.Row
    edits, by_realm = [], {}
    for m in db.execute("SELECT m.Mob_ID, m.Region, m.Model, m.Flags, m.ClassType FROM Mob m JOIN Regions r ON r.RegionID = m.Region "
                        "WHERE m.Name = 'Obelisk' AND r.Expansion = 3 ORDER BY m.Region"):
        change = {}
        if m['ClassType'] != CLASS:
            change['ClassType'] = CLASS
        if int(m['Flags'] or 0) != 16:
            change['Flags'] = 16
        if change:
            edits.append({'table': 'Mob', 'key': m['Mob_ID'], 'set': change})
        by_realm.setdefault(MODELS.get(int(m['Model']) & 0x7FFF, 'no realm'), []).append(m['Region'])
    with open(out, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(edits, f, indent=0)
        f.write('\n')
    print(len(edits), 'edits;', {k: sorted(v) for k, v in by_realm.items()})


if __name__ == '__main__':
    main()
