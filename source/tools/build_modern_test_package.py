"""Build the public modern-test package: this fork's game for the original 1.130 client, without any retail file.

    python build_modern_test_package.py --install <artifacts/modern-client-test> --pristine <pristine 1.130 client> --classic <classic client>
                                        --repo <this repository> --out <new package folder>
                                        --label "<version> modern test" [--zip]

The install (the developer's modern test, only read) gives the server folder and the world. The package gets:
  * runtime/: the install's server folder (personal, diagnostic and run-state files left out, as build_release_037.py
    does), this repository's Release GameServer and launcher builds, and a clean world (build_release_037.clean_world:
    no account, character, bot or save; default keeps, doors and relics; zero-bot start, GM off, 1x XP, automatic
    account creation). The modern switches (native class slots, modern races, the test classes) stay as installed.
    The world ships as data/world-clean.sqlite3.db, never as data/opendaoc.sqlite3.db: the launcher makes the played
    world from it on first start, so copying a newer release over an older folder can't overwrite anyone's saves.
  * runtime/world-updates: one world update per release (modern_world_updates.py). This release's step, from the
    previous release's clean world (found in release-baselines/modern beside the repository) to this one, is added to
    the repository's chain (source/tools/release-baselines/modern/world-updates; commit it with the release), and this
    clean world is kept there for the next release. The launcher applies the steps a world hasn't had, so a world from
    any earlier release gets this release's fixes and keeps its saves. The version is the label's first word: give
    every published build a new one (building the same label again replaces its step).
  * runtime/client-patch.json (make_client_patch.py, from the pristine client) and modern-client.json: on first start
    the launcher copies the player's own 1.130 client into runtime/client-modern/app and patches the copy.
  * tools/connect-local1130.exe (upstream's connect.exe with its 1.130 edit), tools/dotnet (the .NET runtime), the
    START cmd, READ ME FIRST, the licences and docs.
  * RELEASE.txt, the version stamp the launcher's update check reads (release_stamp), and PACKAGE MANIFEST.sha256, the
    SHA-256 of every file, which the launcher's updater checks a downloaded package against. Name the package folder
    DividedRealms-<version>-<name> (the zip takes its name) so the updater finds it on the release page.
No retail client file ships (checked), and nothing is started.

Build the server and launcher first without the developer's paths in the binaries (the package check refuses them):
    dotnet build "source/server/Dawn of Light.sln" -c Release --no-incremental -p:PathMap=<repo>=X:\\OfflineDAoC
        -p:Deterministic=true -p:EnableSourceLink=false -p:EnableSourceControlManagerQueries=false
    (and the same for source/tools/OfflineDaoc.Launcher), then python source/tools/world_editor/build_package.py.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import socket
import sqlite3
import subprocess
from datetime import datetime, timezone
from pathlib import Path

import build_release_037 as release
import make_client_patch
import modern_world_updates
import remove_sluaghbinder

PORT = 10330
CONNECTOR_SHA256 = '18ab2f8a95be682e0f6acbbb6c7332e9ef05f1586872626f63a3122782f6ba70'
CONNECTOR = 'connect-local1130.exe'
# Runtime files of the developer's install that the package writes itself or never ships.
# world-workshop-data holds the developer's editor backups (database snapshots): never shipped. The editor itself
# (world-workshop) is installed from the repository's clean package build instead of the install's copy.
# server-backups holds deploy backups (developer builds), herald the launcher's Herald page written from this game's records.
# world-updates is written from the repository's chain (the install may have an older one).
RUNTIME_SKIP_DIRS = {'client-modern', 'world-workshop', 'world-workshop-data', 'server-backups', 'herald', 'world-updates',
                     '.dr'}  # .dr: the private DevKit's server supervisor state (lock, console log)
WORLD_WORKSHOP = 'artifacts/world-workshop-package/WorldWorkshop'
DARKNESS_FALLS_PROOFS = Path(__file__).resolve().parents[1] / 'server/GameServer/bots/autonomous/data/darkness_falls_navigation_proofs.json'
# host-for-friends.json and the firewall script are the developer's Host for Friends state (friends' addresses, local paths).
RUNTIME_SKIP_NAMES = {'modern-client.json', 'modern-profile-verification.json', 'modern-client-setup-error.txt', 'client-patch.json',
                      'host-for-friends.json', 'host-for-friends-firewall.cmd', 'serverconfig.before-host-for-friends.xml',
                      # The developer's overlay choices: shipped, they would switch the fight card and panels off for everyone.
                      'overlay.json',
                      # The launcher's Bot AI diagnostics switch: a diagnostics file, which check_package refuses.
                      'bot-diagnostics.on',
                      # The private DevKit's deploy record (INSTALLED.json): names this PC's folders and builds.
                      'INSTALLED.json'}
LAUNCHER_FILES = ('OfflineDAoC.dll', 'OfflineDAoC.exe', 'OfflineDAoC.pdb', 'OfflineDAoC.deps.json', 'OfflineDAoC.runtimeconfig.json',
                  'Join Friend.cmd')
TEXT_SUFFIXES = {'.json', '.xml', '.txt', '.md', '.cmd', '.config', '.ini', '.csv'}
# Items whose look needs private client art the client patch can't add yet: the developer's world sells them, the
# package's world doesn't. mount_67 rides mount 89, the saddled Chimera (tools/horses/install_chimera_mount.py adds it
# to gamedata.mpk and figures/, neither of which client-patch.json patches).
WITHHELD_MERCHANT_ITEMS = ('mount_67',)
# Models only the developer's client has, for the same reason, mapped to the stock model the package's world uses instead,
# and the settings that would swap them in. Empty since the fishing rod (objects 4927 and 4928) is in the client patch
# (make_client_patch.py rod_entries); a new private model goes here until the patch adds it.
PRIVATE_MODELS: dict[int, int] = {}
STOCK_SERVER_PROPERTIES: dict[str, str] = {}


def use_stock_models(con, models: dict | None = None, properties: dict | None = None) -> dict:
    """Puts the stock look back on everything that names a private model, and the settings that would swap it in again."""
    templates = sum(con.execute('UPDATE ItemTemplate SET Model = ? WHERE Model = ?', (stock, private)).rowcount
                    for private, stock in (PRIVATE_MODELS if models is None else models).items())
    changed = sum(con.execute('UPDATE ServerProperty SET Value = ? WHERE `Key` = ? AND Value <> ?', (value, key, value)).rowcount
                     for key, value in (STOCK_SERVER_PROPERTIES if properties is None else properties).items())
    return dict(item_templates=templates, server_properties=changed)

def private_models_without_art(con, pristine: Path, patch: dict) -> list:
    """(item, model) for every item drawn with an object above the stock objects.csv's last ID that the client patch doesn't
    add. DR-dev-0.1d shipped the Storm Regalia on objects 4929-4942, which only the developer's client has."""
    import make_client_patch as m
    stock = {m.row_id(line) for line in m.member_of((pristine / m.GAMEDATA).read_bytes(), 'objects.csv').decode('latin1').split('\r\n')}
    stock.discard(None)
    game = next(f for f in patch['Files'] if f['Path'] == m.GAMEDATA)
    added = {m.row_id(row) for part in game['Members'] if part['Member'] == 'objects.csv' for row in part['Rows']}
    return models_without_art(con, max(stock), added)


def models_without_art(con, last_stock_object: int, added: set) -> list:
    return [(item, model) for item, model in con.execute('SELECT Id_nb, Model FROM ItemTemplate WHERE Model > ? ORDER BY Id_nb',
                                                          (last_stock_object,))
            if model not in added and model not in LATER_LIVE_OBJECTS]


# Live objects newer than the pristine copy the patch is built from: the 25th-anniversary cloaks (live patch 2026-10-26).
# A current live client draws them; they are not private art.
LATER_LIVE_OBJECTS = {4924, 4925, 4926}


START_CMD = r'''@echo off
setlocal
cd /d "%~dp0"
rem Divided Realms {label} (the original 1.130 client). Uses the .NET runtime bundled in tools\dotnet, so nothing has to
rem be installed except the Windows .NET Framework 3.5 feature (for the client's connector).
if not exist "%~dp0runtime\OfflineDAoC.exe" (
  echo This folder is incomplete. Extract the whole download again, then retry.
  pause
  exit /b 1
)
set "DOTNET_ROOT=%~dp0tools\dotnet"
set "DOTNET_ROOT_X64=%~dp0tools\dotnet"
set "DOTNET_MULTILEVEL_LOOKUP=0"
start "" "%~dp0runtime\OfflineDAoC.exe"
'''

# Join a friend's world without the launcher's dashboard or this folder's server (docs/PLAY-WITH-FRIENDS.md).
JOIN_CMD = r'''@echo off
setlocal
cd /d "%~dp0"
rem Divided Realms {label}: play in a friend's world. Starts only the game client, never this folder's server or save.
if not exist "%~dp0runtime\OfflineDAoC.exe" (
  echo This folder is incomplete. Extract the whole download again, then retry.
  pause
  exit /b 1
)
set "DOTNET_ROOT=%~dp0tools\dotnet"
set "DOTNET_ROOT_X64=%~dp0tools\dotnet"
set "DOTNET_MULTILEVEL_LOOKUP=0"
start "" "%~dp0runtime\OfflineDAoC.exe" --join
'''

READ_ME = '''Divided Realms {label}
{rule}

A single-player Dark Age of Camelot server with gamebots, for the ORIGINAL retail 1.130 client.
This download contains no game client: you use your own.

1. You need Dark Age of Camelot installed (the 1.130 client), and the Windows feature
   ".NET Framework 3.5" turned on (Control Panel > Programs > Turn Windows features on or off).
2. Extract this whole folder somewhere with about 7 GB free. Keep the tools folder: tools\\dotnet is
   the .NET 10 the server runs on. Without it you need Microsoft's "ASP.NET Core Runtime 10"
   Windows Hosting Bundle installed (https://dotnet.microsoft.com/download/dotnet/10.0).
3. Double-click START DIVIDED REALMS.cmd.
4. On first start the launcher asks for your Dark Age of Camelot folder (the one with game.dll
   and camelot.exe). It copies it into this folder and patches the copy. Your own install is
   not changed. It only accepts the 1.130 client this game is made for.
5. START SERVER, then ENTER REALM. Your account is created on the first login.

Playing with friends: one of you hosts (HOST FOR FRIENDS in the launcher), the others double-click
JOIN FRIEND.cmd (set up your client once with step 4 first). See docs/PLAY-WITH-FRIENDS.md.

Updating from an earlier test build: close the launcher, extract the new download over this
folder and choose to replace every file. Your world and characters are kept: the launcher adds
the new build's world fixes to your world (after a backup in runtime\\backups) and patches your
client copy again (it may ask for your Dark Age of Camelot folder once).
From this build on, the launcher can also do this for you: it looks for a newer release once a
day (Options > Update and repair > CHECK FOR UPDATES to look now; you can turn the daily check
off there) and installs it with your saves and settings kept. See docs/PLAY-MODERN-TEST.md.

This is a development test build. See docs/PLAY-MODERN-TEST.md and docs/PROGRESS-2026-10.md.
'''


def sha(path: Path) -> str:
    return release.sha(path)


# The package's version stamp, at the top of the package beside EDITION.txt (and listed in PACKAGE MANIFEST.sha256). The
# launcher's update check (source/tools/OfflineDaoc.Launcher/Updates) reads Version to know which release a folder is,
# and only offers updates to a folder that has it (or an older package's EDITION.txt) and the manifest.
RELEASE_STAMP = 'RELEASE.txt'
REPOSITORY = 'rambodolphin13/DividedRealms'


def release_stamp(label: str, package: str, commit: str = '') -> str:
    """Key=value lines: Version is the label's first word (the release tag, e.g. DR-dev-0.1d), Package the top folder and
    zip name (without .zip)."""
    lines = [f'Version={label.split()[0]}', f'Label={label}', f'Package={package}', f'Repository={REPOSITORY}']
    if commit:
        lines.append(f'Commit={commit}')
    lines.append(f'BuiltUtc={datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")}')
    return '\r\n'.join(lines) + '\r\n'


def keep_runtime(relative: Path) -> bool:
    if relative.parts[0] in RUNTIME_SKIP_DIRS or relative.name in RUNTIME_SKIP_NAMES:
        return False
    # Any other client copy (client-modern-armortest held a whole retail client) and the developer's own scripts for it.
    if relative.parts[0].lower().startswith('client-') or 'test client' in relative.name.lower():
        return False
    return release.include(relative)


def latest_dotnet(dotnet: Path) -> str:
    """The newest .NET 10 present for all four bundled parts."""
    parts = ['host/fxr', 'shared/Microsoft.NETCore.App', 'shared/Microsoft.WindowsDesktop.App', 'shared/Microsoft.AspNetCore.App']
    common = set.intersection(*({p.name for p in (dotnet / part).iterdir() if p.name.startswith('10.')} for part in parts))
    if not common:
        raise SystemExit('No .NET 10 runtime with all four parts is installed')
    return max(common, key=lambda v: tuple(int(x) for x in v.split('.')))


def server_config(config: Path):
    """The package's world, relative to the server folder (as the classic packages), automatic account creation, and
    Host for Friends off (loopback, no UPnP, no PublicIP) whatever the developer's install has switched on."""
    text = config.read_text(encoding='utf-8-sig')
    text, count = re.subn(r'Data Source=[^;<]*', 'Data Source=../data/opendaoc.sqlite3.db', text)
    assert count == 1, 'serverconfig.xml has no single Data Source'
    for tag, value in (('IP', '127.0.0.1'), ('UdpIP', '127.0.0.1'), ('EnableUPnP', 'False')):
        text = re.sub(rf'<{tag}>[^<]*</{tag}>', f'<{tag}>{value}</{tag}>', text)
    text = re.sub(r'[ \t]*<PublicIP>[^<]*</PublicIP>\r?\n?', '', text)
    config.write_text(text, encoding='utf-8')
    release.set_auto_account_creation(config)
    assert f'<Port>{PORT}</Port>' in text and '<IP>127.0.0.1</IP>' in text, 'the modern server must listen on loopback 10330'


def PATH_NEEDLES(user):
    """The user name as a path folder (backslash or slash on both sides), in UTF-8 and UTF-16: not the bare word, which also turns
    up inside unrelated data (a compression dictionary holds "buenatexto")."""
    forms = [f'{sep}{user}{sep}' for sep in ('\\', '/')]
    return [f.encode(enc) for f in forms for enc in ('utf-8', 'utf-16-le')]


def check_package(out: Path, pristine: Path):
    """Nothing personal, diagnostic or retail ships."""
    runtime = out / 'runtime'
    bad = [p.relative_to(out).as_posix() for p in out.rglob('*') if p.is_file() and (
        p.name.lower() in release.FORBIDDEN_NAMES or p.name.lower().endswith(release.FORBIDDEN_SUFFIXES))]
    bad = [b for b in bad if not b.startswith('tools/dotnet/')]
    assert not bad, f'files that must not ship: {bad[:20]}'
    assert not (runtime / 'client-modern').exists(), 'a client folder would ship'
    for name in ('server/GameServer.dll', 'server/CoreServer.exe', 'server/navmesh/roads.json', 'data/world-clean.sqlite3.db',
                 'world-updates/world-updates.json', 'OfflineDAoC.exe', 'client-patch.json', 'modern-client.json'):
        assert (runtime / name).is_file(), f'required file missing: {name}'
    assert not (runtime / 'data/opendaoc.sqlite3.db').exists(), 'a played-world file would ship and overwrite saves when copied over'
    # Any other mesh shuts Darkness Falls to bots at the first stairs (issue #3): the certificate holds only for its own.
    proofs = json.loads(DARKNESS_FALLS_PROOFS.read_text(encoding='utf-8'))
    assert sha(runtime / 'server/navmesh/zone249.nav').lower() == proofs['NavSha256'].lower(), 'zone249.nav is not the certified Darkness Falls mesh'
    retail = {sha(pristine / name) for name in ('game.dll', 'camelot.exe', 'pregame/character_creation.xml')}
    shipped = [p for p in out.rglob('*') if p.is_file() and p.stat().st_size in {(pristine / n).stat().st_size for n in
               ('game.dll', 'camelot.exe', 'pregame/character_creation.xml')}]
    assert not [p for p in shipped if sha(p) in retail], 'a retail client file would ship'
    personal = re.compile(r'C:\\Users\\|C:/Users/|natex', re.I)
    leaks = [p.relative_to(out).as_posix() for p in out.rglob('*') if p.is_file() and p.suffix.lower() in TEXT_SUFFIXES
             and p.stat().st_size < 8 << 20 and not p.relative_to(out).as_posix().startswith('tools/dotnet/')
             and personal.search(p.read_text(encoding='utf-8', errors='ignore'))]
    assert not leaks, f'files naming the developer\'s machine: {leaks[:20]}'
    # Binaries too (a developer build embeds its PDB and source paths): the user name must not appear in them.
    user = Path.home().name
    binary = [p.relative_to(out).as_posix() for p in out.rglob('*') if p.is_file() and p.stat().st_size < 64 << 20
              and p.suffix.lower() in ('.dll', '.exe', '.pdb', '.json', '.db')
              and not p.relative_to(out).as_posix().startswith('tools/dotnet/')
              and any(n in p.read_bytes() for n in PATH_NEEDLES(user))]
    assert not binary, f'binaries naming the developer: {binary[:20]}'


def build(install: Path, pristine: Path, classic: Path, repo: Path, out: Path, label: str) -> dict:
    if out.exists():
        raise SystemExit(f'{out} exists; refusing to overwrite a package folder')
    with socket.socket() as probe:
        if probe.connect_ex(('127.0.0.1', PORT)) == 0:
            raise SystemExit('Close the modern test server first')
    server_build = repo / 'source/server/Release/lib'
    launcher_build = repo / 'source/tools/OfflineDaoc.Launcher/bin/Release/net10.0-windows'
    for path in [server_build / 'GameServer.dll'] + [launcher_build / n for n in LAUNCHER_FILES]:
        if not path.is_file():
            raise SystemExit(f'Build the Release server and launcher first: {path} is missing')
    if not (repo / WORLD_WORKSHOP / 'WorldWorkshop.exe').is_file():
        raise SystemExit('Build World Workshop first: python source/tools/world_editor/build_package.py')
    connector = install / 'tools' / CONNECTOR
    if sha(connector) != CONNECTOR_SHA256:
        raise SystemExit('The install\'s 1.130 connector is not the expected one')
    chain = repo / 'source/tools/release-baselines/modern/world-updates'
    release_version = label.split()[0]
    previous = modern_world_updates.previous_release(chain, release_version)
    previous_world = modern_world_updates.baseline(previous)
    installed_profile = json.loads((install / 'runtime/modern-client.json').read_text(encoding='utf-8-sig'))
    policy = json.loads((repo / 'source/tools/OfflineDaoc.ProgressImport/progress-policy.json').read_text())
    defaults = json.loads((repo / 'source/tools/release-world-defaults.json').read_text(encoding='utf-8'))

    print('Making the client patch from the pristine client...', flush=True)
    patch = make_client_patch.build(pristine, classic)
    game = next(f for f in patch['Files'] if f['Path'] == 'game.dll')
    if game['OutputSha256'].lower() != installed_profile['GameSha256'].lower():
        raise SystemExit('The client patch does not rebuild the client the test install runs')

    print('Copying the runtime (personal, diagnostic, run-state and client files left out)...', flush=True)
    runtime = out / 'runtime'
    copied = release.copy_tree(install / 'runtime', runtime, keep_runtime)

    print('Building the clean world...', flush=True)
    database = runtime / 'data/opendaoc.sqlite3.db'
    database.parent.mkdir(parents=True, exist_ok=True)
    with sqlite3.connect((install / 'runtime/data/opendaoc.sqlite3.db').as_uri() + '?mode=ro', uri=True) as src, \
            sqlite3.connect(database) as dst:
        src.backup(dst)
    src.close()
    dst.close()
    world = release.clean_world(database, policy, defaults)
    with sqlite3.connect(database) as con:
        # clean_world switches upstream's Sluaghbinder on; this fork has retired it (build_custom_release_037.py).
        release.set_edition(con, False)
        sluaghbinder = remove_sluaghbinder.apply(con)
        withheld = {item: con.execute('DELETE FROM MerchantItem WHERE ItemTemplateID = ?', (item,)).rowcount
                    for item in WITHHELD_MERCHANT_ITEMS}
        stock_models = use_stock_models(con)
        unknown = private_models_without_art(con, pristine, patch)
        if unknown:
            raise SystemExit('Items name private models the client patch does not add (map them in PRIVATE_MODELS or add the '
                             f'art to make_client_patch.py): {unknown[:20]}')
        con.commit()
        world = release.verify_clean(con, policy, defaults)
    con.close()     # the world file is renamed below
    world_data = release.check_world_data(database, repo)

    print("Adding this release's world update...", flush=True)
    world_update = modern_world_updates.add_step(chain, release_version, previous, previous_world, database)
    modern_world_updates.stamp(database, chain)
    modern_world_updates.install(chain, runtime)
    database.rename(runtime / 'data' / modern_world_updates.CLEAN_WORLD)

    server_config(runtime / 'server/config/serverconfig.xml')

    print('Installing the Release server and launcher builds...', flush=True)
    replaced = {}
    # Every server assembly this repository builds, not only GameServer: the install's older Core* copies were built
    # with the developer's paths (0.38-dev2 shipped them). Build with PathMap and SourceLink off (see the docstring).
    for folder in ('server', 'server/lib', 'server/win-x64'):
        for name in [f'{a}.{e}' for a in ('CoreBase', 'CoreDatabase', 'CoreServer', 'GameServer') for e in ('dll', 'pdb')]:
            target = runtime / folder / name
            source = server_build / name if (server_build / name).is_file() else server_build.parent / name
            if target.exists() and source.is_file():
                shutil.copy2(source, target)
                replaced[f'{folder}/{name}'] = sha(target)
    for name in LAUNCHER_FILES:
        shutil.copy2(launcher_build / name, runtime / name)
        replaced[name] = sha(runtime / name)

    # World Workshop for the launcher's WORLD EDITOR button (runtime/world-workshop; it keeps its state beside it).
    shutil.copytree(repo / WORLD_WORKSHOP, runtime / 'world-workshop')
    replaced['world-workshop/WorldWorkshop.exe'] = sha(runtime / 'world-workshop/WorldWorkshop.exe')

    (runtime / 'client-patch.json').write_text(json.dumps(patch, indent=1), encoding='utf-8')
    profile = dict(ClientDirectory='client-modern/app', ConnectorPath='..\\tools\\' + CONNECTOR,
                   GameSha256=game['OutputSha256'].upper(), ConnectorSha256=CONNECTOR_SHA256.upper(), Port=PORT)
    (runtime / 'modern-client.json').write_text(json.dumps(profile, indent=2), encoding='utf-8')
    (out / 'tools').mkdir(parents=True, exist_ok=True)
    shutil.copy2(connector, out / 'tools' / CONNECTOR)

    print('Bundling the .NET runtime...', flush=True)
    dotnet = Path(os.environ.get('ProgramFiles', r'C:\Program Files')) / 'dotnet'
    version = latest_dotnet(dotnet)
    for sub in (f'host/fxr/{version}', f'shared/Microsoft.NETCore.App/{version}', f'shared/Microsoft.WindowsDesktop.App/{version}',
                f'shared/Microsoft.AspNetCore.App/{version}'):
        release.copy_tree(dotnet / sub, out / 'tools/dotnet' / sub)
    for name in ('dotnet.exe', 'LICENSE.txt', 'ThirdPartyNotices.txt'):
        if (dotnet / name).exists():
            shutil.copy2(dotnet / name, out / 'tools/dotnet' / name)

    print('Writing the start file, read-me, licences and docs...', flush=True)
    (out / 'START DIVIDED REALMS.cmd').write_text(START_CMD.format(label=label).replace('\n', '\r\n'), encoding='ascii')
    (out / 'JOIN FRIEND.cmd').write_text(JOIN_CMD.format(label=label).replace('\n', '\r\n'), encoding='ascii')
    title = f'Divided Realms {label}'
    (out / 'READ ME FIRST.txt').write_text(READ_ME.format(label=label, rule='=' * len(title)).replace('\n', '\r\n'), encoding='utf-8')
    (out / 'EDITION.txt').write_text(f'{title}: the original 1.130 client (bring your own), modern test classes.\r\n', encoding='utf-8')
    commit = subprocess.run(['git', '-C', str(repo), 'rev-parse', 'HEAD'], capture_output=True, text=True).stdout.strip()
    (out / RELEASE_STAMP).write_text(release_stamp(label, out.name, commit), encoding='utf-8')
    if not out.name.startswith(f'DividedRealms-{release_version}'):
        print(f'WARNING: the launcher looks for DividedRealms-{release_version}-<name>.zip on the release page; '
              f'this package is {out.name}.zip, so the launcher will not offer it as an update.', flush=True)
    for name in ('LICENSE', 'THIRD_PARTY.md'):
        shutil.copy2(repo / name, out / name)
    # Only the docs the repository tracks: the developer's local hand-off and research notes never ship.
    tracked = set(subprocess.run(['git', '-C', str(repo), 'ls-files', 'docs'], capture_output=True, text=True, check=True).stdout.split())
    release.copy_tree(repo / 'docs', out / 'docs', lambda rel: rel.parts[0] != 'history' and rel.suffix.lower() == '.md'
                      and ('docs/' + rel.as_posix()) in tracked)

    check_package(out, pristine)
    modern_world_updates.keep_baseline(release_version, runtime / 'data' / modern_world_updates.CLEAN_WORLD)
    files = {p.relative_to(out).as_posix(): sha(p) for p in sorted(out.rglob('*')) if p.is_file()}
    (out / 'PACKAGE MANIFEST.sha256').write_text(''.join(f'{h}  {n}\n' for n, h in files.items()), encoding='utf-8')
    report = dict(built_utc=datetime.now(timezone.utc).isoformat(), label=label, runtime_files_copied=len(copied),
                  world=world, world_data=world_data, world_update={f'{previous} -> {release_version}': world_update}, sluaghbinder=sluaghbinder, withheld_merchant_items=withheld, stock_models=stock_models, replaced=replaced, dotnet=version,
                  client_patch={f['Path']: [f['InputSha256'], f['OutputSha256']] for f in patch['Files']}, files=len(files))
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--install', type=Path, required=True)
    parser.add_argument('--pristine', type=Path, required=True)
    parser.add_argument('--classic', type=Path, required=True, help='classic client folder with zones/zone276 (only read)')
    parser.add_argument('--repo', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--label', default='0.38-dev modern test')
    parser.add_argument('--zip', action='store_true')
    args = parser.parse_args()
    out = args.out.resolve()
    report = build(args.install.resolve(), args.pristine.resolve(), args.classic.resolve(), args.repo.resolve(), out, args.label)
    (out.parent / (out.name + '-build-report.json')).write_text(json.dumps(report, indent=2), encoding='utf-8')
    if args.zip:
        print('Zipping...', flush=True)
        archive = shutil.make_archive(str(out), 'zip', out.parent, out.name)
        report['zip'] = dict(path=archive, sha256=sha(Path(archive)), bytes=Path(archive).stat().st_size)
        (out.parent / (out.name + '-build-report.json')).write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k not in ('replaced',)}, indent=2)[:3000])
