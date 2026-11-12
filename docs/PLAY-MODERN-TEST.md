# Playing the modern test (original 1.130 client)

The modern test is Divided Realms for the **original retail 1.130 client**. It includes the live 1.130a class work, the
expansion and royal classes, Master and Champion Levels and the other changes in
[PROGRESS-2026-10.md](PROGRESS-2026-10.md). It is a development test build.

The download contains **no game client**. You use your own Dark Age of Camelot install.

## What you need
- **Your client:** Dark Age of Camelot installed, at the **1.130** client version. The setup checks your `game.dll`
  and accepts only the 1.130 build this game is made for (SHA256 `3915a495…`). A newer or modified client is refused
  and nothing is changed.
- **.NET Framework 3.5:** the Windows feature, for the client's connector. Turn it on in Control Panel → Programs →
  Turn Windows features on or off. The game's own .NET runtime is included.
- **Disk:** about 7 GB free where you extract the game, since your client is copied into it.

## First start
1. Extract the whole download, then double-click **START DIVIDED REALMS.cmd**.
2. The launcher asks you to set up your client. Choose your Dark Age of Camelot folder: the one with `game.dll` and
   `camelot.exe`.
3. It copies the client into `runtime/client-modern/app` and patches the copy. The patch changes three files:
   - `game.dll`: native class slots for the custom and expansion classes.
   - `pregame/character_creation.xml`: room for the extra class buttons.
   - `pregame/splash.mpk`: the Divided Realms loading screen.

   **Your own install is not changed**, so you can still play live with it. Your login and logs aren't copied.
4. **START SERVER**, then **ENTER REALM**. The server creates your account on the first login.

If the setup is cancelled or fails, nothing is set up. It asks again on the next start, or when you press ENTER REALM.

## Updating
From the build after DR-dev-0.1c on, the launcher updates itself.
- **The check:** when the launcher starts, it asks GitHub at most once a day whether a newer Divided Realms release is
  out. It asks for the list of releases only, with a plain program name, and sends nothing about you or your game. If
  GitHub can't be reached, it says nothing. **Options > Update and repair > CHECK FOR UPDATES** asks right away. Untick
  **Check automatically** there to stop the daily check.
- **The offer:** a newer release opens a window with **Install**, **Later** or **Skip this version**. A skipped release
  isn't offered again on start, but CHECK FOR UPDATES still shows it.
- **Install:**
  - Close the server, the game and World Workshop first. The launcher won't update while any of them runs.
  - It downloads the release zip (about 1.2 GB) to `runtime\updates` and shows the progress. A broken download picks up
    where it stopped.
  - It checks the zip against the SHA-256 the release page publishes, then checks every file against the package's own
    `PACKAGE MANIFEST.sha256`.
  - The launcher then closes, and a small window copies the new files over your folder. This is the same as extracting
    over it. Every file it replaces is backed up first in `runtime\updates\backups`. If anything goes wrong, it puts
    every file back. The launcher then opens again.
  - On that start, it adds the release's world fixes to your world (after a backup) and patches your client copy again,
    as after a manual update.
- **Never touched:** your world and characters (`runtime\data\opendaoc.sqlite3.db` and its backups), `runtime\account.txt`,
  logs, your client copy, and your settings (overlay, Host for Friends, a `serverconfig.xml` you changed).
- **Undo an update:** close the launcher, the server and the game. Then double-click **ROLLBACK.cmd** in that update's
  backup folder, `runtime\updates\backups\<old>-to-<new>-<time>`. It puts the game's files back. Your world keeps the
  newer release's world fixes. To undo those too, copy the world backup the launcher made before them
  (`runtime\backups\world-update-*`) over `runtime\data\opendaoc.sqlite3.db`, which loses what you played since.
- **Folders it won't update:** the launcher only updates a folder unpacked from a release download. That folder has
  `RELEASE.txt` or `EDITION.txt` and `PACKAGE MANIFEST.sha256` at its top. It never updates a developer's folder or
  anything inside a Git checkout.

**By hand** (and from DR-dev-0.1c or older, whose launchers can't update themselves): close the launcher, extract the
new download over your folder and replace every file.

## Notes
- **Ports:** the server listens only on this computer, on port 10330.
- **Saves:** your characters and bots are saved in `runtime/data`. To move to a new version of this test, keep that
  folder, or use the progress importer once one ships for this edition.
- **Patch file:** the client patch is `runtime/client-patch.json`, made by `source/tools/make_client_patch.py` from the
  class-slot patch `source/tools/classslots/patch_class_slots.py`. It holds this project's changes, plus the two small
  Marfach Caverns zone files that the 1.130 client lacks. No other Broadsword file is in the download.
