# 1.130 recipes, step 2: names and the Legendary materials (2026-11-08)

Generator `../make_craft_recipes_130_edits.py` (the phase 1 generator, rerun on the world after phase 1). World only;
no server build needed.

## What it adds (2331 row edits: {'ItemTemplate': 3, 'MerchantItem': 9, 'CraftedItem': 445, 'CraftedXItem': 1874})

**445 recipes** (Alb 147 / Mid 142 / Hib 156), all on products the world already has:
- Legendary: 167
- Spell Stone gems: 109
- Rune gems: 79
- Sigil gems: 49
- Dragonsworn: 41

- The client often names a product only inside its category: "Faceted +7" in "Glacial War Rune (Fist Wraps Skill)" is
  the world's "faceted glacial war rune"; "war hammer" in "Dragonsworn Weapons" is "Dragonsworn War Hammer". The
  generator now tries those names too (the first one with an item at the right level wins).
- Material tier 89 is "perfect": Legendary weapons take "3 Perfect Fiery Essence Jewel" (Allakhazam recipe 6424).
- **Otherworldly Ore** (20 per Legendary weapon), **Orb of Entropy** and **Pound of Flesh** are new items, sold at the
  three high-tier craft merchant lists (AlbExpensiveTrade, MidExpensiveTrade, HibExpensiveCraftComponents; e.g. Luke
  in Camelot). Live gathers them in the Otherworlds, which this world lacks (Pendragon hot fix 2015, Otherworlds
  chapter 7). Prices are this project's choice: ore 1 gold, orb / pound 50 gold. The ore uses model 488, not the
  metal bar model: the server picks an ingredient's secondary skill by model.

## Checks

- Applied to a scratch copy of the test world: all 445 recipes pass Recipe.LoadFromDB's rules.
- Sandbox `bountycraft`, `scenarios/craft-legendary.json` (Albion Armsman, Weaponcraft and Metalworking 1100 by GM
  command): bought 20 Otherworldly Ore from Luke for 20 gold, crafted the Legendary Lithic Bishops Mace at the Camelot
  forge ("You successfully make the Lithic Bishops Mace! (98)"), ore and materials used. No log errors. Not played.

## Still missing after this

1539 non-Bountycraft recipes; see `report.json`. Entropic and Somatic Legendary weapons (2015) have no
products in the world; the rest are the formula-built families listed in Research/Scopes/CRAFTING-1130.md.
