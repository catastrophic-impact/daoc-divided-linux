# New races (Undead, Drakulv, Dark Elf): cheap-version scope

Status 2026-11-03: scoping only. Nothing is built, patched or applied.
Evidence: gamedata.mpk of the modern test client (`artifacts/modern-client-test/runtime/client-modern/app`),
`source/tools/classslots/patch_class_slots.py`, `source/tools/stormforged/patch_modern_client.py`,
[MODERN-RACES.md](MODERN-RACES.md).

## The idea: a heritage on top of an existing client race

The client draws a player from the **model ID** the server sends (a `monsters.csv` row), not from the
race byte. Illusions already use this: the Catacombs Mummy and Stone forms are just other rows on the
same race body. So a new race does not need a new client race ID:

- The client keeps seeing the race the character was created as (Briton, Norse, Celt...).
- The server stores a **heritage** (Undead / Drakulv / Dark Elf) and uses it for stats, resists, the race
  name in /who, chat and character info, and the model it sends.
- The look is a **private copy** of an existing body row under a new model ID (the pet-art rule: new IDs,
  stock rows untouched). Armor still fits because the body mesh is the stock race body.

No `game.dll` change is needed for this version.

## What the client already has

| Heritage | Base body | Existing rows to copy | Notes |
|---|---|---|---|
| Dark Elf | Elf (Cata 592/593) | **Svartalf Male/Female Shade**, monsters 1389/1390 (Cata Elf Shade 152/155) | Svartalf means dark elf. Shade skins 6146-6155. Stock row sets `Cat No Helm`; the copy clears it. |
| Undead | Briton / Saracen / Highlander / Avalonian / Inconnu | **Mummy** rows 1277-1286 (rotted head skins 6127-6139) | Stock Mummy rows set `Cat No Armor` on every slot; the copy clears those so gear shows. Option: mummy head and arms only, normal body skin. |
| Drakulv | Norse / Valkyn / Troll | **Drakulv Werewolf Revamp**, monnifs 939 (monsters 617/624/625) | Werewolf body: `No Armor`, monster animation set 392. It would never show armor or weapons poses like a player. Not a cheap option; see below. |

### Drakulv choices

1. **Werewolf body, no visible gear**: looks right, plays like a permanent Berserker form. Gear still
   counts but is never drawn. Monster animations, so casting and some weapon poses may look wrong.
2. **Norse or Valkyn body with a fur/scale skin**: armor works, but the head stays human. Needs new
   textures (cheap to make, the head shape is the problem).
3. **Real wolf head on a player body**: NIF work, the expensive tier from the first estimate.

The owner should pick one before Drakulv is built. Undead and Dark Elf can go ahead without it.

## How a player gets the heritage

The creation screen filters classes by the race chosen there, and the pasted class lists do not match
any one stock race (Dark Elf wants Bard, Druid, Hero, Valewalker, Warden; none are Elf classes). So:

- Create any race of that realm with a class from the heritage's list.
- On first login (or from a heritage NPC at the start points), choose the heritage. The server checks the
  realm and class list, sets the heritage, swaps the model, applies the stats, then saves.
- Size (Short/Average/Tall, bits 11-12 of `CreationModel`) is kept when the model is swapped.

Putting the heritage on the creation screen itself is the `game.dll` route (new race buttons, preview
model, class lists per race). It is possible with the `patch_class_slots.py` technique but is a separate,
larger job.

## Server work

- `DbCoreCharacter`: a heritage column (0 = none). Migration through World Workshop.
- Heritage table: name, realm, allowed classes, base bodies, private model IDs per base race and gender,
  starting stat changes, resists.
- Model: creation, release, Shade/Bainshee/Costume/Berserk resets and `/player` all restore
  `CreationModel`; the swap writes the heritage model into `CreationModel` so every reset returns to it.
  Check `ClassBainshee.cs:126` and `BerserkECSEffect.cs:48`, which pick models by race.
- Race name and resists: about 300 race references in 61 files keep working because the base race does
  not change. Only the display name and resist/stat lookups need the heritage.
- Bots: gamebots and companions can roll heritages later; not part of the first cut.
- Character Passport: carry the heritage column; a world without the private model rows falls back to
  the base race look.

## Client work

- New `monsters.csv` rows in `gamedata.mpk` (private IDs; check free IDs below 2494 as the world editor
  does). Dark Elf: 2 rows. Undead: 2 per base race used (8 for Briton, Saracen, Highlander, Avalonian).
- No new textures for Dark Elf or Undead in the first cut.
- Every shipped client needs the new `gamedata.mpk`; a client without the rows will draw the wrong model.
  It goes into the client patch like other catalog changes.

## Unknowns (need an in-game check)

1. Whether face, hair, eye colour and the face sliders from creation still apply on a copied row, or
   whether the copy's fixed head skin replaces them. Mummy and Svartalf rows set their own head skin.
2. What the Svartalf Shade skins look like on a player body (they may be translucent "shade" skins and
   not dark-elf skin).
3. Whether the character select screen draws the heritage model or the base race.
4. Which client-only panels show the base race name (character sheet). Server text is fine.

## Rough cost

| Piece | Effort |
|---|---|
| Preview the two looks (copied rows, test login, screenshots) | half a day |
| Server heritage column, table, model swap, name/stats/resists, tests | 1-2 days |
| Heritage choice (first-login dialog or NPC) | half a day |
| Client rows and patch packaging | half a day |
| Drakulv | depends on the option chosen |

Dark Elf and Undead: about 3 days, plus play-testing. Bots and Passport support after that.
