"""Trim restored EoD loot tables whose monsters are far below the gear in them: World Workshop edits.

    python -I make_eod_loot_level_trim_edits.py <world.db>

Reads the world read-only. For each table the eod-missing-loot-tables patch added (its LootTemplate_ID rows), the
monster level is the highest live level of any monster using the table (the NpcTemplate level wins over the spawn
row, as in game). Tables whose best gear is 20+ levels above that are trimmed: every gear row (Object_Type not 0)
more than 10 levels above the monster goes. Junk, quest and crafting items stay. Owner OK 2026-11-08 ("Yes, trim
them") after the AI tester saw level 5 arachite greensilk drop level 38-40 Svartalf jewelry.
"""
import collections
import json
import os
import re
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DATE = '2026-11-08'
FLAG_GAP, KEEP_ABOVE = 20, 10
OUT = os.path.join(HERE, f'eod-loot-level-trim-{DATE}')

con = sqlite3.connect('file:' + sys.argv[1].replace('\\', '/') + '?mode=ro', uri=True)
added = json.load(open(os.path.join(HERE, f'eod-missing-loot-tables-{DATE}', f'eod-missing-loot-tables-{DATE}.edits.json')))
added_ids = {e['insert']['LootTemplate_ID'] for e in added}
rows = collections.defaultdict(list)
for lid, t, iid in con.execute('SELECT LootTemplate_ID, TemplateName, ItemTemplateID FROM LootTemplate'):
    if lid in added_ids:
        rows[t.lower()].append((lid, iid))
items = {r[0].lower(): (r[1], r[2], r[3]) for r in con.execute('SELECT Id_nb, Name, Level, Object_Type FROM ItemTemplate')}
tpl = {r[0]: r[1] for r in con.execute('SELECT TemplateId, Level FROM NpcTemplate')}
live = collections.defaultdict(int)
for name, level, tid, flags in con.execute('SELECT Name, Level, NPCTemplateID, Flags FROM Mob'):
    if flags & 16:   # peaceful (e.g. Rheton, L1 in region 244): cannot be killed, its table never drops; keep it whole
        continue
    nums = [int(x) for x in re.findall(r'\d+', str(tpl.get(tid) or ''))]
    live[name.lower()] = max(live[name.lower()], max(nums) if nums else level)
users = collections.defaultdict(list)
for mob, t in con.execute('SELECT MobName, LootTemplateName FROM MobXLootTemplate'):
    users[t.lower()].append(mob.lower())

edits, report = [], []
for t, rs in sorted(rows.items()):
    mobs = [m for m in users[t] if m in live]
    if not mobs:
        continue
    top = max(live[m] for m in mobs)
    gear = [(lid, iid) + items[iid.lower()] for lid, iid in rs if iid.lower() in items and items[iid.lower()][2] != 0]
    if not gear or max(g[3] for g in gear) - top < FLAG_GAP:
        continue
    for lid, iid, name, level, _ in gear:
        if level > top + KEEP_ABOVE:
            edits.append({'table': 'LootTemplate', 'delete': lid})
            report.append({'table': t, 'monster level': top, 'item': name, 'item level': level})
os.makedirs(OUT, exist_ok=True)
json.dump(edits, open(os.path.join(OUT, f'eod-loot-level-trim-{DATE}.edits.json'), 'w'), indent=1)
json.dump(report, open(os.path.join(OUT, 'removed.json'), 'w'), indent=1)
by = collections.Counter(r['table'] for r in report)
print(json.dumps({'tables trimmed': len(by), 'rows removed': len(edits), 'by table': by}, indent=1))
