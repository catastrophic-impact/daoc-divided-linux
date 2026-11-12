# Item spells, round 2 (2026-11-08)

Generator `../make_item_spells_edits.py`, rules in `../herald_items.py`. **Needs the server build with
`eSpellType.SpecSkillBuff`** (feature/bountycraft 0c594556c): older DLLs throw on the unknown spell type.

## What it adds (136 row edits: {'Spell': 27, 'LineXSpell': 27, 'SpellXCustomValues': 17, 'ItemTemplate': 65})

- **17 weapon skill buffs** (SpecSkillBuff + a SpellXCustomValues `BuffProperty` row with the skill's eProperty):
  Sword, Slash, Thrust, Crush, Axe, Left Axe, Hammer, Spear, Scythe, Blades, Blunt, Staff, Dual Wield, Celtic Dual,
  Critical Strike, +10 each (the world's convention for the official "10%", as FlexibleSkillBuff 803380). Different
  skills stack. The skill is taken from the official description ("Increases your Dual Wield by 10%"); the delve's
  "Skill raised" line can name the weapon's own skill instead.
- **3 procs with their real second spell**: 803686 offensive proc -> disarm 4 s; 807678 offensive proc -> cold resist
  debuff 25 for 12 s; 803657 defensive proc -> 100% absorb shield of 150 for 45 s. The delve parser now keeps a proc's
  "Sub-spell:" fields apart (before, they filled the proc's own gaps).
- Damage to Power 803451 / 807687 (DmgReductionAndPowerReturn), Bedazzle miscast 803476 (CastingSpeedDebuff, as the
  world's own Bedazzled 826042), Stealth Lore 806952 (StealthSkillBuff 35).
- **65 Bountycraft items** get these charges / procs. Items still missing at least one: 91 of 749
  (was 156).

## Sandbox check (sandbox `bountycraft`, build 0c594556c, this patch)

`scenarios/item-spells-2.json`: Uther's Otherworldly Resilient Cloak's first charge "Your senses sharpen and your
steps grow quieter." (Stealth Lore), second charge (defensive proc) applied; `/cast spell 803383` and `803379` on
self: "Your sword skill improves!", "Your crush skill improves!" (both kept). No exceptions. Not checked: a proc
actually firing in a fight, the skill value in the client's skill window.

## Still not built (33 spells)

No data in the official records (the newer items print a sentence without numbers: damage reduction, speed +15%,
omni heal 12833, illusions 13604-13606, resist enhancement 7707), or a mechanic with no handler (summons, buff shears,
quick-cast reset, damage reflection, debuff resistance, cost reduction, Arcane Leadership, crit chance, all-stats
penalty, Underhill pet command). `report.json` lists each.
