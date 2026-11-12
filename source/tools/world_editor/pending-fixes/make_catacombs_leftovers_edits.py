"""Plan: the last Catacombs / Darkness Rising regions that have spawn positions in a source we hold (read-only).

 - Cave of Cruachan (region 341, Darkness Rising, Hibernia): 264 spawns from the 2013 archive world dolpubdb V3.1
   (read from the reference database's raw copy, `src_dolpub_*`). EoD has none. Zone point 673 (Connacht -> cave).
 - The Smelting Pot (region 435, a Glashtin Forge wing): 10 spawns from EoD db-public.

Looked at and left out on purpose:
 - region 347 "Lair of the Demon Lord": EoD's 15 rows are Dawn of Light developer NPCs (Phen, BluRaven, Graveen ...);
 - regions 389 and 494: EoD's rows are task-dungeon sets ("Shafts of the Tenebrae", "the Gnolls Den") while the 1.130
   client has the adventure wings "Halls of Flame" and "Heart of Darkness" there, so they would be the wrong monsters.

Same rules as make_catacombs_import_edits.py: no helper models, RoamingRange 0 -> 100 on neutral attackable GameNPCs,
the 1.130 model remap, missing NPC templates added, zone points only where we lack that id and realm.

usage: python -I make_catacombs_leftovers_edits.py <world.sqlite3.db> <eod-db-public data dir> <reference.sqlite3> <out.edits.json>
"""
import json
import sqlite3
import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from make_catacombs_import_edits import HELPER_MODELS, PEACE, ROAM, load, remap_model  # noqa: E402

CRUACHAN, SMELTING_POT = 341, 435


def main():
    world, eod, ref, out = sys.argv[1:5]
    db = sqlite3.connect(f'file:{world}?mode=ro', uri=True)
    db.row_factory = sqlite3.Row
    rf = sqlite3.connect(f'file:{ref}?mode=ro', uri=True)
    rf.row_factory = sqlite3.Row
    mob_cols = [r[1] for r in db.execute('PRAGMA table_info(Mob)')]
    zp_cols = [r[1] for r in db.execute('PRAGMA table_info(ZonePoint)')]
    tmpl_cols = [r[1] for r in db.execute('PRAGMA table_info(NpcTemplate)')]
    have_ids = {r[0] for r in db.execute('SELECT Mob_ID FROM Mob')}
    templates = {int(r[0]) for r in db.execute('SELECT TemplateId FROM NpcTemplate')}
    template_keys = {r[0] for r in db.execute('SELECT NpcTemplate_ID FROM NpcTemplate')}
    our_zp = {(int(r['Id']), int(r['Realm'])) for r in db.execute('SELECT Id, Realm FROM ZonePoint')}
    zp_keys = {r[0] for r in db.execute('SELECT ZonePoint_ID FROM ZonePoint')}
    here = Counter(int(r[0]) for r in db.execute(f'SELECT Region FROM Mob WHERE Region IN ({CRUACHAN}, {SMELTING_POT})'))

    sources = [('dolpub', [dict(r) for r in rf.execute(f'select * from src_dolpub_Mob where cast(Region as int) = {CRUACHAN}')]),
               ('eod', [m for m in load(eod, 'Mob') if int(m.get('Region') or 0) == SMELTING_POT])]
    edits, skipped, added, need_templates = [], Counter(), Counter(), {}
    for source, rows in sources:
        for m in rows:
            reg = int(m['Region'])
            if here.get(reg):
                skipped[f'region {reg} already has spawns'] += 1
                continue
            if m['ClassType'] == 'DOL.GS.GameNPC' and (int(m['Model']) & 0x7FFF) in HELPER_MODELS:
                skipped['invisible helper'] += 1
                continue
            row = {c: m[c] for c in mob_cols if c in m and m[c] is not None}
            if source == 'dolpub':
                row['Mob_ID'] = f"dolpub-{m['Mob_ID']}"          # the archive's numeric ids overlap EoD's
            if row['Mob_ID'] in have_ids:
                skipped['Mob_ID already used'] += 1
                continue
            row['Model'] = remap_model(row['Model'], row.get('Name'), reg)
            if (int(row.get('Realm') or 0) == 0 and row['ClassType'] == 'DOL.GS.GameNPC'
                    and not int(row.get('Flags') or 0) & PEACE and int(row.get('RoamingRange') or 0) == 0):
                row['RoamingRange'] = ROAM
            tid = int(row.get('NPCTemplateID') or -1)
            if tid > 0 and tid not in templates:
                need_templates[tid] = source
            edits.append({'table': 'Mob', 'insert': row})
            added[reg] += 1

    dolpub_templates = {int(t['TemplateId']): dict(t) for t in rf.execute('select * from src_dolpub_npctemplate')}
    eod_templates = {int(t['TemplateId']): t for t in load(eod, 'NpcTemplate')}
    missing_templates = []
    for tid, source in sorted(need_templates.items()):
        t = (dolpub_templates if source == 'dolpub' else eod_templates).get(tid) or eod_templates.get(tid) or dolpub_templates.get(tid)
        if not t:
            missing_templates.append(tid)
            continue
        ins = {c: t[c] for c in tmpl_cols if c in t and t[c] is not None}
        if ins.get('NpcTemplate_ID') in template_keys or not ins.get('NpcTemplate_ID'):
            ins['NpcTemplate_ID'] = f'import-template-{tid}'
        edits.insert(0, {'table': 'NpcTemplate', 'insert': ins})

    zps = [dict(z) for z in rf.execute(f'select * from src_dolpub_zonepoint where cast(TargetRegion as int) = {CRUACHAN} or cast(SourceRegion as int) = {CRUACHAN}')]
    zps += [z for z in load(eod, 'ZonePoint') if int(z.get('TargetRegion') or 0) in (CRUACHAN, SMELTING_POT) or int(z.get('SourceRegion') or 0) in (CRUACHAN, SMELTING_POT)]
    boxes = db.execute('SELECT RegionID, OffsetX, OffsetY, Width, Height FROM Zones').fetchall()
    zp_added = 0
    for z in zps:
        k = (int(z['Id']), int(z['Realm']))
        if k in our_zp:
            continue
        if not any(int(b['RegionID']) == int(z['TargetRegion']) and b['OffsetX'] * 8192 <= int(z['TargetX']) < (b['OffsetX'] + b['Width']) * 8192
                   and b['OffsetY'] * 8192 <= int(z['TargetY']) < (b['OffsetY'] + b['Height']) * 8192 for b in boxes):
            skipped[f"zone point {k[0]} realm {k[1]}: target outside every zone"] += 1   # 673 realm 1 in the archive
            continue
        our_zp.add(k)
        ins = {c: z[c] for c in zp_cols if c in z and z[c] is not None}
        ins['ClassType'] = ''
        if ins.get('ZonePoint_ID') in zp_keys or not ins.get('ZonePoint_ID'):
            ins['ZonePoint_ID'] = f'import-zp-{k[0]}-{k[1]}'
        zp_keys.add(ins['ZonePoint_ID'])
        edits.insert(0, {'table': 'ZonePoint', 'insert': ins})
        zp_added += 1

    Path(out).write_text(json.dumps(edits, indent=0, ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')
    print(len(edits), 'edits | spawns by region:', dict(added), '| zone point rows:', zp_added,
          '| templates added:', len(need_templates) - len(missing_templates), '| templates no source has:', missing_templates)
    print('skipped:', dict(skipped))


if __name__ == '__main__':
    main()
