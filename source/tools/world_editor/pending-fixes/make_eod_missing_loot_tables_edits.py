"""Loot tables the world's MobXLootTemplate rows name but that were never imported, outside ToA: World Workshop edits.

Same arguments as make_toa_named_drops_edits.py (it leaves the ToA tables to that patch). The MobXLootTemplate
rows came from Eve of Darkness db-public (MIT), whose LootTemplate.json has the tables; only rows whose item exists
in the world are added. Applies nothing.
"""
import collections
import contextlib
import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
with contextlib.redirect_stdout(io.StringIO()):
    import make_toa_named_drops_edits as M   # noqa: E402

OUT = os.path.join(HERE, f'eod-missing-loot-tables-{M.DATE}')
done = set(M.tables)   # tables the ToA patch adds
edits, missing_items, no_source = [], collections.Counter(), set()
spawned = collections.Counter(n.lower() for (n,) in M.con.execute('SELECT Name FROM Mob'))
tables = collections.OrderedDict()
for mob, rows in sorted(M.w_mx.items()):
    for r in rows:
        t = r['LootTemplateName'].lower()
        if t in M.w_lt or t in done or t in tables:
            continue
        if t not in M.e_lt:
            no_source.add(t)
            continue
        tab = tables.setdefault(t, {'mobs': set(), 'rows': {}})
        tab['mobs'].add(mob)
        for e in M.e_lt[t]:
            iid = e['ItemTemplateID'].lower()
            if iid not in M.W:
                missing_items[e['ItemTemplateID']] += 1
                continue
            tab['rows'].setdefault(iid, (int(e['Chance']), int(e.get('Count') or 1)))
for t, tab in tables.items():
    for iid, (ch, cnt) in sorted(tab['rows'].items()):
        edits.append({'table': 'LootTemplate', 'insert': {'LootTemplate_ID': M.uid('eod-lt', t, iid), 'TemplateName': t,
                                                          'ItemTemplateID': M.W[iid]['Id_nb'], 'Chance': ch, 'Count': cnt}})
os.makedirs(OUT, exist_ok=True)
json.dump(edits, open(os.path.join(OUT, f'eod-missing-loot-tables-{M.DATE}.edits.json'), 'w'), indent=1)
levels = collections.Counter()
for t, tab in tables.items():
    for mob in tab['mobs']:
        for (lv,) in M.con.execute('SELECT MAX(Level) FROM Mob WHERE Name = ?', (mob,)):
            levels[(lv or 0) // 10 * 10] += 1
summary = {'tables added': sum(1 for t in tables.values() if t['rows']), 'rows': len(edits),
           'tables whose monsters are spawned': sum(1 for t in tables.values() if t['rows'] and any(spawned[m] for m in t['mobs'])),
           'monster level bands (max level, by tens)': dict(sorted(levels.items())),
           'EoD rows naming items missing from the world': sum(missing_items.values()), 'tables with no EoD source': len(no_source)}
json.dump(summary, open(os.path.join(OUT, 'summary.json'), 'w'), indent=1)
print(json.dumps(summary, indent=1))
