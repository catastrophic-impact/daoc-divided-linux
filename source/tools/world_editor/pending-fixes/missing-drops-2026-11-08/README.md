# Missing drops (2026-11-08)

Made by `../make_missing_drops_edits.py` from the official Herald export (1.126). Step 3 of the missing-items survey
(`Research\Audits\Missing-Items-2026-11-08`): official drops of monsters we already spawn. **Applied to the test world 2026-11-08 20:25: part1 undo
`20261108-202551-be369576`, part2 undo `20261108-202555-e5e15536` (undo part2 first), issue DR-173.**
Two patches (World Workshop takes at most 20,000 edits each): apply **part1 then part2**, undo part2 then part1.
Apply after `item-stat-fixes-2026-11-08` (undo 20261108-195425-50d37591).

## What it adds (29,271 inserts, nothing changed or deleted)

| | |
|---|---:|
| New items (package OfflineMissingDrops): Albion 940, Midgard 881, Hibernia 880, all-realm 351 | 3,052 |
| Items we already had that now drop where officially they do | 2,791 |
| LootTemplate rows, in 1,422 tables for 1,778 monsters | 25,279 |
| MobXLootTemplate links | 744 |
| One-time drops (LootOTD) | 166 |
| Item spells built from the official delve (800000 + official id, Item Effects line) | 15 |

- **New items** copy a world item of the same type, slot and realm, closest in level, for the model, weight and
  colour (193 use their Eve of Darkness twin's model instead). They get the official name, bonuses, procs/charges,
  class list, level requirement, DPS/speed or AF/absorption, shield size and bonus level. Level: from DPS
  (977), AF (1,383), level requirement (192) or the monster's level (494, mostly jewelry). No Champion Level gating.
- **Tables:** a monster's own table, or one shared `official drops <hash>` table when several monsters have the
  same official list (Darkness Falls monsters share 166 items). Family tables are never touched. A monster that
  had its own table but no MobX row gets one for it, so the old drops keep working.
- **Rates:** the loot code keeps at most one random template item a kill. Chances are set so a common monster gives
  one of these items in about 20% of kills and a named one (1-2 spawns) in about 50% (1-15% each). Monsters with
  very long lists (Darkness Falls) sit higher because a chance cannot go under 1%: 42 common monsters at 40-50%.

## Left out

- ToA monsters (the ToA patch did them), artifacts, quest-only/merchant items, and items of our own systems that the
  official list says drop: Atlas orb gear (478), OfflineBountycraft (177), drmed (72), ToA named drops (5).
- 16 official item spells could not be built: 88 new items have no proc/charge instead of a dead id
  (`spells-not-built.json`). 11 drops had no world item of their type to copy.
- Models are a same-type stand-in, not the item's own look (the export has no model). The client survey lists
  unused art to improve this later.

## Checks

Planned against the test world (19,000 + 10,271 edits). Applied both parts on a copy: `dr world lint` no new
findings (missing-table links 506 -> 463), then undone and ItemTemplate, LootTemplate, MobXLootTemplate, LootOTD,
Spell and LineXSpell matched exactly. Not booted, not played.
