# ToA named drops pass (2026-11-08)

Made by `../make_toa_named_drops_edits.py` from the modern test world, the official Herald item export
(`DAoC Offline\Research\Sources\Herald-Item-Database-1.126`) and Eve of Darkness db-public (MIT).
Planned against the test world after patch 20261108-165754-ff92672a. **Not applied to the test world.**

**Needs the server build from `feature/toa-named-drops` first:** spell 812900 uses the new `HealAllOverTime`
spell type, and the installed server throws on a spell type it does not know (`Enum.Parse` in `Spell`).
Apply order with the other two patches of this pass: this one, then `official-item-fixes`, then
`eod-missing-loot-tables`; undo in reverse.

## What was wrong

1. **Named ToA monsters dropped nothing of their own.** 359 ToA monsters have a MobXLootTemplate row (from the
   Atlantis import) naming a LootTemplate table that was never imported. Kraken, Medusa, Cetus, Azar, Zahur and
   the rest dropped only the generic family junk. Same in every release world back to 0.38-dev7.
   (Outside ToA, 1,172 more monster links dangle the same way; not touched here.)
2. **Item stats were wrong.** Of 1,320 ToA drop items with an official record, 1,306 differ. The old import put
   ToA bonuses on the wrong properties: cast speed stored as buff effectiveness, Str/Dex/Con/Qui/Int/Pie cap as
   Fatigue/Heal/Power pool/Resist pierce/Spell damage/Spell duration, Acuity cap as Str cap, Hits cap as Hits,
   All focus as All magic. Many lines were missing (AF on weapons, melee/style damage, Hits cap).
3. **Procs and charges were missing** on about 2,150 official item abilities.
4. **456 official ToA drops were never in the world** (for example the "of the Blaze" weapons of Sutekh and
   Chimera, Medusa's Petrified Gloves).

## What the patch does (5,540 rows)

| Table | Rows | What |
|---|---:|---|
| LootTemplate | 3,667 | 389 tables: the EoD table for each ToA monster, plus every official drop of that monster (chance 15) |
| MobXLootTemplate | 11 | monsters that had only a family row (so their own table was never read) |
| LootOTD | 10 | official one-time drops (Lady Farahnaz's Krojer rings, Abtanu's cloaks) |
| ItemTemplate | 1,306 updated | official bonus lines (all of them, up to 10), procs, charges, reuse timer, bonus level, trade/drop flags |
| ItemTemplate | 456 new | copied from the closest world item of the same type and realm for the model; official name, bonuses, abilities; level 51, DPS 16.5 or AF 102 (cloth 51), official speed or absorption |
| Spell + LineXSpell | 47 + 47 | missing item spells, built from the official delve as 800000 + official id in Item Effects |

Per-item before/after: `items.csv`. Built spells: `spells.csv`. Counts: `summary.json`.

## Choices to know about

- **Official 1.126 is the target**, not EoD's older numbers. Items with no official record keep their stats.
- **"vs Undead, Animals and Humanoid" bonuses** (626 lines, PvE-only on live) are given as plain bonuses: the
  server has no conditional bonus. They make those items a little stronger.
- **Artifacts are excluded** (ArtifactXItem and official artifact records); they have their own pass.
- **Spell 10323** (Dark Knight's Fury, first proc) is a FlexibleSkillBuff: +10 Flexible for 60 s on yourself
  (the official delve says "Bonus: 10%"; our skill buffs add points). Its second proc, omni-lifedrain 812921,
  already existed.
- **Spell 12900** (Robes of the Magus, Timeless Indigo Mail, Xanxicar Remnant Cloak, Freezing Wails Vest) is the
  new `HealAllOverTime` type: 3% of health, power and endurance every 5 s for 30 s, also in combat. It shares
  the heal over time effect slot. The export's one-line summary says 5%; the item delve says 3%, used here.
- Buffs whose delve says "Target: Targeted" are cast on a friendly target (Realm), never the enemy.
- **Models on new items** come from the donor item, so they look like a related drop, not necessarily the
  official model. Not checked with `dr art check`.
- Drop rate: each named monster's own row keeps DropCount 2 (1 for a few), shared with its family junk row.

## Checks done

- `cli.py plan`: 5,544 edits, valid.
- Unit tests `UT_HealAllOverTime` (6) pass; the full suite was not run (the branch is not committed).
- Applied to a scratch copy of the test world (12 s), read back (Kraken, Medusa, Cetus, Zahur, Azar tables and
  items), then `undo`: ItemTemplate, LootTemplate, MobXLootTemplate, LootOTD, Spell and LineXSpell match the
  real world exactly.
- All three patches of this pass applied in order to a fresh copy, link check (no item points at a missing
  spell, no unknown spell type, no loot row to a missing item), then undone in reverse: identical again.
- **Not done:** no server boot with it, no kill in game, no proc or charge used in game.

## Apply

Server stopped, owner's OK: `python cli.py --profile modern plan <edits> --out <patch>` again if the world
changed, then `apply <patch>`; record the undo id. Reaches players only through the next release's world step.

## Sandbox test, 2026-11-08 17:30-17:40

Branch commit d7822adee (6,187 server tests pass) deployed to sandbox `toa-drops` (copy of the test world), all
three patches applied there (sandbox undo ids 20261108-173114-0fef4fb7, 173119-8de12bf4, 173124-8a6ae2da), booted
with bots for about 7 minutes: up in 12 s, 0 exceptions, no spell, item or loot errors. AI tester (Albion
Armsman, level 50):
- `/mob viewloot sim 400`: Azar drops its 3 Albion shields (about 12-14 per 100 kills each); Kraken its 6
  jewels (10-15 each); classic Agidad its restored table (Lava Forged Sword 20, Blackened Granite Shield 7,
  Greater Staff of Dominance 7 ...). Family junk, coin, glass and random items still drop alongside.
- Xanxicar Remnant Cloak use: Heal All Over Time icon, 30 s, a pulse every 5 s. Kraken Beak Necklace use: Str/Con
  buff 75 for 10 min. Equip messages show the official bonus lines.
- Not checked: Medusa and Cetus (not seen after teleporting into their dungeons, a dungeon loading matter), a
  real kill, weapon or armor procs firing, Midgard and Hibernia drops, bots looting the new items.

## Real-kill test, 2026-11-08 18:20-18:50 (sandbox toa-drops, same build and patches as the test install)

AI tester killed each monster with GM `/harm` (respawn set to 2 s; autoloot on, drops read from "X drops the ..."):

| Monster | Character | Kills | Named drops | Also dropped |
|---|---|---:|---|---|
| Azar (Alb, region 73) | L50 Armsman | 25 | Shield of Azar 3, Azar's Guard 3, Azar's Defense 2 | glass 5 a kill, coin, random items, mythirian |
| Kraken (Alb) | L50 Armsman | 25 | Tentacle Belt 5, Beak Necklace 4, Black Water Belt 4, Black Water Bracer 2, Kraken's Eye 1, Eyelid Cloak 1 | fish, old boot, random items |
| Azar (Mid, region 30) | L50 Warrior | 20 | Azar's Defense 6, Shield of Azar 6, Azar's Guard 1 | random chain gear |
| Kraken (Mid) | L50 Warrior | 15 | Eye 4, Bracer 4, Eyelid Cloak 3, Beak Necklace 3, Tentacle Belt 1, Black Water Belt 1 | |
| Azar (Hib, region 130) | L50 Hero | 20 | Arcanite Shield of Azar 4, Azar's Defense 1 | random scale gear |
| kraken (Hib, L65 copy) | L50 Hero | 15 | Beak Necklace 4, Kraken's Lost Tooth 4, Bracer 2, Black Water Belt 2, Kraken's Maw 1, Tentacle Belt 1, Eye 1 | |
| Agidad (classic Alb) | L50 Armsman | 20 | Armor Pattern 4, Blackened Granite Shield 3, Bracer of Wonderous Alacrity 3, Lava Forged Sword 3, Pikeman's Belt 1, Archaic Granite Short Sword 1 | giant bones |
| Amadan (classic Hib) | L50 Hero | 15 | Forgotten Silver Jasper Locket 2, Ring of Undead Might 2, Gloves of Black Death 1 | bone, grave dust |
| arachite greensilk (classic Mid) | L4 Warrior | 20 | Svartalf Crafted Necklace 3, Svartalf Crafted Ring 3 | chitin, insect legs, random studded gear |

Each realm only got its own realm's versions. Procs in real fights (8 fights, L50 Armsman): Arcanium Scintillating Blade's
new lifedrain 803832 fired ("You steal N hit points"); Breastplate of the Cryth's ablative fired ("A crystal shield
covers you", "Your ablative absorbs N damage"). Earlier in the sandbox: Xanxicar cloak HealAllOverTime and Kraken
necklace Str/Con charge work. Server: no exceptions during the runs.

**Found:** every arachite greensilk is level 5 in game (its NpcTemplate says 5; the spawn rows say 27-30), so level
5 spiders drop level 38-40 Svartalf jewelry. 18 of the 1,766 restored tables sit on monsters whose highest live level
is 20+ below their best item (wildling L0 and spriggarn L2 with level 51 items, escaped thrall L3, tomte shaman L10,
arachite hatchling/youngling/priest, oak man, ash, large skeleton ...). Not fixed.
