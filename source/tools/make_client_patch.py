"""Make the modern client patch that players apply to their own 1.130 client (no retail file ships, see Marfach below).

    python make_client_patch.py <pristine 1.130 client folder> <classic client folder> <client-patch.json>

Runs the hash-guarded class-slot patch (classslots/patch_class_slots.py: game.dll's native class slots 63-67 and
the creation screen's extra class buttons in pregame/character_creation.xml) on the pristine files, then records
only the edits (our hooks, section and added buttons, not Broadsword's bytes) with each file's input and output
SHA256. The launcher's client setup (OfflineDaoc.Launcher/ModernClientSetup.cs) copies the player's client and applies
these edits in order; it refuses any input that isn't the pristine 1.130 file. These two files and the splash below are
the only client files the modern test changes (a full hash comparison of the test client with a pristine 1.130 client,
2026-10-25, before the splash and the zone table below).

The loading screen, pregame/splash.mpk, is replaced whole by the Divided Realms splash (our own art,
OfflineDaoc.Launcher/Assets/divided-realms-client-splash.mpk): one edit that removes the stock archive and inserts ours,
guarded by the stock file's SHA256 like the others.

The stock zone table, zones/zones.mpk, has none of the regions the server uses for the four battlegrounds and the old RvR
dungeons: entering one dropped the player to character select or left them out of bounds. Its zones.dat is compressed, so
a byte edit of the archive would ship Mythic's whole table. Instead an entry with Member edits the file inside a
single-file archive and the launcher rebuilds the archive with stored zlib blocks, which Python and .NET write byte for
byte alike. Only our added sections and changed values ship.

Live walled off the frontier gates of Castle Sauvage, Snowdonia Fortress and Druim Cain: each zone's bound.csv, which the
client enforces, closes the gate and the way out of the zone, so the gate opens and the player still can't walk through
(issue #2, a bound wall exactly where the 2026-10-28 walk links cross). Those zones' dat and csv archives get Member
edits that swap the closing walls for the classic client's, whose ends meet the neighbouring zones' unchanged walls.
Live also put zone jumps at all six border keeps' frontier gates (a Frontier jump to New Frontiers and untargeted Border /
artfix lines); GATE_JUMPS takes them out of those zones' zonejump.csv, so the gates lead into the old frontiers again.

An entry with From makes a new file from another stock file: Abermenai's terrain is Thidranki's with the archive renamed
(add_abermenai_terrain.py).

The private fishing rod (tools/pet-art/install_fishing_rod.py, the server's fishing_pole_model 4927 and 4928) comes two ways
with nothing retail in it: items/offline_fishrod01.nif is made From the player's own quarterstaff NIF with only our vertex
positions and bounding sphere, and one gamedata.mpk entry with Members adds our three rows to objects.csv and items.csv,
one member after the other. Building the patch needs numpy (the rod's mesh code).

The Sovereign Regalia art (tools/sovereign-art: Storm Regalia objects 4929-4933, 4937 and 4942, Regalia of Tara and of
Camelot 4950-4963) is our painted skins on stock meshes: each kit's two skin archives (pskin061/mskin070, pskin062/mskin071,
pskin063/mskin072) are made From a stock skin archive and ship whole, since every texture in them is ours, and its
pskins.csv, mskins.csv and objects.csv rows join the rod's in the one gamedata.mpk entry. The archives use a fixed member
time, so the same textures give the same bytes. Building them needs Pillow. One texture is not only ours, by the owner's
decision (2026-11-08): the Storm winged helm's wings are the stock ToA winged helm skin's feathers recoloured.

Marfach Caverns (region 276) is the one exception to shipping nothing retail, by the owner's decision (2026-10-30): 1.130
has no copy of its map, so dat276.mpk and its sounds.dat (about 20 KB, read from the classic client when the patch is
built, never kept in Git) travel inside the patch as whole-file edits. The dungeon models it places are stock.
"""
import argparse
import base64
import difflib
import hashlib
import json
import struct
import sys
import zlib
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / 'classslots'))
import patch_class_slots  # noqa: E402
import add_abermenai_terrain  # noqa: E402

FORMAT = 1
CLIENT_VERSION = '1.130'
MERGE_GAP = 32  # in-place changes this close together are kept in one edit
ROW_SIZE = 284  # one directory row of an MPAK archive
SPLASH = Path(__file__).resolve().parent / 'OfflineDaoc.Launcher/Assets/divided-realms-client-splash.mpk'
STORED_BLOCK = 65535  # the most one stored zlib block holds

# (zone, polygon of bound.csv, the live points to replace, the points that replace them): see the docstring.
GATE_WALLS = [
    (0, 1, [(33024, 6272), (32384, 4544), (30976, 2048), (30208, 1280), (30208, 256), (31488, 0), (34560, 0), (37893, 0)], [(37893, 0)]),
    (0, 2, [(33408, 2304), (35072, 2304), (37632, 2304)], [(33600, 1600), (34304, 1280), (34560, 0)]),
    (3, 1, [(32256, 1600), (34014, 3409), (35712, 5056)], [(31104, 1792), (30336, 1024), (29568, 640), (29440, 0)]),
    (3, 2, [(31168, 2752), (31104, 1792), (32512, 832), (33984, 1472), (36480, 4096)],
     [(35584, 0), (35584, 768), (36352, 1536), (36608, 2304), (36608, 3584), (36480, 4096)]),
    (206, 1, [(35757, 1726)], [(38144, 0)]),
    (206, 2, [(35791, 1720)], [(34304, 896), (34432, 0)]),
]
GATE_ARCHIVES = ['zones/zone{0:03}/dat{0:03}.mpk', 'zones/zone{0:03}/csv{0:03}.mpk']
BOUND = 'bound.csv'
# (zone, the zonejump.csv rows to drop, as "number,name"): live put a Frontier jump to New Frontiers just past each border
# keep's frontier gate and untargeted Border / artfix lines around it; the classic tables have none of them. The server
# has no such zone points, so the client stopped every player at the gate ("Invalid ZonePoint ... (ID: 191)" at
# Vindsaul Faste, DR-dev-0.1a report 2026-11-05). The rows left are numbered 1, 2, ... again.
GATE_JUMPS = [
    (0, ['10,Border', '11,Border', '12,Frontier']),                                            # Castle Sauvage
    (3, ['1,Frontier', '2,Border', '3,Border']),                                               # Snowdonia Fortress
    (100, ['12,Border', '13,Border', '14,Border', '15,Border', '28,artfix967', '29,artfix967']),  # Svasud Faste
    (102, ['1,Frontier', '2,Border', '3,Border', '4,Border', '5,Border']),                     # Vindsaul Faste
    (206, ['3,Frontier', '4,Border', '5,Border', '6,Border', '7,Border', '12,artfix', '13,artfix']),  # Druim Cain
    (207, ['10,Border', '11,Border', '12,Border', '13,Border', '24,ArtFix', '25,ArtFix', '26,ArtFix', '27,ArtFix']),  # Druim Ligen
]
ZONE_JUMP = 'zonejump.csv'
# The stock zones the server uses under other regions: (zone, stock region, server region, the name to show).
ZONE_REGIONS = [(250, 166, 250, b'Caledonia'), (251, 167, 251, b'Murdaigean'), (252, 168, 252, b'Thidranki')]
# (new file, the stock file its edit replaces): the classic client's files, see the docstring.
PET_ART = Path(__file__).resolve().parents[2] / 'tools/pet-art'
ROD_SOURCE, ROD_PATH = 'items/b_st_quarterstaff01.nif', 'items/offline_fishrod01.nif'
GAMEDATA = 'gamedata.mpk'
ROD_CATALOGS = ('objects.csv', 'items.csv')
SOVEREIGN_ART = Path(__file__).resolve().parents[2] / 'tools/sovereign-art'
# The Regalia kits (tools/sovereign-art/<kit>/install_<kit>_set.py): our painted skins on stock meshes, under new IDs.
REGALIA_KITS = ('storm', 'tara', 'camelot')
# The stock archive each new skin archive is made From (any file every 1.130 client has; its bytes don't ship).
REGALIA_FROM = 'items/pskins/pskin008.mpk'
REGALIA_STAMP = 1794009600  # 2026-11-07 00:00 UTC, the archives' member time: the same bytes on every build
# Every catalog member the patch adds rows to, in the order the gamedata.mpk entry edits them.
GAMEDATA_CATALOGS = ('objects.csv', 'items.csv', 'pskins.csv', 'mskins.csv')
MARFACH_FILES = [('zones/zone276/dat276.mpk', 'zones/zone247/dat247.mpk'), ('zones/zone276/sounds.dat', 'zones/zone247/sounds.dat')]
# The sections the server's regions need and the stock table lacks, as the classic table has them.
ADDED_SECTIONS = '''

[region248]
entry_music=6

[region250]
entry_music=2
grasscsv=grass_alb.csv
grassmap=grass_alb.tga

[region251]
entry_music=2
grasscsv=grass_alb.csv
grassmap=grass_alb.tga

[region252]
entry_music=2
grasscsv=grass_alb.csv
grassmap=grass_alb.tga

[region253]
entry_music=5
grasscsv=grass_alb.csv
grassmap=grass_alb.tga

[zone246]
enabled=1
type=2
region=246
name=Dodens Gruva
region_offset_x=1
region_offset_y=1
width=8
height=8

[zone247]
enabled=1
type=2
region=247
name=Marfach Caverns
region_offset_x=1
region_offset_y=1
width=8
height=8

[zone248]
enabled=1
type=2
region=248
name=Summoner's Hall
region_offset_x=1
region_offset_y=1
width=8
height=8

[zone253]
enabled=1
region=253
name=Abermenai
region_offset_x=1
region_offset_y=1
width=8
height=8
entry_music=3

[region276]
entry_music=6

[zone276]
enabled=1
type=2
region=276
name=Marfach Caverns
region_offset_x=1
region_offset_y=1
width=8
height=8
map_enabled=0
'''.replace('\n', '\r\n').encode('ascii')


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def edits(original: bytes, patched: bytes):
    """(at, remove, insert): at an offset of the original, drop `remove` bytes and put `insert` there. Same-size files
    (game.dll grows only by its appended section) are compared in place; the layout's added buttons are an insertion,
    so it is compared by sequence and only the inserted text is kept."""
    if len(patched) >= len(original) and len(original) > 1 << 20:
        out, start, last = [], None, None
        for offset in range(len(original)):
            if original[offset] == patched[offset]:
                continue
            if start is not None and offset - last <= MERGE_GAP:
                last = offset
                continue
            if start is not None:
                out.append((start, last + 1 - start, patched[start:last + 1]))
            start = last = offset
        if start is not None:
            out.append((start, last + 1 - start, patched[start:last + 1]))
        if len(patched) > len(original):
            out.append((len(original), 0, patched[len(original):]))
        return out
    matcher = difflib.SequenceMatcher(None, original, patched, autojunk=False)
    return [(i1, i2 - i1, patched[j1:j2]) for tag, i1, i2, j1, j2 in matcher.get_opcodes() if tag != 'equal']


def stored_zlib(data: bytes) -> bytes:
    """A zlib stream of stored blocks: unlike real compression, every writer makes exactly these bytes."""
    out = bytearray(b'\x78\x01')
    for start in range(0, len(data), STORED_BLOCK):
        block = data[start:start + STORED_BLOCK]
        last = 1 if start + STORED_BLOCK >= len(data) else 0
        out += struct.pack('<BHH', last, len(block), len(block) ^ 0xFFFF) + block
    return bytes(out + struct.pack('>I', zlib.adler32(data)))


def archive_parts(archive: bytes):
    """(name size, directory rows, data start) of an MPAK v2 archive."""
    _, directory_size, name_size, count = struct.unpack('<4I', bytes(v ^ i for i, v in enumerate(archive[5:21])))
    if archive[:5] != b'MPAK\x02':
        raise ValueError('not an MPAK v2 archive')
    directory = zlib.decompress(archive[21 + name_size:21 + name_size + directory_size])
    rows = [bytearray(directory[at:at + ROW_SIZE]) for at in range(0, count * ROW_SIZE, ROW_SIZE)]
    return name_size, rows, 21 + name_size + directory_size


def row_of(rows: list, member: str | None) -> bytearray:
    """The named file's directory row; with no name, the only one."""
    found = [row for row in rows if member is None or row[:256].split(b'\0')[0].decode('latin1').lower() == member.lower()]
    if len(found) != 1:
        raise ValueError(f'no single {member or "file"} in the MPAK v2 archive')
    return found[0]


def member_of(archive: bytes, member: str | None = None) -> bytes:
    """A file inside an MPAK v2 archive; with no name, the file of a single-file archive."""
    _, rows, start = archive_parts(archive)
    offset, packed = struct.unpack_from('<2I', row_of(rows, member), 272)
    return zlib.decompress(archive[start + offset:start + offset + packed])


def with_member(archive: bytes, data: bytes, member: str | None = None) -> bytes:
    """The archive with one file replaced, written with stored blocks; every name, timestamp and other file is kept."""
    name_size, rows, start = archive_parts(archive)
    target = row_of(rows, member)
    payloads, position, memory = [], 0, 0
    for row in rows:
        size, offset, packed, crc = struct.unpack_from('<4I', row, 268)
        payload = archive[start + offset:start + offset + packed]
        if row is target:
            payload = stored_zlib(data)
            size, crc = len(data), zlib.crc32(payload)
        struct.pack_into('<5I', row, 264, memory, size, position, len(payload), crc)
        payloads.append(payload)
        position += len(payload)
        memory += size
    directory = stored_zlib(b''.join(rows))
    header = struct.pack('<4I', zlib.crc32(directory), len(directory), name_size, len(rows))
    return (archive[:5] + bytes(v ^ i for i, v in enumerate(header)) + archive[21:21 + name_size] + directory +
            b''.join(payloads))


def splice(source: bytes, changes: list) -> bytes:
    out, position = bytearray(), 0
    for edit in changes:
        out += source[position:edit['At']]
        out += base64.b64decode(edit['Insert'])
        position = edit['At'] + edit['Remove']
    return bytes(out + source[position:])


def row_id(line: str):
    first = line.split(',', 1)[0].strip()
    return int(first) if first.isascii() and first.isdigit() else None


def with_rows(table: str, rows: list):
    """The launcher's WithRows: each row before the first higher ID; None when one of our IDs holds another row."""
    lines = table.split('\r\n')
    for row in rows:
        numbered = [(at, row_id(line)) for at, line in enumerate(lines) if row_id(line) is not None]
        if not numbered:
            return None
        same = [lines[at] for at, n in numbered if n == row_id(row)]
        if same:
            if any(line != row for line in same):
                return None
            continue
        lines.insert(next((at for at, n in numbered if n > row_id(row)), numbered[-1][0] + 1), row)
    return '\r\n'.join(lines)


def adapt(original: bytes, entry: dict) -> bytes:
    """The launcher's Adapt: an AnyBuild archive that isn't the stock one gets the rows by ID, or stays as it is."""
    out = original
    for part in entry['Members']:
        table = member_of(out, part['Member']).decode('latin1')
        added = with_rows(table, part['Rows'])
        if added is None:
            return original
        if added != table:
            out = with_member(out, added.encode('latin1'), part['Member'])
    return out


def apply(original: bytes, entry: dict) -> bytes:
    """The launcher's algorithm, for the tests: check the input, rebuild the output, check it."""
    if sha(original) != entry['InputSha256']:
        if entry.get('AnyBuild'):
            return adapt(original, entry)
        raise ValueError(f"{entry.get('From', entry['Path'])}: not the pristine {CLIENT_VERSION} file")
    member = entry.get('Member')
    if entry.get('Members'):
        # Several files inside one archive, edited in order, each over the archive the one before left.
        out = original
        for part in entry['Members']:
            out = with_member(out, splice(member_of(out, part['Member']), part['Edits']), part['Member'])
    elif member:
        out = with_member(original, splice(member_of(original, member), entry['Edits']), member)
    else:
        out = splice(original, entry['Edits'])
    if len(out) != entry['OutputSize'] or sha(bytes(out)) != entry['OutputSha256']:
        raise ValueError(f"{entry['Path']}: the patched file doesn't match")
    return bytes(out)


def replace_whole(original: bytes, replacement: bytes):
    """One edit that swaps the whole file (the splash archive shares no bytes worth diffing with the stock one)."""
    return [(0, len(original), replacement)]


def changed_middle(original: bytes, patched: bytes):
    """One edit over the span between the common start and end (a renamed archive differs only in its header)."""
    limit = min(len(original), len(patched))
    start = next((i for i in range(limit) if original[i] != patched[i]), limit)
    end = next((i for i in range(limit - start) if original[-1 - i] != patched[-1 - i]), limit - start)
    return [(start, len(original) - end - start, patched[start:len(patched) - end])]


def zone_table_edits(table: bytes):
    """(at, remove, insert) over the stock zones.dat: the battleground zones move to the server's regions, each with its
    own name, and the missing sections are added at the end."""
    found = []
    for zone, stock_region, region, name in ZONE_REGIONS:
        start = table.index(b'[zone%d]' % zone)
        for key, old, new in [(b'region', b'%d' % stock_region, b'%d' % region), (b'name', b'Caledonia', name)]:
            at = table.index(b'\r\n%s=%s\r\n' % (key, old), start)
            if at - start > 200:
                raise ValueError(f'zone{zone}: no {key.decode()}={old.decode()} in its section')
            if old != new:
                found.append((at + len(key) + 3, len(old), new))
    found.append((len(table), 0, ADDED_SECTIONS))
    return sorted(found)


def encoded(changes) -> list:
    return [dict(At=at, Remove=remove, Insert=base64.b64encode(block).decode('ascii')) for at, remove, block in changes]


def zone_table_entry(stock: bytes) -> dict:
    table = member_of(stock)
    changes = zone_table_edits(table)
    patched_table, position = bytearray(), 0
    for at, remove, block in changes:
        patched_table += table[position:at] + block
        position = at + remove
    patched = with_member(stock, bytes(patched_table + table[position:]))
    entry = dict(Path='zones/zones.mpk', Member='zones.dat', InputSha256=sha(stock), OutputSha256=sha(patched),
                 OutputSize=len(patched), Edits=encoded(changes))
    assert apply(stock, entry) == patched
    return entry


def abermenai_entry(client: Path, kind: str) -> dict:
    # The stock dat252 is a later Thidranki than Abermenai's dat253, so plan()'s same-maps check would refuse; these terrain
    # archives are the ones the classic client has too (add_abermenai_terrain.py), and they fit Abermenai there.
    source, target = add_abermenai_terrain.SOURCE_ZONE, add_abermenai_terrain.TARGET_ZONE
    stock_path = f'zones/zone{source}/{kind}{source}.mpk'
    name = f'{kind}{target}.mpk'
    stock = (client / stock_path).read_bytes()
    patched = add_abermenai_terrain.renamed(stock, name.encode('ascii'))
    entry = dict(Path=f'zones/zone{target}/{name}', From=stock_path, InputSha256=sha(stock), OutputSha256=sha(patched),
                 OutputSize=len(patched), Edits=encoded(changed_middle(stock, patched)))
    assert apply(stock, entry) == patched
    return entry


def marfach_entry(client: Path, classic: Path, path: str, stock_path: str) -> dict:
    stock, ours = (client / stock_path).read_bytes(), (classic / path).read_bytes()
    entry = dict(Path=path, From=stock_path, InputSha256=sha(stock), OutputSha256=sha(ours), OutputSize=len(ours),
                 Edits=encoded(replace_whole(stock, ours)))
    assert apply(stock, entry) == ours
    return entry


def open_gate_walls(bound: bytes, zone: int) -> bytes:
    """A zone's bound.csv with the walls of GATE_WALLS swapped; anything but the expected live points is refused."""
    lines = bound.split(b'\r\n')
    for wall_zone, polygon, old, new in GATE_WALLS:
        if wall_zone != zone:
            continue
        flag, _, *points = lines[polygon].rstrip(b', ').split(b', ')
        run = [point for pair in old for point in (b'%d' % pair[0], b'%d' % pair[1])]
        ends = [at for at in range(0, len(points), 2) if points[at:at + len(run)] == run]
        if len(ends) != 1:
            raise ValueError(f'zone{zone}: polygon {polygon} is not the wall this patch knows')
        points[ends[0]:ends[0] + len(run)] = [point for pair in new for point in (b'%d' % pair[0], b'%d' % pair[1])]
        lines[polygon] = b', '.join([flag, b'%d' % (len(points) // 2), *points])
    return b'\r\n'.join(lines)


def without_gate_jumps(table: bytes, zone: int) -> bytes:
    """A zone's zonejump.csv without its GATE_JUMPS rows, the rest numbered again; another table is refused."""
    drop = next(rows for jump_zone, rows in GATE_JUMPS if jump_zone == zone)
    lines = table.split(b'\r\n')
    found = [at for at, line in enumerate(lines) if any(line.startswith(row.encode('latin1') + b',') for row in drop)]
    if len(found) != len(drop):
        raise ValueError(f'zone{zone}: zonejump.csv is not the table this patch knows')
    kept = [line for at, line in enumerate(lines) if at not in found]
    number = 0
    for at, line in enumerate(kept):
        if line.strip():
            number += 1
            kept[at] = b'%d,%s' % (number, line.split(b',', 1)[1])
    return b'\r\n'.join(kept)


def has_member(archive: bytes, member: str) -> bool:
    _, rows, _ = archive_parts(archive)
    return any(row[:256].split(b'\0')[0].decode('latin1').lower() == member.lower() for row in rows)


def gate_entry(client: Path, path: str, zone: int) -> dict:
    """One border-keep zone archive: GATE_WALLS opens bound.csv, GATE_JUMPS takes the gate's zone jumps out of zonejump.csv."""
    stock = (client / path).read_bytes()
    parts, patched = [], stock
    if any(wall[0] == zone for wall in GATE_WALLS):
        parts.append((BOUND, lambda table: open_gate_walls(table, zone)))
    if any(jump[0] == zone for jump in GATE_JUMPS) and has_member(stock, ZONE_JUMP):
        parts.append((ZONE_JUMP, lambda table: without_gate_jumps(table, zone)))
    members = []
    for member, change in parts:
        table = member_of(patched, member)
        new_table = change(table)
        members.append(dict(Member=member, Edits=encoded(edits(table, new_table))))
        patched = with_member(patched, new_table, member)
    entry = dict(Path=path, InputSha256=sha(stock), OutputSha256=sha(patched), OutputSize=len(patched))
    if len(members) == 1:
        entry |= members[0]
    else:
        entry |= dict(Edits=[], Members=members)
    assert apply(stock, entry) == patched
    return entry


def gate_zones() -> list:
    return sorted({wall[0] for wall in GATE_WALLS} | {jump[0] for jump in GATE_JUMPS})


def in_place_edits(original: bytes, patched: bytes):
    """(at, remove, insert) for a same-size file changed in place, nearby changes merged."""
    out, start, last = [], None, None
    for offset in range(len(original)):
        if original[offset] == patched[offset]:
            continue
        if start is not None and offset - last <= MERGE_GAP:
            last = offset
            continue
        if start is not None:
            out.append((start, last + 1 - start, patched[start:last + 1]))
        start = last = offset
    if start is not None:
        out.append((start, last + 1 - start, patched[start:last + 1]))
    return out


def fishing_rod():
    """The private rod's builder (tools/pet-art/install_fishing_rod.py; needs numpy), imported only when a patch is built."""
    sys.path.insert(0, str(PET_ART))
    import install_fishing_rod  # noqa: E402
    return install_fishing_rod


def rod_entries(client: Path) -> tuple:
    """The fishing rod every player's client needs for the server's fishing_pole_model 4927 (and 4928): the rod NIF made
    from the player's own quarterstaff (only the new vertex positions and bounding sphere ship), and our three catalog
    rows for gamedata.mpk's objects.csv and items.csv (gamedata_entry; only those rows ship)."""
    rod = fishing_rod()
    rod.ITEMS = client / 'items'
    stock_nif = (client / ROD_SOURCE).read_bytes()
    nif, _, _ = rod.build()
    assert len(nif) == len(stock_nif)
    nif_entry = dict(Path=ROD_PATH, From=ROD_SOURCE, InputSha256=sha(stock_nif), OutputSha256=sha(nif), OutputSize=len(nif),
                     Edits=encoded(in_place_edits(stock_nif, nif)))
    assert apply(stock_nif, nif_entry) == nif
    game = (client / GAMEDATA).read_bytes()
    return [nif_entry], rod.new_rows(member_of(game, 'objects.csv'), member_of(game, 'items.csv'))


def regalia_kit(name: str):
    """A Regalia installer module (tools/sovereign-art; needs Pillow), imported only when a patch is built."""
    import importlib.util
    path = SOVEREIGN_ART / name / f'install_{name}_set.py'
    spec = importlib.util.spec_from_file_location(f'regalia_{name}', path)
    kit = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = kit
    spec.loader.exec_module(kit)
    return kit


def regalia_entries(client: Path) -> tuple:
    """The Sovereign Regalia art (Storm, Tara, Camelot): each kit's two skin archives, made From a stock archive and shipped
    whole (our painted textures only; the stock DDS lends its header layout), its private mesh copies (Tara's refitted Lug
    helmets: only the changed transform bytes ship), and its catalog rows for gamedata_entry."""
    game = (client / GAMEDATA).read_bytes()
    files = {name: member_of(game, name) for name in GAMEDATA_CATALOGS}
    stock = (client / REGALIA_FROM).read_bytes()
    entries, rows = [], {}
    for name in REGALIA_KITS:
        kit = regalia_kit(name)
        for path, ours in zip(kit.S.skin_targets(kit), kit.S.skin_archives(client, kit, REGALIA_STAMP)):
            entry = dict(Path=path, From=REGALIA_FROM, InputSha256=sha(stock), OutputSha256=sha(ours), OutputSize=len(ours),
                         Edits=encoded(replace_whole(stock, ours)))
            assert apply(stock, entry) == ours
            entries.append(entry)
        for path, source, stock_nif, ours in kit.S.mesh_files(client, kit):   # private mesh copies (Tara's Crown of Lugh)
            entry = dict(Path=path, From=source, InputSha256=sha(stock_nif), OutputSha256=sha(ours), OutputSize=len(ours),
                         Edits=encoded(in_place_edits(stock_nif, ours)))
            assert apply(stock_nif, entry) == ours
            entries.append(entry)
        for catalog, added in kit.S.catalog_rows(files, kit).items():
            rows.setdefault(catalog, []).extend(added)
    return entries, rows


def csv_line(row: list) -> str:
    """A catalog row as the client's tables write it (the installers' line_of)."""
    import csv
    import io
    out = io.StringIO()
    csv.writer(out, lineterminator='').writerow(row)
    return out.getvalue()


def row_insertions(stock: bytes, table: bytes) -> list:
    """(at, 0, lines) for each run of lines `table` inserts into `stock`; anything but insertions is refused."""
    old, new = stock.split(b'\r\n'), table.split(b'\r\n')
    starts, position = [], 0
    for line in old:
        starts.append(position)
        position += len(line) + 2
    out = []
    for tag, i1, i2, j1, j2 in difflib.SequenceMatcher(None, old, new, autojunk=False).get_opcodes():
        if tag == 'equal':
            continue
        if tag != 'insert':
            raise ValueError('a stock row would change')
        out.append((starts[i1] if i1 < len(old) else len(stock) + 2, 0, b''.join(line + b'\r\n' for line in new[j1:j2])))
    if out and out[-1][0] > len(stock):
        raise ValueError('rows after the last line are not supported')
    assert splice(stock, encoded(out)) == table
    return out


def gamedata_entry(client: Path, added: dict) -> dict:
    """One gamedata.mpk entry adding our catalog rows ({catalog: [row, ...]}), one member after the other. Only our rows
    ship: each member's edits are insertions of exactly those lines."""
    game = (client / GAMEDATA).read_bytes()
    members, patched = [], game
    for name in [catalog for catalog in GAMEDATA_CATALOGS if added.get(catalog)]:
        stock = member_of(game, name)
        lines = [csv_line(row) for row in added[name]]
        table = with_rows(stock.decode('latin1'), lines)
        assert table is not None, f'{name}: one of our IDs holds another row in the stock table'
        table = table.encode('latin1')
        changes = row_insertions(stock, table)
        inserted = [line for _, _, block in changes for line in block.decode('latin1').split('\r\n') if line]
        assert sorted(inserted) == sorted(lines), f'{name}: more than our rows'
        members.append(dict(Member=name, Edits=encoded(changes), Rows=lines))
        patched = with_member(patched, table, name)
    # AnyBuild: players' gamedata.mpk is often not this one (live updates it without game.dll, other clients change it), so
    # the launcher adds the Rows by ID to any build and only the stock one is hash-checked end to end.
    entry = dict(Path=GAMEDATA, InputSha256=sha(game), OutputSha256=sha(patched), OutputSize=len(patched), Edits=[],
                 Members=members, AnyBuild=True)
    assert apply(game, entry) == patched
    assert adapt(game, entry) == patched, 'rows placed by ID differ from the stock edits'
    return entry


def build(client: Path, classic: Path, splash: Path = SPLASH) -> dict:
    game = (client / 'game.dll').read_bytes()
    layout = (client / 'pregame/character_creation.xml').read_bytes()
    stock_splash = (client / 'pregame/splash.mpk').read_bytes()
    patched_game, _ = patch_class_slots.patch(game)
    patched_layout = patch_class_slots.patch_layout(layout)
    our_splash = splash.read_bytes()
    files = []
    for path, original, patched, changes in [
            ('game.dll', game, patched_game, edits),
            ('pregame/character_creation.xml', layout, patched_layout, edits),
            ('pregame/splash.mpk', stock_splash, our_splash, replace_whole)]:
        entry = dict(Path=path, InputSha256=sha(original), OutputSha256=sha(patched), OutputSize=len(patched),
                     Edits=encoded(changes(original, patched)))
        assert apply(original, entry) == patched
        files.append(entry)
    files.append(zone_table_entry((client / 'zones/zones.mpk').read_bytes()))
    files.extend(abermenai_entry(client, kind) for kind in add_abermenai_terrain.KINDS)
    files.extend(marfach_entry(client, classic, path, stock_path) for path, stock_path in MARFACH_FILES)
    files.extend(gate_entry(client, path.format(zone), zone) for zone in gate_zones() for path in GATE_ARCHIVES)
    rod_files, rows = rod_entries(client)
    regalia_files, regalia_rows = regalia_entries(client)
    for catalog, added in regalia_rows.items():
        rows.setdefault(catalog, []).extend(added)
    files.extend(rod_files + regalia_files)
    files.append(gamedata_entry(client, rows))
    return dict(Format=FORMAT, ClientVersion=CLIENT_VERSION, Files=files)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('client', type=Path, help='pristine 1.130 client folder (only read)')
    parser.add_argument('classic', type=Path, help='classic client folder with zones/zone276 (only read)')
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    patch = build(args.client, args.classic)
    args.output.write_text(json.dumps(patch, indent=1), encoding='utf-8')
    for entry in patch['Files']:
        changes = [e for part in entry.get('Members') or [entry] for e in part['Edits']]
        print(entry['Path'], entry['InputSha256'][:12], '->', entry['OutputSha256'][:12],
              len(changes), 'edits,', sum(len(base64.b64decode(e['Insert'])) for e in changes), 'bytes')
