# Upstream

Merged-upstream: 084a4334b0598ea92c233afae31db2146c0a89d6

- DividedRealms: https://github.com/rambodolphin13/DividedRealms, snapshot `e7155da` (2026-10-30, 0.38-dev7), merged up to `084a433` (2026-11-04, DR-dev-0.1a, the Fishing Update).
  Upstream history is squashed, so ours is separate from theirs and `git merge` does not work; use `tools/sync-upstream.sh` (below).
- Removed from the snapshot: `source/new-client/` only (World Workshop editor kept).
- Lineage: OpenDAoC -> OfflineDAoC (shadowofze) -> DividedRealms.
- Left out of the 0.1a merge on purpose: the Passport Desk (bringing a character to a friend's world; Windows launcher feature),
  `OfflineDaoc.TestClient`, the Windows launcher changes, and `source/new-client`. `PASSPORT_DESK_PORT_OFFSET` stays in
  BaseServer because a test uses it.
- Our own bot death-pose change was dropped: upstream's `ShowDeadToViewers` / `ShowStandingToViewers` cover it.

## Syncing a new upstream

    git checkout -b sync-<date>
    tools/sync-upstream.sh --check      # what changed upstream since Merged-upstream
    tools/sync-upstream.sh              # apply it (3-way, minus tools/upstream-exclude.txt)

Then resolve conflicts, build (`./build.sh`), run the tests, update `Merged-upstream:` above and the line in
`changes.txt`. If upstream's `GameServer.csproj` or `Tests.csproj` changed, hand-merge them (they are excluded because they
link the Passport and TestClient sources). Pre-sync checklist: `git status` clean, `dist/` not needed.

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
| `AutonomousBotGoalPolicy.cs` | exposes `FilePath` for the admin goals editor |
| `AutonomousBotSchedule.cs` | "no players, no bots" (`BotPresence`) |

Passport hooks removed from upstream files (re-check after each sync; `sync-upstream.sh` greps for them):
`GameServer.cs` (`PassportDesk.Start`), `BaseServer.cs` (UPnP port line), `LotMarker.cs`, `CharacterCreateRequestHandler.cs`,
`WorldInitRequestHandler.cs`, `Account.cs` (`PassportLock` checks).

Also removed (Windows download and install scripts for old Offline DAoC releases, no use in a Linux build): the root
`GET-OFFLINE-DAOC-*.cmd`, `Get-OfflineDAoC.ps1`, `older-versions/`, `package-files*/`, `READ ME FIRST.txt`,
`RELEASE VERIFICATION.txt`, `START WORLD EDITOR.cmd` and `DEVELOPMENT - START HERE.md`. They are in `tools/upstream-exclude.txt`.
