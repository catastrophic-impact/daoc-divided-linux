"""Plan: open Catacombs. Spawns, city NPCs and zone points from EoD db-public (read-only generator).

Our world has the Catacombs zones, regions, NPC templates, equipment templates and factions, but no spawns and no way
in. This writes World Workshop edits that add them from EoD db-public (MIT), following the lessons of the Atlantis
import (Class-Research/claude/toa-restoration/atlantis-import-v2):

  - only rows inside a zone box of our Zones table;
  - no invisible encounter helpers (monsters.csv models 665-667, 1686, 1923 on a plain GameNPC) and no "New Mob";
  - no second copy of an NPC we already have (same name, same region, within 300 units);
  - RoamingRange 0 on a neutral, attackable GameNPC becomes 100 (the import value 0 makes monsters stand still);
  - the 1.130 model remap of make_model_id_shift_edits.py;
  - a class the installed world has never used is listed in the report (dr world lint decides);
  - merchants whose item list has nothing we can sell become plain NPCs.
Albion's Otherworld (region 196) is left out: its 897 EoD rows lie outside the zone box (see the report).
Task dungeon rows (InstanceXElement) are not part of this import.

usage: python -I make_catacombs_import_edits.py <world.sqlite3.db> <eod-db-public data dir> <out prefix> [batch: 1 or 2]
writes <prefix>-1-support.edits.json (templates, merchant items, zone points), <prefix>-2-mobs.edits.json,
       <prefix>.excluded.json and <prefix>.report.txt
"""
import json
import sqlite3
import sys
from collections import Counter, defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from make_model_id_shift_edits import HULDU_MODEL, HULDU_NEW, MODEL_MAP, huldu_row_moves  # noqa: E402

REGIONS = {
    63: 'Roman Aqueducts', 66: "Albion's Underground Forest", 59: "Albion's Glashtin Forges",
    67: 'The Deadlands of Annwn', 68: 'Lower Crypt', 148: 'The Frontlines', 65: 'Inconnu Crypt', 69: "Albion's Darkspire",
    58: "Midgard's Underground Forest", 189: "Midgard's Glashtin Forges", 149: 'Nyttheim', 162: "Midgard's Deadlands",
    195: "Midgard's Otherworld", 243: 'Kobold Undercity', 188: "Midgard's Darkspire",
    92: 'Veil Rift', 96: "Hibernia's Underground Forest", 99: "Hibernia's Glashtin Forge", 95: "Hibernia's Frontlines",
    97: "Hibernia's Deadlands", 197: 'The Otherworld', 94: "The Queen's Labyrinth", 93: 'Shar Labyrinth',
    98: "Hibernia's Darkspire",
}
# Batch 2 (2026-11-09): the Catacombs zones the first list missed (Jordheim's entrance leads to the Burial Grounds),
# plus the Darkness Rising areas EoD has rows for.
REGIONS_2 = {
    227: "Albion's Abandoned Mines", 228: "Hibernia's Abandoned Mines", 226: "Midgard's Abandoned Mines",
    229: 'Burial Grounds', 109: "Albion's Frontlines", 433: "Snarg's Grotto", 430: 'the Plutonian Shore',
    429: "Snyblem's Lair", 436: 'Forges of Flame', 390: 'Lair of the Exiled', 376: "Goblin's Cookery",
    396: 'Depths of Despair', 397: 'the Realm of the Damned', 437: "Hammer Hugak's Smithy", 446: "Blathnait's Refuge",
    447: 'Broken Mirrors', 495: 'Inner Sanctum',
    339: 'Brimstone Caverns', 335: 'The Foothills of Hibernia',
    394: "King Constantine's Throne Room", 360: "King Eirik's Throne Room", 395: "King Lamfhotas' Throne Room",
}
BATCHES = {'1': REGIONS, '2': REGIONS_2}
LEFT_OUT_REGIONS = {196: "Albion's Otherworld"}
HELPER_MODELS = {665, 666, 667, 1686, 1923}
HELPER_NAMES = {'new mob'}
# EoD class names our server spells differently (dr world lint on the 2026-11-09 sandbox trial).
# EoD's RogueTrainer is three classes here, one per realm (Master Thedriwin in the Roman Aqueducts, and others).
ROGUE_TRAINER = {1: 'DOL.GS.Trainer.AlbionRogueTrainer', 2: 'DOL.GS.Trainer.MidgardRogueTrainer', 3: 'DOL.GS.Trainer.StalkerTrainer'}
ROAM = 100
PEACE = 16
report = []


def say(*a):
    line = ' '.join(str(x) for x in a)
    print(line)
    report.append(line)


def load(eod, name):
    rows = []
    for p in sorted(Path(eod).glob(f'{name}.json')) + sorted(Path(eod).glob(f'{name}.*.json')):
        data = json.loads(p.read_text(encoding='utf-8'))
        rows.extend(data if isinstance(data, list) else [])
    return rows


def remap_model(model, name, region):
    m = int(model)
    flag, base = m & ~0x7FFF, m & 0x7FFF
    if base == HULDU_MODEL:
        base = HULDU_NEW if huldu_row_moves(name, int(region)) else base
    else:
        base = MODEL_MAP.get(base, base)
    return flag | base


def main():
    world, eod, prefix = sys.argv[1], sys.argv[2], sys.argv[3]
    global REGIONS
    REGIONS = BATCHES[sys.argv[4] if len(sys.argv) > 4 else '1']
    db = sqlite3.connect(f'file:{world}?mode=ro', uri=True)
    db.row_factory = sqlite3.Row
    cols = {t: [r[1] for r in db.execute(f'PRAGMA table_info({t})')] for t in ('Mob', 'ZonePoint', 'NpcTemplate', 'MerchantItem')}
    boxes = defaultdict(list)
    for r in db.execute('SELECT RegionID, OffsetX, OffsetY, Width, Height FROM Zones'):
        boxes[int(r['RegionID'])].append((int(r['OffsetX']) * 8192, int(r['OffsetY']) * 8192,
                                          int(r['Width']) * 8192, int(r['Height']) * 8192))
    in_zone = lambda reg, x, y: any(ox <= x < ox + w and oy <= y < oy + h for ox, oy, w, h in boxes.get(reg, ()))
    have_ids = {r[0] for r in db.execute('SELECT Mob_ID FROM Mob')}
    ours_here = defaultdict(list)
    for r in db.execute(f"SELECT Name, Region, X, Y FROM Mob WHERE Region IN ({','.join(map(str, REGIONS))})"):
        ours_here[(int(r['Region']), (r['Name'] or '').lower())].append((int(r['X']), int(r['Y'])))
    known_classes = {r[0] for r in db.execute('SELECT DISTINCT ClassType FROM Mob')}
    templates = {int(r[0]) for r in db.execute('SELECT TemplateId FROM NpcTemplate')}
    template_keys = {r[0] for r in db.execute('SELECT NpcTemplate_ID FROM NpcTemplate')}
    items = {r[0].lower() for r in db.execute('SELECT Id_nb FROM ItemTemplate')}
    our_lists = defaultdict(list)
    for r in db.execute('SELECT ItemListID, ItemTemplateID FROM MerchantItem'):
        our_lists[r[0]].append((r[1] or '').lower())

    # ------------------------------------------------------------------ mobs
    mobs, excluded, new_classes, roam_set, remapped = [], [], Counter(), 0, 0
    left_out = Counter()
    for m in load(eod, 'Mob'):
        reg = int(m.get('Region') or 0)
        if reg in LEFT_OUT_REGIONS:
            left_out[reg] += 1
            continue
        if reg not in REGIONS:
            continue
        x, y = int(m['X']), int(m['Y'])
        name = (m.get('Name') or '').lower()
        base_model = int(m['Model']) & 0x7FFF
        why = ('invisible helper' if m['ClassType'] == 'DOL.GS.GameNPC' and base_model in HELPER_MODELS else
               'placeholder name' if name in HELPER_NAMES else
               'outside every zone' if not in_zone(reg, x, y) else
               'Mob_ID already used' if m['Mob_ID'] in have_ids else
               'already in our world' if any((px - x) ** 2 + (py - y) ** 2 <= 300 ** 2 for px, py in ours_here.get((reg, name), ())) else None)
        if why:
            excluded.append({'why': why, 'Mob_ID': m['Mob_ID'], 'Name': m['Name'], 'Region': reg, 'X': x, 'Y': y, 'Z': m['Z'],
                             'Level': m.get('Level'), 'Model': m['Model'], 'ClassType': m['ClassType']})
            continue
        row = {c: m[c] for c in cols['Mob'] if c in m}
        if row['ClassType'] == 'DOL.GS.Trainer.RogueTrainer':
            row['ClassType'] = ROGUE_TRAINER.get(int(row.get('Realm') or 0), row['ClassType'])
        new_model = remap_model(row['Model'], row.get('Name'), reg)
        remapped += new_model != row['Model']
        row['Model'] = new_model
        if (int(row.get('Realm') or 0) == 0 and row['ClassType'] == 'DOL.GS.GameNPC'
                and not int(row.get('Flags') or 0) & PEACE and int(row.get('RoamingRange') or 0) == 0):
            row['RoamingRange'] = ROAM
            roam_set += 1
        if row['ClassType'] not in known_classes:
            new_classes[row['ClassType']] += 1
        mobs.append(row)

    # ------------------------------------------------------------------ merchants: only lists we can sell from
    eod_lists = defaultdict(list)
    for r in load(eod, 'MerchantItem'):
        eod_lists[r['ItemListID']].append(r)
    support, demoted, lists_added = [], [], 0
    for row in mobs:
        lid = row.get('ItemsListTemplateID')
        if 'Merchant' not in row['ClassType'] or not lid:
            continue
        if any(i in items for i in our_lists.get(lid, ())):
            continue
        sellable = [r for r in eod_lists.get(lid, ()) if (r['ItemTemplateID'] or '').lower() in items]
        if sellable and lid not in our_lists:
            for r in sellable:
                support.append({'table': 'MerchantItem', 'insert': {c: r[c] for c in cols['MerchantItem'] if c in r}})
            our_lists[lid] = [(r['ItemTemplateID'] or '').lower() for r in sellable]
            lists_added += 1
        elif not sellable:
            demoted.append((row['Name'], row['Region'], lid))
            row['ClassType'], row['ItemsListTemplateID'] = 'DOL.GS.GameNPC', ''

    # ------------------------------------------------------------------ NPC templates the spawns need
    used = {int(r['NPCTemplateID']) for r in mobs if int(r.get('NPCTemplateID') or -1) > 0}
    missing = used - templates
    eod_templates = {int(t['TemplateId']): t for t in load(eod, 'NpcTemplate')}
    for tid in sorted(missing):
        t = eod_templates.get(tid)
        if t:
            ins = {c: t[c] for c in cols['NpcTemplate'] if c in t}
            if ins.get('NpcTemplate_ID') in template_keys:      # our world uses this row key for another template
                ins['NpcTemplate_ID'] = f'eod-template-{tid}'
            if ins.get('Model') not in (None, ''):
                ins['Model'] = ';'.join(str(remap_model(x, ins.get('Name'), 0)) if x.strip().isdigit() else x
                                        for x in str(ins['Model']).split(';'))
            support.append({'table': 'NpcTemplate', 'insert': ins})
    no_template = sorted(t for t in missing if t not in eod_templates)

    # ------------------------------------------------------------------ zone points
    our_zp = {(int(r['Id']), int(r['Realm'])): r for r in db.execute('SELECT * FROM ZonePoint')}   # one row per id and realm
    zp_classes = {r[0] for r in db.execute("SELECT DISTINCT ClassType FROM ZonePoint WHERE ClassType LIKE 'DOL.GS.ServerRules.%'")}
    zp_added, zp_same, zp_conflict = 0, 0, []
    for z in load(eod, 'ZonePoint'):
        tr, sr = int(z.get('TargetRegion') or 0), int(z.get('SourceRegion') or 0)
        if tr not in REGIONS and sr not in REGIONS:
            continue
        if tr in LEFT_OUT_REGIONS or sr in LEFT_OUT_REGIONS:
            continue
        mine = our_zp.get((int(z['Id']), int(z['Realm'])))
        if mine is None:
            ins = {c: z[c] for c in cols['ZonePoint'] if c in z}
            if ins.get('ClassType') and ins['ClassType'] not in zp_classes:   # an EoD script class we do not have:
                ins['ClassType'] = ''                                         # a plain jump (dr world lint, 2026-11-09)
            support.append({'table': 'ZonePoint', 'insert': ins})
            zp_added += 1
        elif (int(mine['TargetRegion']), int(mine['TargetX']), int(mine['TargetY'])) == (tr, int(z['TargetX']), int(z['TargetY'])):
            zp_same += 1
        else:
            zp_conflict.append((int(z['Id']), f"ours -> region {mine['TargetRegion']}", f"EoD -> region {tr}"))

    # ------------------------------------------------------------------ report and files
    say('Catacombs import plan')
    say('mob rows to add:', len(mobs), '| excluded:', len(excluded), dict(Counter(e['why'] for e in excluded)))
    say('left out regions:', {LEFT_OUT_REGIONS[k]: v for k, v in left_out.items()})
    by_region = Counter(int(r['Region']) for r in mobs)
    for reg, name in REGIONS.items():
        say(f'  {reg:>4} {name:32} {by_region.get(reg, 0):>6}')
    say('realm:', dict(Counter(int(r.get('Realm') or 0) for r in mobs)))
    say('classes:', dict(Counter(r['ClassType'] for r in mobs).most_common(40)))
    say('classes our world has never used:', dict(new_classes))
    say('RoamingRange 0 -> 100 on', roam_set, 'rows; model remapped on', remapped, 'rows')
    say('NPC templates added:', sum(1 for e in support if e['table'] == 'NpcTemplate'), '| missing in EoD too:', no_template)
    say('merchant lists added:', lists_added, '| merchant item rows:', sum(1 for e in support if e['table'] == 'MerchantItem'),
        '| merchants made plain NPCs (nothing sellable):', demoted)
    say('zone points added:', zp_added, '| already ours and the same:', zp_same, '| id used by ours for another place:', zp_conflict)
    Path(f'{prefix}-1-support.edits.json').write_text(json.dumps(support, indent=0, ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')
    Path(f'{prefix}-2-mobs.edits.json').write_text(json.dumps([{'table': 'Mob', 'insert': r} for r in mobs], indent=0, ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')
    Path(f'{prefix}.excluded.json').write_text(json.dumps(excluded, indent=0, ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')
    Path(f'{prefix}.report.txt').write_text('\n'.join(report) + '\n', encoding='utf-8', newline='\n')


if __name__ == '__main__':
    main()
