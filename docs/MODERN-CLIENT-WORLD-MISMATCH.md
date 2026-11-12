# Modern 1.130 client: world mismatch (October 26, 2026)

Tester reports (Orshe): pixie name tags in Tir na Nog, NPCs out of place in Druim Ligen and Mag Mell,
the Druim Ligen door "too far away".

Everything below was measured read-only. No server or client was started, nothing was deployed, and no
game database or client folder was changed. Scratch tools and outputs are in the session scratchpad.

## Summary

| Report | Cause |
|---|---|
| `[ambient pixie]-DOR-NON` | Not a bug. GM accounts see "-DOR" (untargetable) and "-NON" (hidden name) on NPC names (`PacketLib1124.cs:190-204`). |
| NPCs out of place, Druim Ligen door | The 1.130 client draws live's world. The server, database, navmeshes and bots use OpenDAoC's classic world. |

The classic client is a **1.127** build (`PacketLib1127` in the server log). It ships OpenDAoC's curated
classic world data. The retail 1.130 client ships live's world data. Both `game.dll`s use the same loader,
including `newtowns/zones/nifs`. OpenDAoC turns live's "New Towns" off by shipping that folder with only
its own 7 models.

### What differs (world data only)

1,313 files, 316 MB, in the classic client differ from or are missing in 1.130:

| Area | Classic-only / different | Effect on 1.130 |
|---|---|---|
| `Newtowns/zones/Nifs` | 1.130 has 196 extra live models; 5 of OpenDAoC's (border keeps, Avalon teleporter, demon portal) differ | Live building models (Gamebryo 10.1, all rebuilt art) are drawn in classic positions |
| 61 `zones/zoneNNN` folders | object tables, terrain, capital building pieces (`cam*`, `jord*`, `tir*`) | Building IDs and door IDs change; some terrain moves (Dartmoor up to 4,300 units) |
| `zones/Nifs` | 64 + 14 (relic keeps, mile forts, dragon lairs, gate forts) | Old-frontier keep doors differ |
| `zones/Dnifs` | 191 + 179 (dungeon pieces) | Dungeon geometry differs |
| `frontiers/Nifs`, `frontiers/zones` | 1 + 48, 70 | New Frontiers walls, gates, towers and door IDs differ |
| Marfach Caverns (region 276) | missing from 1.130 | 47 NPCs and 6 entrances unreachable on 1.130 |

### Measured impact on 1.130 today

Each client's world was exported with the repository's navmesh builder (`OpenDAoC-BuildNav --obj`),
and every NPC spawn was tested against both geometries:

- **469 NPC spawns** end up inside geometry, on a floor that moved more than 48 units, or with no floor.
  They cluster in about 20 places: Cotswold, Mularn, Mag Mell, the six border keeps (Castle Sauvage,
  Snowdonia Fortress, Svasud Faste, Vindsaul Faste, Druim Ligen, Druim Cain), Ardee, Huginfell, Dartmoor,
  Malmohus, Sheeroe, the old frontier keeps, and Jordheim. The other ~32,000 spawns in these zones are fine.
- **Doors** (333 classic doors checked): 77 missing, 53 moved, 75 extra. That includes 41 of 66 doors
  in the capitals, and 12/39/22 in New Frontiers.
- **Druim Ligen gate:** in live's `hborderkeep1` model the two gate doors `207156901` and `207156902`
  have swapped places, each about 2,200 units from where the server has it. The server rejects border
  keep doors beyond 3x the pickup distance, hence "too far away". Druim Ligen's guards stand on classic
  wall walkways (Z 5695) that don't exist in live's keep (floor Z 5184).

## Fix A: make the 1.130 client show the classic world

Overlay OpenDAoC's world data onto the player's 1.130 copy and remove live's 196 New Towns models.

**Simulated:** a scratch copy of the 1.130 client with the full overlay, exported the same way.
- Doors: **333 of 333 identical** to the classic client (outdoor zones, capitals, Stonehenge Barrows,
  Darkness Falls, all 14 New Frontiers zones). None missing, extra or moved.
- NPCs: **0 affected** out of about 25,000 in 30 zones, and 0 in New Frontiers.
- Swapping only the Newtowns folder is not enough. The 1.130 zone files renumber buildings, so door IDs
  stay wrong.

**Work:**
1. A generator that diffs the classic world data against a pristine 1.130 client and writes the overlay
   manifest: each path with its input SHA256 (or "absent"), output SHA256, and a removal list for the
   196 Newtowns models.
2. `client-patch.json` format 2: `Replace`/`Add` (from an overlay folder in the package) and `Omit`.
   Extend `ModernClientSetup.Install`, `make_client_patch.py`, `build_modern_test_package.py`
   (`check_package` must allow the overlay) and the launcher tests.
3. `zones/zones.mpk`: 1.130's `zones.dat` plus `[region276]`/`[zone276]` for Marfach Caverns.
4. A one-off script for the existing local test install (`artifacts/modern-client-test`), with a backup
   and rollback.
5. In-game check (below).

Estimate: 2-3 days, plus in-game testing.

**Risks:**
- The 1.130 client reading OpenDAoC's older-layout tables. The same files already load on the 1.127
  client, and the loader is shared, so the risk is low but only a real launch confirms it. If it fails,
  convert the classic tables to the 1.130 column layout, keeping positions and IDs.
- The modern package's rule is "no retail file ships". The overlay is OpenDAoC's modified world data, the
  same kind of content the classic packages already ship. Owner's call.
- Players on 1.130 see the classic world, not live's 2015 towns.

**In-game check:** Mag Mell, Druim Ligen (gate and guards), Cotswold, Mularn, Tir na Nog, Camelot,
Jordheim, one dungeon, a New Frontiers keep and relic gate, Marfach Caverns. Do it with GM off.

### Installed in the local test client (October 26, 2026)

`source/tools/modern_world_overlay.py` (tests: `test_modern_world_overlay.py`, 7 passing) applied the
overlay to `artifacts/modern-client-test/runtime/client-modern/app`: 634 files replaced, 679 added,
196 Newtowns models removed (316 MB). The overlay takes the classic `zones/zones.mpk` whole. The 1.130
table also puts the battlegrounds Caledonia, Murdaigean and Thidranki in regions 166-168 (the server uses
250-252), and lacks Abermenai, Dodens Gruva, Marfach Caverns and Summoner's Hall. `game.dll`, the UI,
items, figures and effects were not touched. The test server kept running; it doesn't read client files.

**Checked after install** (doors and geometry exported from the patched client):
- All 333 doors identical to the classic client.
- 0 of 23,492 NPC spawns affected, in 44 zones including New Frontiers.

Not yet checked in game.

Backup and manifest: `artifacts/modern-client-test/backups/world-overlay-20261026-180051-287487`.
Rollback (close the client first; preview without `--apply`):

```
python source/tools/modern_world_overlay.py rollback artifacts/modern-client-test/runtime/client-modern/app artifacts/modern-client-test/backups/world-overlay-20261026-180051-287487 --apply
```

### What a release client gets (October 30, 2026)

The zone-table overlay above went only to the local test client, so release players on their own 1.130 client could not
enter regions 246, 248, 250-253 and 276. `make_client_patch.py` now patches their copy: `zones/zones.mpk` keeps the whole
stock table, with zones 250-252 moved to regions 250-252 and the missing sections added, and `ter253.mpk`/`lod253.mpk` are
made from the client's own zone 252. Marfach Caverns (region 276: 47 NPCs, 6 entrances) needs `dat276.mpk` and its
`sounds.dat`, which 1.130 lacks and can't make: no stock dungeon shares a placement with it. By the owner's decision the
patch carries those two files, read from the classic client at build time (`--classic`). Its models are stock.

## Fix B: move the server to live's world

**Work:**
1. **World database:** re-place the 469 NPC spawns. About half are a floor snap; spawns inside walls
   need placing by hand. Update about 200 door rows from the 1.130 export, and check bind points,
   teleports and world objects near the changed spots. Re-check keep guard and hookpoint placement on
   the six border keeps.
2. **Navmeshes:** rebuild every affected zone from 1.130 data. A 1.130 build differs from the installed
   mesh in nearly every tile; the installed meshes match the classic client exactly (0 tiles in Lough
   Derg and Connacht). Then rebuild ladder links (needs `Detour.dll` built), regenerate `roads.json`,
   `seams.json` and `pockets.json`, and re-prove bot routes (`UT_ZoneStepProbe`, soak runs).
3. **Bot code:** 13 hard-coded points sit in changed areas: RvR staging and raid muster at all six border
   keeps, a Cotswold route repair, the Lough Derg Shrouded Isles portal. Route hotspot repairs were tuned
   to classic geometry.
4. **The other clients now mismatch instead:**
   - The classic client needs live's world files (1,313 files plus 196 models). Either every player has a
     1.130 client to copy them from, or the project ships retail files.
   - The Godot client converts the classic client today. Reconvert from 1.130 data (its NIF reader
     handles 10.1) and re-verify props and doors.
5. Marfach Caverns still needs a 1.130 zone entry.
6. Future OpenDAoC upstream world updates assume the classic world and would conflict.

Estimate: several weeks, with the bot AI the main risk.

### Installed in the local test install (October 26, 2026)

Fix A was rolled back first. The test install now runs fix B:

| Part | What changed | Backup |
|---|---|---|
| Client | live 1.130 world files, plus the classic `zones/zones.mpk` (battleground regions, Marfach Caverns and the old RvR dungeons) and the `zone276` folder | `backups/world-overlay-20261026-192733-136443` |
| Navmeshes | 92 zones rebuilt from 1.130 data with `OpenDAoC-BuildNav` (ladders included); 81 differ from the classic meshes. Tuscaran Glacier re-run through `shadowfix.py` (1,339 polygons). `seams.json`, `pockets.json` and `roads.json` regenerated; the road proof (`UT_ZoneRoadBuild`) passed with 508,788 edges (classic: 510,548). The folder is now a real folder; it used to be a junction to Classic's meshes, which were not touched. | `backups/navmesh-20261026-192744-514841` |
| World (second plan, replacing the first after the in-game test) | `source/tools/live_world_migration.py`: 787 rows: 605 NPC spawns, 121 doors, 29 world objects, 16 zone arrival points, 7 teleports, 6 bind points, 3 safe areas. 54 left alone: 24 doors live doesn't have, about 20 NPCs in the zone-277 quest instance, 7 named NPCs in Dartmoor. | `backups/live-world-20261026-194915-735732` |

Staging, plan and logs: `artifacts/modern-client-test/fixb-staging`. Tools and tests: `live_world_migration.py`,
`live_world_navmesh.py`, `modern_world_overlay.py` (26 tests passing).

**Checked after install** (live geometry; spawns inside a wall or more than 64 units off the floor), in
12 key zones:
- Before: 534. After: 181. For comparison, the classic world on classic geometry scores 245 in the same zones.
- What remains is mostly swarms of flies, ambient rats and pixies.
- Zone borders: the same usable borders in all six regions as with the classic meshes.

**First in-game test:** Svasud Faste's NPCs stood in their usual layout, but outside the keep. Live pulled
three border keeps back inland and kept the old-frontier copies in the zone files where they can't be
reached:

| Keep | Classic | Live (inland) | Turn |
|---|---|---|---|
| Castle Sauvage | Forest Sauvage (585088, 477056) | Camelot Hills (583936, 486720) | 0° |
| Snowdonia Fortress | Snowdonia (528384, 358784) | Black Mtns. North (516608, 372800) | +45° |
| Svasud Faste | Uppland (766208, 669536) | Vale of Mularn (765312, 673472) | -9° |

The turn is measured from each copy's two gate doors. The fixture angles also include the models' own
facing: Druim Ligen and Druim Cain change fixture angle by 180° but face the same way in the world, which
is why their gate door IDs swapped. The second plan carries every NPC within 2,000 units, and the bind
points, teleports, banners, New Frontiers arrival points and safe area of each keep, with the keep. It
turns their headings too and sets them on the new floor (Castle Sauvage: 2600 → 2186).

**Rolling back the first plan** used `--skip-changed`. At startup the server had rewritten the three
dragons (Golestandt, Gjalpinulva, Cuuldurach) to the classic lair heights hard-coded in
`scripts/namedmobs/Dragons/DragonLairPlacement.cs` and the dragon scripts. It will keep doing that, about
150 units above live's lair floors, until that code changes.

**Town NPCs (open):** New Towns is a redesign, not a shift. In Cotswold, Mularn and Mag Mell most
merchants and trainers stand inside buildings that live moved 300-6,100 units (6-14 per building). They
are still near their classic spots. There is no live-era spawn data locally to restore from
(`offline_classic165_removed_mobs` has nothing near the new buildings).

**Bot code (follow-up, not deployed):** RvR staging and the raid muster hubs hard-code Castle Sauvage,
Snowdonia and Svasud Faste at their classic spots. Bots still work, but muster 4,000-18,000 units from the
inland keeps. The fix is to read the keep positions from the Area table, which the migration moves. It
needs a server build, which wasn't done while other uncommitted server work sits in the source.

**Bot code (snapping):** of the 12 hard-coded points, 9 are on live floor. Three (the Cotswold route repair, Druim
Ligen and Druim Cain RvR staging) are 144-190 units above the new floor. Their callers snap to the
navmesh within 256 or 600 units, so no code change was needed, and the shared source stays right for the
Classic and Stormforged editions.

**Border-keep gates baked shut (found and fixed 2026-10-28):**
- **Symptom:** the server log of 2026-10-28 had 2,623 Albion goals end "No safe connected zone seam", and no Albion
  goal in Pennine Mountains, Forest Sauvage, Snowdonia or Hadrian's Wall was productive.
- **Cause:** the 1.130 meshes were built with three border-keep gates closed. Each gate left a 30-unit gap on flat
  floor, so the courtyard (joined to the homeland) and the ground past the gate were separate areas:
  - Castle Sauvage north gate
  - Snowdonia Fortress south-east gate. Its 45°-turned keep ground was also never stitched to the frontier ground
    around it: they meet at the same height with a 15-25-unit gap and no wall in the geometry.
  - Druim Cain south gate. Valley of Bri Leith's main ground no longer met Mount Collory, and the RvR staging point
    there stood on the cut-off strip.
- **Other keeps:** a door-by-door check of all six border keeps and the frontier gate forts, on both worlds' meshes,
  found only these three changed. Svasud Faste, Vindsaul Faste and Druim Ligen are joined as in classic.
- **Second cause:** the modern `seams.json` had no `"main"` section (`add_main_areas.py` was not run). So the
  itinerary could not reject a crossing onto a ridge that never reaches the destination's main ground. Midgard's
  trips to Jamtland bounced between Vale of Mularn and Uppland on the mountain corner at (753760, 671744).
- **Fix:**
  - 11 two-way walk links (`tools/navmesh_splice/modern-gate-links.json`, applied with `add_offmesh.py`).
  - `seams.json` and `pockets.json` regenerated with `seamgraph.py`, then `add_main_areas.py`.
  - `roads.json` regenerated with `roadgraph.py` and `UT_ZoneRoadBuild` (passed).
  - The regenerated pockets no longer list Snowdonia Fortress's own grounds, where the world migration had moved its
    NPCs.
  - Backup: `backups/navmesh-gate-links-20261028-210857`.
- **Proof:** every homeland bind point was walked crossing by crossing to the centre of each of the realm's four
  frontier zones (`UT_ZoneStepProbe.ProbeZoneRoutes`), and in the goal zone the corridor check must reach the goal.

  | Realm | Classic meshes | Modern before | Modern after |
  |---|---|---|---|
  | Albion | 68 / 112 | 0 / 116 | 66 / 116 |
  | Midgard | 48 / 52 | 49 / 56 | 56 / 56 |
  | Hibernia | 71 / 72 | 67 / 72 | 71 / 72 |

  The Albion trips that still fail mostly start from bind points that are off the mesh or cut off in classic too.
  Six fail in modern but not classic: they bounce at the Black Mtns. South / Camelot Hills corner (548800, 485664) or
  the Black Mtns. North / Snowdonia ridge (544160, 368704). One Hibernian trip that worked before now fails the way it
  does in classic (Valley of Bri Leith to Cruachan Gorge, bouncing at Lough Derg / Connacht). No other trip got worse.
  Not yet seen in game.
- **Players:** the note here used to say they were not affected. That was wrong: see the next section.

**Border-keep gates walled for players (issue #2, found 2026-11-01):**
- **Symptom:** a Heretic opened the north gate of Castle Sauvage and could see through it, but an invisible barrier stopped them.
- **Cause:** the gates were never baked shut by a door. Live's `bound.csv` in zones 0, 3 and 206 closes each gate and the way
  out of the zone, and the client enforces it. All 11 walk links cross such a wall on stock 1.130; none does on classic. The
  mesh builder treats those walls as geometry, which is what left the 30-unit gaps above. Bots crossed on the links because the
  server moves them; players can't. This also holds Snowdonia Fortress and Druim Cain, which nobody had reported.
- **Fix:** `make_client_patch.py` `GATE_WALLS` swaps the closing walls for the classic client's, in each zone's dat and csv
  archive. The zones beyond (11, 12, 210) are the same in both clients and their walls end where the patched ones do.
- **Open:** checked against the client's data only. Enforcement of the patched table has not been seen in a real client.

**Border-keep gates still walled on DR-dev-0.1a (reported 2026-11-05):**
- **Symptom:** at Castle Sauvage, Snowdonia Fortress, Svasud Faste and Druim Cain the gate opens but the player can't walk
  out. The chat shows `Invalid ZonePoint. Wrong ID or mismatching realm. (ID: 191)` at Vindsaul Faste and `(ID: 193)` at
  Druim Cain.
- **Cause:** live's `zonejump.csv` in zones 0, 3, 100, 102, 206 and 207 has a `Frontier` jump just past each border keep's
  frontier gate (zone points 188, 189, 191 and 193, to New Frontiers) and untargeted `Border` / `artfix` lines around it
  (Svasud Faste and Druim Ligen have only these). The classic tables have none of these rows, the server has no such zone
  points, and the client stops the player there. With the `bound.csv` patch the walls themselves are open: tracing the patched
  wall maps outward from each gate reaches the frontier zone as on classic, and Svasud Faste's and Druim Cain's were never closed.
- **Fix:** `make_client_patch.py` `GATE_JUMPS` drops those rows (each named; another table is refused) and numbers the rest
  1, 2, ... again, in each zone's dat and csv archive. The Old Frontiers zones' tables are the same in both clients.
- **Open:** not yet seen in a real client.

**Frenzy models (reported 2026-11-01):** model ids 3, 4, 12 and 13 are the classic table's own copies of Vendo Man, Stag Man and
their small forms. On 1.130 they are "empty" and coconut figures, so a Berserker played the frenzy and never morphed. Both
tables have the same forms at 582, 583, 859 and 2032, which the effects now use.

**Darkness Falls mesh (issue #3, found 2026-11-01):** the 1.130 rebuild of `zone249.nav` dropped the entrance falls and climb
pads, so it stopped matching `darkness_falls_navigation_proofs.json` and the certificate closed: gamebots got no Darkness Falls
goals and no route down the first steps. 7,897 of 7,941 tiles are identical to the classic mesh, every Darkness Falls mob and
exit row still matches the certificate, and `UT_DarknessFallsMeshProbe` completes 4,101 of 4,101 routes on the classic mesh and
0 on the rebuilt one. The certified mesh `41f1489c` belongs in the modern world too, and `build_modern_test_package.py` now
refuses a package without it.

**Bots stuck in the Darkness Falls drops (issue #3 follow-up, 2026-11-01):** a server owner reported that `41f1489c` let some
`/spawn` bots down the steps while others stuck. The "steps" are nine one-way drops of 130-200 units, three per realm. Around 40-60%
of the way down, no mesh is in Detour's reach (`UT_DarknessFallsMeshProbe.ProbeInsideDrops`: 10 of 99 points), so a re-plan there
returns `NoPathFound`, every retry fails the same way, and floor repair cannot help because the floor is too far down. Only
autonomous bots had the protected fall, and it gave way as soon as the destination moved 64 units, so followers and `/spawn` bots
were re-planned in mid-drop whenever their leader kept moving. All bots now finish a certified fall before taking the next order, and
a bot left hanging settles on the floor below (`TryFindFloorBelowDrop`, 10 of 10 points). A closed certificate now logs
`DF_CERTIFICATE_CLOSED` with the reason; it used to be silent. Mesh coverage was not the problem: 13,842 of 13,862 floor points around
the drops reach the bottom. The other 20 are one cut-off Hibernian pocket near 43270,38840.

**Not migrated:** saved player and gamebot positions. A bot standing in a changed spot is handled by the
stuck watchdog and the pockets file. Not yet tested in game.

**Rollback** (server and client closed; each previews without `--apply`), in this order:

```
python source/tools/live_world_migration.py rollback artifacts/modern-client-test/runtime/data/opendaoc.sqlite3.db artifacts/modern-client-test/backups/live-world-20261026-194915-735732 --apply --skip-changed
python source/tools/live_world_navmesh.py rollback artifacts/modern-client-test/runtime/server artifacts/modern-client-test/backups/navmesh-20261026-192744-514841 --apply
python source/tools/modern_world_overlay.py rollback artifacts/modern-client-test/runtime/client-modern/app artifacts/modern-client-test/backups/world-overlay-20261026-192733-136443 --apply
```

To switch to fix A after that, re-apply the full overlay (`modern_world_overlay.py apply ... --apply`).

## Recommendation

**Fix A.** The server, database, navmeshes, bot AI and the other two clients are all classic. Fix A
changes only the 1.130 client, and the simulation shows it reproduces the classic world exactly. Fix B
is only worth it if live's look on 1.130 matters more than everything else staying as it is.

## Separate finding

This project's private art (custom pet models such as `ab_pet01` and `uh_hero01`, `q*` spell effects,
reskinned monsters, and their catalog rows in `gamedata.mpk`) exists only in the classic client. On
1.130 those pets and effects show stock or missing models. This needs its own patch step.
