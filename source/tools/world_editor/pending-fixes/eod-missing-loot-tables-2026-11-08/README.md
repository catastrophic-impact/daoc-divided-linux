# Missing loot tables outside ToA (2026-11-08)

Made by `../make_eod_missing_loot_tables_edits.py`. **Not applied.** Apply last of the three patches of this pass.

The world has 2,267 MobXLootTemplate rows outside ToA naming LootTemplate tables that were never imported (the
same bug as the ToA named drops). Eve of Darkness db-public (MIT) has 1,766 of those tables; this adds them:
**9,309 LootTemplate rows** (8,835 gear, 474 quest or junk items; chances mostly 10-20%, 18 rows at 100%,
no timed XP rows). 1,171 of the tables belong to monsters spawned in the world, the rest to names nothing spawns
yet. Examples: corpse crawler and dungeon chitin (Flayed Hagbui armor), blodfelag wolfwarrior (Blodsnitt, Pillager
Gauntlets), braen (Stone Cutter, rings), Anpassa Foranst's Composite Bow.

Not covered: 494 tables with no EoD source; 32 EoD rows whose item is not in the world.

This brings back a lot of named classic, SI and dungeon loot: it changes the economy and what bots find.

Checks: planned (9,309 edits), applied on a copy after the other two, undone exactly. Dangling links on the copy
went from 2,645 to 500. Not booted.

## Level trim (applied 2026-11-08 19:37, undo 20261108-193747-d8c1ab63)

The AI tester saw level 5 arachite greensilk (its NpcTemplate is level 5, the spawn rows say 27-30) drop level 38-40
Svartalf jewelry. `../make_eod_loot_level_trim_edits.py` removes, from restored tables whose best gear is 20+ levels
above the highest live level of their monsters, every gear row more than 10 levels above it: 32 rows in 17 tables
(arachite greensilk/hatchling/youngling/priest/shadowslinker/weblasher, wildling, spriggarn, escaped thrall, tomte
shaman, giant frog, ash, oak man, corpse candle, disturbed presence, keltoi spiritualist, large skeleton; list in
`../eod-loot-level-trim-2026-11-08/removed.json`). Junk and crafting items stay. Rheton (peaceful, level 1, region
244) keeps its level 50 set: nobody can kill it. Undo the trim before undoing the tables patch.
