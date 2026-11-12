"""World-wide item fixes from the official Herald export, outside the ToA named drops: World Workshop edits.

Same arguments as make_toa_named_drops_edits.py, whose matching and spell builder it reuses. Apply the ToA named
drops patch FIRST: this patch leaves the ToA items and spells to it and uses the spells it adds.

    python -I make_official_item_fixes_edits.py <world.db> <daoc_item_database_combined.zip> <eod-db-public/data> <ArtifactEncounters.Data.cs>

Only plain import rows are changed (packages Public_DB, dolpubdb, ClassicQuest, none), never a row an earlier World
Workshop patch touched, an artifact, a Mythirian, Atlas orb gear or our own packages. For each item with an
official twin (same name, realm and type):
- bonuses are set to the official lines only when the difference is an import error: the same values on other
  property numbers, and/or lines the official item has that ours lost. Items whose values differ (another version
  of the item) are only listed in the CSV, for the owner.
- procs and charges are set to the official ones when ours has fewer and every official spell exists or can be built.
"""
import collections
import contextlib
import csv
import glob
import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
with contextlib.redirect_stdout(io.StringIO()):
    import make_toa_named_drops_edits as M   # noqa: E402  (reads argv; builds the ToA plan in memory)

DATE = M.DATE
OUT = os.path.join(HERE, f'official-item-fixes-{DATE}')
PACKAGES = {'public_db', 'dolpubdb', 'classicquest', ''}
backups = os.path.join(os.path.dirname(os.path.dirname(M.db_path.replace('\\', '/'))), 'world-workshop-data', 'backups')
touched = set()
for p in glob.glob(os.path.join(backups, '*', 'patch.json')):
    try:
        d = json.load(open(p, encoding='utf8'))
    except (OSError, ValueError):
        continue
    for c in d.get('changes', []):
        if c.get('table') == 'ItemTemplate':
            touched.add(str(c.get('key')).lower())
toa = set(M.updates) | set(M.created)


def classify(ol, wl):
    if ol == wl:
        return 'same'
    a = {p: v for p, v in wl.items() if ol.get(p) != v}
    b = {p: v for p, v in ol.items() if wl.get(p) != v}
    moved = 0
    for p, v in list(a.items()):
        q = [x for x, y in b.items() if y == v]
        if len(q) == 1:
            moved += 1
            del a[p]
            del b[q[0]]
    if a:
        return 'other version'
    if not b:
        return 'renumbered'
    return 'renumbered + lines added' if moved else 'lines added'


def resolvable(sid):
    ours = 800000 + sid
    if ours in M.spells:
        return True
    row, why = M.build_spell(sid)
    if row is None:
        return False
    M.spells[ours] = row
    new_spells.append(row)
    return True


edits, report, new_spells = [], [], []
skipped = collections.Counter()
for wid, w in sorted(M.W.items()):
    if wid in toa or wid in M.ARTIFACT_ITEMS:
        continue
    if (w['PackageID'] or '').lower() not in PACKAGES:
        skipped['package'] += 1
        continue
    if wid in touched:
        skipped['touched by an earlier patch'] += 1
        continue
    o = M.best_official_for(w)
    if not o:
        continue
    lines, unknown = M.bonus_lines(o)
    ol, wl = collections.Counter(dict(lines)), M.wlines(w)
    kind = 'unknown official bonus codes' if unknown else classify(ol, wl)
    s = {}
    if kind in ('renumbered', 'lines added', 'renumbered + lines added') and len(lines) <= 10:
        for n in range(1, 11):
            p, v = lines[n - 1] if n <= len(lines) else (0, 0)
            s[f'Bonus{n}Type'], s[f'Bonus{n}'] = p, v
        s['ExtraBonusType'], s['ExtraBonus'] = 0, 0
    abil = o.get('abilities') or []
    have = sum(1 for k in ('SpellID', 'SpellID1', 'ProcSpellID', 'ProcSpellID1') if w[k])
    abil_note = ''
    if len(abil) > have:
        if all(a.get('magic_type') in (1, 2, 6) for a in abil) and all(resolvable(a['spell']) for a in abil):
            ab, notes = M.ability_set(o)
            s.update(ab)
            abil_note = 'abilities set to official'
        else:
            abil_note = 'official abilities not buildable; left as is'
    diff = {k: v for k, v in s.items() if w.get(k) != v}
    if diff:
        edits.append({'table': 'ItemTemplate', 'key': w['Id_nb'], 'set': diff})
    if diff or kind != 'same' or abil_note:
        report.append({'Id_nb': w['Id_nb'], 'Name': w['Name'], 'Level': w['Level'], 'Realm': w['Realm'], 'Package': w['PackageID'],
                       'Bonuses': kind, 'Changed': 'yes' if diff else 'no', 'Abilities': abil_note, 'Official id': o['id'],
                       'Before': ' '.join(f'{k}:{v}' for k, v in sorted(wl.items())), 'Official': ' '.join(f'{k}:{v}' for k, v in lines)})
for row in new_spells:
    edits.append({'table': 'Spell', 'insert': row})
    if row['SpellID'] not in M.line_x:
        edits.append({'table': 'LineXSpell', 'insert': {'LineXSpell_ID': M.uid('lxs', row['SpellID']), 'LineName': 'Item Effects', 'SpellID': row['SpellID'], 'Level': 1}})
        M.line_x.add(row['SpellID'])
# Item spells the ToA patch adds must already be in its Item Effects line; nothing else to link.

os.makedirs(OUT, exist_ok=True)
json.dump(edits, open(os.path.join(OUT, f'official-item-fixes-{DATE}.edits.json'), 'w'), indent=1)
with open(os.path.join(OUT, 'items.csv'), 'w', newline='', encoding='utf8') as fh:
    wr = csv.DictWriter(fh, fieldnames=list(report[0].keys()))
    wr.writeheader()
    wr.writerows(report)
summary = {
    'items changed': sum(1 for e in edits if e['table'] == 'ItemTemplate'),
    'by bonus kind (all listed)': collections.Counter(r['Bonuses'] for r in report),
    'changed by bonus kind': collections.Counter(r['Bonuses'] for r in report if r['Changed'] == 'yes'),
    'abilities': collections.Counter(r['Abilities'] for r in report if r['Abilities']),
    'spells created': len(new_spells), 'skipped': skipped, 'edits': len(edits),
}
json.dump(summary, open(os.path.join(OUT, 'summary.json'), 'w'), indent=1, default=str)
print(json.dumps(summary, indent=1, default=str))
