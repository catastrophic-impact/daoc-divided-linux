# Full buff potions and barrels: proposal (not built)

Player suggestion (Discord, 2026-11-08): "full buff potions/barrels similar to those on eden/phoenix. Not capped
buffs of course, but maybe blue/yellow of base/specs in 1 potion."

Status: proposal only. Nothing is built or in the world. Everything below needs the owner's decisions first.

## What already exists to build on

- **One potion, several buffs:** `GameServer/spells/AllRegenBuff.cs` (spell type `AllRegenBuff`, spell 31052).
  It starts three sub-spells from the `Potions_Effects` line. The world uses it for the Draught of Combined Regens
  (10 charges) and the Barrel of Combined Regens (100 charges). Both are sold by 3 merchant entries.
- **Single-stat alchemy elixirs:** Strength / Fortitude / Dexterity 20 (31039-31041), Enlightenment acuity 35 (31042),
  Might str/con 35 (31043), Deftness dex/qui 35 (31044). All last 20 minutes.
- **What the world's level 50 class buffs reach** (`Spell` rows on baseline lines up to level 50, test world): base
  AF 250, single stat 48-70, str/con and dex/qui 75, acuity 62-75, haste 25. Before a build these need checking per
  realm and per line, because the query mixes hybrid and pure lines.

## The proposal

**One new spell type, `FullBuffPotion`.** It works like `AllRegenBuff`, but reads its sub-spell ids from data
(for example a comma list in the spell's `LifeDrainReturn` or a small table) instead of hard-coded ids. Each sub-spell
is an ordinary buff in `Potions_Effects`, so:
- it stacks with class buffs the normal way: the higher value in the same effect group wins, nothing doubles
- it shows in the buff window, ends on its timer and is purged by normal dispels

**Contents ("blue/yellow"):** about 60-75% of a level 50 class buff. That is clearly better than today's 20-35 elixirs
and clearly below a buffbot.

| Slot | Buff type | Suggested value | Notes |
|---|---|---|---|
| Base AF | BaseArmorFactorBuff | ~150 | |
| Strength | StrengthBuff | ~35 | |
| Constitution | ConstitutionBuff | ~35 | |
| Dexterity | DexterityBuff | ~35 | |
| Str/Con | StrengthConstitutionBuff | ~50 | |
| Dex/Qui | DexterityQuicknessBuff | ~50 | |
| Acuity | AcuityBuff | ~45 | casters only, so it could be its own "caster" potion |
| Optional | CombatSpeedBuff, regen | - | the owner decides; haste is the strongest single slot |

Duration: 20 minutes like the elixirs, or longer for barrels.

**Forms:**
- **Potion:** 1 use, self only.
- **Barrel:** many charges, like the Combined Regens barrel. It could also be group-targeted for raids, which is
  how most freeshards use barrels.

**How players get them (pick one or both):**
1. **Alchemy.** These are new recipes at about 1000-1100 skill, with drop-only catalysts such as Ancient Giant Blood
   or Enriched Quicksilver, which keeps them a high-end craft. One catch: the 1.130 client builds its crafting
   window only from its own `tdl.crf`, so a brand-new recipe id never appears there. Players could only make it if
   we patch `tdl.crf` (client patch, hash-guarded) or add a crafting NPC or `/craft` command. The existing Combined
   Regens recipes (30011-30015, 20513-20523) have the same problem: the client does not list them.
2. **Merchant.** Sold for gold or BPs at a capital alchemist. This is simplest and needs no client change. Price it
   so that it is a real gold sink.

## Balance questions for the owner

1. Strength: what fraction of a level 50 class buff? Is the ~60-75% above right? Should spec buffs be in at all, or
   base buffs only?
2. Haste and regen: in or out?
3. Who gets it: players only, or do autonomous gamebots buy and drink them too? Bots buying them changes PvP balance
   against player groups.
4. RvR: allowed in RvR zones and battlegrounds, or PvE only (like some freeshards' "PvE buff" potions)?
5. Source: alchemy (needs the client recipe question solved), merchant, or both? What price or BP cost?
6. Self-only potion, group barrel, or both? How many charges?
7. Does it replace or sit beside companion buff bots? Today a player's own buffer bot covers this role.
8. Stormforged and other custom classes: any interaction to rule out? (No nerfs to Stormforged.)

## Effort once decided

- Code: about 1 day for the `FullBuffPotion` handler with tests, built from `AllRegenBuff`.
- World: new sub-spells, 1 parent spell, item templates per realm, and merchant rows or recipes. These go in through
  the World Workshop CLI with an undo id.
- Client: only if it becomes an alchemy recipe (a `tdl.crf` patch).
