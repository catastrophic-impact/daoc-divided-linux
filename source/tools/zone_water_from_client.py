"""Writes GameServer/world/data/zone_water.json from the 1.130 client's zone files.

The client draws water only inside the [riverNN] shapes of each zone's sector.dat, so land below
a region's water level (Stygian Delta, Land of Atum) is dry. The server reads these shapes for
the zones listed here instead of the flat zone/region water level.

Usage: python -I zone_water_from_client.py "<client folder>" [zone ids...]
Default zones: the three Atlantis copies (regions 30, 73, 130).
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'tools', 'asset-tool'))
import archive  # noqa: E402

ATLANTIS = list(range(30, 45)) + list(range(73, 88)) + list(range(130, 145))
ATLANTIS_REGIONS = {30, 73, 130}
WATER_TYPES = {'DEEPOCEAN', 'OCEAN', 'LAKE', 'RIVER', 'POND', 'SWAMP'}


def mpk_files(path):
    _, entries = archive.read(open(path, 'rb').read())
    return {e.name.lower(): e.data for e in entries}


def sections(text):
    out, name = {}, None
    for line in text.replace('\r', '').split('\n'):
        line = line.split(';', 1)[0].strip()
        m = re.match(r'^\[(.+)\]$', line)
        if m:
            name = m.group(1).lower()
            out[name] = {}
        elif name and '=' in line:
            k, v = line.split('=', 1)
            out[name][k.strip().lower()] = v.strip()
    return out


def shapes(sector):
    result = []
    for name, kv in sector.items():
        if not name.startswith('river'):
            continue
        kind = kv.get('type', '').upper()
        if kind not in WATER_TYPES:
            continue
        n = int(kv.get('bankpoints', '0') or 0)
        left = [tuple(int(v) * 256 for v in kv[f'left{i:02d}'].split(',')[:2]) for i in range(n)]
        right = [tuple(int(v) * 256 for v in kv[f'right{i:02d}'].split(',')[:2]) for i in range(n)]
        quads = [[*left[i], *right[i], *right[i + 1], *left[i + 1]] for i in range(n - 1)]
        if quads:
            result.append({'name': name, 'type': kind, 'height': int(kv['height']), 'quads': quads})
    return result


def main():
    client = sys.argv[1]
    wanted = [int(z) for z in sys.argv[2:]] or ATLANTIS
    zones_dat = sections(mpk_files(os.path.join(client, 'zones', 'zones.mpk'))['zones.dat'].decode('latin1'))
    out = {}
    for zone in wanted:
        entry = zones_dat.get(f'zone{zone:03d}')
        if entry is None or (zone in ATLANTIS and int(entry.get('region', -1)) not in ATLANTIS_REGIONS):
            continue
        art = int(entry.get('proxy_zone', zone))
        dat = os.path.join(client, 'zones', f'zone{art:03d}', f'dat{art:03d}.mpk')
        sector = mpk_files(dat).get('sector.dat')
        if sector is None:
            raise SystemExit(f'zone {zone}: no sector.dat in {dat}')
        out[str(zone)] = shapes(sections(sector.decode('latin1')))
    target = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'server', 'GameServer', 'world', 'data', 'zone_water.json')
    with open(target, 'w', newline='\n') as f:
        json.dump(out, f, separators=(',', ':'))
    print(f'{len(out)} zones, {sum(len(v) for v in out.values())} water shapes -> {os.path.normpath(target)}')


if __name__ == '__main__':
    main()
