"""Plan: put back archived live spawns in the thin and borderline classic levelling zones (read-only generator).

The modern world inherited the classic edition's 2026-08-29 clean-up, which moved every post-legacy neutral GameNPC
spawn into offline_classic165_removed_mobs ("excluded from the server 1.65 authority"). For the 1.130 world those
are live content: the 2026-11-08 zone survey found the classic levelling zones about 10,000 spawns short of
EoD db-public, and almost all of them are sitting in that archive.

A removed row comes back only when an independent capture backs its exact place:
  - it lies in one of the survey's thin or borderline zones (ZONES below),
  - EoD db-public has a spawn of the same name within RADIUS units of it,
  - we have no spawn of that name within RADIUS of that EoD spawn (so nothing is doubled; one row per EoD spawn),
  - its Mob_ID is free and its NPCTemplateID (if any) exists.
Rows are inserted as archived, minus the archive's bookkeeping columns, with the 1.130 model remap of
make_model_id_shift_edits.py applied (the archive predates it).

usage: python -I make_thin_zones_restore_edits.py <world.sqlite3.db> <eod-db-public data dir> <out.edits.json> [report.csv]
"""
import csv
import json
import sqlite3
import sys
from collections import Counter, defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from make_model_id_shift_edits import HULDU_MODEL, HULDU_NEW, MODEL_MAP, huldu_row_moves  # noqa: E402

RADIUS = 350
EXCLUDE_NAMES = {'pirate ship battle mob'}   # scripted encounter helpers, not camp spawns
REASON = 'Post-legacy generic neutral spawn excluded from the server 1.65 authority'
ZONES = {
    # thin (survey 2026-11-08)
    4: 'Dartmoor', 10: 'Lyonesse', 6: 'Cornwall', 7: 'Llyn Barfog', 8: 'Campacorentin Forest', 9: 'Avalon Marsh',
    104: 'Muspelheim', 116: 'Malmohus',
    201: 'Silvermine Mts.', 202: 'Shannon Estuary', 204: 'Lough Gur', 208: 'Cursed Forest', 216: 'Sheeroe Hills',
    # borderline
    0: 'Camelot Hills', 1: 'Salisbury Plains', 2: 'Black Mtns. South',
    100: 'Vale of Mularn', 101: 'East Svealand', 103: 'Gotar',
    200: 'Lough Derg', 207: 'Connacht',
}


# Heights from World Workshop's navmesh audit of a trial apply (2026-11-09, regions 1, 100, 200): a restored row the
# audit found underground or floating gets the navmesh floor as its Z; one with no floor at all is left out.
_ZFIX_FILE = Path(__file__).with_name('thin-zones-restore-2026-11-09.zfix.json')
ZFIX = json.loads(_ZFIX_FILE.read_text(encoding='utf-8')) if _ZFIX_FILE.exists() else {}


def key(name):
    return (name or '').strip().lower()


def zone_boxes(db):
    out = defaultdict(list)
    for zid, rid, ox, oy, w, h in db.execute('SELECT ZoneID, RegionID, OffsetX, OffsetY, Width, Height FROM Zones'):
        if int(zid) in ZONES:
            out[int(rid)].append((int(ox) * 8192, (int(ox) + int(w)) * 8192, int(oy) * 8192, (int(oy) + int(h)) * 8192, int(zid)))
    return out


def zone_of(boxes, region, x, y):
    try:
        region, x, y = int(region), int(x or 0), int(y or 0)
    except (TypeError, ValueError):
        return None
    for x0, x1, y0, y1, zid in boxes.get(region, ()):
        if x0 <= x < x1 and y0 <= y < y1:
            return zid
    return None


def near(points, x, y, radius):
    r2 = radius * radius
    return any((px - x) ** 2 + (py - y) ** 2 <= r2 for px, py in points)


def remap_model(model, name, region):
    try:
        m = int(model)
    except (TypeError, ValueError):
        return model
    if m == HULDU_MODEL:
        return HULDU_NEW if huldu_row_moves(name, int(region)) else m
    return MODEL_MAP.get(m, m)


def build(world, eod_dir, radius=RADIUS):
    db = sqlite3.connect(f'file:{world}?mode=ro', uri=True)
    db.row_factory = sqlite3.Row
    boxes = zone_boxes(db)
    mob_cols = [r[1] for r in db.execute('PRAGMA table_info(Mob)')]
    have_ids = {r[0] for r in db.execute('SELECT Mob_ID FROM Mob')}
    templates = {r[0] for r in db.execute('SELECT TemplateId FROM NpcTemplate')}
    known_models = {r[0] for r in db.execute('SELECT DISTINCT Model FROM Mob')}

    ours = defaultdict(list)            # (zone, name) -> [(x, y)]
    for r in db.execute('SELECT Name, Region, X, Y FROM Mob'):
        z = zone_of(boxes, r['Region'], r['X'], r['Y'])
        if z is not None:
            ours[(z, key(r['Name']))].append((int(r['X']), int(r['Y'])))
    eod = defaultdict(list)
    for p in sorted(Path(eod_dir).glob('Mob.*.json')):
        for r in json.loads(p.read_text(encoding='utf-8')):
            if str(r.get('Realm', 0)) != '0' or not r.get('Name'):
                continue
            z = zone_of(boxes, r.get('Region'), r.get('X'), r.get('Y'))
            if z is not None:
                eod[(z, key(r['Name']))].append((int(r['X']), int(r['Y'])))
    archive = defaultdict(list)
    skipped = Counter()
    for r in db.execute('SELECT * FROM offline_classic165_removed_mobs WHERE RemovalReason = ? ORDER BY Mob_ID', (REASON,)):
        z = zone_of(boxes, r['Region'], r['X'], r['Y'])
        if z is None:
            continue
        if r['Mob_ID'] in have_ids:
            skipped['Mob_ID already in Mob'] += 1
        elif r['NPCTemplateID'] not in (None, '', 0, -1) and r['NPCTemplateID'] not in templates:
            skipped['NPC template missing'] += 1
        elif key(r['Name']) in EXCLUDE_NAMES:
            skipped['encounter helper left out'] += 1
        elif (z, key(r['Name'])) not in eod:
            skipped['name not in EoD for this zone'] += 1
        else:
            archive[(z, key(r['Name']))].append(r)

    edits, rows = [], []
    for (z, name), spots in sorted(eod.items()):
        pool = list(archive.get((z, name), ()))
        if not pool:
            continue
        have = list(ours.get((z, name), ()))
        for ex, ey in sorted(spots):
            if near(have, ex, ey, radius):
                continue
            best, best_d = None, radius * radius
            for r in pool:
                d = (int(r['X']) - ex) ** 2 + (int(r['Y']) - ey) ** 2
                if d <= best_d:
                    best, best_d = r, d
            if best is None:
                skipped['EoD spawn with no archived row near it'] += 1
                continue
            pool.remove(best)
            have.append((int(best['X']), int(best['Y'])))
            fix = ZFIX.get(best['Mob_ID'])
            if fix and fix['floor'] is None:
                skipped['no navmesh floor at the archived place (zfix)'] += 1
                continue
            row = {c: best[c] for c in mob_cols if c in best.keys()}
            row['Model'] = remap_model(row.get('Model'), row.get('Name'), row.get('Region'))
            if fix:
                row['Z'] = int(round(fix['floor']))
                skipped['(not skipped) Z moved to the navmesh floor (zfix)'] += 1
            edits.append({'table': 'Mob', 'insert': row})
            rows.append(dict(zone=ZONES[z], name=best['Name'], level=best['Level'], model=row['Model'],
                             model_known='yes' if row['Model'] in known_models else 'NEW',
                             region=best['Region'], x=best['X'], y=best['Y'], z=best['Z'],
                             eod_distance=round(best_d ** 0.5), mob_id=best['Mob_ID']))
        skipped['archived row left (place already filled or not in EoD)'] += len(pool)
    return edits, rows, skipped


def main():
    world, eod_dir, out = sys.argv[1], sys.argv[2], sys.argv[3]
    report = sys.argv[4] if len(sys.argv) > 4 else None
    edits, rows, skipped = build(world, eod_dir)
    with open(out, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(edits, f, indent=1, ensure_ascii=False)
        f.write('\n')
    if report:
        with open(report, 'w', encoding='utf-8', newline='') as f:
            w = csv.DictWriter(f, fieldnames=list(rows[0].keys()) if rows else ['zone'])
            w.writeheader()
            w.writerows(rows)
    per_zone = Counter(r['zone'] for r in rows)
    print(f'{len(edits)} spawns to restore, {len({(r["zone"], r["name"]) for r in rows})} zone/name pairs')
    for zone, n in per_zone.most_common():
        print(f'  {zone:24} {n:5}  ({len({r["name"] for r in rows if r["zone"] == zone})} names)')
    print('new models (not used by any current Mob row):', sorted({r['model'] for r in rows if r['model_known'] == 'NEW'}))
    for why, n in skipped.most_common():
        print(f'  skipped: {why}: {n}')


if __name__ == '__main__':
    main()
