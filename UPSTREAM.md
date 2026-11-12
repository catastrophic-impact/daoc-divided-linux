# Upstream

Merged-upstream: a0555cd433c10d813f17128474543917b5e29939

- DividedRealms: https://github.com/rambodolphin13/DividedRealms, snapshot `e7155da` (2026-10-30, 0.38-dev7), merged up to `a0555cd` (2026-11-11, DR-dev-0.1g, Catacombs and Crafting).
  Upstream history is squashed, so ours is separate from theirs and `git merge` does not work; use `tools/sync-upstream.sh` (below).
- Removed from the snapshot: `source/new-client/` only (World Workshop editor kept).
- Lineage: OpenDAoC -> OfflineDAoC (shadowofze) -> DividedRealms.
- Left out of the merges on purpose: the Passport Desk (bringing a character to a friend's world; Windows launcher feature),
  `OfflineDaoc.TestClient`, the Windows launcher changes, and `source/new-client`. `PASSPORT_DESK_PORT_OFFSET` stays in
  BaseServer because a test uses it.
- Our own bot death-pose change was dropped: upstream's `ShowDeadToViewers` / `ShowStandingToViewers` cover it.

## Syncing a new upstream

    git checkout -b sync-<date>
    tools/sync-upstream.sh --check      # what changed upstream since Merged-upstream
    tools/sync-upstream.sh              # apply it (3-way, minus tools/upstream-exclude.txt)

Then resolve conflicts, build (`./build.sh`), run the tests, update `Merged-upstream:` above and the line in
`changes.txt`, the version label `OfflineDaocVersion` in `source/server/Admin.Host/AdminService.cs` (shown by `daoc-admin status`), the
status lines in `README.md` and the `RELEASE_TAG` default in `tools/build-release.sh`. If upstream's `GameServer.csproj` or `Tests.csproj` changed, hand-merge them (they are excluded because they
link the Passport and TestClient sources). Do not skip this: without upstream's new `EmbeddedResource` lines (lore, epic chains,
zone water, test fixtures) the build still succeeds but about 1,500 tests fail with `epic_chains_data.json is not built into
GameServer.dll` and a `GameObject` type-initializer error. Check `git diff <old> <new> -- <the two csproj>` and add the lines. Pre-sync checklist: `git status` clean, `dist/` not needed.

## Our edits to upstream files (all marked `daoc-divided-linux:` in the code)

Find them with `grep -rn "daoc-divided-linux:" source`. Everything else we added is in new files and cannot conflict:
`Admin.Host`, `Admin.Protocol`, `DaocAdmin`, `bots/bracket`, `bots/governor`, `tui/`, `deploy/`, `build.sh`, `tools/*.sh`.

| Upstream file | Our hook |
|---|---|
| `GameBot.cs` | bracket bots stop at their cap; `EquipBracketKit()` (runs first at login) |
| `AutonomousObjectiveAssignments.cs` | bracket bots only take the battleground goal |
| `AutonomousPopulationController.cs` | load governor holds logins; bracket bots are not ramped in; `QueueLogin` |
| `LoginRequestHandler.cs` | `NewAccountPrivLevelProvider` (admin roles from `config/admins.json`) |
| `ConsoleStart.cs`, `CoreServer.csproj`, `DOLLinux.sln` | admin host and Linux build |
| `AutonomousBotGoalPolicy.cs` | exposes `FilePath` and adds `Apply()` for the admin goals editor |
| `AutonomousBotSchedule.cs` | "no players, no bots" (`BotPresence`) |

## Merge resolutions (the files that conflicted at the DR-dev-0.1g sync; expect them again)

| File | How it was resolved |
|---|---|
| `GameBot.cs` | the XP cap line is `Level >= ExperienceStopLevel \|\| BracketBotService.IsAtCap(this)` (upstream's stop level plus our bracket cap). `EquipBracketKit()` stays; upstream's comment on the next method wins. |
| `AutonomousBotGoalPolicy.cs` | keep both: our `Apply()` and upstream's level scaling (`ScaleForLevel`, `WeightsFor`). |
| `ConsoleStart.cs` | ours (`HandleConsoleLine`); take upstream's `ConsoleCommandClient.Create()` where it builds the command client. The file has CRLF line endings. |
| `.gitignore` | keep both sets of lines. |
| `README.md` | ours always (upstream's README is their Windows one, kept as `README-DividedRealms.md`). |
| `GameServer.csproj`, `Tests.csproj` | not synced (they link Passport and TestClient): hand-merge upstream's new `EmbeddedResource` lines each time. |

Not conflicts but ours, to re-check on every sync: `source/server/Admin.Host/OptionsAdmin.cs` lists the server settings the Options tab
may change (marked `daoc-divided-linux:`); new upstream settings are added there by hand. 0.1g added 57; 24 are on the tab.

## Changes that came from other people's forks

Kept in our own files (so they cannot conflict with upstream) and credited in `changes.txt`:
- ThatFontGuy (GitHub fork of this repo): admin screen reads big replies, `tui/daoc_admin_tui.py` (8 MiB line limit). Their fork also has a
  `build.sh --cross` mode, and moves Stormforged for a custom class (Drengr); we did not take those (see `changes.txt`).

Passport hooks removed from upstream files (re-check after each sync; `sync-upstream.sh` greps for them):
`GameServer.cs` (`PassportDesk.Start`), `BaseServer.cs` (UPnP port line), `LotMarker.cs`, `CharacterCreateRequestHandler.cs`,
`WorldInitRequestHandler.cs`, `Account.cs` (`PassportLock` checks).

Also removed (Windows download and install scripts for old Offline DAoC releases, no use in a Linux build): the root
`GET-OFFLINE-DAOC-*.cmd`, `Get-OfflineDAoC.ps1`, `older-versions/`, `package-files*/`, `READ ME FIRST.txt`,
`RELEASE VERIFICATION.txt`, `START WORLD EDITOR.cmd` and `DEVELOPMENT - START HERE.md`. They are in `tools/upstream-exclude.txt`.
