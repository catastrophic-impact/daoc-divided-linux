"""Plan: Halls of Helgardh (Midgard) and the missing part of Cave of Cruachan (Hibernia). Read-only generator.

Darkness Rising chapter 3: each realm's demon lord is guarded by a Circle of Five. Our world has Albion's dungeon
(Brimstone Caverns, 339, from EoD db-public), one wing of Hibernia's (Cave of Cruachan, 341) and nothing of
Midgard's (Halls of Helgardh, 340). No spawn database has them. What exists is in
Research/Sources/Allakhazam-DR-Bestiary-2026-11-09: the Allakhazam bestiary (names, level ranges) and, read from its
location images, an exact /loc for the bosses and named demons and a rough spot for each boss's minions.

Every row this writes carries its provenance in PackageID:
  DR3-exact     at the /loc printed on the Allakhazam image (zone-local + 8192): the real spot
  DR3-minion    a boss's minion type: the name is real, the spot is within about 700 units of the map marker,
                the count is the Albion counterpart's count in Brimstone (no source gives counts)
  DR3-filler    the common demons between rooms: names and group sizes are those of a Brimstone room of the same
                kind (the three dungeons are built from the same 16 room pieces), the spot is a random point on
                the floor near the room's centre. An estimate, not data.
All spots are on the navmesh floor built from the client's dungeon geometry (Source/artifacts/dr-ch3-navmesh) and
can be walked to from the room's anchor. Stats, model and size come from the Albion counterpart's NPC template,
copied under the real name with Allakhazam's level range.

usage: python -I make_dr_chapter3_edits.py <world.sqlite3.db> <eod-db-public data dir> <out prefix>
"""
import csv
import html
import importlib.util
import json
import math
import random
import re
import sqlite3
import sys
from collections import Counter, defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
SOURCE = HERE.parents[3]                                  # the checkout
OFFLINE = Path(r'C:\Users\natex\Desktop\DAoC Offline')
ZAM = OFFLINE / 'Research' / 'Sources' / 'Allakhazam-DR-Bestiary-2026-11-09'
NAV = OFFLINE / 'Source' / 'artifacts' / 'dr-ch3-navmesh'
PROBE = OFFLINE / 'Source' / 'artifacts' / 'atlantis-navmesh' / 'detour_probe.py'
ARCHIVE = OFFLINE / 'Source' / 'tools' / 'asset-tool' / 'archive.py'
CLIENT_ZONES = Path(r'C:\Users\natex\Desktop\OpenDAoC\DAoC Full Client\Dark Age of Camelot\zones')

ALBION = 339
TARGETS = {340: 271, 341: 268}                           # our region -> Allakhazam zone id
# Target name -> its Albion counterpart in our Brimstone data (bosses by their minion sets, see the source README).
COUNTERPART = {
    340: {'farbjodr': 'abaddon', 'banahogg': 'moloch', 'ofridr': 'astaroth', 'soltinn': 'xaphan', 'myrkr': 'belial',
          "banahogg's spawn": "moloch's spawn", 'eye of babahogg': 'eye of moloch',
          "myrkr's brood": 'belials brood', "myrkr's child": "belial's child", 'aspect of myrkr': 'aspect of belial',
          "ofridr's front guard": "astaroth's frontguard", "ofridr's worms": "astaroth's worms",
          'soremaiden of ofridr': 'soremaiden of asteroth', "soltinn's soldier": "xaphan's soldier"},
    341: {'glamaim': 'moloch', 'glaimaim': 'moloch', 'teugmhail': 'astaroth', 'grimeil': 'xaphan', 'malairt': 'abaddon', 'uamhas': 'belial',
          "glaimaim's spawn": "moloch's spawn", 'eye of glaimaim': 'eye of moloch',
          "teugmhail's front guard": "astaroth's frontguard", "teugmhail's worms": "astaroth's worms",
          'soremaiden of teugmhail': 'soremaiden of asteroth', "grimeil's soldier": "xaphan's soldier",
          "uamhas' brood": 'belials brood', "uamhas' child": "belial's child", 'aspect of uamhas': 'aspect of belial'},
}
RENAME = {'eye of babahogg': 'Eye of Banahogg'}       # Allakhazam page typo. The Hibernian boss stays "Glamaim": the official Herald export spells him so (his minions are "Glaimaim's" on Allakhazam)
# Named demons the three dungeons share; our Brimstone rows spell "Chtonic" and lack two of them.
SHARED_FALLBACK = {'marquis grigori': 'marquis halphas', 'inquisitor mehalia': 'inquisitor admoreth'}
SKIP = {"soltinn's implement", "grimeil's implement"}     # invisible encounter helpers (as Xaphan's implement in Albion)
COMMON = {'umbrood warrior', 'umbral hulk', 'nightmare', 'cursed infernalist', 'succubus', 'essence shredder',
          'naburite drinker', 'fire demon', 'umbral aegis', 'mahr', 'chthonian crawler', 'pale guardian',
          'infernalist worshipper', 'lesser succubus'}
TEMPLATE_BASE = {340: 61340000, 341: 61341000}
SEED = 20261109


def load_module(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    argv, sys.argv = sys.argv, [name, 'none']
    try:
        spec.loader.exec_module(mod)
    finally:
        sys.argv = argv
    return mod


probe = load_module(PROBE, 'probe')
archive = load_module(ARCHIVE, 'archive')


def norm(name):
    n = html.unescape(name or '').split(' (')[0].strip().lower()
    return n.replace('chthonic', 'chtonic')


def pieces(zone):
    ents = {e.name.lower(): e.data.decode('latin-1') for e in archive.read((CLIENT_ZONES / f'zone{zone}' / f'dat{zone}.mpk').read_bytes())[1]}
    chunks = [l.strip() for l in ents['dungeon.chunk'].splitlines() if l.strip()]
    out = []
    for line in ents['dungeon.place'].splitlines():
        p = [x.strip() for x in line.split(',')]
        if len(p) >= 8:
            kind = re.sub(r'^dr_act3_(alb|mid|hib)_', '', chunks[int(p[0])].lower()).replace('.nif', '')
            # layout to world, fitted on Brimstone (1,450 of 1,462 spawns stand on the resulting mesh floor)
            out.append({'kind': kind, 'x': 32768 - float(p[1]), 'y': 32768 + float(p[2]), 'z': 16000 + float(p[3])})
    return out


class Floor:
    def __init__(self, region):
        self.q = probe.query(probe.load(str(NAV / f'zone{region}.nav')))

    def snap(self, x, y, z, reach=48, up=250):
        st, p = probe.closest(self.q, (x, y, z), ext=(reach, reach, up))
        return p

    def snap_any_height(self, x, y, reach=400):
        for z in range(12500, 19001, 250):
            p = self.snap(x, y, z, reach=reach, up=140)
            if p:
                return p
        return None

    def reachable(self, a, b):
        st, nodes = probe.path(self.q, a, b)
        return bool(st & probe.SUCCESS) and not st & probe.PARTIAL and len(nodes) > 0

    def scatter(self, rng, anchor, radius, taken, spacing=110, tries=80):
        for _ in range(tries):
            r, t = radius * math.sqrt(rng.random()), rng.random() * 2 * math.pi
            cx, cy = anchor[0] + r * math.cos(t), anchor[1] + r * math.sin(t)
            p = self.snap(cx, cy, anchor[2], reach=6, up=220)
            # keep clear of the mesh edge (walls, ledges): the floor must also be there 45 units to every side
            if p and not all(self.snap(p[0] + dx, p[1] + dy, p[2], reach=6, up=60) for dx, dy in ((45, 0), (-45, 0), (0, 45), (0, -45))):
                p = None
            if p and all((p[0] - q[0]) ** 2 + (p[1] - q[1]) ** 2 > spacing ** 2 or abs(p[2] - q[2]) > 150 for q in taken) \
                    and self.reachable(anchor, p):
                taken.append(p)
                return p
        return None


def zam_levels(zone):
    out = {}
    for m in json.loads((ZAM / f'zone-{zone}' / 'mobs.json').read_text(encoding='utf-8')):
        t = (ZAM / f'zone-{zone}' / 'pages' / f"{m['id']}.html").read_text('utf-8', 'replace')
        lo = re.search(r'Minimum Level\D{0,80}?(\d+)', re.sub(r'<[^>]+>', ' ', t))
        hi = re.search(r'Maximum Level\D{0,80}?(\d+)', re.sub(r'<[^>]+>', ' ', t))
        if lo and hi:
            out[m['id']] = (int(lo.group(1)), int(hi.group(1)))
    return out


def main():
    world, eod, prefix = sys.argv[1], sys.argv[2], sys.argv[3]
    db = sqlite3.connect(f'file:{world}?mode=ro', uri=True)
    db.row_factory = sqlite3.Row
    mob_cols = [r[1] for r in db.execute('PRAGMA table_info(Mob)')]
    tmpl_cols = [r[1] for r in db.execute('PRAGMA table_info(NpcTemplate)')]
    zp_cols = [r[1] for r in db.execute('PRAGMA table_info(ZonePoint)')]
    templates = {int(r['TemplateId']): dict(r) for r in db.execute('SELECT * FROM NpcTemplate')}
    used_template_ids = set(templates)
    template_keys = {t['NpcTemplate_ID'] for t in templates.values()}
    have_ids = {r[0] for r in db.execute('SELECT Mob_ID FROM Mob')}
    alb_rows = defaultdict(list)
    for r in db.execute('SELECT * FROM Mob WHERE Region = ?', (ALBION,)):
        alb_rows[norm(r['Name'])].append(dict(r))
    alb_pieces = pieces(ALBION)
    alb_by_piece = defaultdict(list)
    for name, rows in alb_rows.items():
        if name not in COMMON:
            continue
        for r in rows:
            if int(r['NPCTemplateID'] or -1) == 13382:      # the odd untargetable pale guardian variant
                continue
            i = min(range(len(alb_pieces)), key=lambda k: (alb_pieces[k]['x'] - r['X']) ** 2 + (alb_pieces[k]['y'] - r['Y']) ** 2)
            d = math.hypot(alb_pieces[i]['x'] - r['X'], alb_pieces[i]['y'] - r['Y'])
            if d <= 2500:
                alb_by_piece[i].append((r, d))
    donors = defaultdict(list)
    for i, p in enumerate(alb_pieces):
        donors[p['kind']].append(i)

    edits, report = [], []
    rng = random.Random(SEED)
    for region, zam_zone in TARGETS.items():
        floor = Floor(region)
        levels = zam_levels(zam_zone)
        positions = list(csv.DictReader((ZAM / f'zone-{zam_zone}' / 'positions.csv').open(encoding='utf-8')))
        existing = defaultdict(list)
        for r in db.execute('SELECT Name, X, Y, Z FROM Mob WHERE Region = ?', (region,)):
            existing[norm(r['Name'])].append((r['X'], r['Y'], r['Z']))
        all_existing = [p for v in existing.values() for p in v]
        taken, counts, notes = list(all_existing), Counter(), []
        seq = [0]
        next_template = [TEMPLATE_BASE[region]]
        made_templates = {}

        def template_for(name, counterpart_row, level):
            key = name.lower()
            if key in made_templates:
                return made_templates[key]
            src = templates.get(int(counterpart_row['NPCTemplateID'] or -1))
            if src is None:
                return -1
            while next_template[0] in used_template_ids:
                next_template[0] += 1
            tid = next_template[0]
            used_template_ids.add(tid)
            t = {c: src[c] for c in tmpl_cols}
            t.update(TemplateId=tid, Name=name, NpcTemplate_ID=f'dr3-{region}-{re.sub(r"[^a-z0-9]+", "-", key).strip("-")}',
                     PackageID='DR3-template')
            if level:
                t['Level'] = str(level[0]) if level[0] == level[1] else f'{level[0]}-{level[1]}'
            if t.get('ClassType') and 'Scripts.Legion' in t['ClassType']:
                t['ClassType'] = 'DOL.GS.GameNPC'          # the Darkness Falls Legion script is not for this dungeon
            assert t['NpcTemplate_ID'] not in template_keys
            template_keys.add(t['NpcTemplate_ID'])
            edits.append({'table': 'NpcTemplate', 'insert': t})
            made_templates[key] = tid
            return tid

        def add(name, donor, x, y, z, heading, package, template_id=None):
            seq[0] += 1
            row = {c: donor[c] for c in mob_cols}
            row.update(Mob_ID=f'dr3-{region}-{seq[0]:04d}', Name=name, Region=region, X=int(round(x)), Y=int(round(y)), Z=int(round(z)),
                       Heading=int(heading) % 4096, PackageID=package)
            if template_id is not None:
                row['NPCTemplateID'] = template_id
            if 'Scripts.Legion' in (row.get('ClassType') or ''):
                row['ClassType'] = 'DOL.GS.GameNPC'
            assert row['Mob_ID'] not in have_ids
            edits.append({'table': 'Mob', 'insert': row})
            counts[package] += 1

        # ---- bosses and named demons at their real /loc; minions around their marker
        for p in positions:
            raw = html.unescape(p['name'])
            key = norm(raw)
            display = RENAME.get(raw.split(' (')[0].strip().lower(), raw.split(' (')[0].strip())
            plain = raw.split(' (')[0].strip().lower()
            if plain in SKIP:
                notes.append(f'left out (invisible helper): {display}')
                continue
            counterpart = COUNTERPART[region].get(plain) or SHARED_FALLBACK.get(plain) or key
            donor_rows = alb_rows.get(norm(counterpart))
            if not donor_rows:
                notes.append(f'no Albion counterpart in our world, left out: {display}')
                continue
            level = levels.get(int(p['zam_id']))
            wx, wy = int(p['world_x']), int(p['world_y'])
            if p['loc_z'] and len(donor_rows) == 1:         # a single named creature at its real spot
                if any((ex - wx) ** 2 + (ey - wy) ** 2 < 600 ** 2 for ex, ey, _ in existing.get(norm(display), ())):
                    notes.append(f'already spawned there: {display}')
                    continue
                tid = template_for(display, donor_rows[0], level)
                add(display, donor_rows[0], wx, wy, int(p['loc_z']), 0, 'DR3-exact', tid)
                taken.append((wx, wy, int(p['loc_z'])))
            else:
                if existing.get(norm(display)):
                    notes.append(f'minion type already spawned ({len(existing[norm(display)])}): {display}')
                    continue
                # a minion type; one or two have an exact /loc for one of them, the rest only a marker
                anchor = floor.snap(wx, wy, int(p['loc_z']), reach=200) if p['loc_z'] else floor.snap_any_height(wx, wy)
                if not anchor:
                    notes.append(f'marker off the floor, left out: {display}')
                    continue
                tid = template_for(display, donor_rows[0], level)
                placed = 0
                for donor in donor_rows:
                    spot = floor.scatter(rng, anchor, 700, taken)
                    if spot:
                        add(display, donor, spot[0], spot[1], spot[2], rng.randrange(4096), 'DR3-minion', tid)
                        placed += 1
                notes.append(f'{display}: {placed} of {len(donor_rows)} (Albion count) placed near the marker')

        # ---- the common demons, room by room
        tgt_pieces = pieces(region)
        turn = Counter()
        empty_rooms = filled_rooms = 0
        for piece in tgt_pieces:
            anchor = floor.snap(piece['x'], piece['y'], piece['z'], reach=700, up=700)
            if not anchor:
                continue
            if any((ex - anchor[0]) ** 2 + (ey - anchor[1]) ** 2 < 1200 ** 2 for ex, ey, _ in all_existing):
                continue                                    # this part of the dungeon already has real spawns
            pool = donors.get(piece['kind'])
            if not pool:
                empty_rooms += 1
                continue
            donor_piece = pool[turn[piece['kind']] % len(pool)]
            turn[piece['kind']] += 1
            group = alb_by_piece.get(donor_piece, [])
            if not group:
                empty_rooms += 1
                continue
            filled_rooms += 1
            for donor, dist in group:
                spot = floor.scatter(rng, anchor, min(900, dist + 200), taken)
                if spot:
                    add(donor['Name'], donor, spot[0], spot[1], spot[2], rng.randrange(4096), 'DR3-filler')
        report.append(f'region {region}: {dict(counts)}; rooms {len(tgt_pieces)}, filled {filled_rooms}, left empty like their Albion twin {empty_rooms}')
        report.extend(f'    {n}' for n in notes)

    # ---- the way in and out of Helgardh (EoD db-public has both rows)
    ours = {(int(r['Id']), int(r['Realm'])) for r in db.execute('SELECT Id, Realm FROM ZonePoint')}
    keys = {r[0] for r in db.execute('SELECT ZonePoint_ID FROM ZonePoint')}
    for z in json.loads((Path(eod) / 'ZonePoint.json').read_text(encoding='utf-8')):
        k = (int(z['Id']), int(z['Realm']))
        if k in ((674, 2), (670, 2)) and k not in ours:
            ins = {c: z[c] for c in zp_cols if c in z and z[c] is not None}
            ins['ClassType'] = ''
            if ins.get('ZonePoint_ID') in keys:
                ins['ZonePoint_ID'] = f'dr3-zp-{k[0]}-{k[1]}'
            edits.append({'table': 'ZonePoint', 'insert': ins})
            report.append(f'zone point {k[0]} realm {k[1]}: region {z["SourceRegion"]} -> {z["TargetRegion"]}')

    # World Workshop's own navmesh audit of a sandbox trial is the last word: an estimated row it found off the floor
    # is dropped (ids are stable: the placement is seeded). Rows at a real /loc stay where the game had them.
    drop_file = HERE / 'dr-chapter3-2026-11-09.audit-drop.json'
    drop = json.loads(drop_file.read_text(encoding='utf-8')) if drop_file.exists() else {}
    before = len(edits)
    edits = [e for e in edits if not (e['table'] == 'Mob' and e['insert']['Mob_ID'] in drop and e['insert']['PackageID'] != 'DR3-exact')]
    report.append(f'dropped after the navmesh audit: {before - len(edits)} estimated rows')
    edits.sort(key=lambda e: {'NpcTemplate': 0, 'ZonePoint': 1, 'Mob': 2}[e['table']])
    Path(f'{prefix}.edits.json').write_text(json.dumps(edits, indent=0, ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')
    report.insert(0, f'{len(edits)} edits: {dict(Counter(e["table"] for e in edits))}')
    Path(f'{prefix}.report.txt').write_text('\n'.join(report) + '\n', encoding='utf-8', newline='\n')
    print('\n'.join(report))


if __name__ == '__main__':
    main()
