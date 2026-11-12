# Item stat fixes (2026-11-08)

Made by `../make_item_stat_fixes_edits.py` from the official Herald export (1.126), after the missing-items survey
(`Research\Audits\Missing-Items-2026-11-08`). Owner asked for item stats to be brought up to date.
**Applied to the test world 2026-11-08 19:54, undo `20261108-195425-50d37591`, issue DR-171.** Applied after `weapon-damage-types-2026-11-08` (undo 20261108-194549-cadd6694); undo this one first.

## What it changes (3,205 items, ItemTemplate only)

| Change | Items |
|---|---:|
| Bonus lines set to the official ones (includes the "other version" items the earlier pass only listed) | 1,870 |
| DPS / AF corrected (within 10% of ours, e.g. 16.2 -> 16.5 DPS, AF 98 -> 100) | 1,008 |
| Weapon speed / armour absorption set to official | 852 |
| Realm 0: all-realm officially, stored for one realm, no other realm copy, no class list | 643 |
| Shield size (Type_Damage on shields) | 57 |

By realm: Albion 1,109, Midgard 1,052, Hibernia 966, all-realm 78. No spells added, no procs/charges changed (the
earlier official pass already gave every buildable one).

## Left alone

- **780 items** whose official DPS/AF is more than 10% from ours: the export has no item level, so the record is
  another level's version of the item (or a ToA scaling placeholder, DPS 3.0 / AF 16). Listed in `items.csv`.
- Quality (official "base quality" is a drop baseline, not a fixed value), item Level (not in the export).
- Rows another non-official World Workshop patch created or changed (2,044), our own packages (4,240), ToA named
  drops and artifacts (1,944), Mythirians (85), Stormforged/Sovereign class items. No Champion Level requirement set.
- Items whose official bonus codes do not convert, or have more than 10 lines (listed).

Some items get weaker (e.g. Scarlet Gloves of the Covetous 16/16/16 -> 7/6/6): that is the official 1.126 version.

## Checks

Planned against the test world (3,205 edits). Applied on a copy: bonus types all within eProperty (max 261), then
undone and the ItemTemplate table matched exactly. Not booted, not played.
