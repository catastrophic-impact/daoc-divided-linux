"""World Workshop from the command line: look up world data and make guarded, reversible fixes without the browser.

Every command reads the database read-only, except `apply` and `undo`, which go through the editor's own save path:
the same table/column checks, a refusal while the game or server runs, a full database snapshot and a reversible patch.

    python source/tools/world_editor/cli.py --profile classic tables
    python source/tools/world_editor/cli.py --profile classic find Mob --where Name=Mularn Guard --columns Mob_ID,X,Y,Z
    python source/tools/world_editor/cli.py --profile classic sql "SELECT count(*) FROM Mob WHERE Region=1"
    python source/tools/world_editor/cli.py --profile classic item cloak_25th_anniversary_hibernia
    python source/tools/world_editor/cli.py --profile classic items --issue nowhere --limit 20
    python source/tools/world_editor/cli.py --profile classic plan edits.json --out fix.patch.json
    python source/tools/world_editor/cli.py --profile classic apply fix.patch.json
    python source/tools/world_editor/cli.py --profile modern audit --region 100 --out audit.json
    python source/tools/world_editor/cli.py --profile classic history
    python source/tools/world_editor/cli.py --profile classic undo 20261027-101500-ab12cd34

A world is --profile classic|stormforged|modern (this workspace) or --game <Offline DAoC folder>, or --db with --client.

An edits file (for `plan`) is a JSON list of:
    {"table": "Mob", "key": "<Mob_ID>", "set": {"X": 1234}}             change one row
    {"table": "Mob", "where": {"Name": "Foo"}, "set": {"Level": 50}}    change every matching row
    {"table": "ItemTemplate", "copy": "<Id_nb>", "key": "<new Id_nb>", "set": {...}}   duplicate a row
    {"table": "MerchantItem", "insert": {...}}                          add a row (missing columns get their defaults)
    {"table": "Mob", "delete": "<Mob_ID>"}                              remove a row
`plan` turns them into a patch with each row's current values, validates it and prints what would change.
"""
from __future__ import annotations

import argparse
import json
import sqlite3
import sys
import uuid
from contextlib import closing
from pathlib import Path

if __package__:
    from .editor import Editor, connect, WORKSPACE, REPO, DATA_REPO
    from .app import runtime_of
    from . import audit
else:
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
    from world_editor.editor import Editor, connect, WORKSPACE, REPO, DATA_REPO
    from world_editor.app import runtime_of
    from world_editor import audit

PROFILES = {
    'classic': WORKSPACE / 'Classic',
    'stormforged': WORKSPACE / 'Stormforged',
    'modern': DATA_REPO / 'artifacts/modern-client-test',
}


def open_editor(args):
    if args.db or args.client:
        if not (args.db and args.client):
            raise SystemExit('--db and --client go together')
        db, client = args.db, args.client
        state = args.state or db.resolve().parent.parent / 'world-workshop-data'
    else:
        game = args.game or PROFILES[args.profile or 'classic']
        found = runtime_of(game)
        if not found:
            raise SystemExit(f'{game} is not an Offline DAoC game folder')
        runtime, db, client = found
        state = args.state or runtime / 'world-workshop-data'
    return Editor(Path(db), Path(client), Path(state))


def out(data):
    print(json.dumps(data, indent=1, ensure_ascii=False, default=str))


def parse_where(pairs):
    where = {}
    for pair in pairs or []:
        if '=' not in pair:
            raise SystemExit(f'--where wants COLUMN=VALUE, not {pair!r}')
        column, value = pair.split('=', 1)
        where[column] = value
    return where


def typed(info, column, value):
    """A command-line or JSON value in the column's stored type."""
    if column not in info['columns']:
        raise ValueError(f'Unknown column {column}')
    kind = info['columns'][column][0]
    if isinstance(value, str) and kind == 'int' and value.lstrip('-').isdigit():
        return int(value)
    if isinstance(value, str) and kind == 'real':
        try:
            return float(value)
        except ValueError:
            pass
    return value


def select(ed, con, table, where, limit=None, columns='*'):
    if table not in ed.schema:
        raise ValueError(f'{table} is not an editable table (see `tables`)')
    info = ed.schema[table]
    clause = ' AND '.join(f'"{c}"=?' for c in where)
    sql = f'SELECT {columns} FROM "{table}"' + (f' WHERE {clause}' if clause else '') + ' ORDER BY rowid'
    args = [typed(info, c, v) for c, v in where.items()]
    if limit:
        sql += f' LIMIT {int(limit)}'
    return [dict(r) for r in con.execute(sql, args)]


def cmd_tables(ed, args):
    out({t: {'group': i['group'], 'key': i['key'], 'columns': len(i['columns'])} for t, i in ed.schema.items()})


def cmd_find(ed, args):
    where = parse_where(args.where)
    columns = ', '.join(f'"{c.strip()}"' for c in args.columns.split(',')) if args.columns else '*'
    with closing(connect(ed.db)) as con:
        if args.text:
            out(ed.rows(args.table, args.text, offset=0, limit=args.limit)['rows'])
        else:
            out(select(ed, con, args.table, where, args.limit, columns))


def cmd_sql(ed, args):
    # A read-only connection: the database refuses any write here.
    with closing(connect(ed.db)) as con:
        rows = con.execute(args.query).fetchmany(args.limit)
        out([dict(r) for r in rows])


def cmd_item(ed, args):
    with closing(connect(ed.db)) as con:
        row = con.execute('SELECT * FROM ItemTemplate WHERE Id_nb = ?', (args.id,)).fetchone()
    if row is None:
        raise SystemExit(f'No ItemTemplate {args.id}')
    out({'item': dict(row), 'sources': ed.item_sources(args.id)})


def cmd_items(ed, args):
    out(ed.items(args.text or '', args.kind or '', args.realm or '', None, 0, args.limit, args.sort,
                 args.min_level or '', args.max_level or '', args.slot or '', args.issue or ''))


def build_changes(ed, edits):
    """Edits file entries -> editor row changes, with every touched row's current values as 'before'."""
    changes = []
    with closing(connect(ed.db)) as con:
        present = ed.present_tables(con)
        for n, e in enumerate(edits, 1):
            table = e.get('table')
            if table not in ed.schema:
                raise ValueError(f'Edit {n}: {table} is not an editable table')
            info, key = ed.schema[table], ed.schema[table]['key']
            sets = {c: typed(info, c, v) for c, v in (e.get('set') or {}).items()}
            if 'delete' in e:
                rows = select(ed, con, table, {key: e['delete']}) if table in present else []
                if not rows:
                    raise ValueError(f'Edit {n}: no {table} {e["delete"]}')
                changes.append({'table': table, 'key': rows[0][key], 'op': 'delete', 'before': rows[0], 'after': {}})
            elif 'insert' in e or 'copy' in e:
                if 'copy' in e:
                    rows = select(ed, con, table, {key: e['copy']}) if table in present else []
                    if not rows:
                        raise ValueError(f'Edit {n}: no {table} {e["copy"]} to copy')
                    row = rows[0]
                else:
                    row = {c: None for c in info['columns']}
                    if table in present:
                        for r in con.execute(f'PRAGMA table_info("{table}")'):
                            if r[4] is not None:
                                row[r[1]] = con.execute(f'SELECT {r[4]}').fetchone()[0]
                    row.update({c: typed(info, c, v) for c, v in e['insert'].items()})
                row.update(sets)
                new = e.get('key', row.get(key))
                if new in (None, ''):
                    new = ed.new_key(table)['key']
                row[key] = typed(info, key, new)
                # The unique <Table>_ID column of a copied row must not repeat its source's.
                own = f'{table}_ID'
                if own in row and own != key and ('copy' in e or not row[own]):
                    row[own] = str(uuid.uuid4())
                if 'LastTimeRowUpdated' in row and not row['LastTimeRowUpdated']:
                    row['LastTimeRowUpdated'] = '2000-01-01 00:00:00'
                changes.append({'table': table, 'key': row[key], 'op': 'insert', 'before': {}, 'after': row})
            else:
                if not sets:
                    raise ValueError(f'Edit {n}: nothing to set')
                where = {key: e['key']} if 'key' in e else e.get('where')
                if not where:
                    raise ValueError(f'Edit {n}: give "key" or "where"')
                rows = select(ed, con, table, where) if table in present else []
                if not rows:
                    raise ValueError(f'Edit {n}: no {table} row matches {where}')
                for row in rows:
                    changed = {c: v for c, v in sets.items() if row.get(c) != v}
                    if changed:
                        changes.append({'table': table, 'key': row[key], 'op': 'update',
                                        'before': {c: row[c] for c in changed}, 'after': changed})
    # Several edits of one row become one change; the same column set to two values is a mistake in the edits.
    merged = {}
    for c in changes:
        ident = (c['table'], str(c['key']))
        if ident not in merged:
            merged[ident] = c
            continue
        first = merged[ident]
        if c['op'] != 'update' or first['op'] != 'update' or \
                any(first['after'].get(k, v) != v for k, v in c['after'].items()):
            raise ValueError(f'{c["table"]} {c["key"]} is edited twice with different values')
        first['before'].update(c['before'])
        first['after'].update(c['after'])
    return list(merged.values())


def describe(changes):
    lines = []
    for c in changes:
        if c['op'] == 'update':
            diff = ', '.join(f'{k}: {c["before"][k]!r} -> {v!r}' for k, v in c['after'].items())
            lines.append(f'update {c["table"]} {c["key"]}: {diff}')
        else:
            row = c['after'] or c['before']
            label = row.get('Name') or row.get('Id_nb') or ''
            lines.append(f'{c["op"]} {c["table"]} {c["key"]} {label}'.rstrip())
    return lines


def cmd_plan(ed, args):
    edits = json.loads(Path(args.edits).read_text(encoding='utf-8-sig'))
    changes = build_changes(ed, edits if isinstance(edits, list) else [edits])
    if not changes:
        raise SystemExit('Nothing would change: every row already has those values.')
    patch = ed.patch(changes, ed.world_fingerprint(fresh=True))
    target = Path(args.out or Path(args.edits).with_suffix('.patch.json'))
    target.write_text(json.dumps(patch, indent=1, ensure_ascii=False), encoding='utf-8')
    print('\n'.join(describe(changes)))
    print(f'\n{len(changes)} row edits for {ed.db}\nPatch: {target}  (nothing applied yet)')


def cmd_apply(ed, args):
    patch = json.loads(Path(args.patch).read_text(encoding='utf-8'))
    ed.check_patch(patch)
    print('\n'.join(describe(patch['changes'])))
    result = ed.apply(patch)
    print(f'\n{result["message"]}\nUndo with: undo {result["id"]}')


def cmd_history(ed, args):
    folder = ed.state / 'backups'
    rows = []
    for b in sorted(folder.iterdir() if folder.is_dir() else [], reverse=True)[:args.limit]:
        if (b / 'applied.txt').is_file():
            changes = json.loads((b / 'patch.json').read_text(encoding='utf-8'))['changes']
            rows.append({'id': b.name, 'edits': len(changes), 'first': describe(changes[:1])[0] if changes else ''})
    out(rows)


def cmd_audit(ed, args):
    report = audit.run(ed.db, args.navmesh or ed.runtime / 'server' / 'navmesh',
                       regions={int(r) for r in args.region} if args.region else None,
                       zones={int(z) for z in args.zone} if args.zone else None,
                       routes=not args.no_routes, progress=lambda m: print(m, file=sys.stderr, flush=True))
    if args.out:
        Path(args.out).write_text(json.dumps(report, indent=1, ensure_ascii=False), encoding='utf-8')
    by_zone = {}
    for f in report['findings']:
        if not f['status'].startswith('ignored'):
            key = (f['zone'], f['kind'], f['status'])
            by_zone[key] = by_zone.get(key, 0) + 1
    out({'summary': report['summary'], 'zones without navmesh': report['zones without navmesh'],
         'problems by zone': [f'zone {z}: {n} {k} {s}' for (z, k, s), n in sorted(by_zone.items(), key=lambda i: -i[1])][:60],
         'details': args.out or 'pass --out FILE for every finding'})


def cmd_undo(ed, args):
    result = ed.rollback(args.backup)
    print(f'{result["message"]}\nThat undo is itself undoable: undo {result["id"]}')


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--profile', choices=sorted(PROFILES))
    parser.add_argument('--game', type=Path, help='an Offline DAoC game folder')
    parser.add_argument('--db', type=Path)
    parser.add_argument('--client', type=Path)
    parser.add_argument('--state', type=Path, help='backups folder (default <runtime>/world-workshop-data)')
    sub = parser.add_subparsers(dest='command', required=True)
    sub.add_parser('tables', help='editable tables')
    p = sub.add_parser('find', help='rows of one table')
    p.add_argument('table')
    p.add_argument('--where', action='append', metavar='COLUMN=VALUE')
    p.add_argument('--text', help='free-text search over text columns')
    p.add_argument('--columns')
    p.add_argument('--limit', type=int, default=50)
    p = sub.add_parser('sql', help='a read-only SQL query')
    p.add_argument('query')
    p.add_argument('--limit', type=int, default=200)
    p = sub.add_parser('item', help='one item and where players get it')
    p.add_argument('id')
    p = sub.add_parser('items', help='search items or run an item check')
    p.add_argument('--text')
    p.add_argument('--kind')
    p.add_argument('--realm')
    p.add_argument('--slot')
    p.add_argument('--min-level')
    p.add_argument('--max-level')
    p.add_argument('--sort', default='name')
    p.add_argument('--issue', choices=['missing_model', 'nowhere', 'unequippable', 'no_price'])
    p.add_argument('--limit', type=int, default=50)
    p = sub.add_parser('plan', help='turn an edits file into a checked patch (applies nothing)')
    p.add_argument('edits')
    p.add_argument('--out')
    p = sub.add_parser('apply', help='apply a patch with a backup (game and server must be closed)')
    p.add_argument('patch')
    p = sub.add_parser('audit', help='check NPCs, routes and arrival points against the navmeshes (read-only)')
    p.add_argument('--region', action='append')
    p.add_argument('--zone', action='append')
    p.add_argument('--navmesh', type=Path, help='navmesh folder (default <runtime>/server/navmesh)')
    p.add_argument('--no-routes', action='store_true')
    p.add_argument('--out', help='write every finding to this JSON file')
    p = sub.add_parser('history', help='applied patches, newest first')
    p.add_argument('--limit', type=int, default=20)
    p = sub.add_parser('undo', help='reverse an applied patch row by row')
    p.add_argument('backup')
    args = parser.parse_args(argv)
    try:
        ed = open_editor(args)
        globals()['cmd_' + args.command](ed, args)
    except (ValueError, OSError, sqlite3.Error) as exc:
        raise SystemExit(f'error: {exc}')


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    main()
