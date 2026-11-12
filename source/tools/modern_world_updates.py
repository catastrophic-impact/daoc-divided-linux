"""World updates for the modern test packages: one per release, so a world from any earlier release can be brought up to date.

    python modern_world_updates.py seed --world 0.38-dev=<clean db> --world 0.38-dev2=<clean db> ... [--chain <folder>]
    python modern_world_updates.py check --start <name>=<db> ... --target <clean db of the newest release> [--chain <folder>]

The chain lives in source/tools/release-baselines/modern/world-updates: world-updates.json lists the steps in order, each
a make_patch world update (world-<version>.json) from the previous release's clean world to that release's. A release
package ships the chain in runtime/world-updates, and the launcher (WorldUpdates.cs) applies every step a world hasn't
had (offline_patch_log) with the patcher's WorldUpdate: rows are added, and changed or removed only where they still have
the released values, so saves and rows the game rewrote stay as they are. build_modern_test_package.py adds each new
release's step (add_step) and marks its clean world as having them all (stamp). It finds the previous release's clean
world in release-baselines/modern next to this repository (outside Git: they are large) and keeps its own there for the
next build (keep_baseline), so each release only needs the build command. Building the same version again replaces its
step (from the same previous release).

seed rebuilds the chain from the published releases' clean worlds (oldest first, at least two). check applies the whole
chain to each start and compares with the target: only re-rolled monster fields may differ (the running server re-rolls
monster size, level, stats and model, so two builds of the same world differ there and an older step can re-roll a row
again).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import sqlite3
import tempfile
from pathlib import Path

import make_patch

REPO = Path(__file__).resolve().parents[2]
CHAIN = REPO / 'source/tools/release-baselines/modern/world-updates'
BASELINES = REPO.parent / 'release-baselines' / 'modern'     # each release's clean world: world-<version>.sqlite3.db
INDEX = 'world-updates.json'
CLEAN_WORLD = 'world-clean.sqlite3.db'      # the shipped world; the launcher makes data/opendaoc.sqlite3.db from it
POLICY = REPO / 'source/tools/OfflineDaoc.ProgressImport/progress-policy.json'
# Monster fields the running server rewrites itself (size, level and stats are re-rolled; an NPC whose template lists
# several models picks one at spawn): check() lets these differ. The updates themselves still carry real fixes to them.
REROLLED = {'Mob': {'Size', 'Level', 'Strength', 'Constitution', 'Dexterity', 'Quickness', 'Intelligence', 'Piety', 'Empathy',
                    'Charisma', 'Model', 'LastTimeRowUpdated'}}


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def policy() -> dict:
    return json.loads(POLICY.read_text(encoding='utf-8'))


def load(chain: Path) -> dict:
    index = chain / INDEX
    return json.loads(index.read_text(encoding='utf-8')) if index.is_file() else {'Format': 1, 'Steps': []}


def save(chain: Path, data: dict):
    chain.mkdir(parents=True, exist_ok=True)
    (chain / INDEX).write_text(json.dumps(data, indent=1) + '\n', encoding='utf-8')


def previous_release(chain: Path, version: str) -> str:
    """The release this version's step starts from: the chain's last one, or, when this version is being built again,
    the one its step already starts from."""
    steps = load(chain)['Steps']
    if not steps:
        raise SystemExit(f'{chain} has no steps yet: seed it from the published releases first')
    if steps[-1]['Version'] == version:
        return steps[-1]['From']
    if any(s['Version'] == version for s in steps):
        raise SystemExit(f'{version} is already in the chain and is not its last step: choose a new version label')
    return steps[-1]['Version']


def baseline(version: str, baselines: Path = BASELINES) -> Path:
    path = baselines / f'world-{version}.sqlite3.db'
    if not path.is_file():
        raise SystemExit(f"The clean world of {version} is missing ({path}). Copy that release's "
                         'runtime/data/world-clean.sqlite3.db (opendaoc.sqlite3.db up to 0.38-dev6) there.')
    return path


def keep_baseline(version: str, world: Path, baselines: Path = BASELINES):
    """This release's clean world, for the next release's step."""
    baselines.mkdir(parents=True, exist_ok=True)
    shutil.copy2(world, baselines / f'world-{version}.sqlite3.db')


def add_step(chain: Path, version: str, from_version: str, from_db: Path, to_db: Path) -> dict:
    """The update from the previous release's clean world to this one's, checked, as the chain's last step. Building the
    same release again replaces its step; a step can't go before an existing one."""
    data = load(chain)
    if data['Steps'] and from_version != previous_release(chain, version):
        raise SystemExit(f'{version} must start from {previous_release(chain, version)}, not {from_version}')
    steps = [s for s in data['Steps'] if s['Version'] != version]
    update = make_patch.world_update(from_db, to_db, policy(), version)
    make_patch.check_world_update(update, from_db, to_db, policy())
    name = f'world-{version}.json'
    chain.mkdir(parents=True, exist_ok=True)
    (chain / name).write_text(json.dumps(update, separators=(',', ':')), encoding='utf-8')
    steps.append({'Version': version, 'From': from_version, 'File': name, 'Sha256': sha(chain / name)})
    save(chain, {'Format': 1, 'Steps': steps})
    return update['Summary']


def seed(chain: Path, worlds: list[tuple[str, Path]]) -> dict:
    if len(worlds) < 2:
        raise SystemExit('seed needs the clean worlds of at least two releases, oldest first')
    if chain.exists():
        shutil.rmtree(chain)
    return {version: add_step(chain, version, worlds[i][0], worlds[i][1], db) for i, (version, db) in enumerate(worlds[1:])}


def stamp(database: Path, chain: Path):
    """A release's clean world has every step of its chain: a fresh install runs none of them."""
    con = sqlite3.connect(database)
    with con:
        con.execute('CREATE TABLE IF NOT EXISTS offline_patch_log (Version TEXT PRIMARY KEY, AppliedUtc TEXT, Notes TEXT)')
        for step in load(chain)['Steps']:
            con.execute("INSERT OR IGNORE INTO offline_patch_log VALUES (?, '', 'in the release''s clean world')", (step['Version'],))
    con.close()


def install(chain: Path, runtime: Path):
    target = runtime / 'world-updates'
    target.mkdir(parents=True, exist_ok=True)
    data = load(chain)
    for step in data['Steps']:
        if sha(chain / step['File']) != step['Sha256']:
            raise SystemExit(f'{step["File"]} does not match {INDEX}')
        shutil.copy2(chain / step['File'], target / step['File'])
    shutil.copy2(chain / INDEX, target / INDEX)


def applied_versions(con: sqlite3.Connection) -> set[str]:
    """The steps a world has had, from its offline_patch_log (none when the table is missing)."""
    if not con.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name='offline_patch_log'").fetchone():
        return set()
    return {row[0] for row in con.execute('SELECT Version FROM offline_patch_log')}


def check(chain: Path, starts: list[tuple[str, Path]], target_db: Path) -> dict:
    """The whole chain applied to each start must give the target world, re-rolled monster fields aside."""
    steps = [(s['Version'], json.loads((chain / s['File']).read_text(encoding='utf-8'))) for s in load(chain)['Steps']]
    result = {}
    for name, db in starts:
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as tmp:
            work = Path(tmp) / 'world.db'
            shutil.copy2(db, work)
            with sqlite3.connect(work) as con:
                # As the launcher does (WorldUpdates.cs): a step the world already has (offline_patch_log) is skipped.
                # Replaying old steps on a newer world flipped rows later releases had changed back (setup review
                # 2026-11-06: "Mob changed 28" even from DR-dev-0.1b).
                done = applied_versions(con)
                for version, step in steps:
                    if version not in done:
                        make_patch.apply_world_update(con, step)
            diff = make_patch.world_update(work, target_db, policy(), 'check')
        wrong = [t['Table'] for t in diff['NewTables']] + [f'{c["Table"]}.{c["Column"]}' for c in diff['NewColumns']]
        rerolled = 0
        for t in diff['Tables']:
            noise = REROLLED.get(t['Table'], set())
            if t['Insert'] or t['Delete'] or any(set(row.get('Set', t['Columns'])) - noise for row in t['Update']):
                wrong.append(f'{t["Table"]} {diff["Summary"][t["Table"]]}')
            else:
                rerolled += len(t['Update'])
        if wrong:
            raise SystemExit(f'the chain from {name} does not reach the target world: {wrong}')
        result[name] = {'rerolled_monsters_differ': rerolled}
    return result


def pairs(values: list[str]) -> list[tuple[str, Path]]:
    out = []
    for value in values:
        name, _, path = value.partition('=')
        if not path or not Path(path).is_file():
            raise SystemExit(f'expected <name>=<database>, got {value}')
        out.append((name, Path(path).resolve()))
    return out


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('command', choices=('seed', 'check'))
    parser.add_argument('--chain', type=Path, default=CHAIN)
    parser.add_argument('--world', action='append', default=[], help='seed: <release version>=<its clean world>, oldest first')
    parser.add_argument('--start', action='append', default=[], help='check: <name>=<world to bring up to date>')
    parser.add_argument('--target', type=Path, help='check: the newest release\'s clean world')
    args = parser.parse_args()
    if args.command == 'seed':
        print(json.dumps(seed(args.chain.resolve(), pairs(args.world)), indent=1))
    else:
        if not args.target:
            raise SystemExit('check needs --target')
        print(json.dumps(check(args.chain.resolve(), pairs(args.start), args.target.resolve()), indent=1))
