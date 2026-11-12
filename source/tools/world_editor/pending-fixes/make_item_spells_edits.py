"""Item spells for the Bountycraft products, and fixes to item spells built from the wrong delve block.

    python -I make_item_spells_edits.py <opendaoc.sqlite3.db> <daoc_item_database_combined.zip> <out dir> [online-herald-*.json ...]

Writes <out dir>/item-spells-<date>.edits.json, report.json and summary.json. Applies nothing.

1. Every item of package OfflineBountycraft names its official record ("Bountycraft (official item N)"). Its official
   abilities are built as spells 800000 + id with herald_items' rules (2026-11-08 rules for Arrogance, omni heal,
   Damage Conversion, Damage to Power, Heal Bonus, ...; two-part spells get sub spells 850000 / 860000 + id), and the
   item's charge / proc columns are set where a spell now exists.
2. Spells this converter built earlier (packages OfflineBountycraft and OfflineToANamedDrops) are rebuilt with the
   fixed delve parser (a "Secondary Charged Magic Item:" header was missed, so a second ability read the first one's
   text). A row whose type or numbers differ is corrected; a row that has no rule now is reported and left as it is.
"""
import collections
import json
import os
import sqlite3
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from herald_items import Herald  # noqa: E402

DATE = '2026-11-08'
OUT_NAME = 'item-spells'
NS = uuid.UUID('5c1e7f0a-2b9d-4c3e-8a61-7d4f2e9b1c08')
REBUILD_PACKAGES = ('OfflineBountycraft', 'OfflineToANamedDrops')
COMPARE = ('Type', 'Value', 'Damage', 'Duration', 'Target', 'Range', 'Radius', 'Frequency', 'DamageType', 'SubSpellID',
           'ResurrectHealth')
ABILITY_COLUMNS = ('SpellID', 'Charges', 'MaxCharges', 'SpellID1', 'Charges1', 'MaxCharges1', 'ProcSpellID', 'ProcSpellID1',
                   'CanUseEvery')


def uid(*parts):
    return str(uuid.uuid5(NS, '|'.join(str(p) for p in parts)))


def main(db_path, zip_path, out_dir, *extra):
    con = sqlite3.connect(f'file:{db_path}?mode=ro', uri=True)
    con.row_factory = sqlite3.Row
    spells = {r['SpellID']: dict(r) for r in con.execute('SELECT * FROM Spell')}
    line_x = {r[0] for r in con.execute("SELECT SpellID FROM LineXSpell WHERE LineName = 'Item Effects'")}
    custom = {(r[0], r[1]) for r in con.execute("SELECT SpellID, Value FROM SpellXCustomValues WHERE KeyName = 'MultipleSubSpells'")}
    params = {(r[0], r[1]) for r in con.execute("SELECT SpellID, KeyName FROM SpellXCustomValues WHERE KeyName <> 'MultipleSubSpells'")}
    next_param = (con.execute('SELECT max(CustomParamID) FROM SpellXCustomValues').fetchone()[0] or 0) + 1
    # rules look up donor rows among the world's other spells, never among the rows being rebuilt
    donors = {k: v for k, v in spells.items() if v['PackageID'] not in REBUILD_PACKAGES}
    h = Herald(zip_path, spells=donors, package='OfflineBountycraft', date=DATE, extra=extra)
    items = {i['id']: i for i in h.items}
    edits, report = [], {'spells built': [], 'spells corrected': [], 'spells not built': {}, 'rebuild without a rule': [],
                         'items updated': []}

    def add_spell(row, sid):
        nonlocal next_param
        row['Spell_ID'] = f'offline-item-{row["SpellID"]}'
        edits.append({'table': 'Spell', 'insert': row})
        spells[row['SpellID']] = row
        for sub in h.extra_rows.get(row['SpellID'], {}).get('spells', []):
            if sub['SpellID'] not in spells:
                edits.append({'table': 'Spell', 'insert': sub})
                spells[sub['SpellID']] = sub
        for spell_id in [row['SpellID']] + [s['SpellID'] for s in h.extra_rows.get(row['SpellID'], {}).get('spells', [])]:
            if spell_id not in line_x:
                edits.append({'table': 'LineXSpell', 'insert': {'LineXSpell_ID': uid('lxs', spell_id), 'LineName': 'Item Effects',
                                                                 'SpellID': spell_id, 'Level': 1}})
                line_x.add(spell_id)
        for sub_id in h.extra_rows.get(row['SpellID'], {}).get('custom', []):
            if (row['SpellID'], str(sub_id)) not in custom:
                edits.append({'table': 'SpellXCustomValues', 'insert': {'CustomParamID': next_param, 'SpellID': row['SpellID'],
                                                                         'KeyName': 'MultipleSubSpells', 'Value': str(sub_id)}})
                custom.add((row['SpellID'], str(sub_id)))
                next_param += 1
        for key, value in h.extra_rows.get(row['SpellID'], {}).get('params', []):
            # e.g. SpecSkillBuff's BuffProperty (the eProperty of the skill raised)
            if (row['SpellID'], key) not in params:
                edits.append({'table': 'SpellXCustomValues', 'insert': {'CustomParamID': next_param, 'SpellID': row['SpellID'],
                                                                         'KeyName': key, 'Value': str(value)}})
                params.add((row['SpellID'], key))
                next_param += 1

    # 2. rebuild what the old parser built
    for spell_id, old in sorted(spells.items()):
        if old['PackageID'] not in REBUILD_PACKAGES or not 800000 <= spell_id < 850000:
            continue
        new, why = h.build_spell(spell_id - 800000)
        if new is None:
            report['rebuild without a rule'].append((spell_id, old['Name'], old['Type'], why))
            continue
        diff = {k: new[k] for k in COMPARE if old.get(k) != new.get(k)}
        if diff:
            diff.update({'Name': new['Name'], 'Description': new['Description']})
            edits.append({'table': 'Spell', 'where': {'SpellID': spell_id}, 'set': diff})
            report['spells corrected'].append((spell_id, old['Name'], {k: (old.get(k), new.get(k)) for k in COMPARE if k in diff}))
            for sub in h.extra_rows.get(spell_id, {}).get('spells', []):
                if sub['SpellID'] not in spells:
                    add_spell_sub = dict(sub)
                    edits.append({'table': 'Spell', 'insert': add_spell_sub})
                    spells[sub['SpellID']] = add_spell_sub
                    if sub['SpellID'] not in line_x:
                        edits.append({'table': 'LineXSpell', 'insert': {'LineXSpell_ID': uid('lxs', sub['SpellID']),
                                                                         'LineName': 'Item Effects', 'SpellID': sub['SpellID'], 'Level': 1}})
                        line_x.add(sub['SpellID'])
            for sub_id in h.extra_rows.get(spell_id, {}).get('custom', []):
                if (spell_id, str(sub_id)) not in custom:
                    edits.append({'table': 'SpellXCustomValues', 'insert': {'CustomParamID': next_param, 'SpellID': spell_id,
                                                                             'KeyName': 'MultipleSubSpells', 'Value': str(sub_id)}})
                    custom.add((spell_id, str(sub_id)))
                    next_param += 1

    # 1. the Bountycraft products' abilities
    rows = con.execute("SELECT * FROM ItemTemplate WHERE PackageID = 'OfflineBountycraft' AND Description LIKE 'Bountycraft (official item %'")
    for item in rows:
        oid = int(item['Description'].split('official item ')[1].rstrip(')'))
        o = items.get(oid)
        if not o:
            continue
        for a in o.get('abilities') or []:
            ours = 800000 + a['spell']
            if ours in spells or a['spell'] in report['spells not built']:
                continue
            row, why = h.build_spell(a['spell'])
            if row is None:
                report['spells not built'][a['spell']] = why
                continue
            add_spell(row, a['spell'])
            report['spells built'].append((ours, row['Type'], row['Value'], row['Duration'], row['Description'][:80]))
        wanted, _ = h.ability_set(o)
        for col in ('SpellID', 'SpellID1', 'ProcSpellID', 'ProcSpellID1'):
            if wanted[col] and wanted[col] not in spells:
                wanted[col] = 0
                if col == 'SpellID':
                    wanted.update({'Charges': 0, 'MaxCharges': 0})
                if col == 'SpellID1':
                    wanted.update({'Charges1': 0, 'MaxCharges1': 0})
        diff = {k: wanted[k] for k in ABILITY_COLUMNS if item[k] != wanted[k]}
        if diff:
            edits.append({'table': 'ItemTemplate', 'key': item['Id_nb'], 'set': diff})
            report['items updated'].append((item['Id_nb'], diff))

    still = collections.Counter()
    for item in con.execute("SELECT Description FROM ItemTemplate WHERE PackageID = 'OfflineBountycraft' AND Description LIKE 'Bountycraft (official item %'"):
        o = items.get(int(item[0].split('official item ')[1].rstrip(')')))
        if o and any(800000 + a['spell'] not in spells and a.get('magic_type') in (1, 2, 6) for a in o.get('abilities') or []):
            still['items still missing an ability'] += 1
    os.makedirs(out_dir, exist_ok=True)
    json.dump(edits, open(os.path.join(out_dir, f'item-spells-{DATE}.edits.json'), 'w', encoding='utf-8'), indent=1)
    json.dump(report, open(os.path.join(out_dir, 'report.json'), 'w', encoding='utf-8'), indent=1, default=str)
    summary = {'spells built': len(report['spells built']), 'spells corrected': len(report['spells corrected']),
               'spells not built': len(report['spells not built']), 'items updated': len(report['items updated']),
               'rebuild without a rule': report['rebuild without a rule'], **still,
               'edits': len(edits), 'by table': collections.Counter(e['table'] for e in edits)}
    json.dump(summary, open(os.path.join(out_dir, 'summary.json'), 'w', encoding='utf-8'), indent=1, default=str)
    print(json.dumps(summary, indent=1, default=str)[:5000])


if __name__ == '__main__':
    main(*sys.argv[1:])
