"""Plan: remove the doubled NPCs the 2026-11-09 Catacombs imports left (read-only generator).

EoD db-public holds two capture layers for Catacombs: the older one (numeric Mob_IDs) and a newer one (PackageID
"Yggdrasil"). They share no names, but in two ways they describe the same thing twice:

 1. Hub NPCs captured again under a slightly different name, as neutral level 80 placeholders that show purple:
    "Explorer Tegena" beside our "Explorer Tegana", "Sergeant Urba" on "Guard Ubra", "Guard Gonnik" by "Sergeant
    Gonnik" (Burial Grounds entrance, seen by the owner in game). The placeholder goes.
 2. Several rows on exactly the same spot: alternates of one spawn point (live spawns one of them), e.g. Viking Guard,
    Dwarven Guard and Troll Guard standing inside each other. One stays (a realm NPC first, then the older layer,
    then the lowest id). A row that would take its name out of the region is kept and moved 90 units aside instead.
Ambient critters and untargetable rows (Flags 4 or 8) are left alone.

usage: python -I make_catacombs_doubles_edits.py <world.sqlite3.db> <out.edits.json> [report.csv]
"""
import csv
import difflib
import json
import sqlite3
import sys
from collections import Counter, defaultdict

REGIONS = [58, 59, 63, 65, 66, 67, 68, 92, 93, 94, 95, 96, 97, 99, 148, 149, 162, 189, 195, 197, 243,
           109, 226, 227, 228, 229, 376, 390, 429, 430, 433, 436, 339, 335, 394, 360, 395]


def main():
    world, out = sys.argv[1], sys.argv[2]
    report = sys.argv[3] if len(sys.argv) > 3 else None
    db = sqlite3.connect(f'file:{world}?mode=ro', uri=True)
    db.row_factory = sqlite3.Row
    rows = db.execute(f"SELECT Mob_ID, Name, Realm, Level, Flags, ClassType, Region, X, Y, Z, PackageID FROM Mob "
                      f"WHERE Region IN ({','.join(map(str, REGIONS))})").fetchall()
    newer = lambda r: r['PackageID'] == 'Yggdrasil'
    edits, log, gone = [], [], set()

    # 1. neutral peace placeholders that double a realm NPC of nearly the same name
    realm_npcs = [r for r in rows if r['Realm'] and not newer(r)]
    for r in rows:
        if not (newer(r) and r['Realm'] == 0 and (r['Flags'] or 0) & 16 and r['ClassType'] == 'DOL.GS.GameNPC'
                and (r['Name'] or ' ')[0].isupper() and (r['Level'] or 0) >= 50):
            continue
        for o in realm_npcs:
            if o['Region'] != r['Region'] or (o['X'] - r['X']) ** 2 + (o['Y'] - r['Y']) ** 2 > 600 ** 2:
                continue
            a, b = r['Name'].lower(), o['Name'].lower()
            if max(difflib.SequenceMatcher(None, a, b).ratio(),
                   difflib.SequenceMatcher(None, a.split()[-1], b.split()[-1]).ratio()) >= 0.75:
                edits.append({'table': 'Mob', 'delete': r['Mob_ID']})
                gone.add(r['Mob_ID'])
                log.append(dict(action='delete placeholder', region=r['Region'], name=r['Name'], kept=o['Name'], x=r['X'], y=r['Y'], mob_id=r['Mob_ID']))
                break

    # 2. exact same-spot stacks among visible, targetable rows
    names = Counter((r['Region'], (r['Name'] or '').lower()) for r in rows if r['Mob_ID'] not in gone)
    spots = defaultdict(list)
    for r in rows:
        if r['Mob_ID'] not in gone and not (r['Flags'] or 0) & 12:
            spots[(r['Region'], r['X'], r['Y'], r['Z'])].append(r)
    for stack in spots.values():
        if len(stack) < 2:
            continue
        stack.sort(key=lambda r: (0 if r['Realm'] else 1, 1 if newer(r) else 0, str(r['Mob_ID'])))
        keep = stack[0]
        for i, r in enumerate(stack[1:], 1):
            key = (r['Region'], (r['Name'] or '').lower())
            if names[key] <= 1:
                edits.append({'table': 'Mob', 'key': r['Mob_ID'], 'set': {'X': r['X'] + 90 * i}})
                log.append(dict(action='move aside (only one of its name)', region=r['Region'], name=r['Name'], kept=keep['Name'], x=r['X'], y=r['Y'], mob_id=r['Mob_ID']))
            else:
                names[key] -= 1
                edits.append({'table': 'Mob', 'delete': r['Mob_ID']})
                log.append(dict(action='delete stacked alternate', region=r['Region'], name=r['Name'], kept=keep['Name'], x=r['X'], y=r['Y'], mob_id=r['Mob_ID']))

    with open(out, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(edits, f, indent=0, ensure_ascii=False)
        f.write('\n')
    if report:
        with open(report, 'w', encoding='utf-8', newline='') as f:
            w = csv.DictWriter(f, fieldnames=['action', 'region', 'name', 'kept', 'x', 'y', 'mob_id'])
            w.writeheader()
            w.writerows(log)
    print(len(edits), 'edits:', dict(Counter(l['action'] for l in log)))
    for l in log:
        if l['action'] != 'delete stacked alternate' or l['region'] == 229:
            print(' ', l['action'], '|', l['region'], l['name'], '| stays:', l['kept'])


if __name__ == '__main__':
    main()
