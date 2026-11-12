# daoc-divided-linux

A portable **Linux server build of [DividedRealms](https://github.com/rambodolphin13/DividedRealms)**: a free, offline
Dark Age of Camelot world with every modern class, Master and Champion Levels, mounts, and a world full of bots that level,
talk, raid and siege on their own. DividedRealms is a Windows package; this project turns its server into one self-contained
folder that runs on Linux (including SteamOS / Steam Deck), with a terminal admin screen to run it.

You play with the **original retail 1.130 client**. No game files are included in this project.

> **Status: development. Based on DividedRealms DR-dev-0.1g, "Catacombs and Crafting" (2026-11-11).** The server builds and runs on
> Linux and its ~8,200 unit tests pass (tests only). The six expansion classes (Vampiir, Bainshee, Heretic, Valkyrie, Warlock, Mauler)
> and the battleground bracket bots were played in-game on the stock client with the earlier DR-dev-0.1a build; nothing from 0.1b to
> 0.1g has been played in-game here yet. Everything else DividedRealms offers is included as it ships upstream and has not been
> separately tested here. See [Known gaps](#known-gaps).

## What is in this version

Everything in DividedRealms DR-dev-0.1g, plus the Linux layer:

- **From DividedRealms (see their [changelog](CHANGELOG.md)):** fishing (own rod model, camps, cooking, `/fish`); gamebots that do
  classic quests, craft, and trade crafted gear; guild merit and dues for bots you invite; faster power regeneration; bots with
  player-like armor; safer world-database saving with daily backups; relic carriers that keep the relic across regions; many bot AI
  and loot-table fixes.
- **Added here:** the terminal admin screen, portable Linux packaging, world updates on start, the client patch toggle and
  `launcher.bat`, and the [battleground bracket bots](#battleground-bracket-bots-new-in-this-project) with their gear kit, the
  "no players, no bots" rule and the load governor.

## What is different from DividedRealms on Windows

| | DividedRealms (Windows) | daoc-divided-linux |
|---|---|---|
| Server | Windows package, .NET | The same server code, built for Linux, **fully portable** (.NET bundled) |
| Control | Windows launcher (WinForms) | **Terminal admin screen** (`./daoc-admin`) and a command line, no GUI toolkit needed |
| Client | Launcher copies your install and patches the copy | **Client tab** switches two files in your client between patched and original; no copy |
| Starting the game | Launcher button | A generated **`launcher.bat`** that you run with your own Wine setup |
| Godot "new client", Passport Desk (bring a character to a friend's world), Host/Join Friend | Included | **Not included** (Windows launcher features, left out on purpose) |
| Updates | Launcher applies world updates in place | `daoc-server.sh start` applies them automatically, with a backup (see [Upgrading](#upgrading)) |

The game logic, bots, classes, world data and navmeshes are DividedRealms' own. We change as little of their code as possible
so their updates stay easy to merge (each edit is marked `daoc-divided-linux` in the source and listed in [UPSTREAM.md](UPSTREAM.md);
`tools/sync-upstream.sh` brings in a newer DividedRealms).

## Quick start

### 1. Build it (one script)

```bash
./build.sh
```

That is all you need. When it finishes, `dist/daoc-divided/` is a complete, ready-to-go server (world, navmeshes, admin screen,
everything) and you start it with one command (step 2). The script does the setup for you:

- **With distrobox installed** (preinstalled on SteamOS): it creates an Ubuntu 22.04 container named `divided-box` with its own
  home under `~/boxes/divided-box-home`, installs the build tools and the .NET 10 SDK inside it, and builds there. Your system
  stays untouched, and the result runs on any x86-64 Linux with glibc 2.34 or newer.
- **Without distrobox:** it builds on your system. It needs `cmake`, `g++`, `make`, `curl`, `python3` and `xz` (it tells you the
  install command for your distro if one is missing). If you have no .NET 10 SDK it installs a **private one inside this folder**
  (`.cache/dotnet`). Its caches stay in `.cache/` too, apart from a 205-byte default `~/.nuget/NuGet/NuGet.Config` that the .NET tools may create. Build artifacts never end up in your home.

Force one or the other with `./build.sh --box` or `./build.sh --local`. You need about 8 GB of free disk space and internet for
the first build. It downloads the world database, navmeshes and a few data files from the DividedRealms release (about 2.2 GB,
cached in `.cache/`, so later builds reuse it), plus a Python for the admin screen.

Output:

```
dist/daoc-divided/                          the portable server folder, ready to run
dist/daoc-divided-<version>-linux-x64.tar.gz   the same folder as one archive
```

### 2. Run it

```bash
cd dist/daoc-divided
./daoc-server.sh start      # starts the server and opens the admin screen
```

See [deploy/README-release.md](deploy/README-release.md) (shipped inside the folder as `README.md`) for running, the admin
screen, accounts and connecting.

## What "fully portable" means

- The server folder is **self-contained**: the .NET runtime, the native pathing library and a Python with the admin screen are all
  inside it. Nothing is installed on the system, and it does not need .NET.
- **Everything the server writes stays in that folder**: `config/`, `data/` (the world and all characters), `logs/`, `run/`. Nothing is
  written to `~/.config` or anywhere else in your home. Move or copy the folder and it keeps working.
- Config files live **next to the binary** (`config/`), not in your home.
- The build itself is contained too: the container has its own home, or the private SDK and caches live in `.cache/` (the .NET tools may still create a 205-byte default NuGet config in `~/.nuget`).
- The only time anything outside the server folder is touched is when *you* ask the Client tab to patch your client or write a
  `launcher.bat` into your client folder.

## The admin screen (TUI)

`./daoc-admin` opens a terminal screen (mouse works). It talks to the running server over a local socket (`run/admin.sock`); it
opens no network port. Tabs:

- **Server**: status, Start / Stop / Restart.
- **Bots**: list, create, delete. **Population**: bots on/off.
- **Accounts**: create accounts, set player / GM / admin.
- **Options, Server Rules**: bot and server switches; XP, RP, BP, money and regen multipliers.
- **RvR**: battlegrounds, keeps and relics. **Console**: live server output.
- **Client**: patch and launcher tools (below).

The same actions exist as single commands (`./daoc-admin help`). `--detach` starts the server in the background.

## Battleground bracket bots (new in this project)

A low-level character who walks into a battleground would otherwise find it empty, because bots level up and leave the low brackets.
**Bracket bots** fix that: they are ordinary roster bots (random names, real characters) that live at a battleground's level range and are
**logged in only while a player is inside that battleground**. They fill each realm, fight the battleground goal in waves, and log out about two
minutes after the last player leaves. Ordinary bots keep behaving as before, including joining battlegrounds while they level.

```bash
./daoc-admin bracket create --battleground all --count 12   # 12 bots per realm per battleground (levels 15-35)
./daoc-admin bracket                                        # roster / online per battleground
```

The same is on the admin screen's **Bracket Bots** tab, and the settings are on the **Options** tab (`bracket_bots_*`): bots **per realm** (default 12,
always per realm, minimum 8 = the size of a wave), **fill mode** (*fill* tops each realm up counting players and bots already there; *extra* always adds
the full number), **keep even** (raise every realm toward the biggest), and the logout delay. Cathal Valley (45-49) is not covered, because the bot
logic only knows the four classic battlegrounds. This is new code and only lightly tested.

Bracket bots are equipped once, at their first login, with a full kit for their class and level (weapons, shield, ranged weapon, armor, jewelry), so they never fight bare. Bots do not lose item condition, so they never need repairs. They start with 50 gold.

## Keeping the server light

- **No players, no bots.** By default the ordinary bots log out 120 seconds after the last player leaves the world and stay out until a player arrives (server properties `bots_only_with_players`, `bots_only_with_players_grace_seconds`). Bracket bots follow their own battleground rule.
- **Load governor.** Every 10 seconds the server checks its game-loop tick rate and the free memory (Linux). Below 90% of the target rate (`load_governor_min_tps_percent`) or under 1500 MB free (`load_governor_min_free_mb`) it stops logging bots in, and if that lasts two minutes it logs a few bots out at a time, only ones that can leave safely. It resumes after a minute at 95% or better. Turn it off with `load_governor_enabled`.

These are server properties for now (no Options tab entry yet). Both are new and only lightly tested.

## The client: patching and `launcher.bat`

The server **never starts or talks to the game client**. It only helps with two things, both on the **Client** tab: switching the client between patched and original (the full DividedRealms client patch), and writing a `launcher.bat`. Point the tab at
your client folder (a Wine prefix, Lutris, Steam, Bottles... it only needs to be a path your terminal can see).

### Switching the client between patched and original (no copy)

DividedRealms needs a patched 1.130 client: the extra classes (Death Knight, the Sovereigns, Stormforged), the battleground and
Abermenai zones, the door and building files, Marfach Caverns and the new art. The six expansion classes listed
above work on the **stock** client. This server ships DividedRealms' own client patch (`tools/client-patch.json`, about 19 MB, no
retail game file in it) and applies it **in place** to your client folder, the same 40 files the Windows launcher patches:

- `game.dll` and `pregame/character_creation.xml`: the extra class slots and creation buttons; `pregame/splash.mpk`: the loading screen
- `zones/zones.mpk`: the zone table with the battleground regions (this replaces the old Battleground fix, see below)
- zone 0, 3, 100, 102, 206 and 207 data and csv archives (doors, buildings, gate walls)
- new files made from stock ones: zone 253 (Abermenai) and zone 276 (Marfach) terrain, the Tara helms, skins and the fishing rod
- `gamedata.mpk`: the new catalog rows, added by ID so any build of it works

The tab offers the patch as **two independent switches**, each with its own **Switch to patched** / **Switch to original**:

1. **Extra classes** (2 files: `game.dll` and `pregame/character_creation.xml`). Only needed to play the added classes (Death Knight,
   the Sovereigns, Stormforged and the others); the stock client's classes work without it.
2. **World and art** (the other 38 files: zone table, Abermenai, Marfach, doors and buildings, new items and skins, `gamedata.mpk`
   rows, loading screen). Needed whatever classes you play, for example to enter a battleground at all.

Each part shows its state (*ready*, *patched*, *blocked*). It only accepts stock 1.130 files (SHA-256 checked) and **writes nothing
unless all of that part's files check out**. Every replaced file is kept as `<name>.orig`; switching back restores them and removes the
files the patch added, without the original client and **without copying the multi-GB client**. A client patched the old way (only
`game.dll` and the layout, or with the Battleground fix on) is brought up to date from its `.orig` backups. Code: `tui/client_patch.py`
(a port of the launcher's algorithm, see UPSTREAM.md).
A build without `tools/client-patch.json` falls back to the old two-file patch.

### Battleground fix (only without the full client patch)

The full client patch above already includes the battleground zone table. With the full patch this switch is greyed out; it is only used by a build that has no `client-patch.json`.

The plain 1.130 client doesn't know this server's battleground regions, so walking into any battleground sends you to the login screen. The Client tab's
**Switch battleground fix on** replaces just `zones/zones.mpk` (about 10 KB) with the classic zone table, keeps the original as `zones.mpk.orig`, and **Switch ... off**
puts it back. The classic table ships with the server (`tools/zones-classic.mpk`), so no other client is needed. It only replaces the retail 1.130 file (hash-checked).

### `launcher.bat`

Optional. The retail client can't be pointed at another server by itself; `connect-local1130.exe` starts it with a server address and
login. `launcher.bat` just runs that for you. It's useful when you start the client under Wine, Lutris, Steam or Bottles, and it also runs
on Windows. If you already have your own way to start the client with a server address, you don't need it.

The **Create launcher.bat** button writes two files into the client folder (the one you entered on the Client tab, next to `game.dll`): `launcher.bat` and the small login connector
`connect-local1130.exe`. The batch file does what a hand-made one would:

```bat
@echo off
cd /d "%~dp0"
set DAOC_ACCOUNT=...
set DAOC_PASSWORD=...
connect-local1130.exe "game.dll" 127.0.0.1:10300 %DAOC_ACCOUNT% %DAOC_PASSWORD%
pause
```

Leave the account or password blank and the batch file asks for it each time, so nothing secret is stored. If you fill them in they
are written in plain text into that file (the tab says so); the admin screen itself never saves them. You then run `launcher.bat`
with whatever you use for Windows programs (for example `wine launcher.bat`, or add it to Lutris/Steam/Bottles). It works with
any Wine setup because the server is not involved.

## The World Workshop editor

DividedRealms' browser-based editor for NPCs, items, merchants, loot, spells, quests and server settings runs here too:

```bash
./daoc-server.sh stop                                   # the editor saves into the world, so the server must be stopped
./world-editor.sh --client "/path/to/Dark Age of Camelot"
```

It opens in your browser on `127.0.0.1` only. It reads the art for its 3D views **straight from your own retail client folder**; no converted or
upscaled assets are used or included (the Godot client's art pipeline is not part of this project; only its small Python readers for the
client's files are kept, about 1.5 MB). Its state (backups of every save, caches) goes to `editor-data/` inside the server folder. It runs on the
bundled Python, which carries Pillow and NumPy for the previews (about 90 MB; build with `WITH_EDITOR_DEPS=0 ./build.sh` to leave them out).

## How the build works

`build.sh` -> either `tools/setup-build-box.sh --build` (distrobox) or a local setup -> `tools/build-release.sh`, which:

1. `dotnet publish`es the server (`CoreServer`) and the command-line admin tool (`DaocAdmin`) for linux-x64, self-contained.
2. Builds the native pathing library (Detour) with CMake, linking the C++ runtime statically so it runs on older glibc systems.
3. Fetches the world data from the DividedRealms release with `tools/fetch-divided-release.py` (reads only the needed files out
   of the ~1.2 GB zip with HTTP range requests; cached in `.cache/divided/`): the world database, the navmeshes, quest data and
   the login connector.
4. Bundles Python 3.12 and [Textual](https://github.com/Textualize/textual) for the admin screen (`tools/fetch-python.sh`).
5. Writes `dist/daoc-divided/` and the tarball.

Building directly with .NET also works: `cd source/server && dotnet build DOLLinux.sln -c Release` then
`dotnet test Tests/Tests.csproj -c Release --no-build` (about 5,700 tests).

## Upgrading

Extract the new build **over** the old folder. The new build contains no `data/` or `config/`, so your world, characters and settings
stay where they are, and only the program files are replaced. On the next `./daoc-server.sh start` the server checks
`data/opendaoc.sqlite3.db`:

- **No world yet** (first install): a fresh one is created.
- **A world exists:** it is **backed up** to `backups/world-update-<version>-<time>/` and brought up to the new release's content
  (new items, NPC fixes and so on). Each update step runs once, in one transaction, and only changes rows that still have their released
  values, so your characters and anything play has changed are left alone. A damaged update file or a failure stops the start and
  changes nothing.

`./daoc-server.sh world-update --check` shows what is pending without changing anything; `./daoc-server.sh world-update` applies it
without starting the server. If you extract into a new folder instead, copy the old `data/` and `config/` across first. This is a port of
DividedRealms' own updater (`tools/world_update.py`).

## Known gaps

- **New in DR-dev-0.1a to 0.1g, not separately tested here:** Catacombs and Darkness Rising (they may need client files the Client tab does not install yet), crafting, bot quests and crafting, fishing, and the new bot armor rules (`bot_quests`,
  `bot_crafting` and `bot_craft_orders` switch the first three off). The bracket bots' behaviour in a full battleground is
  upstream's: a realm with 8 or more bots in a battleground marches on the central keep, a smaller group roams.
- **Not separately tested:** Master Levels, Champion Levels, the bot raids and sieges, mounts and Trials of Atlantis items are included
  as DividedRealms ships them. They are not independently verified here.
- **Battleground exit portal has no graphic:** the exit portal in each portal keep exists, but the arch draws without the portal graphic with the 1.130 client, so you have to know where it is. Cosmetic; not fixed.
- **Battlegrounds need the Battleground fix on the client:** the plain 1.130 client maps the battlegrounds to other regions (and has no Abermenai), so the server sends you to the login screen when you enter one. The Client tab's full client patch includes the zone table and the zone files DividedRealms changes (older builds had a smaller **Battleground fix** that swapped only `zones/zones.mpk`).
- **Startup log noise:** about 450 ERR lines at startup are upstream data issues (items referring to missing spells, and so on), not
  failures of this build.
- **Gating:** flying mounts, account companions and the invented classes ship as upstream has them; switches to turn them off are
  planned.
- **World Workshop editor:** works on Linux through `./world-editor.sh`, except its collision-floor feature, which needs a Windows tool.
- x86-64 only.

## License

GPL-3.0, like the projects it builds on (see [LICENSE](LICENSE) and [THIRD_PARTY.md](THIRD_PARTY.md)). Dark Age of Camelot is the
property of its owners (Broadsword Online Games; originally Mythic Entertainment and Electronic Arts). This is an unofficial fan
project, not affiliated with them. No game client, art or sound is included or distributed.

## Credits

This project is a thin Linux layer over other people's work. Thank you to:

- **[rambodolphin13 / DividedRealms](https://github.com/rambodolphin13/DividedRealms)**: the server, classes, Master and Champion
  Levels, bots, world data, navmeshes and the World Workshop editor that this project packages. Almost everything good here is theirs.
- **[shadowofze / OfflineDAoC](https://github.com/shadowofze/OfflineDAoC)**: the offline DAoC server that DividedRealms grew from,
  including the autonomous and companion bots.
- **[OpenDAoC](https://github.com/OpenDAoC)** (OpenDAoC-Core, OpenDAoC-Database, OpenDAoC-BuildNav) and the original
  **Dawn of Light (DOL)** team and community: the server emulator everything here is based on.
- **[Eve of Darkness db-public](https://github.com/Eve-of-Darkness/db-public)** (MIT): the vanilla database used as the reference for the
  expansion classes.
- **[Recast & Detour](https://github.com/recastnavigation/recastnavigation)** (Mikko Mononen): the navigation mesh library.
- **[Textual](https://github.com/Textualize/textual)** (Textualize) for the terminal admin screen, and **[python-build-standalone](https://github.com/astral-sh/python-build-standalone)**
  (Astral) for the relocatable Python it runs on.
- **[.NET](https://dotnet.microsoft.com/)**, **[distrobox](https://distrobox.it)** and **[Bottles](https://usebottles.com)**, which make the
  build and testing possible on a Steam Deck.
- The historical sources DividedRealms credits in [THIRD_PARTY.md](THIRD_PARTY.md): Allakhazam, the Internet Archive, CapnBry's DAoC radar
  and the Uthgard bestiary, and the Three.js project (used by the kept World Workshop editor).
- **Claude** (Anthropic), which did much of the Linux porting work under the owner's direction.

And to everyone who keeps Dark Age of Camelot alive.
