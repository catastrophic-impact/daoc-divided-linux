"""Builds the World Workshop edits files for the rest of the 2026-11-07 playtest data fixes (read-only on the world).

    python make_playtest_1107_followup_edits.py <world.db> <navmesh folder>

Writes, next to this script:
- playtest-1107-toa-aggro-<midgard|albion|hibernia>.edits.json: Atlantis monsters had aggro range 800 (the import),
  against 500 everywhere else, so every camp was ~2.5x larger by area. One `where` edit per Atlantis region:
  800 -> 500 (owner OK 2026-11-07). One file per realm copy, as a patch holds at most 20,000 row edits.
- playtest-1107-djinn-landings.edits.json: Djinn Teleport rows that land within reach of an aggressive monster
  (most were copied from the boss spawn) move to the nearest navmesh point, 300-2,500 away, that no aggressive
  monster reaches (at the new 500 range), on the same connected navmesh as the old point.
- playtest-1107-artifact-camps.edits.json: the solo artifacts in camps. Goddess Necklace's duplicate "overgrown
  statue" world rows stop attacking; The Winged Helm (Hermes' Island) and Golden Scarab Vest anchors move out of the
  marble automaton and Mau camps by the same rule as the djinn landings.
- playtest-1107-reaver-procs.edits.json: Reaver flail procs as 1.126: Boomslang and Taipan snare 40% (copies of
  20301/20304, which other styles share), Constrictor procs Reaver Pain (45 damage, a copy of 20103) instead of a
  60% snare. Indigosnake (20106, 30% life drain) is already right.
Then: python ../cli.py --game <game> plan <file>  (applies nothing).
"""
import json
import math
import sqlite3
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))
import audit  # noqa: E402  (World: navmesh floors per zone)

PEACE = 16
TOA_REGIONS = set(range(30, 48)) | set(range(72, 92)) | set(range(130, 148))
OLD_RANGE, NEW_RANGE = 800, 500
SAFE_MARGIN = 150          # clear of every aggressive monster's aggro range by this much
MIN_MOVE, MAX_MOVE, STEP = 300, 2500, 100
ANGLES = 32
ARTIFACT_MOVES = ('artifact:1030', 'artifact:1058')   # Golden Scarab Vest, The Winged Helm
STATUE_SUFFIX = '100008959'                           # Goddess Necklace's duplicate overgrown statue


def effective_range(region, rng):
    return NEW_RANGE if region in TOA_REGIONS and rng == OLD_RANGE else rng


def aggressive(db):
    """Aggressive realm-0 monsters per region: (x, y, z, reach)."""
    by_region = {}
    for region, x, y, z, rng, flags in db.execute(
            "select Region, X, Y, Z, AggroRange, Flags from Mob where Realm=0 and AggroLevel>0 and AggroRange>0"):
        if flags & PEACE:
            continue
        by_region.setdefault(region, []).append((x, y, z, max(effective_range(region, rng), 300) + SAFE_MARGIN))
    return by_region


def threatened(mobs, x, y, z):
    for mx, my, mz, reach in mobs:
        if abs(mz - z) < 600 and (mx - x) ** 2 + (my - y) ** 2 < reach * reach:
            return True
    return False


def safe_point(world, mobs, region, x, y, z):
    """The nearest navmesh point 300-2,500 from (x, y, z), on the same connected navmesh, that no monster reaches."""
    zone = world.zone(region, x, y)
    mesh = world.mesh(zone) if zone is not None else None
    if mesh is None:
        return None, 'no navmesh'
    origin = mesh.locate(x, y, z, tolerance=400)
    area = mesh._find(origin) if origin is not None else None
    for r in range(MIN_MOVE, MAX_MOVE + 1, STEP):
        for k in range(ANGLES):
            a = 2 * math.pi * k / ANGLES
            cx, cy = int(x + r * math.cos(a)), int(y + r * math.sin(a))
            if world.zone(region, cx, cy) != zone:
                continue
            poly = mesh.locate(cx, cy, z, tolerance=400)
            if poly is None or (area is not None and mesh._find(poly) != area):
                continue
            cz = int(round(mesh.height(poly, cx, cy)))
            if not threatened(mobs, cx, cy, cz):
                return (cx, cy, cz), f'{r} away'
    return None, 'no clear navmesh point within 2,500'


AGGRO_FILES = {  # one patch per realm copy: a patch holds at most 20,000 row edits (~11,000 each)
    'midgard': set(range(30, 48)),
    'albion': set(range(73, 92)),
    'hibernia': set(range(130, 148)) | {72},
}


def aggro_edits(db, regions_of_file):
    regions = [r for (r,) in db.execute(f"select distinct Region from Mob where AggroRange={OLD_RANGE}") if r in regions_of_file]
    return [{'table': 'Mob', 'where': {'Region': r, 'AggroRange': OLD_RANGE}, 'set': {'AggroRange': NEW_RANGE}} for r in sorted(regions)]


def djinn_edits(db, world, threats):
    edits, report = [], []
    for key, dest, realm, region, x, y, z in db.execute(
            "select Teleport_ID, TeleportID, Realm, RegionID, X, Y, Z from Teleport where Type='Djinn' and RegionID<>0 order by TeleportID, Realm"):
        if not threatened(threats.get(region, []), x, y, z):
            continue
        point, why = safe_point(world, threats.get(region, []), region, x, y, z)
        report.append(f'{dest} (realm {realm}, region {region}): ' + (f'moved {why} to {point}' if point else f'LEFT: {why}'))
        if point:
            edits.append({'table': 'Teleport', 'key': key, 'set': {'X': point[0], 'Y': point[1], 'Z': point[2]}})
    return edits, report


def artifact_edits(db, world, threats):
    edits, report = [], []
    for key, name in db.execute(f"select Mob_ID, Name from Mob where Mob_ID like '%{STATUE_SUFFIX}' and AggroLevel>0"):
        edits.append({'table': 'Mob', 'key': key, 'set': {'AggroLevel': 0, 'AggroRange': 0, 'Flags': PEACE}})
        report.append(f'{key} {name}: at peace (duplicates the Goddess Necklace anchor)')
    for key, name, package, region, x, y, z in db.execute(
            "select Mob_ID, Name, PackageID, Region, X, Y, Z from Mob where PackageID in (%s) order by Mob_ID" % ','.join('?' * len(ARTIFACT_MOVES)),
            ARTIFACT_MOVES):
        if not threatened(threats.get(region, []), x, y, z):
            report.append(f'{key} {name}: already clear')
            continue
        point, why = safe_point(world, threats.get(region, []), region, x, y, z)
        report.append(f'{key} {name} (region {region}): ' + (f'moved {why} to {point}' if point else f'LEFT: {why}'))
        if point:
            edits.append({'table': 'Mob', 'key': key, 'set': {'X': point[0], 'Y': point[1], 'Z': point[2]}})
    return edits, report


def reaver_edits(db):
    def free_ids(n):
        ids, i = [], 20312
        while len(ids) < n:
            if not db.execute('select 1 from Spell where SpellID=?', (i,)).fetchone():
                ids.append(i)
            i += 1
        return ids
    boomslang, taipan, pain = free_ids(3)
    edits = [
        {'table': 'Spell', 'copy': '20301', 'key': str(boomslang),
         'set': {'SpellID': boomslang, 'Value': 40, 'Description': "Reduces the target's movement speed by 40% for 19 seconds."}},
        {'table': 'Spell', 'copy': '20304', 'key': str(taipan),
         'set': {'SpellID': taipan, 'Value': 40, 'Description': "Reduces the target's movement speed by 40% for 23 seconds."}},
        {'table': 'Spell', 'copy': '20103', 'key': str(pain),
         'set': {'SpellID': pain, 'Name': 'Reaver Pain', 'Damage': 45, 'Description': 'Does 45 damage to the target.'}},
    ]
    for style, spell in ((363, boomslang), (365, taipan), (366, pain)):
        edits.append({'table': 'StyleXSpell', 'where': {'StyleID': style, 'ClassID': 0}, 'set': {'SpellID': spell}})
    return edits


def main():
    db = sqlite3.connect(f'file:{sys.argv[1]}?mode=ro', uri=True)
    db.row_factory = sqlite3.Row  # audit.World reads its Zones rows by name
    world = audit.World(db, Path(sys.argv[2]))
    threats = aggressive(db)
    outputs = {
        **{f'playtest-1107-toa-aggro-{copy}.edits.json': (aggro_edits(db, regions), []) for copy, regions in AGGRO_FILES.items()},
        'playtest-1107-djinn-landings.edits.json': djinn_edits(db, world, threats),
        'playtest-1107-artifact-camps.edits.json': artifact_edits(db, world, threats),
        'playtest-1107-reaver-procs.edits.json': (reaver_edits(db), []),
    }
    for name, (edits, report) in outputs.items():
        (HERE / name).write_text(json.dumps(edits, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')
        print(f'{name}: {len(edits)} edits')
        for line in report:
            print('  ' + line)


if __name__ == '__main__':
    main()
