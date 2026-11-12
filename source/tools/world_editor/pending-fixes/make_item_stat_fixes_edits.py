"""Item stat fixes: items we have whose stats differ from the official 1.126 record, set to official.

    python -I make_item_stat_fixes_edits.py <world.db> <daoc_item_database_combined.zip> <eod-db-public/data> <ArtifactEncounters.Data.cs>

Follows the 2026-11-08 missing-items survey (Research\\Audits\\Missing-Items-2026-11-08), which found ~2,100-2,800
items per realm with stats that differ from official. Reuses make_toa_named_drops_edits.py's matching (same name,
realm and type), bonus converter and spell builder, like make_official_item_fixes_edits.py, which only fixed import
errors. This pass also takes the "other version" items that one listed for the owner (owner asked for the stats to
be brought up to date, 2026-11-08).

Which rows: plain import rows only (packages Public_DB, dolpubdb, ClassicQuest, none), never an artifact, a Mythirian
(slot 37), a Stormforged/Sovereign item, a ToA named drop (the ToA patch set those), or a row that a non-official
World Workshop patch created or changed (class pass, remakes, loot, crafting ... stay as their author left them).

What changes, each only where ours differs:
- Bonus1-10 (+ ExtraBonus) set to the official lines, when every official code converts and they fit in 10 lines.
  No Champion Level requirement is ever set.
- Procs and charges set to official when ours has fewer and every official spell exists or can be built (800000+id).
- Weapons: DPS_AF = official DPS x 10, SPD_ABS = speed x 10. Armour: DPS_AF = AF, SPD_ABS = absorption.
  Shields: DPS_AF/SPD_ABS as weapons, Type_Damage = shield size.
  DPS/AF is only corrected within 10% (or 3 points) of ours. A bigger gap means the official record is another
  level's version of the item (the export has no item level) or a placeholder (DPS 3.0 / AF 16): the whole item is
  then left alone and listed. Quality is not changed (the official base quality is a drop baseline).
- Realm 0 for items that are all-realm officially but stored for one realm, when no other realm has its own copy
  and the item has no class list.
Item level is not changed: the export has no item level.
Applies nothing; writes item-stat-fixes-<date>/ with edits, summary and a per-item CSV.
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
OUT = os.path.join(HERE, f'item-stat-fixes-{DATE}')
PACKAGES = {'public_db', 'dolpubdb', 'classicquest', ''}
PROTECT_CLASSES = {'64', '65', '66', '67'}
# World Workshop patches built from the same official export: rows they touched may be refined further.
OFFICIAL_PATCHES = {'20261108-174347-393dec3c', '20261108-174351-673e2f66', '20261108-194549-cadd6694'}
WEAPON_OT = set(range(1, 29))

backups = os.path.join(os.path.dirname(os.path.dirname(M.db_path.replace('\\', '/'))), 'world-workshop-data', 'backups')
touched = collections.defaultdict(set)
for p in glob.glob(os.path.join(backups, '*', 'patch.json')):
    pid = os.path.basename(os.path.dirname(p))
    if pid in OFFICIAL_PATCHES:
        continue
    try:
        d = json.load(open(p, encoding='utf8'))
    except (OSError, ValueError):
        continue
    for c in d.get('changes', []):
        if c.get('table') == 'ItemTemplate':
            touched[str(c.get('key')).lower()].add(pid)
toa = set(M.updates) | set(M.created)

names_by_realm = collections.defaultdict(set)   # (norm name, Object_Type) -> realms with a world copy
for w in M.W.values():
    names_by_realm[(M.norm(w['Name']), w['Object_Type'])].add(w['Realm'])


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


def near(off, ours):
    """Same item at the same level: within 10% (or 3 points) of ours. A bigger gap means the official record is
    another level's version of the item (or an export placeholder such as DPS 3.0 / AF 16)."""
    return abs(off - ours) <= max(3, 0.1 * ours)


edits, report, new_spells = [], [], []
skipped, changed_fields, held = collections.Counter(), collections.Counter(), collections.Counter()
for wid, w in sorted(M.W.items()):
    if wid in toa or wid in M.ARTIFACT_ITEMS:
        skipped['ToA named drop or artifact'] += 1
        continue
    if (w['PackageID'] or '').lower() not in PACKAGES:
        skipped['own package'] += 1
        continue
    if w['Item_Type'] == 37:
        skipped['mythirian'] += 1
        continue
    if set(str(w['AllowedClasses'] or '').replace(',', ';').split(';')) & PROTECT_CLASSES:
        skipped['Stormforged/Sovereign'] += 1
        continue
    if wid in touched:
        skipped['changed by an earlier non-official patch'] += 1
        continue
    o = M.best_official_for(w)
    if not o:
        continue
    s, notes = {}, []
    td, cat = o.get('type_data') or {}, o['category']
    far = []
    if cat in (1, 3) and w['Object_Type'] in WEAPON_OT | {42}:
        dps, spd = td.get('dps'), td.get('speed')
        if dps:
            v = int(round(dps * 10))
            (s.__setitem__('DPS_AF', v) if near(v, w['DPS_AF']) else far.append(f'DPS {dps} vs ours {w["DPS_AF"] / 10}'))
        if spd:
            s['SPD_ABS'] = int(round(spd * 10))
        if cat == 3 and td.get('shield_size') in (1, 2, 3):
            s['Type_Damage'] = td['shield_size']
    elif cat == 2 and 31 <= w['Object_Type'] <= 38:
        af, ab_ = td.get('armor_factor'), td.get('absorption')
        if af:
            (s.__setitem__('DPS_AF', af) if near(af, w['DPS_AF']) else far.append(f'AF {af} vs ours {w["DPS_AF"]}'))
        if ab_ is not None:
            s['SPD_ABS'] = ab_
    if far:
        held['another level version (whole item left)'] += 1
        report.append({'Id_nb': w['Id_nb'], 'Name': w['Name'], 'Realm': w['Realm'], 'Object_Type': w['Object_Type'],
                       'Official id': o['id'], 'Changed': '', 'Before': '', 'After': '',
                       'Notes': 'official ' + ', '.join(far) + ': another level version or placeholder; whole item left'})
        continue

    lines, unknown = M.bonus_lines(o)
    if unknown:
        notes.append(f'official bonus codes not converted {unknown}; bonuses left')
    elif len(lines) > 10:
        notes.append('official item has more than 10 bonus lines; bonuses left')
    elif collections.Counter(dict(lines)) != M.wlines(w):
        for n in range(1, 11):
            p, v = lines[n - 1] if n <= len(lines) else (0, 0)
            s[f'Bonus{n}Type'], s[f'Bonus{n}'] = p, v
        s['ExtraBonusType'], s['ExtraBonus'] = 0, 0

    abil = o.get('abilities') or []
    have = sum(1 for k in ('SpellID', 'SpellID1', 'ProcSpellID', 'ProcSpellID1') if w[k])
    if len(abil) > have:
        if all(a.get('magic_type') in (1, 2, 6) for a in abil) and all(resolvable(a['spell']) for a in abil):
            ab, n2 = M.ability_set(o)
            s.update(ab)
            notes += n2
        else:
            notes.append('official abilities not buildable; left')

    if (o['realm'] == 0 and w['Realm'] != 0 and str(w['AllowedClasses'] or '0') in ('0', '')
            and names_by_realm[(M.norm(w['Name']), w['Object_Type'])] == {w['Realm']}):
        s['Realm'] = 0

    diff = {k: v for k, v in s.items() if w.get(k) != v}
    if diff:
        edits.append({'table': 'ItemTemplate', 'key': w['Id_nb'], 'set': diff})
        for g in {('Bonus lines' if k.startswith(('Bonus', 'ExtraBonus')) else
                   'Procs/charges' if k in ('SpellID', 'Charges', 'MaxCharges', 'SpellID1', 'Charges1', 'MaxCharges1',
                                            'ProcSpellID', 'ProcSpellID1', 'CanUseEvery') else k) for k in diff}:
            changed_fields[g] += 1
    if diff or notes:
        report.append({'Id_nb': w['Id_nb'], 'Name': w['Name'], 'Realm': w['Realm'], 'Object_Type': w['Object_Type'],
                       'Official id': o['id'], 'Changed': ' '.join(sorted(diff)),
                       'Before': ' '.join(f'{k}:{w.get(k)}' for k in sorted(diff)),
                       'After': ' '.join(f'{k}:{v}' for k, v in sorted(diff.items())), 'Notes': '; '.join(notes)})

for row in new_spells:
    edits.append({'table': 'Spell', 'insert': row})
    if row['SpellID'] not in M.line_x:
        edits.append({'table': 'LineXSpell', 'insert': {'LineXSpell_ID': M.uid('lxs', row['SpellID']), 'LineName': 'Item Effects',
                                                       'SpellID': row['SpellID'], 'Level': 1}})
        M.line_x.add(row['SpellID'])

os.makedirs(OUT, exist_ok=True)
json.dump(edits, open(os.path.join(OUT, f'item-stat-fixes-{DATE}.edits.json'), 'w'), indent=1)
with open(os.path.join(OUT, 'items.csv'), 'w', newline='', encoding='utf8') as fh:
    wr = csv.DictWriter(fh, fieldnames=list(report[0].keys()))
    wr.writeheader()
    wr.writerows(report)
summary = {
    'items changed': sum(1 for e in edits if e['table'] == 'ItemTemplate'),
    'items changed by realm': collections.Counter(M.W[e['key'].lower()]['Realm'] for e in edits if e['table'] == 'ItemTemplate'),
    'items with each kind of change': changed_fields,
    'values held back (far from ours)': held,
    'spells created': len(new_spells), 'skipped': skipped, 'edits': len(edits),
}
json.dump(summary, open(os.path.join(OUT, 'summary.json'), 'w'), indent=1, default=str)
print(json.dumps(summary, indent=1, default=str))
