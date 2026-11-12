# Official item fixes, outside ToA (2026-11-08)

Made by `../make_official_item_fixes_edits.py` (reuses the ToA generator's matching and spell builder) from the
official Herald export. **Not applied.** Apply after `toa-named-drops`, before `eod-missing-loot-tables`.

## What it changes (3,010 rows)

- **2,754 items.** Only plain import rows (packages Public_DB, dolpubdb, ClassicQuest, none), never one an earlier
  World Workshop patch touched (2,044 skipped), an artifact, a Mythirian, Atlas orb gear or our own packages
  (3,522 skipped).
  - Bonuses set to official where ours is an import error: 643 "renumbered" (same values on other property
    numbers, e.g. cast speed stored as buff effectiveness, caps as other bonuses, flat power instead of the live
    % power pool), 777 "lines added" (official lines ours lost), 287 both.
  - Procs and charges set to official on 1,537 items that had fewer (often none).
- **128 item spells** (+ Item Effects links), built from the official delve as 800000 + official id.

## Listed for the owner, not changed (`items.csv`)

- **2,383 "other version" items:** our values differ from official 1.126 (older or newer versions of the item,
  e.g. Demon-Bound jewelry: Power 3 here, Hits 60 officially). 533 of them still get their missing procs/charges.
- **161 items with mythical bonus codes** the converter does not map yet (types 55-80).
- **100 items whose official abilities have no spell rule yet** (endurance drain, melee absorption debuff,
  bladeturn, stun, summon elemental, spreadheal, damage conversion and some with no delve function).

## Checks

Planned (3,010 edits), applied on a copy after the ToA patch, link check clean, undone exactly. Not booted.
