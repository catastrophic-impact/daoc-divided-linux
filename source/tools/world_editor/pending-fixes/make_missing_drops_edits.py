"""Missing drops: official 1.126 monster drops for monsters already spawned in our world, as World Workshop edits.

    python -I make_missing_drops_edits.py <world.db> <daoc_item_database_combined.zip> <eod-db-public/data> <ArtifactEncounters.Data.cs>

Step 3 of the 2026-11-08 missing-items survey (Research\\Audits\\Missing-Items-2026-11-08): ~1,300-1,700 missing items
per realm drop officially from monsters we already spawn. Reuses make_toa_named_drops_edits.py (M): its matching,
bonus converter, ability columns and item spell builder. ToA monsters are left to the ToA patch (already applied).

For every weapon, armour, shield, instrument and jewelry item the export lists as a normal or one-time drop of a
hostile (realm 0) monster name we spawn:
- Item: our matching item (same name, realm and type) if we have one, else a new item copied from a world item of
  the same object type, slot and realm close in level (for model, weight and colour; the Eve of Darkness db-public
  twin's model is used when it has one), given the official name, bonuses, procs/charges, class list, DPS/speed or
  AF/absorption, shield size and bonus level. Package OfflineMissingDrops.
  Level: weapons and shields from DPS (16.5 = 51, 0.3 per level), armour from AF (cloth = level, others = 2 x level),
  jewelry from the official level requirement, else the monster's level. Export placeholders (DPS 3.0 / AF 16 on a
  monster of level 25+) take the monster's level and the standard DPS/AF for it. No Champion Level requirement.
- Drop: one-time drops go to LootOTD. Normal drops go to the monster's own LootTemplate table, or, when several
  monsters have the same official list, one shared "official drops <hash>" table they each get a MobXLootTemplate
  row for (never a family table). A monster that had an own table and no MobX row gets a row for its own table
  too, since the loot code reads only named tables once a monster has one. At most one random template item drops
  a kill (LootList DropCount 1); chances aim at one new item in ~20% of kills (named, 1-2 spawns: ~50%), 1-15%.
- Item spells: official spell X = our 800000 + X in Item Effects, built from the delve; an item whose spell cannot be
  built keeps no dead id.
Never touched: artifacts, ToA monsters, items of our own packages (Atlas orb gear, bounty crafting ...), existing
rows (inserts only).
Applies nothing; writes missing-drops-<date>/ with two edits files (part1, part2: a patch holds at most 20,000
edits), summary, items.csv and spells.csv.
"""
import collections
import contextlib
import csv
import io
import json
import os
import re
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
with contextlib.redirect_stdout(io.StringIO()):
    import make_toa_named_drops_edits as M   # noqa: E402  (reads argv; builds the ToA plan in memory)

DATE = M.DATE
OUT = os.path.join(HERE, f'missing-drops-{DATE}')
PKG = 'OfflineMissingDrops'
CATS = {1, 2, 3, 4, 5}
con = sqlite3.connect('file:' + M.db_path.replace('\\', '/') + '?mode=ro', uri=True)
con.row_factory = sqlite3.Row

# ---------------- world monsters and their tables ----------------
mob_level = collections.defaultdict(int)
mob_spawns = collections.Counter()
for r in con.execute('SELECT Name, Level FROM Mob WHERE Realm = 0'):
    n = (r['Name'] or '').lower()
    mob_level[n] = max(mob_level[n], r['Level'] or 0)
    mob_spawns[n] += 1
lt = collections.defaultdict(dict)   # table -> {item id lower: row}
for r in con.execute('SELECT * FROM LootTemplate'):
    lt[r['TemplateName'].lower()][r['ItemTemplateID'].lower()] = dict(r)
mx = collections.defaultdict(list)
for r in con.execute('SELECT * FROM MobXLootTemplate'):
    mx[r['MobName'].lower()].append(dict(r))
otd = set((r[0].lower(), r[1].lower()) for r in con.execute('SELECT MobName, ItemTemplateID FROM LootOTD'))

eod_by_name = collections.defaultdict(list)
for r in M.eod('ItemTemplate.json'):
    eod_by_name[(M.norm(r.get('Name')), int(r.get('Realm') or 0))].append(r)


def reachable(mob):
    """Every item a kill of this monster can already roll (its MobX tables, or its own table without MobX)."""
    rows = mx.get(mob)
    names = {(r['LootTemplateName'] or mob).lower() for r in rows} if rows else {mob}
    out = set()
    for t in names:
        out |= set(lt.get(t, {}))
    return out


# ---------------- level and stats for new items ----------------
def level_from(i, mob):
    td, cat = i.get('type_data') or {}, i['category']
    req = (i.get('requirements') or {}).get('level_required')
    ml = max(1, min(51, mob_level.get(mob, 0) or 1))
    if cat in (1, 3) and td.get('dps'):
        if td['dps'] <= 3.0 and ml >= 25:
            return ml, 'placeholder DPS; monster level'
        return max(1, min(51, round((td['dps'] - 1.2) / 0.3))), 'from DPS'
    if cat == 2 and td.get('armor_factor'):
        if td['armor_factor'] <= 16 and ml >= 25:
            return ml, 'placeholder AF; monster level'
        cloth = M.official_type(i)[0] == 32
        return max(1, min(51, td['armor_factor'] if cloth else round(td['armor_factor'] / 2))), 'from AF'
    if req:
        return max(1, min(51, req)), 'level requirement'
    return ml, 'monster level'


def donor_for(i, level):
    ot, it, _ = M.official_type(i)
    if ot is None:
        return None
    words = set(w for w in M.norm(i['name']).split() if w not in ('of', 'the'))
    best, score = None, None
    for w in M.W.values():
        if w['Object_Type'] != ot or (it is not None and w['Item_Type'] != it) or not w['Model']:
            continue
        if not M.realm_ok(i['realm'], w['Realm']) or w['Id_nb'].lower() in M.ARTIFACT_ITEMS:
            continue
        s = (-abs((w['Level'] or 0) - level), len(words & set(M.norm(w['Name']).split())), w['Realm'] == i['realm'],
             (w['PackageID'] or '').lower() in ('public_db', 'dolpubdb', ''))
        if score is None or s > score or (s == score and w['Id_nb'] < best['Id_nb']):
            best, score = w, s
    return best


def new_item_set(i, mob, d):
    s, notes = M.item_set(i)
    ot, it, hand = M.official_type(i)
    td, cat = i.get('type_data') or {}, i['category']
    level, how = level_from(i, mob)
    req = i.get('requirements') or {}
    s.update({'Name': i['name'], 'Realm': i['realm'], 'PackageID': PKG, 'Level': level, 'BonusLevel': i.get('bonus_level') or 0,
              'Description': f'Official drop (item {i["id"]})', 'IsPickable': 1, 'CanDropAsLoot': 1,
              'Quality': td.get('base_quality') or 100, 'LevelRequirement': req.get('level_required') or 0,
              'AllowedClasses': ';'.join(str(c) for c in req['usable_by']) if req.get('usable_by') else '0',
              'Condition': 50000, 'MaxCondition': 50000, 'Durability': 50000, 'MaxDurability': 50000,
              'TranslationId': '', 'ClassType': '', 'Emblem': 0, 'Effect': 0, 'ProcChance': 0,
              'PoisonSpellID': 0, 'PoisonCharges': 0, 'PoisonMaxCharges': 0, 'Bonus': 0, 'MaxCount': 1, 'PackSize': 1,
              'IsIndestructible': 0, 'Flags': 0, 'SalvageYieldID': 0})
    if i.get('sell_value'):
        s['Price'] = int(i['sell_value']) * 2
    if ot is not None:
        s['Object_Type'] = ot
    if it is not None:
        s['Item_Type'] = it
    if cat in (1, 3):
        dps = td.get('dps') or 0
        s['DPS_AF'] = int(round((1.2 + 0.3 * level) * 10)) if 'placeholder' in how or not dps else int(round(dps * 10))
        if td.get('speed'):
            s['SPD_ABS'] = int(round(td['speed'] * 10))
        if cat == 1:
            if td.get('damage_type') in (1, 2, 3):
                s['Type_Damage'] = td['damage_type']
            if hand is not None:
                s['Hand'] = hand
        elif td.get('shield_size') in (1, 2, 3):
            s['Type_Damage'] = td['shield_size']
    elif cat == 2:
        cloth = s.get('Object_Type') == 32
        af = td.get('armor_factor') or 0
        s['DPS_AF'] = (level if cloth else level * 2) if 'placeholder' in how or not af else af
        if td.get('absorption') is not None:
            s['SPD_ABS'] = td['absorption']
    twin = next(iter(eod_by_name.get((M.norm(i['name']), i['realm']), [])), None)
    if twin and int(twin.get('Model') or 0):
        s['Model'] = int(twin['Model'])
        for k in ('Color', 'Effect', 'Weight', 'Extension'):
            if twin.get(k) not in (None, ''):
                s[k] = int(twin[k])
        notes.append(f'model from EoD twin {twin.get("Id_nb")}')
    else:
        notes.append(f'model from {d["Id_nb"]}')
    notes.append(f'level {level} ({how})')
    return s, notes


# ---------------- collect official drops on our monsters ----------------
drops = collections.defaultdict(list)   # mob -> [(kind, official item)]
for i in M.OFF:
    if i['category'] not in CATS:
        continue
    for kind, lst in ((i.get('sources') or {}).get('monsters') or {}).items():
        if kind not in ('normal_drop', 'one_time_drop'):
            continue
        for m in lst:
            m = m.lower()
            if m in mob_level and m not in M.all_toa:
                drops[m].append((kind, i))

edits, report, spell_report = [], [], []
created = {}        # new id lower -> id
made_for = {}       # (official id) -> world id lower
skipped = collections.Counter()
W_names = set(M.W)
our_need = set()   # official spell ids the new items use


def slug(s):
    return re.sub(r'[^a-z0-9]+', '_', s.lower()).strip('_')[:60]


def world_item_for(i, mob):
    if i['id'] in made_for:
        return made_for[i['id']]
    w = M.best_world_for(i)
    if w:
        if (w['PackageID'] or '').lower() not in ('public_db', 'dolpubdb', 'classicquest', ''):
            # Our own systems' items (Atlas orb gear, bounty crafting, ...) stay where those systems give them.
            skipped[f'our item belongs to package {w["PackageID"]}'] += 1
            made_for[i['id']] = None
            return None
        made_for[i['id']] = w['Id_nb'].lower()
        return made_for[i['id']]
    lvl, _ = level_from(i, mob)
    d = donor_for(i, lvl)
    if not d:
        skipped['no donor of that type'] += 1
        return None
    nid = f"md_{slug(i['name'])}_{M.REALM_TAG[i['realm']]}"
    n, k = nid, 2
    while n.lower() in W_names or n.lower() in created:
        n, k = f'{nid}{k}', k + 1
    s, notes = new_item_set(i, mob, d)
    for a in i.get('abilities') or []:
        our_need.add(a['spell'])
    edits.append({'table': 'ItemTemplate', 'copy': d['Id_nb'], 'key': n, 'set': s})
    created[n.lower()] = n
    made_for[i['id']] = n.lower()
    report.append({'Id_nb': n, 'Name': i['name'], 'Realm': i['realm'], 'Level': s['Level'], 'Object_Type': s.get('Object_Type'),
                   'Result': 'new', 'Official id': i['id'], 'Monster': mob,
                   'Bonuses': ' '.join(f'{k}:{v}' for k, v in M.bonus_lines(i)[0]), 'Notes': '; '.join(notes)})
    return n.lower()


plan = collections.defaultdict(dict)    # mob -> {world id lower: realm}
for mob, lst in sorted(drops.items()):
    have = reachable(mob)
    for kind, i in lst:
        wid = world_item_for(i, mob)
        if not wid:
            continue
        if wid in M.ARTIFACT_ITEMS:
            skipped['artifact'] += 1
            continue
        idnb = created.get(wid) or M.W[wid]['Id_nb']
        if kind == 'one_time_drop':
            if (mob, wid) not in otd:
                otd.add((mob, wid))
                edits.append({'table': 'LootOTD', 'insert': {'LootOTD_ID': M.uid('mdotd', mob, wid), 'MobName': mob,
                                                            'ItemTemplateID': idnb, 'MinLevel': 0}})
            continue
        if wid in have:
            skipped['already drops there'] += 1
            continue
        plan[mob][wid] = (idnb, i['realm'])

# Monsters with the same official list share one table (Darkness Falls monsters all drop the same 166 items).
# The loot code pools every table's random rolls and keeps at most one winner a kill (LootList DropCount 1), so the
# chance is set from how many of the items one realm can roll: a common monster gives one of them about 20% of
# kills, a named one (1-2 spawns) about 50%. Chances are whole percents: never under 1, never over 15.
import hashlib
groups = collections.defaultdict(list)   # (items, named) -> [mob]
for mob, items in plan.items():
    groups[(tuple(sorted(items)), mob_spawns[mob] <= 2)].append(mob)
lt_rows = mx_rows = 0
per_kill = collections.Counter()
for (key, named), mobs in sorted(groups.items(), key=lambda kv: kv[1]):
    items = plan[mobs[0]]
    per_realm = collections.Counter()
    for wid in key:
        realm = items[wid][1]
        for r in ((1, 2, 3) if realm == 0 else (realm,)):
            per_realm[r] += 1
    n = max(per_realm.values())
    target = 0.5 if named else 0.2
    chance = max(1, min(15, round(100 * (1 - (1 - target) ** (1 / n)))))
    p_any = 1 - (1 - chance / 100) ** n
    per_kill[f'{"named" if named else "common"} {int(p_any * 10) * 10}-{int(p_any * 10) * 10 + 9}%'] += len(mobs)
    if len(mobs) == 1:
        table = mobs[0]
    else:
        table = 'official drops ' + hashlib.sha1('|'.join(key).encode()).hexdigest()[:10] + (' named' if named else '')
    for wid in key:
        edits.append({'table': 'LootTemplate', 'insert': {'LootTemplate_ID': M.uid('mdlt', table, wid), 'TemplateName': table,
                                                         'ItemTemplateID': items[wid][0], 'Chance': chance, 'Count': 1}})
        lt_rows += 1
    for mob in mobs:
        rows = mx.get(mob, [])
        names = {(r['LootTemplateName'] or '').lower() for r in rows}
        if table == mob and (not rows or mob in names):
            continue   # its own table is already read
        if not rows and mob in lt:
            # Its own table was read without a MobX row; once it has one, only named tables are read: keep it.
            edits.append({'table': 'MobXLootTemplate', 'insert': {'MobXLootTemplate_ID': M.uid('mdmx', mob, mob), 'MobName': mob,
                                                                 'LootTemplateName': mob, 'DropCount': 1}})
            mx_rows += 1
        if table not in names:
            edits.append({'table': 'MobXLootTemplate', 'insert': {'MobXLootTemplate_ID': M.uid('mdmx', mob, table), 'MobName': mob,
                                                                 'LootTemplateName': table, 'DropCount': 1}})
            mx_rows += 1

# ---------------- spells ----------------
made_spells, failed = 0, []
for sid in sorted(our_need):
    ours = 800000 + sid
    if ours in M.spells:
        continue
    row, why = M.build_spell(sid)
    if row is None:
        failed.append((sid, why))
        continue
    edits.append({'table': 'Spell', 'insert': row})
    M.spells[ours] = row
    made_spells += 1
    spell_report.append({'Official spell': sid, 'Our spell': ours, 'Type': row['Type'], 'Copied from': why, 'Damage': row['Damage'],
                         'Value': row['Value'], 'Duration': row['Duration'], 'Target': row['Target'], 'Description': row['Description']})
for sid in sorted(our_need):
    ours = 800000 + sid
    if ours in M.spells and ours not in M.line_x:
        edits.append({'table': 'LineXSpell', 'insert': {'LineXSpell_ID': M.uid('lxs', ours), 'LineName': 'Item Effects', 'SpellID': ours, 'Level': 1}})
        M.line_x.add(ours)
dead = {800000 + sid for sid, _ in failed}
dead_items = 0
for e in edits:
    if e['table'] == 'ItemTemplate':
        hit = False
        for k in ('SpellID', 'SpellID1', 'ProcSpellID', 'ProcSpellID1'):
            if e['set'].get(k) in dead:
                e['set'][k], hit = 0, True
                if k == 'SpellID':
                    e['set'].update({'Charges': 0, 'MaxCharges': 0})
                if k == 'SpellID1':
                    e['set'].update({'Charges1': 0, 'MaxCharges1': 0})
        dead_items += hit

os.makedirs(OUT, exist_ok=True)
# World Workshop takes at most 20,000 edits a patch: part 1 = items, spells, one-time drops and the first table rows;
# part 2 = the remaining table rows and the MobXLootTemplate links. Apply 1 then 2; undo 2 then 1.
order = {'ItemTemplate': 0, 'Spell': 1, 'LineXSpell': 2, 'LootOTD': 3, 'LootTemplate': 4, 'MobXLootTemplate': 5}
edits.sort(key=lambda e: order[e['table']])
first = [e for e in edits if e['table'] != 'MobXLootTemplate'][:19000]
rest = edits[len(first):]
for old in ('', '.part1', '.part2'):
    with contextlib.suppress(FileNotFoundError):
        os.remove(os.path.join(OUT, f'missing-drops-{DATE}{old}.edits.json'))
json.dump(first, open(os.path.join(OUT, f'missing-drops-{DATE}.part1.edits.json'), 'w'), indent=1)
json.dump(rest, open(os.path.join(OUT, f'missing-drops-{DATE}.part2.edits.json'), 'w'), indent=1)
with open(os.path.join(OUT, 'items.csv'), 'w', newline='', encoding='utf8') as fh:
    wr = csv.DictWriter(fh, fieldnames=list(report[0].keys()))
    wr.writeheader()
    wr.writerows(report)
with open(os.path.join(OUT, 'spells.csv'), 'w', newline='', encoding='utf8') as fh:
    wr = csv.DictWriter(fh, fieldnames=['Official spell', 'Our spell', 'Type', 'Copied from', 'Damage', 'Value', 'Duration', 'Target', 'Description'])
    wr.writeheader()
    wr.writerows(spell_report)
new_ids = set(created)
summary = {
    'monsters with official drops we spawn (non-ToA)': len(drops),
    'monsters getting new table rows': len(plan),
    'items created': len(created),
    'items created by realm': collections.Counter(r['Realm'] for r in report),
    'existing items newly dropped': len({w for items in plan.values() for w in items if w not in new_ids}),
    'LootTemplate rows': lt_rows, 'MobXLootTemplate rows': mx_rows, 'tables': len(groups),
    'monsters by chance a kill gives one new drop': dict(sorted(per_kill.items())),
    'LootOTD rows': sum(1 for e in edits if e['table'] == 'LootOTD'),
    'spells created': made_spells, 'spells not buildable': len(failed), 'items that lost a spell they could not get': dead_items,
    'skipped': skipped, 'edits': len(edits), 'part1 edits': len(first), 'part2 edits': len(rest), 'by table': collections.Counter(e['table'] for e in edits),
    'new item level source': collections.Counter(re.search(r'level \d+ \(([^)]*)\)', r['Notes']).group(1) for r in report),
    'new item model source': collections.Counter('EoD twin' if 'EoD twin' in r['Notes'] else 'donor' for r in report),
}
json.dump(summary, open(os.path.join(OUT, 'summary.json'), 'w'), indent=1, default=str)
json.dump(failed, open(os.path.join(OUT, 'spells-not-built.json'), 'w'), indent=1, default=str)
print(json.dumps(summary, indent=1, default=str))
