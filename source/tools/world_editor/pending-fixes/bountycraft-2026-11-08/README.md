# Bountycraft world patch (2026-11-08)

Generator: `../make_bountycraft_edits.py` (uses `../herald_items.py` and `../make_craft_recipes_130_edits.py`).
Plan with `cli.py --profile modern plan bountycraft-2026-11-08.edits.json`; the patch is `bountycraft-2026-11-08.patch.json`.

**Needs the feature/bountycraft server build first.** Without `eCraftingSkill.BountyCrafting` the recipes are
skill 5 rows that no player has, and the box and mold limits do nothing.

## What it adds (5,317 row edits)

| Table | Rows | What |
|---|---|---|
| ItemTemplate | 716 | 12 materials (Stardust, 3 molds, 7 "Recipe:" items, Box of Rare Alch. Components) and 704 products |
| CraftedItem | 974 | 971 Bountycraft recipes (client ids, trade 5) + 3 Alchemy 1000 Stardust recipes |
| CraftedXItem | 3,447 | ingredients, the client's names and counts |
| Spell / LineXSpell | 36 / 36 | item spells 800000 + official id, built from the official delve text |
| MerchantItem | 107 | 11 Bounty Master items on the 4 bounty lists (13 merchants); Stardust on 63 alchemy merchant lists |
| ServerProperty | 1 | `allow_craft_norealm_items` = True (below) |

Prices: molds 5,000 / 6,250 / 7,500 BP; recipes 50,000 (Pictslayer) to 175,000 (Curse) BP; box 2,500 BP;
Stardust 2.5 platinum (1.126 values). Products sell for nothing (official "cannot be sold").

Products: 105 recipes use an item the world already has; 704 new items from the official 1.126 Herald record
(found by the official id in the client recipe, else by name with the "Class - " prefix and "(Cloth)" style suffix
read): official bonuses, charges and procs, class limits, DPS 16.5 / AF 102, the official model, level 51 (Mythirians
50). No Champion level requirement (owner rule).

## Realm-0 products

The 1.130 client lists realm-0 products (the shared Bountycraft items, and from the 1.130 recipe pass the Mauler staffs,
horse gear, metal and cloth conversions...) in every realm's window, but `NormalServerRules.IsAllowedToCraft` refuses a
realm-0 product unless `allow_craft_norealm_items` is on ("that recipe is for no realm"). Found by the sandbox run;
296 recipes in the test world were refused this way before this patch.

## Sandbox check 2026-11-08 (sandbox `bountycraft`, build 944c9eded, this patch)

`OfflineDaoc.TestClient --scenario scenarios/bountycraft.json`, a level 50 realm rank 1 Albion Armsman (player account):
all 28 steps pass. The Camelot Bounty Master sells the molds one at a time ("At your realm rank you can buy 1 Bounty
Accessory Mold at a time.", 5,000 BP each) and Recipe: Seasonal (100,000 BP); crafting away from a forge is refused;
at a Camelot forge "You begin working on the Midwinter's Frozen Medal of Honor. (100%)" then "You successfully make
the Midwinter's Frozen Medal of Honor! (100)"; the materials are used up; the box opens for 2-5 of one component.
Server logs: no exceptions, no [CRAFTING] warnings. Undo and re-apply of the patch work.

Not checked: the 1.130 client's own trade skill window (the test client has no UI), Midgard and Hibernia, a
realm-only product, Stardust at an alchemy merchant, the Alchemy 1000 Stardust recipe.

## Known gaps

- 46 client recipes skipped: 45 have no official record in the 1.126 export (the Aurulite "Unyielding ... Vest"
  chestpieces and a few weapons, added after 1.126); 1 world item already has other ingredients.
- 96 official item spells have no conversion rule yet (Arrogance, Damage Conversion, Heal Bonus, illusions,
  chained procs, weapon skill buffs...). 332 of the 704 new items lose at least one charge or proc; their bonuses are
  complete. `summary.json` lists every spell and why.
- Not played. The client's display of trade 5 has not been seen in game.
