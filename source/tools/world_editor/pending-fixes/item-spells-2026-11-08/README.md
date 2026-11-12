# Item spells for the Bountycraft products (2026-11-08)

Generator `../make_item_spells_edits.py` (rules in `../herald_items.py`). Needs the server build with the
EnduranceHeal fix (feature/bountycraft 96c7a3005) for the omni heals' endurance part; everything else works on the
installed build.

## What it does (333 row edits)

- **41 new item spells** (800000 + official id; two-part spells add sub spells 850000 / 860000 + id and
  4 SpellXCustomValues MultipleSubSpells rows): Arrogance, omni heal (Heal + PowerHeal + EnduranceHeal), cure disease +
  poison, damage conversion, mesmerize duration reduction, heal bonus, melee damage boost, style damage shield,
  resistance buffs and debuffs, bedazzle (fumble), raise dead, wave of healing, efficient healing, attack speed,
  parry, Con debuff, spread heal, melee absorption, boon of kings, illusion (to-hit part), mesmerize, disease.
  Each uses the handler the world already uses for that live function (examples in `herald_items.rules_item_spells`).
- **222 Bountycraft items** get their charge / proc columns.
- **11 spells corrected** that the converter built before from the wrong delve block (a "Secondary
  Charged Magic Item:" header was not recognised, so position 2 read position 1's text). Real errors among them:
  803483 (Heal -> melee resist buff), 803484 (heal over time -> lore debuff), 809576 (Str/Con -> heat resist),
  810535 (Heal -> casting speed debuff), and from the ToA named drop pass 801592 (power heal -> power regen 4),
  803403 (shield 75 -> 250), 812051 (range 2000 -> 1000); the rest fix a duration or target.

## Not done

- 57 official spells still have no rule (156 of 749 Bountycraft items
  still miss at least one charge or proc; their bonuses are complete). Mostly one-offs with no matching handler:
  weapon skill buffs (Sword, Axe, ... by 10%), procs that cast a second spell (offensive / defensive proc), summons,
  buff shears, cost reduction, arcane leadership, quick-cast reset, damage reflection, mesmerize immunity "next
  spell" is approximated as the world's CCResist 20 (808711).
- 813354 (built as Heal 230) is a stat enhancement whose official record has no numbers, and ToA 812050 (built as a
  0-damage direct damage) is Siege Lore; both are left as they are.
- Bedazzle's miscast half (3476) has no handler; only the fumble half (3477) is built.

## Sandbox check (sandbox `bountycraft`, build 96c7a3005, this patch)

`scenarios/bountycraft-item-spells.json`, Albion Armsman: Uther's Otherworldly Hardy Cloak's omni heal healed 18%
(capped at the 93 missing hit points), the Gem of the Harbinger's Arrogance applied, the Otherworldly Mighty Ring's
second charge (cure) fired; no exceptions in the logs. Not checked in game: power / endurance parts (the Armsman has
no power and was at full endurance), the procs.

Unbuilt spells and why: `report.json` ("spells not built").
