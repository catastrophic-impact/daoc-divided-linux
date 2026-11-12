"""Checks of the modern client patch (make_client_patch.py): synthetic edits, and the real pristine 1.130 client when
this machine has one (OFFLINE_DAOC_PRISTINE_1130, or the developer's OpenDAoC copy)."""
import base64
import hashlib
import json
import os
import re
import struct
import sys
import unittest
import zlib
from pathlib import Path
import make_client_patch as m

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools' / 'asset-tool'))
import archive  # noqa: E402

PRISTINE = Path(os.environ.get('OFFLINE_DAOC_PRISTINE_1130', Path.home() / 'Desktop/OpenDAoC/DAoC Full Client/Dark Age of Camelot'))
CLASSIC = Path(os.environ.get('OFFLINE_DAOC_CLASSIC', Path(__file__).resolve().parents[3] / 'Classic/runtime/client-opendaoc/app'))
MODERN_TEST = Path(__file__).resolve().parents[2] / 'artifacts/modern-client-test/runtime/client-modern/app'


def entry(original, patched):
    return dict(Path='x', InputSha256=m.sha(original), OutputSha256=m.sha(patched), OutputSize=len(patched),
                Edits=[dict(At=at, Remove=remove, Insert=base64.b64encode(block).decode('ascii'))
                       for at, remove, block in m.edits(original, patched)])


def single_file_archive(data, stamp=1234):
    return archive.write(b'zones.mpk', [archive.Entry('zones.dat', data, stamp)])


def sections(table):
    found, current = {}, None
    for line in table.decode('latin1').splitlines():
        head = re.match(r'\s*\[(\w+)\]', line)
        if head:
            current = found.setdefault(head.group(1).lower(), {})
        elif current is not None and '=' in line and not line.lstrip().startswith(';'):
            key, value = line.split('=', 1)
            current[key.strip().lower()] = value.split(';')[0].strip()
    return found


ZONE_OFFSETS = {0: (548864, 483328), 3: (483328, 368640), 206: (385024, 483328)}
# The zones past each gate, unchanged from classic, and the two wall ends their bound.csv leaves open at the home zone's edge.
GATE_NEIGHBOURS = {0: (11, 565248, 417792, [(583424, 483322), (586757, 483322)]),
                   3: (12, 499712, 303104, [(512768, 368640), (518912, 368640)]),
                   206: (210, 385024, 417792, [(419456, 483328), (423168, 483328)])}
GATE_LINKS = Path(__file__).resolve().parents[1] / 'server/tools/navmesh_splice/modern-gate-links.json'


def bound_polygons(bound):
    polygons = []
    for line in bound.decode('latin1').splitlines():
        numbers = [int(n) for n in re.findall(r'-?\d+', line)]
        if numbers:
            polygons.append(list(zip(numbers[2::2], numbers[3::2])))
    return polygons


def crosses(a, b, c, d):
    def side(p, q, r):
        return (q[0] - p[0]) * (r[1] - p[1]) - (q[1] - p[1]) * (r[0] - p[0])
    return side(a, b, c) * side(a, b, d) < 0 and side(c, d, a) * side(c, d, b) < 0


STOCK_TABLE = b''.join(
    b'[zone%d] ;20-24 Level PVP Zone\r\nenabled=1\r\nregion=%d\r\nname=Caledonia\r\nregion_offset_x=1\r\n\r\n' % pair
    for pair in [(250, 166), (251, 167), (252, 168)]) + b'[zone260]\r\nregion=9\r\nname=Other'


class ClientPatchTests(unittest.TestCase):
    def test_rows_go_in_by_id_or_not_at_all(self):
        # The launcher's WithRows: another build of a catalog gets our rows at their place; a taken ID leaves it alone.
        table = '10,staff\r\n12,new live row\r\n20,spear\r\n14999,end\r\n'
        self.assertEqual(m.with_rows(table, ['15,rod', '16,rod staff']),
                         '10,staff\r\n12,new live row\r\n15,rod\r\n16,rod staff\r\n20,spear\r\n14999,end\r\n')
        self.assertEqual(m.with_rows('10,staff\r\n15,rod\r\n', ['15,rod']), '10,staff\r\n15,rod\r\n')
        self.assertIsNone(m.with_rows('10,staff\r\n15,garden pitcher\r\n', ['15,rod']))
        self.assertEqual(m.with_rows('10,staff\r\n', ['15,rod']), '10,staff\r\n15,rod\r\n')

    def test_in_place_changes_and_growth(self):
        original = bytes(range(256)) * 5000  # over 1 MB: compared in place
        patched = bytearray(original)
        patched[10:14] = b'ABCD'
        patched[500000] = 0xFF
        patched += b'new section'
        e = entry(original, bytes(patched))
        self.assertEqual(m.apply(original, e), bytes(patched))
        self.assertEqual(len(e['Edits']), 3)

    def test_an_insertion_keeps_only_the_new_text(self):
        original = b''.join(b'<line%d>value %d</line%d>\r\n' % (i, i * 7, i) for i in range(60))
        cut = original.index(b'<line30>')
        patched = original[:cut] + b'<c>added</c>\r\n' + original[cut:]
        e = entry(original, patched)
        self.assertEqual(m.apply(original, e), patched)
        # Only the added line's length ships (the matcher may align it a few bytes either way).
        self.assertEqual(sum(len(base64.b64decode(x['Insert'])) for x in e['Edits']), len(b'<c>added</c>\r\n'))
        self.assertTrue(all(x['Remove'] == 0 for x in e['Edits']))

    def test_refuses_another_input(self):
        original, patched = b'x' * 100, b'y' * 100
        with self.assertRaises(ValueError):
            m.apply(b'z' * 100, entry(original, patched))

    def test_a_whole_file_replacement_is_one_guarded_edit(self):
        stock, ours = b'stock splash archive' * 50, b'our splash'
        e = entry(stock, ours) | dict(Edits=[dict(At=at, Remove=remove, Insert=base64.b64encode(block).decode('ascii'))
                                             for at, remove, block in m.replace_whole(stock, ours)])
        self.assertEqual(len(e['Edits']), 1)
        self.assertEqual(m.apply(stock, e), ours)
        with self.assertRaises(ValueError):
            m.apply(b'a different splash', e)

    def test_stored_zlib_inflates_and_splits_long_files(self):
        data = bytes(range(256)) * 600
        stream = m.stored_zlib(data)
        self.assertEqual(zlib.decompress(stream), data)
        self.assertEqual(struct.unpack_from('<BHH', stream, 2), (0, 65535, 0))

    def test_a_member_entry_rebuilds_a_valid_archive(self):
        stock = single_file_archive(STOCK_TABLE)
        entry = m.zone_table_entry(stock)
        patched = m.apply(stock, entry)
        name, entries = archive.read(patched)
        self.assertEqual((name, entries[0].name, entries[0].timestamp), (b'zones.mpk', 'zones.dat', 1234))
        archive.verify_memory_image(patched)
        with self.assertRaises(ValueError):
            m.apply(single_file_archive(b'a newer table'), entry)

    def test_a_member_of_a_larger_archive_changes_alone(self):
        stock = archive.write(b'dat000.mpk', [archive.Entry('a.csv', b'first', 1), archive.Entry('bound.csv', b'old wall', 2),
                                              archive.Entry('z.csv', b'last' * 100, 3)])
        patched = m.with_member(stock, b'a new wall', 'BOUND.csv')
        name, entries = archive.read(patched)
        self.assertEqual(name, b'dat000.mpk')
        self.assertEqual([(e.name, e.data, e.timestamp) for e in entries],
                         [('a.csv', b'first', 1), ('bound.csv', b'a new wall', 2), ('z.csv', b'last' * 100, 3)])
        archive.verify_memory_image(patched)
        self.assertEqual(m.member_of(patched, 'z.csv'), b'last' * 100)
        with self.assertRaises(ValueError):
            m.member_of(stock)
        with self.assertRaises(ValueError):
            m.with_member(stock, b'x', 'missing.csv')

    def test_gate_jumps_go_and_the_rest_are_numbered_again(self):
        table = (b'1,PVPzone,0,0,0,0,0,0,4,,\r\n2,Erik,1,2,3,4,5,6,112,0,1\r\n12,Border,1,2,3,4,5,6,,,\r\n'
                 b'13,Border,1,2,3,4,5,6,,,\r\n14,Border,1,2,3,4,5,6,,,\r\n15,Border,1,2,3,4,5,6,,,\r\n'
                 b'16,toKT1,1,2,3,4,5,6,428\r\n28,artfix967,1,2,3,4,5,6,\r\n29,artfix967,1,2,3,4,5,6,\r\n')
        self.assertEqual(m.without_gate_jumps(table, 100),
                         b'1,PVPzone,0,0,0,0,0,0,4,,\r\n2,Erik,1,2,3,4,5,6,112,0,1\r\n3,toKT1,1,2,3,4,5,6,428\r\n')
        with self.assertRaises(ValueError):
            m.without_gate_jumps(table.replace(b'28,artfix967', b'28,somethingelse'), 100)

    def test_the_real_gate_jumps_go(self):
        if not (PRISTINE / 'game.dll').is_file():
            self.skipTest('No pristine 1.130 client on this machine')
        for zone, drop in m.GATE_JUMPS:
            for template in m.GATE_ARCHIVES:
                path = PRISTINE / template.format(zone)
                stock = path.read_bytes()
                if not m.has_member(stock, m.ZONE_JUMP):
                    continue
                before = m.member_of(stock, m.ZONE_JUMP).decode('latin1').split('\r\n')
                after = m.member_of(m.apply(stock, m.gate_entry(PRISTINE, template.format(zone), zone)), m.ZONE_JUMP)
                after = after.decode('latin1').split('\r\n')
                names = [line.split(',')[1] for line in after if line.strip()]
                self.assertFalse({'Frontier', 'Border'} & set(names), path)
                self.assertFalse([n for n in names if n.lower().startswith('artfix')], path)
                self.assertEqual([line.split(',', 1)[0] for line in after if line.strip()],
                                 [str(n) for n in range(1, len(names) + 1)])
                self.assertEqual(len([line for line in before if line.strip()]) - len(drop), len(names))

    def test_a_gate_wall_edit_refuses_another_wall(self):
        with self.assertRaises(ValueError):
            m.open_gate_walls(b'0, 2, 1, 2, 3, 4\r\n0, 2, 5, 6, 7, 8\r\n0, 2, 9, 9, 8, 8\r\n', 206)

    def test_the_zone_table_gets_the_server_regions_and_nothing_else(self):
        stock = single_file_archive(STOCK_TABLE)
        table = archive.read(m.apply(stock, m.zone_table_entry(stock)))[1][0].data
        found = sections(table)
        for zone, name in [(250, 'Caledonia'), (251, 'Murdaigean'), (252, 'Thidranki'), (253, 'Abermenai'),
                           (246, 'Dodens Gruva'), (247, 'Marfach Caverns'), (248, "Summoner's Hall"), (276, 'Marfach Caverns')]:
            self.assertEqual((found[f'zone{zone}']['region'], found[f'zone{zone}']['name']), (str(zone), name))
        self.assertEqual(found['zone260'], dict(region='9', name='Other'))
        self.assertTrue(table.startswith(b'[zone250] ;20-24 Level PVP Zone\r\nenabled=1\r\nregion=250\r\nname=Caledonia\r\n'))

    def test_a_table_laid_out_differently_is_refused(self):
        with self.assertRaises(ValueError):
            m.zone_table_edits(STOCK_TABLE.replace(b'region=167', b'region=99'))

    def test_a_small_rename_ships_only_the_changed_bytes(self):
        original, patched = b'head' + b'x' * 500 + b'tail', b'head' + b'yy' + b'x' * 499 + b'tail'
        (at, remove, block), = m.changed_middle(original, patched)
        self.assertEqual(original[:at] + block + original[at + remove:], patched)
        self.assertEqual((at, remove, block), (4, 1, b'yy'))

    def test_the_real_client_patch_rebuilds_the_test_client(self):
        if not (PRISTINE / 'game.dll').is_file() or not (CLASSIC / 'zones/zone276').is_dir():
            self.skipTest('No pristine 1.130 and classic clients on this machine')
        try:
            import numpy  # noqa: F401  (the fishing rod's mesh code)
        except ImportError:
            self.skipTest('No numpy for the fishing rod (run with the World Workshop build dependencies on PYTHONPATH)')
        patch = m.build(PRISTINE, CLASSIC)
        self.assertEqual([f['Path'] for f in patch['Files']],
                         ['game.dll', 'pregame/character_creation.xml', 'pregame/splash.mpk', 'zones/zones.mpk',
                          'zones/zone253/ter253.mpk', 'zones/zone253/lod253.mpk',
                          'zones/zone276/dat276.mpk', 'zones/zone276/sounds.dat',
                          'zones/zone000/dat000.mpk', 'zones/zone000/csv000.mpk',
                          'zones/zone003/dat003.mpk', 'zones/zone003/csv003.mpk',
                          'zones/zone100/dat100.mpk', 'zones/zone100/csv100.mpk',
                          'zones/zone102/dat102.mpk', 'zones/zone102/csv102.mpk',
                          'zones/zone206/dat206.mpk', 'zones/zone206/csv206.mpk',
                          'zones/zone207/dat207.mpk', 'zones/zone207/csv207.mpk',
                          'items/offline_fishrod01.nif',
                          'items/pskins/pskin061.mpk', 'figures/Mskins/mskin070.mpk',
                          'items/pskins/pskin062.mpk', 'figures/Mskins/mskin071.mpk'] +
                         [f'items/Tara_helm01_{sex}_{race}.nif' for race in ('cel', 'elf', 'lur', 'fir', 'syl', 'sha')
                          for sex in 'MF'] +
                         ['items/pskins/pskin063.mpk', 'figures/Mskins/mskin072.mpk', 'gamedata.mpk'])
        # The fishing rod: only our vertex positions ship.
        rod, game = next(f for f in patch['Files'] if f['Path'] == 'items/offline_fishrod01.nif'), patch['Files'][-1]
        self.assertLess(sum(len(base64.b64decode(e['Insert'])) for e in rod['Edits']), 1000)
        # The rod's three catalog rows and the Storm, Tara and Camelot Regalia rows ship, and every other catalog is untouched.
        self.assertEqual([part['Member'] for part in game['Members']], list(m.GAMEDATA_CATALOGS))
        added = {part['Member']: [line.split(',', 1)[0] for e in part['Edits']
                                  for line in base64.b64decode(e['Insert']).decode('latin1').strip('\r\n').split('\r\n')]
                 for part in game['Members']}
        self.assertEqual(added, {'objects.csv': ['4927', '4928', '4929', '4930', '4931', '4932', '4933', '4937', '4942'] +
                                                [str(n) for n in range(4950, 4965)],
                                 'items.csv': [str(n) for n in range(44, 56)] + ['3085'],
                                 'pskins.csv': [str(n) for n in range(5790, 5798)] + ['5800'] + [str(n) for n in range(5810, 5829)],
                                 'mskins.csv': [str(n) for n in range(2740, 2746)] + [str(n) for n in range(2750, 2762)]})
        stock_game = (PRISTINE / 'gamedata.mpk').read_bytes()
        patched_game = m.apply(stock_game, game)
        archive.verify_memory_image(patched_game)
        before, after = archive.read(stock_game)[1], archive.read(patched_game)[1]
        self.assertEqual([e.name for e in before], [e.name for e in after])
        self.assertEqual([e.data for e in before if e.name.lower() not in m.GAMEDATA_CATALOGS],
                         [e.data for e in after if e.name.lower() not in m.GAMEDATA_CATALOGS])
        # The Regalia skin archives hold only our textures, the same ones the test client's installers put in (its Storm
        # archive has more concept textures, and its classic cloak came from an older Pillow: the same image, other DXT3 bits).
        # The Crown of Lugh: the stock Lug helmets with only their 52 transform bytes changed (pipeline/fit_lugh.py).
        lugh = [f for f in patch['Files'] if f['Path'].startswith('items/Tara_helm01_')]
        self.assertEqual(len(lugh), 12)
        for mesh in lugh:
            self.assertTrue(mesh['From'].startswith('items/Lum_helm01_'))
            self.assertLessEqual(sum(len(base64.b64decode(e['Insert'])) for e in mesh['Edits']), 52)
        for skins in [f for f in patch['Files'] if f.get('From') == m.REGALIA_FROM]:
            ours = m.apply((PRISTINE / skins['From']).read_bytes(), skins)
            archive.verify_memory_image(ours)
            self.assertTrue(all(e.name.lower().startswith(('sf_rw_', 'sf_wolfcloak', 'sf_wingedhelm', 'tara_', 'camelot_'))
                                for e in archive.read(ours)[1]), skins['Path'])
            if MODERN_TEST.is_dir():
                installed = {e.name: e.data for e in archive.read((MODERN_TEST / skins['Path']).read_bytes())[1]}
                for e in archive.read(ours)[1]:
                    if e.name != 'sf_wolfcloak256.dds':
                        self.assertEqual(installed.get(e.name), e.data, e.name)
        game = patch['Files'][0]
        self.assertEqual((game['InputSha256'][:8], game['OutputSha256'][:8]), ('3915a495', 'f6660890'))  # 2c7e306e0 (Vampiir hover); the test client and DR-dev-0.1d match
        # The stock 1.130 loading screen in, the Divided Realms splash out, in one edit.
        splash = patch['Files'][2]
        self.assertEqual(splash['InputSha256'][:8], '9348c3a6')
        self.assertEqual(splash['OutputSha256'], m.sha(m.SPLASH.read_bytes()))
        self.assertEqual(len(splash['Edits']), 1)
        self.assertEqual(m.apply((PRISTINE / 'pregame/splash.mpk').read_bytes(), splash), m.SPLASH.read_bytes())
        # Every stock section stays, only the three battleground zones change, and only our text ships.
        zones = patch['Files'][3]
        stock_zones = (PRISTINE / 'zones/zones.mpk').read_bytes()
        stock, ours = sections(m.member_of(stock_zones)), sections(m.member_of(m.apply(stock_zones, zones)))
        self.assertLessEqual(set(stock), set(ours))
        self.assertEqual({k for k in stock if stock[k] != ours[k]}, {'zone250', 'zone251', 'zone252'})
        self.assertLess(sum(len(base64.b64decode(e['Insert'])) for e in zones['Edits']), 1200)
        for terrain in patch['Files'][4:6]:
            self.assertLess(sum(len(base64.b64decode(e['Insert'])) for e in terrain['Edits']), 64)
            m.apply((PRISTINE / terrain['From']).read_bytes(), terrain)
        # Marfach's map is the classic client's, unchanged, and is the only retail data the patch adds.
        for marfach in patch['Files'][6:8]:
            self.assertEqual(m.apply((PRISTINE / marfach['From']).read_bytes(), marfach), (CLASSIC / marfach['Path']).read_bytes())
        self.assertLess(sum(f['OutputSize'] for f in patch['Files'][6:8]), 25000)
        if MODERN_TEST.is_dir():
            # The test client keeps its hand-installed classic zone table until it is set up from this patch.
            # Its gamedata.mpk got the rod rows from the hand installer (compressed differently, and with other private art).
            gate_archives = {template.format(zone) for zone in m.gate_zones() for template in m.GATE_ARCHIVES}
            # The Regalia archives were stamped when they were installed there (their contents are compared above).
            regalia = {f['Path'] for f in patch['Files'] if f.get('From') == m.REGALIA_FROM}
            regalia |= {f['Path'] for f in lugh if not (MODERN_TEST / f['Path']).exists()}  # until the Tara kit is reinstalled
            for f in [f for f in patch['Files'] if f['Path'] not in ('zones/zones.mpk', m.GAMEDATA) and f['Path'] not in gate_archives
                      and f['Path'] not in regalia]:
                self.assertEqual(hashlib.sha256((MODERN_TEST / f['Path']).read_bytes()).hexdigest(), f['OutputSha256'], f['Path'])

    def test_the_real_gates_open_and_nothing_else_moves(self):
        if not (PRISTINE / 'game.dll').is_file():
            self.skipTest('No pristine 1.130 client on this machine')
        links = json.loads(GATE_LINKS.read_text(encoding='utf-8'))['zones']
        for zone, (ox, oy) in ZONE_OFFSETS.items():
            neighbour, nx, ny, wall_ends = GATE_NEIGHBOURS[zone]
            beyond = m.member_of((PRISTINE / f'zones/zone{neighbour:03}/dat{neighbour:03}.mpk').read_bytes(), m.BOUND)
            ends = {(x + nx, y + ny) for polygon in bound_polygons(beyond) for x, y in (polygon[0], polygon[-1])}
            self.assertLessEqual(set(wall_ends), ends)
            for template in m.GATE_ARCHIVES:
                path = template.format(zone)
                stock = (PRISTINE / path).read_bytes()
                entry = m.gate_entry(PRISTINE, path, zone)
                patched = m.apply(stock, entry)
                archive.verify_memory_image(patched)
                # Only bound.csv (and zonejump.csv) change, and only our numbers ship.
                bound_edits = next(part for part in entry.get('Members') or [entry] if part['Member'] == m.BOUND)['Edits']
                self.assertLess(sum(len(base64.b64decode(e['Insert'])) for e in bound_edits), 150)
                before, after = archive.read(stock)[1], archive.read(patched)[1]
                self.assertEqual([e.name for e in before], [e.name for e in after])
                self.assertEqual([e.data for e in before if e.name.lower() not in (m.BOUND, m.ZONE_JUMP)],
                                 [e.data for e in after if e.name.lower() not in (m.BOUND, m.ZONE_JUMP)])
                old, new = bound_polygons(m.member_of(stock, m.BOUND)), bound_polygons(m.member_of(patched, m.BOUND))
                self.assertEqual(len(old), len(new))
                touched = {wall[1] for wall in m.GATE_WALLS if wall[0] == zone}
                self.assertEqual([p for i, p in enumerate(old) if i not in touched], [p for i, p in enumerate(new) if i not in touched])
                for i in touched:
                    line = m.member_of(patched, m.BOUND).split(b'\r\n')[i].split(b', ')
                    self.assertEqual(int(line[1]), len(new[i]))
                # The ends of the patched walls meet the walls of the zone beyond (Forest Sauvage's sit 6 units short).
                loose = {(x + ox, y + oy) for i in touched for x, y in (new[i][0], new[i][-1])}
                for end in wall_ends:
                    self.assertTrue(any(abs(end[0] - x) <= 8 and abs(end[1] - y) <= 8 for x, y in loose), end)
                # Every walk link through the gate is clear of every wall.
                walls = [(p[i], p[i + 1]) for p in new for i in range(len(p) - 1)]
                for link in links[f'{zone:03}']['links']:
                    a, b = [tuple(float(v) for v in end.split(',')[:2]) for end in link.split(':')[:2]]
                    a, b = (a[0] - ox, a[1] - oy), (b[0] - ox, b[1] - oy)
                    self.assertFalse([w for w in walls if crosses(a, b, *w)], link)


if __name__ == '__main__':
    unittest.main()
