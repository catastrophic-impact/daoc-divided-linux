"""Offline DAoC world editor: local UI, guarded world edits, portable row patches.

Only this process's configured database and client directories can be accessed.
No game or server process is launched by this tool.
"""
from __future__ import annotations

import argparse
import base64
import configparser
import hashlib
import json
import os
import re
import secrets
import shutil
import sqlite3
import struct
import subprocess
import sys
import threading
import uuid
import webbrowser
import zlib
from contextlib import closing
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse, parse_qs

HERE = Path(__file__).resolve().parent
FROZEN = getattr(sys, 'frozen', False)          # the packaged WorldWorkshop.exe (app.py)
TOOLS = HERE.parent
REPO = TOOLS.parents[1]
# Run from a git worktree (Source/.claude/worktrees/<name>), the tools still work on the main checkout's installs,
# build outputs and workspace folders, which are not in Git (setup review 2026-11-06).
DATA_REPO = REPO.parents[2] if REPO.parent.name == 'worktrees' and REPO.parent.parent.name == '.claude' else REPO
WORKSPACE = DATA_REPO.parent
sys.path.insert(0, str(REPO / 'source/new-client/pipeline'))
sys.path.insert(0, str(TOOLS))
from daoc_mpk import read_mpk
import modern_world_overlay as overlay

FIELDS = {
    'Mob': {'Name': 'text', 'X': 'coord', 'Y': 'coord', 'Z': 'coord',
            'Heading': (0, 65535), 'Level': (0, 255), 'Realm': (0, 3),
            'Model': (0, 65535), 'Size': (0, 255), 'FactionID': (0, 2147483647),
            'RespawnInterval': (-1, 2147483647), 'RoamingRange': (-1, 65535),
            'AggroLevel': (-1, 2147483647), 'AggroRange': (-1, 65535)},
    'WorldObject': {'Name': 'text', 'X': 'coord', 'Y': 'coord', 'Z': 'coord',
                    'Heading': (0, 4095), 'Model': (0, 65535), 'Realm': (0, 3)},
    'Door': {'Name': 'text', 'X': 'coord', 'Y': 'coord', 'Z': 'coord',
             'Heading': (0, 4095), 'Level': (0, 255), 'Realm': (0, 255), 'Locked': (0, 1)},
    'Zones': {'Name': 'text', 'WaterLevel': 'coord', 'IsLava': (0, 1),
              'DivingFlag': (0, 255), 'Experience': (0, 1000), 'Realmpoints': (0, 1000),
              'Bountypoints': (0, 1000), 'Coin': (0, 1000), 'Realm': (0, 3)},
}
KEYS = {'Mob': 'Mob_ID', 'WorldObject': 'WorldObject_ID', 'Door': 'Door_ID', 'Zones': 'ZoneID'}

# Game-content tables the Game data view may browse and edit, grouped for the UI.
# Player saves (accounts, characters, inventories, houses, guilds, bots) are never listed.
DATA_TABLES = {
    'World': ['Mob', 'WorldObject', 'Door', 'Zones', 'Regions', 'Area', 'BindPoint', 'ZonePoint', 'Teleport',
              'JumpPoint', 'StartupLocation', 'Path', 'PathPoints', 'Keep', 'KeepPosition', 'Relic', 'Battleground'],
    'NPCs': ['NpcTemplate', 'NPCEquipment', 'MobXAmbientBehaviour', 'Faction', 'LinkedFaction'],
    'Items & loot': ['ItemTemplate', 'MerchantItem', 'LootTemplate', 'MobXLootTemplate', 'LootGenerator', 'LootOTD',
                     'MobDropTemplate', 'DropTemplateXItemTemplate', 'StarterEquipment', 'Salvage', 'SalvageYield',
                     'CraftedItem', 'CraftedXItem', 'Artifact', 'ArtifactBonus', 'ArtifactXItem', 'offline_currency_lists'],
    'Spells & classes': ['Spell', 'SpellLine', 'LineXSpell', 'Style', 'StyleXSpell', 'Specialization', 'SpecXAbility',
                         'ClassXSpecialization', 'Ability', 'ClassXRealmAbility_Atlas', 'Race',
                         'SpellXCustomValues'],
    'Quests': ['DataQuest'],
    'Server settings': ['ServerProperty', 'offline_population_settings', 'offline_local_options', 'offline_auction_settings'],
}
# Tables the editor may create on first save; the server's DbOfflineCurrencyList declares the same columns.
VIRTUAL_TABLES = {
    'offline_currency_lists': [
        "CREATE TABLE IF NOT EXISTS `offline_currency_lists` (`ItemListID` VARCHAR(255) NOT NULL DEFAULT '' COLLATE NOCASE, "
        "`CurrencyItem` VARCHAR(255) NOT NULL DEFAULT '' COLLATE NOCASE, `Description` TEXT DEFAULT NULL COLLATE NOCASE, "
        "`LastTimeRowUpdated` DATETIME NOT NULL DEFAULT '2000-01-01 00:00:00', "
        "`offline_currency_lists_ID` VARCHAR(255) DEFAULT NULL COLLATE NOCASE, PRIMARY KEY (`ItemListID`))",
        'CREATE UNIQUE INDEX IF NOT EXISTS `U_offline_currency_lists_offline_currency_lists_ID` '
        'ON `offline_currency_lists` (`offline_currency_lists_ID`)'],
}
# World tables drawn in the 3D view: which columns hold position, facing, region and label.
LAYERS = {
    'Mob': dict(x='X', y='Y', z='Z', heading='Heading', region='Region', name='Name', sub='Guild', label='NPCs'),
    'WorldObject': dict(x='X', y='Y', z='Z', heading='Heading', region='Region', name='Name', label='Objects'),
    'Door': dict(x='X', y='Y', z='Z', heading='Heading', name='Name', label='Doors'),
    'ZonePoint': dict(x='SourceX', y='SourceY', z='SourceZ', region='SourceRegion', title='Zone point', sub='Id', label='Zone points'),
    'BindPoint': dict(x='X', y='Y', z='Z', region='Region', title='Bind point', radius='Radius', label='Bind points'),
    'Teleport': dict(x='X', y='Y', z='Z', heading='Heading', region='RegionID', name='TeleportID', sub='Type', label='Teleports'),
    'JumpPoint': dict(x='Xpos', y='Ypos', z='Zpos', heading='Heading', region='Region', name='Name', label='Jump points'),
    'Keep': dict(x='X', y='Y', z='Z', heading='Heading', region='Region', name='Name', label='Keeps'),
    'Relic': dict(x='X', y='Y', z='Z', heading='Heading', region='Region', title='Relic', label='Relics'),
    'Area': dict(x='X', y='Y', z='Z', region='Region', name='Description', sub='ClassType', radius='Radius', label='Areas'),
    'StartupLocation': dict(x='XPos', y='YPos', z='ZPos', heading='Heading', region='Region', title='Start location', label='Start locations'),
    'PathPoints': dict(x='X', y='Y', z='Z', name='PathID', sub='Step', label='Path waypoints'),
}
# Stored as text in the database; edited as text.
READONLY = {'LastTimeRowUpdated'}
EDITABLE_FILES = {'.json', '.xml', '.txt', '.ini', '.cfg', '.config', '.csv'}
INT_RANGES = [('UNSIGNED TINYINT', 0, 255), ('UNSIGNED SMALLINT', 0, 65535), ('UNSIGNED INT', 0, 4294967295),
              ('UNSIGNED BIGINT', 0, 2**64-1), ('TINYINT', -128, 127), ('SMALLINT', -32768, 32767),
              ('BIGINT', -2**63, 2**63-1), ('INTEGER', -2**63, 2**63-1), ('INT', -2147483648, 2147483647)]


def legacy_rule(table, rule):
    if rule == 'text':
        return ('text', 255, table == 'Door')
    low, high = (-2147483648, 2147483647) if rule == 'coord' else rule
    return ('int', low, high, False)


# Only the original placement fields: used when no database schema is supplied.
LEGACY_SCHEMA = {t: {'key': KEYS[t], 'columns': {c: legacy_rule(t, r) for c, r in f.items()}} for t, f in FIELDS.items()}


def column_rule(declared, notnull):
    """Translate a declared SQLite column type into a validation rule."""
    kind = (declared or '').upper()
    nullable = not notnull
    if 'INT' in kind:
        low, high = next((lo, hi) for name, lo, hi in INT_RANGES if name in kind)
        return ('int', low, high, nullable)
    if any(k in kind for k in ('REAL', 'DOUBLE', 'FLOAT', 'NUMERIC', 'DECIMAL')):
        return ('real', nullable)
    size = re.search(r'CHAR\((\d+)\)', kind)
    return ('text', int(size.group(1)) if size else 1_000_000, nullable)


def read_schema(con):
    """Editable tables in this database, their key and per-column rules."""
    present = {r[0] for r in con.execute("select name from sqlite_master where type='table'")}
    schema = {}
    for group, tables in DATA_TABLES.items():
        for table in tables:
            if table not in present and table not in VIRTUAL_TABLES:
                continue
            if table in present:
                info = con.execute(f'PRAGMA table_info("{table}")').fetchall()
            else:
                with closing(sqlite3.connect(':memory:')) as scratch:
                    for ddl in VIRTUAL_TABLES[table]:
                        scratch.execute(ddl)
                    info = scratch.execute(f'PRAGMA table_info("{table}")').fetchall()
            pk = [r[1] for r in info if r[5]]
            key = KEYS.get(table) or (pk[0] if len(pk) == 1 else None)
            if not key:
                continue
            columns = {}
            for r in info:
                rule = column_rule(r[2], r[3] or r[1] == key)
                if table in FIELDS and r[1] in FIELDS[table]:
                    rule = legacy_rule(table, FIELDS[table][r[1]])
                    if rule[0] == 'text':
                        rule = ('text', rule[1], not r[3])
                columns[r[1]] = rule
            schema[table] = {'key': key, 'columns': columns, 'group': group,
                             'keyKind': 'int' if columns[key][0] == 'int' else ('uuid' if key.endswith('_ID') else 'text')}
    return schema


def check_value(table, field, rule, value):
    if value is None:
        if rule[-1]:
            return
        raise ValueError(f'{table}.{field} cannot be empty')
    if rule[0] == 'text':
        if not isinstance(value, str) or len(value) > rule[1] or '\x00' in value:
            raise ValueError(f'{field}: use text of at most {rule[1]} characters')
    elif rule[0] == 'real':
        if type(value) not in (int, float) or value != value:
            raise ValueError(f'{field}: expected a number')
    else:
        low, high = rule[1], rule[2]
        if type(value) is not int or not low <= value <= high:
            raise ValueError(f'{field}: expected integer between {low} and {high}')


def digest(data):
    return hashlib.sha256(json.dumps(data, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


def connect(db):
    con = sqlite3.connect(db.resolve().as_uri() + '?mode=ro', uri=True, timeout=1)
    con.row_factory = sqlite3.Row
    return con


def pcx(raw):
    """Read the 8-bit, single-plane PCX height fields without extra dependencies."""
    if len(raw) < 128 or raw[0] != 10 or raw[2] != 1 or raw[3] != 8 or raw[65] != 1:
        raise ValueError('Unsupported terrain PCX format')
    x0, y0, x1, y1 = struct.unpack_from('<4H', raw, 4)
    width, height = x1 - x0 + 1, y1 - y0 + 1
    stride = struct.unpack_from('<H', raw, 66)[0]
    if not 1 <= width <= stride <= 4096 or not 1 <= height <= 4096:
        raise ValueError('Invalid PCX dimensions')
    result = bytearray()
    cursor = 128
    while len(result) < stride * height:
        b = raw[cursor]
        cursor += 1
        if b & 192 == 192:
            n, b = b & 63, raw[cursor]
            cursor += 1
            result.extend([b] * n)
        else:
            result.append(b)
    return [[result[y * stride + x] for x in range(width)] for y in range(height)]


def client_zones(client):
    raw = read_mpk(client / 'zones/zones.mpk')['zones.dat'].decode('latin1').lstrip('\ufeffï»¿')
    cp = configparser.ConfigParser(strict=False, interpolation=None, inline_comment_prefixes=(';',))
    # Stock zones.dat contains uncommented section banners between INI sections.
    cp.read_string('\n'.join(line for line in raw.splitlines()
                             if line.lstrip().startswith(('[', ';', '#')) or re.match(r'^\s*\w+\s*=', line)))
    return {int(s[4:]): dict(cp[s]) for s in cp.sections() if s.lower().startswith('zone') and s[4:].isdigit()}


def validate_changes(changes, schema=None):
    """Check every row edit against the editable tables/columns.

    op 'update' (default) changes listed columns, 'insert' adds a complete new row ('before' empty),
    'delete' removes a row whose full original values are in 'before' ('after' empty).
    """
    schema = schema or LEGACY_SCHEMA
    if not isinstance(changes, list) or not 0 < len(changes) <= 20000:
        raise ValueError('A patch must contain 1–20,000 world row edits')
    seen = set()
    for c in changes:
        table, op = c.get('table'), c.get('op', 'update')
        if table not in schema or not isinstance(c.get('key'), (str, int)) or isinstance(c.get('key'), bool):
            raise ValueError('Unsupported world table or row key')
        if op not in ('update', 'insert', 'delete'):
            raise ValueError('Unknown row operation')
        identity = (table, str(c['key']))
        if identity in seen:
            raise ValueError('Duplicate patch row')
        seen.add(identity)
        if not isinstance(c.get('before'), dict) or not isinstance(c.get('after'), dict):
            raise ValueError('Missing original or edited values')
        columns, key = schema[table]['columns'], schema[table]['key']
        if op == 'update' and (not c['after'] or set(c['before']) != set(c['after'])):
            raise ValueError('Original and edited columns must match')
        if op == 'update' and key in c['after']:
            raise ValueError(f'{table}.{key} identifies the row and cannot be changed; duplicate the row instead')
        if op == 'insert' and (c['before'] or str(c['after'].get(key)) != str(c['key'])):
            raise ValueError('A new row must contain its key and no original values')
        if op == 'delete' and (c['after'] or str(c['before'].get(key)) != str(c['key'])):
            raise ValueError('A deleted row must contain its original values only')
        for field, value in (c['before'] if op == 'delete' else c['after']).items():
            if field not in columns or (op == 'update' and field in READONLY):
                raise ValueError(f'{table}.{field} is not editable')
            if op != 'delete':
                check_value(table, field, columns[field], value)


def process_paths():
    if os.name != 'nt':
        return []
    result = subprocess.run(['powershell', '-NoProfile', '-Command',
                             'Get-Process | Where-Object { $_.Path } | ForEach-Object { $_.Path }'],
                            capture_output=True, text=True, timeout=25,
                            creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        raise ValueError('Could not check running games; no changes made')
    return [Path(p.strip()).resolve() for p in result.stdout.splitlines() if p.strip()]


def ensure_closed(roots):
    for path in process_paths():
        if any(path.is_relative_to(root.resolve()) for root in roots):
            raise ValueError(f'Close the game/server before applying changes: {path.name}')


class Editor:
    def __init__(self, db, client, state, geometry=None, exporter=None, targets=None):
        self.db, self.client, self.state = db.resolve(), client.resolve(), state.resolve()
        if not self.db.is_file() or not (self.client / 'zones/zones.mpk').is_file():
            raise ValueError('Select an existing world database and a client containing zones/zones.mpk')
        self.state.mkdir(parents=True, exist_ok=True)
        self.geometry = geometry.resolve() if geometry else None
        self.exporter = exporter.resolve() if exporter and exporter.is_file() else None
        self.targets = {str(i): p.resolve() for i, p in enumerate(targets or []) if p.resolve() != self.client}
        self.token = secrets.token_urlsafe(32)
        self.lock = threading.RLock()
        self._fingerprint = None
        cache_path = self.state / 'world-hashes.json'
        try:
            cached = json.loads(cache_path.read_text())
            self.hash_cache = cached['files'] if cached['client'] == str(self.client) else {}
        except (OSError, ValueError, KeyError):
            self.hash_cache = {}
        self.czones = client_zones(client)
        with closing(connect(self.db)) as con:
            self.schema = read_schema(con)
        # Server text files (configs, bot settings) live beside the database's runtime folder.
        self.runtime = self.db.parent.parent
        self.preview_key = secrets.token_hex(16)
        self.meshes = {}
        self.visual_jobs = {}
        self._visual_python = None

    def world_fingerprint(self, fresh=False):
        with self.lock:
            if self._fingerprint is None or fresh:
                files = overlay.world_files(self.client)
                hashes, cache = {}, {}
                for k, p in sorted(files.items()):
                    stat = p.stat()
                    signature = [stat.st_size, stat.st_mtime_ns, stat.st_ctime_ns]
                    old = self.hash_cache.get(k)
                    value = old['sha'] if old and old['stat'] == signature else overlay.sha(p)
                    hashes[k] = value
                    cache[k] = {'sha': value, 'stat': signature}
                self._fingerprint, self.hash_cache = hashes, cache
                (self.state / 'world-hashes.json').write_text(json.dumps({'client': str(self.client), 'files': cache}), encoding='utf-8')
            return digest(self._fingerprint)

    def zones(self):
        with closing(connect(self.db)) as con:
            rows = [dict(r) for r in con.execute('select * from Zones order by ZoneID')]
        for z in rows:
            cz = self.czones.get(z['ZoneID'])
            z['client'] = cz
            z['layoutMatches'] = bool(cz) and all(int(cz.get(k, default)) == z[field] for k, field, default in (
                ('region', 'RegionID', z['ZoneID']), ('region_offset_x', 'OffsetX', 0),
                ('region_offset_y', 'OffsetY', 0), ('width', 'Width', 8), ('height', 'Height', 8)))
        return rows

    def zone(self, zid):
        zone = next((z for z in self.zones() if z['ZoneID'] == zid), None)
        if not zone:
            raise ValueError('Unknown server zone')
        ox, oy = zone['OffsetX'] * 8192, zone['OffsetY'] * 8192
        w, h = zone['Width'] * 8192, zone['Height'] * 8192
        rows = []
        with closing(connect(self.db)) as con:
            for table, layer in LAYERS.items():
                if table not in self.schema:
                    continue
                x, y = layer['x'], layer['y']
                inside = f'"{x}">=? and "{x}"<? and "{y}">=? and "{y}"<?'
                if table == 'Door':
                    # Door IDs encode the client zone, not a region column.
                    query, args = 'InternalID / 1000000 = ?', (zid,)
                elif table == 'PathPoints':
                    if 'Path' not in self.schema:
                        continue
                    query, args = f'PathID in (select PathID from Path where RegionID=?) and {inside}', (zone['RegionID'], ox, ox+w, oy, oy+h)
                else:
                    query, args = f'"{layer["region"]}"=? and {inside}', (zone['RegionID'], ox, ox+w, oy, oy+h)
                key = self.schema[table]['key']
                for row in con.execute(f'SELECT * FROM "{table}" WHERE {query} LIMIT 20000', args):
                    r = dict(row)
                    rows.append({'table': table, 'key': r[key], 'values': r})
            # The server uses the region's water level for zones whose own WaterLevel is 0.
            region_water = 0
            if 'WaterLevel' in self.schema.get('Regions', {}).get('columns', {}):
                found = con.execute('SELECT WaterLevel FROM Regions WHERE RegionID = ?', (zone['RegionID'],)).fetchone()
                region_water = (found[0] or 0) if found else 0
        return {'zone': zone, 'entities': rows, 'fields': FIELDS, 'regionWater': region_water,
                'geometry': self.terrain(zid), 'fingerprint': self.world_fingerprint()}

    def rows(self, table, text='', column=None, equals=None, offset=0, limit=100):
        """Search one editable table: free text over text columns, or an exact column value."""
        if table not in self.schema:
            raise ValueError('That table is not editable here')
        info = self.schema[table]
        with closing(connect(self.db)) as con:
            if table not in self.present_tables(con):
                return {'table': table, 'total': 0, 'offset': 0, 'rows': []}
        where, args = [], []
        if column:
            if column not in info['columns']:
                raise ValueError('Unknown column')
            where.append(f'"{column}"=?')
            args.append(int(equals) if info['columns'][column][0] == 'int' and re.fullmatch(r'-?\d+', str(equals)) else equals)
        if text:
            searchable = [c for c, r in info['columns'].items() if r[0] == 'text' or c == info['key']]
            where.append('(' + ' or '.join(f'CAST("{c}" AS TEXT) LIKE ?' for c in searchable) + ')')
            args += [f'%{text}%'] * len(searchable)
        clause = (' WHERE ' + ' and '.join(where)) if where else ''
        limit, offset = max(1, min(500, int(limit))), max(0, int(offset))
        with closing(connect(self.db)) as con:
            total = con.execute(f'SELECT count(*) FROM "{table}"{clause}', args).fetchone()[0]
            found = [dict(r) for r in con.execute(f'SELECT * FROM "{table}"{clause} ORDER BY rowid LIMIT ? OFFSET ?', (*args, limit, offset))]
        return {'table': table, 'total': total, 'offset': offset, 'rows': found}

    def new_key(self, table):
        if table not in self.schema:
            raise ValueError('That table is not editable here')
        info = self.schema[table]
        if info['keyKind'] == 'int':
            with closing(connect(self.db)) as con:
                return {'key': (con.execute(f'SELECT max("{info["key"]}") FROM "{table}"').fetchone()[0] or 0) + 1}
        return {'key': str(uuid.uuid4()) if info['keyKind'] == 'uuid' else ''}

    def file_list(self):
        """Editable server text files: settings, bot configuration and language strings."""
        found = []
        server = self.runtime / 'server'
        candidates = list(server.rglob('*')) if server.is_dir() else []
        candidates += [p for p in self.runtime.glob('*.txt')]
        for path in candidates:
            try:
                self.file_path(str(path.relative_to(self.runtime)).replace('\\', '/'))
            except ValueError:
                continue
            found.append({'path': str(path.relative_to(self.runtime)).replace('\\', '/'), 'size': path.stat().st_size})
        return sorted(found, key=lambda f: f['path'].lower().rpartition('/')[::2])

    def file_path(self, relative):
        if not isinstance(relative, str) or '\x00' in relative:
            raise ValueError('Invalid file path')
        path = (self.runtime / relative).resolve()
        if not path.is_relative_to(self.runtime) or not path.is_file():
            raise ValueError('File not found')
        parts = [p.lower() for p in path.relative_to(self.runtime).parts]
        name = parts[-1]
        allowed = (parts[0] == 'server' and not {'logs', 'lib', 'win-x64', 'runtimes'} & set(parts[1:-1])) or len(parts) == 1
        if (not allowed or path.suffix.lower() not in EDITABLE_FILES or name == 'account.txt'
                or name.endswith(('.deps.json', '.runtimeconfig.json')) or path.stat().st_size > 4*1024*1024):
            raise ValueError('That file is not editable here')
        return path

    @staticmethod
    def decode(raw):
        bom = raw.startswith(b'\xef\xbb\xbf')
        try:
            text, encoding = raw[3 if bom else 0:].decode('utf-8'), 'utf-8'
        except UnicodeDecodeError:
            text, encoding = raw.decode('latin1'), 'latin1'
        return text, encoding, bom

    def read_file(self, relative):
        raw = self.file_path(relative).read_bytes()
        text, encoding, _ = self.decode(raw)
        return {'path': relative, 'text': text.replace('\r\n', '\n'), 'sha': hashlib.sha256(raw).hexdigest(),
                'encoding': encoding, 'backups': self.file_backups(relative)}

    def file_backups(self, relative):
        root = self.state / 'file-backups'
        return sorted((p.parent.relative_to(root).parts[0] for p in root.glob('*/' + relative)
                       if p.is_file()), reverse=True) if root.is_dir() else []

    def read_file_backup(self, backup_id, relative):
        if not isinstance(backup_id, str) or Path(backup_id).name != backup_id:
            raise ValueError('Invalid backup ID')
        root = (self.state / 'file-backups' / backup_id).resolve()
        path = (root / relative).resolve()
        if not path.is_relative_to(root) or not path.is_file():
            raise ValueError('Backup not found')
        return {'text': self.decode(path.read_bytes())[0].replace('\r\n', '\n')}

    def save_file(self, relative, text, expected):
        if not isinstance(text, str) or len(text) > 4*1024*1024:
            raise ValueError('File text is too large')
        with self.lock:
            path = self.file_path(relative)
            raw = path.read_bytes()
            if hashlib.sha256(raw).hexdigest() != expected:
                raise ValueError('The file changed on disk since it was opened. Reload it before saving.')
            if path.suffix.lower() == '.json':
                try:
                    json.loads(text)
                except ValueError as exc:
                    raise ValueError(f'Invalid JSON, not saved: {exc}')
            elif path.suffix.lower() in ('.xml', '.config'):
                import xml.etree.ElementTree as ET
                try:
                    ET.fromstring(text.lstrip('﻿'))
                except ET.ParseError as exc:
                    raise ValueError(f'Invalid XML, not saved: {exc}')
            ensure_closed([self.runtime / 'server'])
            _, encoding, bom = self.decode(raw)
            if b'\r\n' in raw:
                text = text.replace('\r\n', '\n').replace('\n', '\r\n')
            data = (b'\xef\xbb\xbf' if bom else b'') + text.encode(encoding)
            backup_id = datetime.now().strftime('%Y%m%d-%H%M%S-') + uuid.uuid4().hex[:8]
            backup = self.state / 'file-backups' / backup_id / relative
            backup.parent.mkdir(parents=True, exist_ok=True)
            backup.write_bytes(raw)
            temp = path.with_name(path.name + '.world-editor-tmp')
            temp.write_bytes(data)
            os.replace(temp, path)
            return {'message': f'Saved {relative}. Restart the game server to load it.', 'backup': str(backup),
                    'sha': hashlib.sha256(data).hexdigest()}

    def terrain(self, zid):
        try:
            return self._terrain(zid)
        except (ValueError, KeyError, IndexError, configparser.Error, zlib.error) as exc:
            return {'warning': f'Terrain unavailable ({exc}). Load collision geometry for this zone.'}

    def _terrain(self, zid):
        cz = self.czones.get(zid)
        if not cz:
            return {'warning': 'This client does not contain this zone. Placement is disabled.'}
        name = f'zone{zid:03}'
        if int(cz.get('type', 0)) != 0:
            return {'warning': 'City/dungeon: load collision geometry before placing objects.'}
        folders = [self.client / 'zones' / name, self.client / 'frontiers/zones' / name]
        archive = next((p / f'dat{zid:03}.mpk' for p in folders if (p / f'dat{zid:03}.mpk').is_file()), None)
        if not archive:
            return {'warning': 'No terrain archive. Load collision geometry before placing objects.'}
        dat = read_mpk(archive)
        cp = configparser.ConfigParser(strict=False, interpolation=None, inline_comment_prefixes=(';',))
        cp.read_string(dat['sector.dat'].decode('latin1').lstrip('\ufeffï»¿'))
        a, b = pcx(dat['terrain.pcx']), pcx(dat['offset.pcx'])
        scale, offset = cp.getfloat('terrain', 'scalefactor'), cp.getfloat('terrain', 'offsetfactor')
        # A 129² overview; exact full-resolution samples are retained for ground snap.
        grid = [[a[min(y,255)][min(x,255)] * scale + b[min(y,255)][min(x,255)] * offset
                 for x in range(257)] for y in range(257)]
        return {'heights': grid, 'step': 256, 'origin': [int(cz.get('region_offset_x', 0))*8192,
                int(cz.get('region_offset_y', 0))*8192],
                'warning': 'Terrain only until collision geometry is loaded; buildings are not yet shown.'}

    def mesh(self, zid):
        if zid in self.meshes:
            return self.meshes[zid]
        fingerprint = self.world_fingerprint()
        cache = self.state / 'geometry' / fingerprint
        cache.mkdir(parents=True, exist_ok=True)
        obj = cache / 'zones' / f'zone{zid:03}.obj'
        if self.geometry:
            # Imported geometry needs a matching provenance receipt.
            receipt = self.geometry / 'world-editor-source.json'
            if not receipt.is_file() or json.loads(receipt.read_text())['fingerprint'] != fingerprint:
                raise ValueError('Geometry source receipt missing or does not match this client. Use the built-in exporter.')
            obj = self.geometry / f'zone{zid:03}.obj'
        elif not obj.is_file():
            if not self.exporter:
                raise ValueError('Collision exporter is unavailable. Build OpenDAoC-BuildNav or select its executable with --exporter.')
            cmd = [str(self.exporter), '--daoc='+str(self.client), '--zones='+str(zid), '--obj=true']
            result = subprocess.run(cmd, cwd=cache, env={**os.environ, 'DAOC_BUILDNAV_BASE': str(cache)},
                                    capture_output=True, text=True, timeout=240,
                                    creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
            (cache / f'zone{zid:03}-export.log').write_text(result.stdout + result.stderr, encoding='utf-8')
            if result.returncode or not obj.is_file():
                raise ValueError(f'Geometry export failed. See {cache / (f"zone{zid:03}-export.log")}')
        vertices, triangles = [], []
        with obj.open() as stream:
            for line in stream:
                parts = line.split()
                if parts and parts[0] == 'v':
                    # BuildNav exports global coordinates / 32, Y up.
                    vertices.append([float(v)*32 for v in parts[1:4]])
                elif parts and parts[0] == 'f':
                    face = [int(v.split('/')[0])-1 for v in parts[1:]]
                    for j in range(1, len(face)-1):
                        triangles.extend([face[0], face[j], face[j+1]])
        if not vertices or not triangles:
            raise ValueError('Exporter returned empty collision geometry')
        data = {'positions': vertices, 'indices': triangles, 'fingerprint': fingerprint,
                'source': 'Client collision mesh (untextured)'}
        log = cache / f'zone{zid:03}-export.log'
        data['warnings'] = [line for line in log.read_text(encoding='utf-8').splitlines()
                            if '[Error]' in line or '[Warn]' in line] if log.is_file() else []
        self.meshes[zid] = data
        return data

    def visual_start(self, zid, extra_models=None):
        """Spawn a read-only asset worker; polling leaves editing responsive during conversion."""
        if zid not in self.czones:
            raise ValueError('This client does not contain that zone')
        if extra_models is not None and (not isinstance(extra_models,list) or len(extra_models)>128 or
                any(type(m) is not int or not 0<=m<=65535 for m in extra_models)):
            raise ValueError('Preview model IDs must be a list of at most 128 unsigned integers')
        data = self.zone(zid)
        data['entities'] += [{'table':'Mob','values':{'Model':m}} for m in (extra_models or [])]
        catalog = self.client / 'gamedata.mpk'
        key = digest({'version': 2, 'world': self.world_fingerprint(), 'zone': zid,
                      'catalog': overlay.sha(catalog) if catalog.is_file() else '',
                      'models': sorted({int(e['values'].get('Model',0)) for e in data['entities'] if e['table']=='Mob'})})
        for job_id, job in list(self.visual_jobs.items()):
            if job['key'] == key:
                result = self.visual_status(job_id)
                if result['status'] != 'failed':
                    return result
                del self.visual_jobs[job_id]
        folder = self.state / 'visuals' / key
        folder.mkdir(parents=True, exist_ok=True)
        job_id = secrets.token_hex(20)
        job = {'key': key, 'folder': folder, 'process': None, 'zone': zid}
        self.visual_jobs[job_id] = job
        manifest = folder / 'scene.json'
        if manifest.is_file():
            cached = json.loads(manifest.read_text(encoding='utf-8'))
            def unchanged(relative, expected):
                path = (self.client / relative).resolve()
                if not path.is_relative_to(self.client) or not path.is_file():
                    return False
                stat = path.stat()
                return [stat.st_size, stat.st_mtime_ns] == expected
            if all(unchanged(p, v) for p,v in cached.get('inputs',{}).items()):
                return self.visual_status(job_id)
            manifest.unlink()
        try:
            self.render_python()
        except ValueError:
            del self.visual_jobs[job_id]
            raise
        request = folder / 'request.json'
        request.write_text(json.dumps({'entities': [e for e in data['entities'] if e['table'] == 'Mob']}), encoding='utf-8')
        with (folder / 'conversion.log').open('w', encoding='utf-8') as log:
            job['process'] = subprocess.Popen([*self.worker('visual_worker'), '--client', str(self.client),
                                              '--out', str(folder), '--zone', str(zid), '--request', str(request)],
                                             stdout=log, stderr=log,
                                             env={**os.environ, 'PYTHONUTF8':'1'},
                                             creationflags=subprocess.CREATE_NO_WINDOW if os.name=='nt' else 0)
        return {'id': job_id, 'status': 'running', 'message': 'Preparing client textures and models…'}

    def worker(self, name):
        """Command for a worker: the packaged program runs them itself, a source checkout uses a Pillow Python."""
        if FROZEN:
            return [sys.executable, '--worker', name]
        return [self.render_python(), str(HERE / f'{name}.py')]

    def features(self):
        """Server abilities the UI depends on. Item-currency merchants need the GameServer.dll that has
        GameCustomCurrencyMerchant (OfflineDAoC builds without it cannot load such merchants)."""
        if getattr(self, '_features', None) is None:
            dll = self.runtime / 'server' / 'GameServer.dll'
            try:
                currency = dll.is_file() and b'GameCustomCurrencyMerchant' in dll.read_bytes()
            except OSError:
                currency = False
            self._features = {'currencyMerchant': currency}
        return self._features

    def render_python(self):
        """A Python with NumPy and Pillow, for the preview and custom-look workers."""
        if FROZEN:
            return sys.executable
        if self._visual_python is None:
            candidates = [os.environ.get('WORLD_EDITOR_RENDER_PYTHON'), sys.executable,
                          str(Path.home() / '.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe')]
            for candidate in candidates:
                if not candidate or not Path(candidate).is_file():
                    continue
                probe = subprocess.run([candidate, '-c', 'import numpy; from PIL import Image'], capture_output=True,
                                       timeout=20, creationflags=subprocess.CREATE_NO_WINDOW if os.name=='nt' else 0)
                if probe.returncode == 0:
                    self._visual_python = candidate
                    break
            if self._visual_python is None:
                raise ValueError('Textured previews and custom looks need Python with NumPy and Pillow. Set WORLD_EDITOR_RENDER_PYTHON to that interpreter.')
        return self._visual_python

    def assets(self, *args):
        result = subprocess.run([*self.worker('asset_worker'), *args, '--client', str(self.client)],
                                capture_output=True, text=True, timeout=600, env={**os.environ, 'PYTHONUTF8': '1'},
                                creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
        try:
            data = json.loads(result.stdout.strip().splitlines()[-1])
        except (ValueError, IndexError):
            raise ValueError('Custom look tool failed: ' + (result.stderr.strip().splitlines() or ['no output'])[-1])
        if isinstance(data, dict) and 'error' in data:
            raise ValueError(data['error'])
        return data

    def models(self):
        if getattr(self, '_models', None) is None:
            self._models = self.assets('catalog')
        return self._models

    def looks(self):
        root = self.state / 'look-backups'
        found = []
        for path in sorted(root.glob('*/manifest.json'), reverse=True) if root.is_dir() else []:
            manifest = json.loads(path.read_text(encoding='utf-8'))
            if Path(manifest['client']).resolve() == self.client and not (path.parent / 'uninstalled.txt').exists():
                found.append({'id': path.parent.name, **{k: manifest[k] for k in ('name', 'model', 'base', 'created', 'parts')}})
        return found

    @staticmethod
    def data_url(value, limit=24*1024*1024):
        if not isinstance(value, str) or not value.startswith('data:') or ';base64,' not in value:
            raise ValueError('Upload data is not a file')
        raw = base64.b64decode(value.split(';base64,', 1)[1], validate=True)
        if not 0 < len(raw) <= limit:
            raise ValueError('Uploaded file is empty or too large')
        return raw

    def install_look(self, data):
        with self.lock:
            folder = self.state / 'look-uploads' / uuid.uuid4().hex
            folder.mkdir(parents=True)
            images = {}
            for col, url in (data.get('images') or {}).items():
                if not str(col).isdigit():
                    raise ValueError('Unknown model part')
                raw = self.data_url(url)
                if not raw.startswith((b'\x89PNG', b'\xff\xd8')):
                    raise ValueError('Textures must be PNG or JPEG images')
                images[str(col)] = str(folder / f'{col}.img')
                Path(images[str(col)]).write_bytes(raw)
            nif = None
            if data.get('nif'):
                nif = folder / 'model.nif'
                nif.write_bytes(self.data_url(data['nif']))
            with closing(connect(self.db)) as con:
                used = set()
                for table in ('Mob', 'NpcTemplate'):
                    if table in self.schema:
                        for (value,) in con.execute(f'SELECT DISTINCT Model FROM {table}'):
                            used.update(int(v) & 0x7fff for v in re.findall(r'\d+', str(value or '')))
            request = folder / 'request.json'
            request.write_text(json.dumps({'model': int(data['model']), 'name': str(data.get('name', '')),
                                           'scale': int(data['scale']) if data.get('scale') else None,
                                           'images': images, 'nif': str(nif) if nif else None,
                                           'usedModels': sorted(used)}), encoding='utf-8')
            ensure_closed([self.runtime / 'server', self.client])
            result = self.assets('install', '--request', str(request), '--backups', str(self.state / 'look-backups'))
            self._models = None
            return {**result, 'message': f'Installed "{result["name"]}" as model {result["model"]} in this client. '
                                         'Set an NPC\'s Model to it, then Review & save.'}

    def uninstall_look(self, look_id):
        if not isinstance(look_id, str) or Path(look_id).name != look_id:
            raise ValueError('Invalid look ID')
        folder = self.state / 'look-backups' / look_id
        if not (folder / 'manifest.json').is_file():
            raise ValueError('Unknown custom look')
        with self.lock:
            ensure_closed([self.runtime / 'server', self.client])
            result = self.assets('uninstall', '--backup', str(folder))
            self._models = None
            self._fingerprint = None
            return result

    def fixtures(self, zid):
        return self.assets('fixtures', '--zone', str(int(zid)))

    def edit_fixtures(self, data):
        """Change the placed buildings in the client's own fixtures.csv. Undone from the Custom looks list."""
        with self.lock:
            ensure_closed([self.runtime / 'server', self.client])
            folder = self.state / 'look-uploads' / uuid.uuid4().hex
            folder.mkdir(parents=True)
            try:
                request = folder / 'request.json'
                request.write_text(json.dumps({'zone': int(data['zone']), 'edits': data['edits']}), encoding='utf-8')
                result = self.assets('fixture-edit', '--request', str(request), '--backups', str(self.state / 'look-backups'))
            finally:
                shutil.rmtree(folder, ignore_errors=True)
            self._fingerprint = None
            return result

    # ---- Items: client appearances (objects.csv) vs server ItemTemplate rows ----
    def item_catalog(self):
        stamp = (self.client / 'gamedata.mpk').stat().st_mtime_ns
        if getattr(self, '_items_stamp', None) != stamp:
            self._items, self._items_stamp = None, stamp
        if getattr(self, '_items', None) is None:
            items = self.assets('items')
            with closing(connect(self.db)) as con:
                used = {}
                if 'ItemTemplate' in self.schema:
                    for model, count, example in con.execute('SELECT Model, count(*), min(Id_nb) FROM ItemTemplate GROUP BY Model'):
                        used[model] = (count, example)
            for item in items:
                item['used'], item['example'] = used.get(item['id'], (0, None))
            self._items = items
        return self._items

    ITEM_KINDS = {
        'weapon': 'Object_Type BETWEEN 1 AND 28', 'armor': 'Object_Type BETWEEN 31 AND 38 AND Item_Type != 26',
        'cloak': 'Item_Type = 26', 'shield': 'Object_Type = 42', 'jewelry': 'Object_Type = 41',
        'housing': 'Object_Type BETWEEN 49 AND 71',
        'other': 'NOT (Object_Type BETWEEN 1 AND 38 OR Object_Type IN (41, 42) OR Object_Type BETWEEN 49 AND 71 OR Item_Type = 26)'}

    ITEM_SORTS = {'name': 'Name COLLATE NOCASE', 'level': 'Level, Name COLLATE NOCASE', 'level_desc': 'Level DESC, Name COLLATE NOCASE',
                  'price': 'Price, Name COLLATE NOCASE', 'price_desc': 'Price DESC, Name COLLATE NOCASE',
                  'type': 'Object_Type, Item_Type, Level, Name COLLATE NOCASE', 'realm': 'Realm, Level, Name COLLATE NOCASE'}
    # Checks that find items needing attention; each is a WHERE condition.
    ITEM_ISSUES = {
        'unequippable': '((Object_Type BETWEEN 1 AND 28 AND Item_Type NOT IN (10, 11, 12, 13)) OR '
                        '(Object_Type BETWEEN 32 AND 38 AND Item_Type NOT IN (21, 22, 23, 25, 26, 27, 28)) OR '
                        '(Object_Type = 42 AND Item_Type != 11))',
        'no_price': 'Price <= 0',
    }
    # Tables that hand items to players (table, item column); 'nowhere' lists items none of them mention.
    ITEM_SOURCES = [('MerchantItem', 'ItemTemplateID'), ('LootTemplate', 'ItemTemplateID'), ('LootOTD', 'ItemTemplateID'),
                    ('DropTemplateXItemTemplate', 'ItemTemplateID'), ('CraftedItem', 'Id_nb'), ('ArtifactXItem', 'ItemID'),
                    ('StarterEquipment', 'TemplateID')]

    def items(self, text='', kind='', realm='', model=None, offset=0, limit=100, sort='name', min_level='', max_level='',
              slot='', issue=''):
        """Server item templates, filtered by kind (object/slot type), realm, slot, level range, appearance, text or a
        problem check, and sorted."""
        where, args = [], []
        if kind:
            if kind not in self.ITEM_KINDS:
                raise ValueError('Unknown item kind')
            where.append(self.ITEM_KINDS[kind])
        for column, value, op in (('Realm', realm, '='), ('Model', model, '='), ('Item_Type', slot, '='),
                                  ('Level', min_level, '>='), ('Level', max_level, '<=')):
            if value is not None and str(value).strip().isdigit():
                where.append(f'{column} {op} ?')
                args.append(int(value))
        if text:
            where.append('(Name LIKE ? OR Id_nb LIKE ?)')
            args += [f'%{text}%'] * 2
        if issue == 'missing_model':
            ids = sorted({c['id'] for c in self.item_catalog()})
            where.append(f'Model NOT IN ({",".join(map(str, ids)) or "-1"})')
        elif issue and issue != 'nowhere':
            if issue not in self.ITEM_ISSUES:
                raise ValueError('Unknown item check')
            where.append(self.ITEM_ISSUES[issue])
        if sort not in self.ITEM_SORTS:
            raise ValueError('Unknown sort')
        clause = (' WHERE ' + ' AND '.join(where)) if where else ''
        wanted = ['Id_nb', 'Name', 'Level', 'Realm', 'Object_Type', 'Item_Type', 'Model', 'Price', 'DPS_AF', 'SPD_ABS', 'Quality',
                  'PackSize', 'Effect', *[c for i in range(1, 11) for c in (f'Bonus{i}Type', f'Bonus{i}')]]
        limit, offset = max(1, min(500, int(limit))), max(0, int(offset))
        with closing(connect(self.db)) as con:
            have = self.columns(con, 'ItemTemplate')
            columns = ', '.join(c for c in wanted if c in have)
            if issue == 'nowhere':
                # One read per source table, then filter here: a per-row subquery over quests took ~40 s.
                given = self.given_items(con)
                found = [dict(r) for r in con.execute(f'SELECT {columns} FROM ItemTemplate{clause} ORDER BY {self.ITEM_SORTS[sort]}', args)
                         if str(r['Id_nb']).lower() not in given]
                return {'total': len(found), 'offset': offset, 'rows': found[offset:offset + limit]}
            total = con.execute(f'SELECT count(*) FROM ItemTemplate{clause}', args).fetchone()[0]
            rows = [dict(r) for r in con.execute(f'SELECT {columns} FROM ItemTemplate{clause} ORDER BY {self.ITEM_SORTS[sort]} LIMIT ? OFFSET ?',
                                                 (*args, limit, offset))]
        return {'total': total, 'offset': offset, 'rows': rows}

    def given_items(self, con):
        """Lower-case Id_nb of every item a merchant, loot table, recipe, artifact, starter kit or quest hands out."""
        present, given = self.present_tables(con), set()
        for table, column in self.ITEM_SOURCES:
            if table in present and column in self.columns(con, table):
                given.update(str(v).lower() for (v,) in con.execute(f'SELECT DISTINCT "{column}" FROM "{table}"') if v)
        if 'DataQuest' in present:
            for row in con.execute('SELECT FinalRewardItemTemplates, OptionalRewardItemTemplates FROM DataQuest'):
                for value in row:
                    given.update(x.strip().lower() for x in re.split(r'[;|]', value or '') if x.strip())
        return given

    def item_sources(self, idnb):
        """Where players get an item: merchants, loot tables and the mobs that drop them, one-time drops, quests,
        starter kits; and lists that take it as currency."""
        with closing(connect(self.db)) as con:
            present = self.present_tables(con)
            q = lambda sql, *a: [dict(r) for r in con.execute(sql, a)]
            out = {'merchants': [], 'loot': [], 'otd': [], 'quests': [], 'starter': [], 'currency': [], 'crafted': [], 'artifacts': []}
            if 'CraftedItem' in present:
                out['crafted'] = q('SELECT CraftingLevel, CraftingSkillType FROM CraftedItem WHERE Id_nb = ? LIMIT 5', idnb)
            if 'ArtifactXItem' in present:
                out['artifacts'] = q('SELECT ArtifactID FROM ArtifactXItem WHERE ItemID = ? LIMIT 5', idnb)
            if 'MerchantItem' in present and 'ItemsListTemplateID' not in self.columns(con, 'Mob'):
                out['merchants'] = q('SELECT ItemListID, 0 AS sellers, NULL AS seller FROM MerchantItem WHERE ItemTemplateID = ? '
                                     'GROUP BY ItemListID LIMIT 50', idnb)
            elif 'MerchantItem' in present:
                out['merchants'] = q('SELECT m.ItemListID, count(DISTINCT n.Mob_ID) AS sellers, min(n.Name) AS seller FROM MerchantItem m '
                                     'LEFT JOIN Mob n ON n.ItemsListTemplateID = m.ItemListID WHERE m.ItemTemplateID = ? '
                                     'GROUP BY m.ItemListID LIMIT 50', idnb)
            if 'LootTemplate' in present:
                loot = q('SELECT TemplateName, Chance, Count FROM LootTemplate WHERE ItemTemplateID = ? LIMIT 100', idnb)
                for l in loot:
                    l['mobs'] = [r['MobName'] for r in q('SELECT DISTINCT MobName FROM MobXLootTemplate WHERE LootTemplateName = ? LIMIT 12',
                                                         l['TemplateName'])] if 'MobXLootTemplate' in present else []
                out['loot'] = loot
            if 'LootOTD' in present:
                out['otd'] = q('SELECT MobName, MinLevel FROM LootOTD WHERE ItemTemplateID = ? LIMIT 50', idnb)
            if 'DataQuest' in present:
                out['quests'] = q("SELECT ID, Name FROM DataQuest WHERE ';' || FinalRewardItemTemplates || ';' LIKE ? "
                                  "OR ';' || OptionalRewardItemTemplates || ';' LIKE ? LIMIT 20", f'%;{idnb};%', f'%;{idnb};%')
            if 'StarterEquipment' in present:
                out['starter'] = q('SELECT Class FROM StarterEquipment WHERE TemplateID = ? LIMIT 20', idnb)
            if 'offline_currency_lists' in present:
                out['currency'] = q('SELECT ItemListID FROM offline_currency_lists WHERE CurrencyItem = ?', idnb)
        return out

    def item_preview(self, model, effect=0, figure='nor_m', extension=0, color=0):
        """Appearance (objects.csv) plus an item Effect (spweapons.csv; 0 = the appearance's own effect)."""
        model, effect, extension, color = int(model), int(effect or 0), int(extension or 0), int(color or 0)
        if not 0 < model < 100000 or not 0 <= effect < 100000 or figure not in ('nor_m', 'bri_m', 'cel_m', 'nor_f', 'bri_f', 'cel_f'):
            raise ValueError('Invalid item appearance')
        # Keyed by the catalog too: an import or live patch changes gamedata.mpk and must not reuse an old preview.
        stat = (self.client / 'gamedata.mpk').stat()
        name = f'{model}-e{effect}-x{extension}-c{color}-{figure}-{stat.st_size:x}{stat.st_mtime_ns:x}-v5'
        folder = self.state / 'item-previews' / self.world_fingerprint()[:16] / name
        manifest = folder / 'preview.json'
        if not manifest.is_file():
            result = self.assets('item-preview', '--model', str(model), '--effect', str(effect), '--figure', figure, '--extension', str(extension), '--color', str(color), '--out', str(folder),
                                 '--cache', str(self.state / 'item-previews'))
            manifest.write_text(json.dumps(result), encoding='utf-8')
        return {**json.loads(manifest.read_text(encoding='utf-8')),
                'base': f'/item-assets/{self.preview_key}/{self.world_fingerprint()[:16]}/{name}/'}

    def item_enums(self):
        """Names for item columns, read from the server source's enums when it is present."""
        enums = REPO / 'source/server/GameServer/Enums'
        bundled = HERE / 'enums.json'               # snapshot written by build_package.py for the packaged program
        if not enums.is_dir() and bundled.is_file():
            result = json.loads(bundled.read_text(encoding='utf-8'))
            if getattr(self, '_effects', None) is None:
                self._effects = self.assets('effects')
            result['effect'] = self._effects
            return result
        result = {}
        for key, file in (('objectType', 'eObjectType.cs'), ('property', 'eProperty.cs'), ('damageType', 'eDamageType.cs'),
                          ('realm', 'eRealm.cs'), ('characterClass', 'eCharacterClass.cs'), ('craftingSkill', '../craft/CraftingMgr.cs')):
            path = (enums / file).resolve()
            names = {}
            if path.is_file():
                for name, number in re.findall(r'^\s*(\w+)\s*=\s*(\d+)\s*,?', path.read_text(encoding='utf-8-sig'), re.M):
                    if not name.startswith('_') and not name.endswith(('_First', '_Last')) and number not in names:
                        names[number] = re.sub(r'(?<=[a-z])(?=[A-Z])', ' ', name).replace('_', ' ')
            result[key] = names
        if getattr(self, '_effects', None) is None:
            self._effects = self.assets('effects')
        result['effect'] = self._effects
        result['itemType'] = {'10': 'Right hand', '11': 'Left hand', '12': 'Two-handed', '13': 'Ranged', '21': 'Helm',
                              '22': 'Gloves', '23': 'Boots', '24': 'Jewel', '25': 'Torso', '26': 'Cloak', '27': 'Legs',
                              '28': 'Arms', '29': 'Neck', '32': 'Belt', '33': 'Bracer', '35': 'Ring', '37': 'Mythical',
                              '0': 'Not equipped'}
        return result

    def merchant_lists(self, text=''):
        with closing(connect(self.db)) as con:
            lists = {r[0]: {'id': r[0], 'items': r[1], 'spawns': 0, 'currency': None}
                     for r in con.execute('SELECT ItemListID, count(*) FROM MerchantItem WHERE ItemListID LIKE ? GROUP BY ItemListID',
                                          (f'%{text}%',))}
            sellers = con.execute('SELECT ItemsListTemplateID, count(*) FROM Mob WHERE ItemsListTemplateID LIKE ? '
                                  "AND ItemsListTemplateID != '' GROUP BY ItemsListTemplateID", (f'%{text}%',))                 if 'ItemsListTemplateID' in self.columns(con, 'Mob') else []
            for list_id, count in sellers:
                lists.setdefault(list_id, {'id': list_id, 'items': 0, 'spawns': 0, 'currency': None})['spawns'] = count
            if 'offline_currency_lists' in self.present_tables(con):
                for list_id, item in con.execute('SELECT ItemListID, CurrencyItem FROM offline_currency_lists'):
                    if list_id in lists:
                        lists[list_id]['currency'] = item
        return sorted(lists.values(), key=lambda l: l['id'].lower())[:2000]

    def merchant_list(self, list_id):
        with closing(connect(self.db)) as con:
            slots = [dict(r) for r in con.execute(
                'SELECT m.*, i.Name AS ItemName, i.Price AS ItemPrice, i.Model AS ItemModel, i.Level AS ItemLevel, '
                'i.Object_Type AS ItemObjectType FROM MerchantItem m LEFT JOIN ItemTemplate i ON i.Id_nb = m.ItemTemplateID '
                'WHERE m.ItemListID = ? ORDER BY m.PageNumber, m.SlotPosition', (list_id,))]
            mob = self.columns(con, 'Mob')
            wanted = [c for c in ('Mob_ID', 'Name', 'Guild', 'ClassType', 'Region', 'X', 'Y') if c in mob]
            merchants = [dict(r) for r in con.execute(
                f'SELECT {",".join(wanted)} FROM Mob WHERE ItemsListTemplateID = ? LIMIT 50', (list_id,))]                 if 'ItemsListTemplateID' in mob else []
            currency = None
            if 'offline_currency_lists' in self.present_tables(con):
                row = con.execute('SELECT * FROM offline_currency_lists WHERE ItemListID = ?', (list_id,)).fetchone()
                currency = dict(row) if row else None
        return {'id': list_id, 'slots': slots, 'merchants': merchants, 'currency': currency}

    @staticmethod
    def columns(con, table):
        return {r[1] for r in con.execute(f'PRAGMA table_info("{table}")')}

    @staticmethod
    def present_tables(con):
        return {r[0] for r in con.execute("select name from sqlite_master where type='table'")}

    def visual_status(self, job_id):
        if job_id not in self.visual_jobs:
            raise ValueError('Unknown preview job')
        job = self.visual_jobs[job_id]
        proc = job['process']
        if proc is not None and proc.poll() is None:
            try:
                progress = json.loads((job['folder']/'progress.json').read_text(encoding='utf-8'))['message']
            except (OSError, ValueError, KeyError):
                progress = 'Preparing client models…'
            return {'id': job_id, 'status': 'running', 'message': progress}
        if proc is not None and proc.returncode:
            return {'id': job_id, 'status': 'failed', 'message': f'Textured preview failed. See {job["folder"] / "conversion.log"}'}
        scene = json.loads((job['folder']/'scene.json').read_text(encoding='utf-8'))
        return {'id': job_id, 'status': 'ready', 'base': f'/assets/{job_id}/', 'scene': scene}

    def patch(self, changes, fingerprint):
        validate_changes(changes, self.schema)
        if fingerprint != self.world_fingerprint(fresh=True):
            raise ValueError('Client world files changed. Reload the editor before applying this patch.')
        return {'format': 'offline-daoc-world-editor-2', 'created': datetime.now(timezone.utc).isoformat(),
                'clientWorld': fingerprint, 'changes': changes,
                'note': 'Server-sent NPC/object positions need no client binary patch. Geometry edits require matching client assets and rebuilt navigation.'}

    def check_patch(self, patch):
        if patch.get('format') not in ('offline-daoc-world-editor-1', 'offline-daoc-world-editor-2'):
            raise ValueError('Unknown patch format')
        validate_changes(patch.get('changes'), self.schema)
        if patch.get('clientWorld') != self.world_fingerprint(fresh=True):
            raise ValueError('Patch uses a different client world. Synchronize the client world or choose the correct profile first.')

    def conflicts(self, con, changes):
        present = self.present_tables(con)
        for c in changes:
            key = self.schema[c['table']]['key']
            # A table the editor creates on first save (VIRTUAL_TABLES) has no rows yet.
            row = con.execute(f'SELECT * FROM "{c["table"]}" WHERE "{key}"=?', (c['key'],)).fetchone() \
                if c['table'] in present else None
            if c.get('op') == 'insert':
                if row is not None:
                    raise ValueError(f'Conflict: {c["table"]} {c["key"]} already exists. Nothing applied.')
            elif row is None or any(row[k] != v for k, v in c['before'].items()):
                raise ValueError(f'Conflict: {c["table"]} {c["key"]} changed since this edit was prepared. Nothing applied.')

    def apply(self, patch):
        with self.lock:
            self.check_patch(patch)
            ensure_closed([self.db.parent.parent / 'server', self.client])
            folder = self.state / 'backups' / (datetime.now().strftime('%Y%m%d-%H%M%S-') + uuid.uuid4().hex[:8])
            folder.mkdir(parents=True)
            with closing(sqlite3.connect(self.db, timeout=0.5)) as con:
                con.row_factory = sqlite3.Row
                try:
                    con.execute('BEGIN IMMEDIATE')
                    for table in {c['table'] for c in patch['changes']} & set(VIRTUAL_TABLES):
                        for ddl in VIRTUAL_TABLES[table]:
                            con.execute(ddl)
                    self.conflicts(con, patch['changes'])
                    # Hold the write reservation throughout snapshot and edits; other writers cannot race.
                    with closing(connect(self.db)) as src, closing(sqlite3.connect(folder / 'world-before.db')) as dst:
                        src.backup(dst)
                    (folder / 'patch.json').write_text(json.dumps(patch, indent=2), encoding='utf-8')
                    for c in patch['changes']:
                        table, key, op = c['table'], self.schema[c['table']]['key'], c.get('op', 'update')
                        if op == 'insert':
                            columns = ','.join(f'"{k}"' for k in c['after'])
                            con.execute(f'INSERT INTO "{table}" ({columns}) VALUES ({",".join("?" for _ in c["after"])})',
                                        tuple(c['after'].values()))
                        elif op == 'delete':
                            con.execute(f'DELETE FROM "{table}" WHERE "{key}"=?', (c['key'],))
                        else:
                            sets = ','.join(f'"{k}"=?' for k in c['after'])
                            con.execute(f'UPDATE "{table}" SET {sets} WHERE "{key}"=?', (*c['after'].values(), c['key']))
                    con.commit()
                except BaseException:
                    con.rollback()
                    raise
            (folder / 'applied.txt').write_text(str(self.db), encoding='utf-8')
            self._items = None
            return {'message': f'Applied {len(patch["changes"])} row edits. Restart the game server to load them.',
                    'backup': str(folder), 'id': folder.name}

    def rollback(self, backup_id):
        if not isinstance(backup_id, str) or Path(backup_id).name != backup_id:
            raise ValueError('Invalid backup ID')
        folder = self.state / 'backups' / backup_id
        if not (folder / 'applied.txt').is_file():
            raise ValueError('Backup is not an applied editor patch')
        patch = json.loads((folder / 'patch.json').read_text())
        for c in patch['changes']:
            c['before'], c['after'] = c['after'], c['before']
            c['op'] = {'insert': 'delete', 'delete': 'insert'}.get(c.get('op'), 'update')
        # Row-wise rollback preserves character progress and unrelated later edits.
        return self.apply(patch)

    def compare(self, target_id):
        if target_id not in self.targets:
            raise ValueError('Choose a configured target client')
        manifest = overlay.plan(self.client, self.targets[target_id])
        return {'manifest': manifest, 'summary': overlay.summary(manifest), 'id': digest(manifest)}

    def sync(self, target_id, expected):
        with self.lock:
            report = self.compare(target_id)
            if report['id'] != expected:
                raise ValueError('Client files changed after comparison. Compare again before synchronizing.')
            target = self.targets[target_id]
            ensure_closed([self.db.parent.parent / 'server', self.client, target, target.parent.parent / 'server'])
            folder = overlay.apply(self.client, target, self.state / 'client-backups', True)
            return {'message': 'Client world synchronized to the selected reference. NPC positions come from the shared server.',
                    'backup': str(folder) if folder else None}


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        # Avoid logging the local bearer token.
        if self.path.startswith('/api/'):
            print(fmt % (self.path.split('?')[0], *args[1:]))

    @property
    def editor(self):
        return self.server.editor

    def send(self, data, status=200, content_type='application/json'):
        raw = json.dumps(data).encode() if content_type == 'application/json' else data
        self.send_response(status)
        self.send_header('Content-Type', content_type)
        self.send_header('Content-Length', str(len(raw)))
        self.send_header('Cache-Control', 'no-store')
        self.send_header('X-Content-Type-Options', 'nosniff')
        self.send_header('Content-Security-Policy', "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; connect-src 'self' data:; object-src 'none'; frame-ancestors 'none'")
        self.end_headers()
        self.wfile.write(raw)

    def authorized(self):
        host = self.headers.get('Host', '')
        allowed = f'127.0.0.1:{self.server.server_port}'
        if host != allowed or self.headers.get('Origin', 'http://'+allowed) != 'http://'+allowed:
            raise ValueError('Untrusted request origin')
        if not secrets.compare_digest(self.headers.get('X-Editor-Token', ''), self.editor.token):
            raise ValueError('Editor session expired. Open the launcher URL again.')

    def do_GET(self):
        try:
            url = urlparse(self.path)
            if url.path.startswith('/api/'):
                self.authorized()
                q = parse_qs(url.query)
                if url.path == '/api/config':
                    self.send({'database': str(self.editor.db), 'client': str(self.editor.client),
                               'targets': self.editor.targets and {k: str(v) for k,v in self.editor.targets.items()},
                               'fields': FIELDS, 'zones': self.editor.zones(), 'schema': self.editor.schema,
                               'layers': {t: l for t, l in LAYERS.items() if t in self.editor.schema},
                               'groups': {g: [t for t in ts if t in self.editor.schema] for g, ts in DATA_TABLES.items()},
                               'runtime': str(self.editor.runtime), 'features': self.editor.features()})
                elif url.path == '/api/rows':
                    one = lambda k, d=None: q.get(k, [d])[0]
                    self.send(self.editor.rows(one('table'), one('q', ''), one('column'), one('equals'),
                                               int(one('offset', 0)), int(one('limit', 100))))
                elif url.path == '/api/new-key':
                    self.send(self.editor.new_key(q['table'][0]))
                elif url.path == '/api/files':
                    self.send(self.editor.file_list())
                elif url.path == '/api/file':
                    self.send(self.editor.read_file(q['path'][0]))
                elif url.path == '/api/file-backup':
                    self.send(self.editor.read_file_backup(q['id'][0], q['path'][0]))
                elif url.path == '/api/models':
                    self.send(self.editor.models())
                elif url.path == '/api/model-template':
                    self.send(self.editor.assets('template', '--model', str(int(q['model'][0]))))
                elif url.path == '/api/looks':
                    self.send(self.editor.looks())
                elif url.path == '/api/items':
                    one = lambda k, d='': q.get(k, [d])[0]
                    self.send(self.editor.items(one('q'), one('kind'), one('realm'), one('model', None), int(one('offset', 0)),
                                                int(one('limit', 100)), one('sort', 'name'), one('min_level'), one('max_level'),
                                                one('slot'), one('issue')))
                elif url.path == '/api/item-sources':
                    self.send(self.editor.item_sources(q['id'][0]))
                elif url.path == '/api/items-catalog':
                    self.send(self.editor.item_catalog())
                elif url.path == '/api/item-preview':
                    one = lambda k, d='0': q.get(k, [d])[0]
                    self.send(self.editor.item_preview(one('model'), one('effect'), one('figure', 'nor_m'), one('extension'), one('color')))
                elif url.path == '/api/item-enums':
                    self.send(self.editor.item_enums())
                elif url.path == '/api/merchant-lists':
                    self.send(self.editor.merchant_lists(q.get('q', [''])[0]))
                elif url.path == '/api/merchant-list':
                    self.send(self.editor.merchant_list(q['id'][0]))
                elif url.path == '/api/zone':
                    self.send(self.editor.zone(int(q['id'][0])))
                elif url.path == '/api/fixtures':
                    self.send(self.editor.fixtures(q['zone'][0]))
                elif url.path == '/api/mesh':
                    with self.editor.lock:
                        self.send(self.editor.mesh(int(q['id'][0])))
                elif url.path == '/api/backups':
                    self.send([p.parent.name for p in sorted((self.editor.state / 'backups').glob('*/applied.txt'), reverse=True)])
                elif url.path == '/api/visual-status':
                    self.send(self.editor.visual_status(q['id'][0]))
                else:
                    self.send({'error': 'Unknown endpoint'}, 404)
                return
            if url.path.startswith('/icons/'):
                # Item icon sheets, read from the client (not secret; plain <img> requests carry no token).
                name = url.path[7:].lower()
                if name not in ('itm_1200.bmp', 'itm_1300.bmp', 'itm_1400.bmp', 'wpn_900.bmp'):
                    raise ValueError('Unknown icon sheet')
                path = next((p for p in (self.editor.client / 'icons').glob('*') if p.name.lower() == name), self.editor.client / 'missing')
            elif url.path.startswith('/item-assets/'):
                parts = url.path.split('/', 5)
                if len(parts) != 6 or not secrets.compare_digest(parts[2], self.editor.preview_key):
                    raise ValueError('Unknown preview asset')
                root = (self.editor.state / 'item-previews' / parts[3] / parts[4]).resolve()
                path = (root / parts[5]).resolve()
                if not path.is_relative_to(self.editor.state / 'item-previews') or not path.is_relative_to(root)                         or path.suffix.lower() not in ('.gltf', '.png', '.bin'):
                    raise ValueError('Invalid preview asset path')
            elif url.path.startswith('/assets/'):
                parts = url.path.split('/', 3)
                if len(parts) != 4 or parts[2] not in self.editor.visual_jobs:
                    raise ValueError('Unknown preview asset')
                root = self.editor.visual_jobs[parts[2]]['folder']
                path = (root / parts[3]).resolve()
                if not path.is_relative_to(root) or path.suffix.lower() not in ('.gltf','.png','.jpg','.bin'):
                    raise ValueError('Invalid preview asset path')
            elif url.path == '/':
                path = HERE / 'static/index.html'
            else:
                path = (HERE / 'static' / url.path.lstrip('/')).resolve()
                if not path.is_relative_to(HERE / 'static'):
                    raise ValueError('Invalid file path')
            if not path.is_file():
                self.send({'error': 'Not found'}, 404)
                return
            mime = {'.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8',
                    '.css': 'text/css; charset=utf-8', '.gltf':'model/gltf+json', '.png':'image/png',
                    '.jpg':'image/jpeg', '.bin':'application/octet-stream', '.bmp':'image/bmp'}.get(path.suffix.lower(), 'text/plain')
            self.send(path.read_bytes(), content_type=mime)
        except (ValueError, KeyError, sqlite3.Error, OSError, subprocess.SubprocessError) as exc:
            self.send({'error': str(exc)}, 400)

    def do_POST(self):
        try:
            self.authorized()
            length = int(self.headers.get('Content-Length', 0))
            if not 0 < length <= 96*1024*1024:
                raise ValueError('Invalid request size')
            data = json.loads(self.rfile.read(length))
            ed = self.editor
            if self.path == '/api/export':
                self.send(ed.patch(data['changes'], data['fingerprint']))
            elif self.path == '/api/apply':
                self.send(ed.apply(data['patch']))
            elif self.path == '/api/validate':
                ed.check_patch(data['patch'])
                with closing(connect(ed.db)) as con:
                    ed.conflicts(con, data['patch']['changes'])
                self.send({'message': 'Patch matches client world and original database values.'})
            elif self.path == '/api/rollback':
                self.send(ed.rollback(data['id']))
            elif self.path == '/api/compare':
                self.send(ed.compare(data['target']))
            elif self.path == '/api/visual-start':
                with ed.lock:
                    self.send(ed.visual_start(int(data['zone']), data.get('models')))
            elif self.path == '/api/sync':
                self.send(ed.sync(data['target'], data['id']))
            elif self.path == '/api/file-save':
                self.send(ed.save_file(data['path'], data['text'], data['sha']))
            elif self.path == '/api/look-install':
                self.send(ed.install_look(data))
            elif self.path == '/api/look-uninstall':
                self.send(ed.uninstall_look(data['id']))
            elif self.path == '/api/fixture-edit':
                self.send(ed.edit_fixtures(data))
            else:
                self.send({'error': 'Unknown endpoint'}, 404)
        except (ValueError, KeyError, TypeError, sqlite3.Error, OSError, subprocess.SubprocessError, SystemExit) as exc:
            self.send({'error': str(exc)}, 400)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--db', type=Path, required=True)
    parser.add_argument('--client', type=Path, required=True)
    parser.add_argument('--state', type=Path, default=DATA_REPO / 'artifacts/world-editor')
    parser.add_argument('--target-client', type=Path, action='append', default=[])
    parser.add_argument('--geometry', type=Path)
    parser.add_argument('--exporter', type=Path, default=DATA_REPO / 'source/development-tools/OpenDAoC-BuildNav/bin/Release/net10.0-windows7.0/OpenDAoC-BuildNav.exe')
    parser.add_argument('--port', type=int, default=0)
    parser.add_argument('--no-browser', action='store_true')
    args = parser.parse_args(argv)
    editor = Editor(args.db, args.client, args.state, args.geometry, args.exporter, args.target_client)
    server = ThreadingHTTPServer(('127.0.0.1', args.port), Handler)
    server.editor = editor
    url = f'http://127.0.0.1:{server.server_port}/#token={editor.token}'
    print(f'World editor: {url}', flush=True)
    print('No game/server is started. Close this window or press Ctrl+C to stop the editor.', flush=True)
    if not args.no_browser:
        webbrowser.open(url)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == '__main__':
    main()
