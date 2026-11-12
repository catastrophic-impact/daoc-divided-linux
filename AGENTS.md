# Divided Realms (Offline DAoC): instructions for AI assistants and developers

Read this first, then [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md). This file is also loaded by
Claude Code as `CLAUDE.md`.

## What this is

**Divided Realms** is a single-player Dark Age of Camelot server (OpenDAoC fork, .NET 10, SQLite) for the
**original retail 1.130 client**, with autonomous gamebots, companion bots and a Windows launcher. Friends can
join a hosted game. The public repository is `rambodolphin13/DividedRealms`.
- **Releases** are development test builds tagged `DR-dev-0.1`, `DR-dev-0.1a`, `DR-dev-0.1b`, ... (see the
  README for the latest). `main` is the last published release.
- **Integration branch:** `feature/bot-ai-trips`. Work goes on a `fix/...` or `feature/...` branch (usually its
  own worktree under `.claude/worktrees/`), is merged into `feature/bot-ai-trips` and deployed from there.
- **Classes:** the live 1.130 classes plus Death Knight, Bainshee, Vampiir, Heretic, Valkyrie, Warlock, Mauler and
  the Storm, Pendragon and Tuatha Sovereigns (Stormforged is class 64). All 21 races.
- The older **Offline DAoC 0.3x** line (classic client, the Godot new client, the Sluaghbinder) is legacy: its
  tools (`*_036.py`, `*_037.py`, `make_patch.py`, `OfflineDaoc.Patcher`) and notes stay for reference only.

## Layout

| Path | What it is |
|---|---|
| `source/server` | Game server, all bot AI, tests (`Tests/`). The solution is `Dawn of Light.sln`. |
| `source/server/GameServer/bots` | Companion bots, class AI, bot brains |
| `source/server/GameServer/bots/autonomous` | Autonomous gamebots: goals, camps, groups, travel, watchdog |
| `source/tools/OfflineDaoc.Launcher` | Launcher (the in-game version label is `MainForm.DisplayVersion`) |
| `source/tools/world_editor` | **World Workshop**: the browser world editor and `cli.py` (guarded, reversible world fixes). `pending-fixes/` holds edit files. |
| `source/tools/build_modern_test_package.py` | Builds a Divided Realms release package (see its docstring) |
| `source/tools/modern_world_updates.py` | The per-release world update steps; the chain is in `source/tools/release-baselines/modern/world-updates/` |
| `source/tools/make_client_patch.py`, `source/tools/classslots/patch_class_slots.py` | The 1.130 client patch the launcher applies to a copy of the player's client, and the class-slot `game.dll` patch |
| `tools/sovereign-art`, `tools/pet-art` | Client art pipelines (Sovereign Regalia kits, pet models and spell effects). Each install step has a rollback. |
| `docs/` | Player and developer guides; `docs/history/` holds earlier versions' notes |

The developer's **modern test install** is `artifacts/modern-client-test/runtime` (ignored by Git; server on
TCP 10330, UDP 10430). Its world is `runtime/data/opendaoc.sqlite3.db`, the server is in `runtime/server`,
deploy backups are in `runtime/backups/`, and World Workshop snapshots are in `runtime/world-workshop-data/backups/`.

## How changes reach the test install

- **Server code:** build `Dawn of Light.sln` in Release from a *committed* tree (the DLL's ProductVersion carries
  the commit, so build after committing), run the tests, then with the server stopped copy `GameServer.dll` and
  `.pdb` into `runtime/server`, `runtime/server/lib` and `runtime/server/win-x64`. First copy the old ones to
  `runtime/backups/gameserver-<what>-<yyyyMMdd-HHmmss>/` with a `README.txt`: what was replaced (checksum and
  commit), what was installed, the world undo ids, and how to roll back.
- **World data:** never edit the database by hand. Write an edits file in
  `source/tools/world_editor/pending-fixes/`, then
  `python source/tools/world_editor/cli.py --game <install> plan <edits.json>` (changes nothing) and, with the
  owner's OK, `apply <patch.json>`. Every apply prints an undo id: record it in the deploy README.
  `undo <id>` reverses it (newest first).
- **Releases:** `build_modern_test_package.py` adds the release's world step to the chain; commit it with the
  release. Fixes applied to the test world reach players only through that step.
- **Local-only files:** `.git/info/exclude` keeps the daoc.gg class data (`source/tools/*_data.py`) and some research
  docs out of Git by the owner's rule (2026-10-25). Never commit them. Tests that need them skip without them.
- **CI** (`.github/workflows/build-and-test.yml`) runs on pushes to any branch except `main`: the server build, the
  server tests minus `.github/ci-excluded-tests.txt` (they need installed game files), the launcher tests and
  the Python tool tests.

## Rules

- **Protect player data.**
  - Never commit or publish a played database, `account.txt`, logs, bot rosters, backups or
    credentials.
  - Public packages are built only by `build_modern_test_package.py` (legacy: `build_release_0xx.py`),
    which ships a clean world (no account, character, bot or save; default keeps, doors and relics)
    and checks that no retail client file or developer path is in the package. Automatic account
    creation is on.
  - Never ship DXVK (`d3d9.dll`, `dxvk.conf`), diagnostics switches or the new client's
    `dev_mode.txt`.
- **Keep the owner's gameplay rules.** Preserve these unless the owner asks otherwise:
  - real loot, inventories, coins and equipment upgrades
  - the Realm Exchange
  - stablemaster routes
  - selling only at natural task breaks
  - gamebots start without armor and earn it
  - Darkness Falls is open to solo bots
  - the Sovereign / Stormforged classes are meant to be overpowered: fix correctness, never suggest nerfs
  - no Champion Level requirement or CL-unlocked bonus on items (also on future item imports)
- **Tell bot types apart.** Real players, companion bots and autonomous gamebots are different.
  Check which one a change affects.
- **Don't start or deploy without permission.**
  - Never start a server or copy builds over a running install without asking.
  - Only one server can use a port at a time (classic installs 10300, the modern test 10330).
- **Build and test separately.** Deploy only tested outputs, with a backup of what you replace:
  `GameServer.dll` goes to `runtime/server`, `runtime/server/lib` and `runtime/server/win-x64`.
- **Client files:**
  - Keep hash guards on any binary patch; never patch an unverified `game.dll`.
  - Private art is added under new IDs; stock models, skins and effects are never edited in place.
  - `tools/pet-art` shows the pattern: byte copies of NIFs with same-length texture renames, and
    new catalog rows.
- **Navigation:** don't rebuild or replace navmeshes globally to fix one route without evidence.
- **Be honest in reports.** Report automated tests separately from real in-game checks. Don't
  describe Darkness Falls raid AI, Legion or the hardest level 70+ encounters as implemented.
  - A feature counts as working only once it has been seen working in one of the owner's PC logs
    (owner, 2026-11-08). Until then the notes say "tests only". Unit tests, CI, simulations and test-world
    bot runs are all tests.
  - Never give an earlier build's result as the result of the build being shipped. Say which build each
    result came from.
- **Keep the old releases.** v0.3–v0.36c stay available as legacy releases, and their branches
  and tags are not rewritten.

## Useful facts

- **Edition switch:**
  - This fork has retired the Sluaghbinder (class 63): its server code is gone and
    `source/tools/remove_sluaghbinder.py` drops its world rows (`install_hybrid_lines.py` and
    `build_custom_release_037.py` run it). Character creation refuses class 63 and the Hibernian
    Acolyte novice.
  - The database row `classes / enable_sluaghbinder` stays `False`: the launcher and progress
    importer still read it to name the edition, and the importer refuses saves with Sluaghbinder
    characters.
- **Item model chain:** `objects.csv` model ID → `items.csv` row → `items/<nif>`.
  - Shields that carry an emblem take their base texture from a `pskins.csv` override.
  - `tools/pet-art/nif4_geom.py` reads the old 4.x item NIFs that pyffi can't.
- **Client art, spells and sounds:** read [docs/CLIENT-MODDING-GUIDE.txt](docs/CLIENT-MODDING-GUIDE.txt)
  before changing models, meshes, skins, spell effects, icons or NPC sounds. It lists the client's
  catalog rules (no blank lines, terminators, Expansion Only, ID order), confirmed and unconfirmed
  limits, every tool, and past mistakes.
- **Helmets with no face:** see [docs/HELMET-FACE-FIX.md](docs/HELMET-FACE-FIX.md) and
  `tools/claude-version/helmet_face_check.py`.
- **Legacy, two clients, one server (0.36c-0.37c):** the launcher switches to its new-client mode when
  `..\godot\project.godot` exists beside it (`MainForm.IsNewClientFolder`). Both launchers read
  the same `runtime/account.txt` (`PortableCredentials`), so either client logs in to the same
  account.
- **1.130 client:** the patched `game.dll` comes from `source/tools/classslots/patch_class_slots.py`; when it
  changes, update its hash in the install's `runtime/modern-client.json` too.
- **Server settings:** Offline-specific settings are in the database tables
  `offline_population_settings` and `offline_local_options`, and in `runtime/server/bot-goals.json`.

The owner may customize their fork's rules. These are safety defaults, not a ban on changes the
owner asks for.
