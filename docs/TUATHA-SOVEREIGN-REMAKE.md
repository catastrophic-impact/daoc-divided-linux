# Tuatha Sovereign remake: Scythe, Blades and Shield

Status: design for owner review, 2026-11-06 (second draft: its own Hibernian identity, not a mirror of the Storm).
Nothing built. Branch `feature/tuatha-remake` (from bot-ai-trips d2700336b). Still overpowered on purpose.
The Storm (64) and Pendragon (65) kits are not touched.

## Identity

The Storm Sovereign is a burst-and-thunder king: it builds charges on itself and spends them in big lightning blows.
The Tuatha Sovereign is a **Sidhe king of Tir na Nog**: it **marks** its enemies with glamour, wears them down with spirit
rot, bleeds and roots, and **detonates** the marks for drains and party healing. It fights with Hibernian tools: evasion,
roots and mesmerize, spirit damage, regeneration, a faerie companion, and the two faerie **courts**.

| | Storm Sovereign | Tuatha Sovereign |
|---|---|---|
| Resource | Tempest charges on yourself | **Sidhe Marks** on each enemy (up to 5 per enemy) |
| Spending | Finishers spend all charges | **Detonations** burst marks on one target or every marked enemy nearby |
| Choice | Weapon setup | Weapon setup **and court**: Seelie (summer, heal and ward) or Unseelie (winter, rot and drain) |
| Defense | Block and parry | **Evade**, parry and block; thorns that hurt attackers |
| Control | Stuns | Stuns, **roots** and **mesmerize** |
| Damage | Energy and cold | **Spirit**, bleeds and rot |

## Weapons

**Scythe, Blades, Shields, Parry, Stealth**, all training with level, plus **Evade** (Evade I at 1, rising to Evade VII
by 50) as a class ability, like Hibernia's blade classes. Blunt, Piercing, Large Weapons and Celtic Spear stop training
and their styles go. Characters keep every item they own; loot and bots stop rolling those weapons for this class.

| Setup | Weapons | Plays as |
|---|---|---|
| Reaper | Scythe | Sweeps that mark whole packs, then one detonation for all of them |
| Duelist | Blade and shield | Evade and parry reactions, bleeds, single-target executes, the court's guard |

## Sidhe Marks

- Styles put marks on the enemies they hit (most 1, openers and some reactions 2-3, a few mark every enemy nearby).
  Up to **5 marks per enemy**, each Tuatha's marks are their own, and marks fade after 20 seconds without a new one.
- **Detonations** (finishers, Spirit Lance, Otherworld Burst) remove marks for **+25% damage per mark**, and in the
  Unseelie court heal you for part of it.
- **Showing the marks (owner's choice, 2026-11-06): effect flashes, no orbs.** Each mark that lands plays a stock spirit or
  faerie effect on the enemy, a bigger one at 3 marks and again at 5, and a detonation plays a burst sized by the marks spent.
  Nothing stays on screen between hits. The Warlock chamber orbs stay the Storm's alone.
- Blocks, parries and evades put 1 mark on the attacker (2 with the full Regalia).

## The two courts

One button, **Turn of the Courts**, swaps between them (10-second reuse). Your current court shows as a buff.

| | Seelie Court (summer) | Unseelie Court (winter) |
|---|---|---|
| Detonations | heal your party 3% per mark burst | drain: you heal 50% of the damage |
| Marks | - | each mark ticks spirit rot on its enemy |
| Wards from styles | 50% stronger | - |
| You | +10% evade, +5% healing taken | +10% critical chance |
| Fairy Ring | heals allies inside | drains enemies inside |
| Court of the Sidhe chant | adds health regeneration | adds a spirit damage add |

## Styles (61)

Unlock levels follow the usual Sovereign spacing (1, 3, 5, 8, 10, 13 ... 48). Openers 0.85 growth, steps 1.1,
detonations 1.45, capstones 1.5, signatures 1.6. Stuns, roots and mez use normal immunity.

### Scythe: Reaper of the Unseen (two-handed, marks packs, mass detonations)

| Lvl | Style | Opens from | Effect |
|---:|---|---|---|
| 1 | Reaper's Arc | anytime | Sweep: hits the target and 2 enemies near it; 1 mark each |
| 3 | Gleaning | Reaper's Arc | Sweep again, 1 more mark each, 30% snare |
| 10 | Unseen Harvest | Gleaning | **Detonates** the marks on every enemy within 350 |
| 5 | Moonlit Counter | after you parry | Spirit strike, 2 marks |
| 13 | Waning Crescent | Moonlit Counter | Spirit sweep within 350 of the target, 1 mark each |
| 38 | Pale Moon | Waning Crescent | **Detonates** within 350; enemies that had 3+ marks are stunned 3 s |
| 8 | Thresh | anytime | Wide sweep: target and 4 enemies near it for 60% |
| 28 | Winnow | Thresh | Attack speed -25% 12 s on everything it hits |
| 40 | Grain of the Dead | Winnow | **Detonates** within 350; court rider doubled |
| 18 | Crescent Hook | beside target | 40% snare, 1 mark |
| 23 | Bind the Sheaf | Crescent Hook | The target and enemies within 250 are **rooted** 8 s |
| 20 | Reaper's Shadow | behind target | Heavy blow, 2 marks |
| 25 | Sickle of Samhain | Reaper's Shadow | Spirit rot (12 s), 1 mark |
| 43 | Last Harvest | Sickle of Samhain | Capstone: **detonates** the target, marks count double below 25% health |
| 15 | Veiled Reaping | stealth | Great opener, 3 marks |
| 30 | Silent Scythe | Veiled Reaping | 3 s stun |
| 33 | Feast of Samhain | anytime | Strike that heals you for 30% of it |
| 35 | Moonfeast | Feast of Samhain | Heals your party 10% |
| 45 | Call of the Morrigan | anytime | Every enemy within 500 gets 2 marks |
| 48 | Wings of the Morrigan | Call of the Morrigan | Signature: **detonates** everything within 500; drains heal your party |

### Blades: the glamour duelist (evasion, bleeds, single-target kills)

| Lvl | Style | Opens from | Effect |
|---:|---|---|---|
| 1 | Thornkiss | anytime | Bleed (12 s), 1 mark |
| 3 | Rosecut | Thornkiss | 2 marks |
| 10 | Briar Crown | Rosecut | **Detonates** the target; each mark also adds a bleed tick |
| 5 | Fey Sidestep | after you **evade** | Strike, 2 marks; you gain +10% evade 10 s |
| 13 | Glamour Flurry | Fey Sidestep | Two quick strikes, 1 mark each |
| 38 | Dance of Lugh | Glamour Flurry | **Detonates**, your Faerie Drake strikes too, 3 s stun |
| 8 | Silver Riposte | after you parry | Strike and 30% snare, 1 mark |
| 28 | Mirror of Manannan | Silver Riposte | You gain +20% evade and parry for 10 s |
| 40 | Hand of Nuada | Mirror of Manannan | 4 s stun; you gain 20% absorption 10 s |
| 18 | Thorn Hook | beside target | Strike, 40% snare, 1 mark |
| 23 | Wither | Thorn Hook | Attack speed -25% 12 s |
| 20 | Backthorn | behind target | Bleed, 50% snare, 2 marks |
| 25 | Gouge of the Sidhe | Backthorn | +25% per mark on the target (does not spend them) |
| 43 | Sidhe's Claim | Gouge of the Sidhe | Capstone: **detonates**, +15 critical chance, doubled below 25% |
| 15 | Glamoured Strike | stealth | Great opener, bleed, 3 marks |
| 30 | Fade and Fall | Glamoured Strike | 3 s stun |
| 33 | Moonsip | anytime | Strike that heals you |
| 35 | Dew of Tir na Nog | Moonsip | Heals you 10% and breaks snares and roots |
| 45 | Mark of the Morrigan | anytime | Fills the target to 5 marks; its resists -15% for 15 s |
| 48 | Queen's Blade | Mark of the Morrigan | Signature: **detonates** at double strength and bursts every bleed you have on it |

### Shields: the thornwarden (thorns, roots, the court's guard)

| Lvl | Style | Opens from | Effect |
|---:|---|---|---|
| 1 | Tara's Rebuke | anytime | Taunt, 1 mark |
| 3 | Bramble Guard | Tara's Rebuke | Thorns on you 15 s: attackers take spirit damage |
| 10 | Crown of Thorns | Bramble Guard | 4 s stun |
| 5 | Hold the Hill | after you block | Ward on you |
| 13 | Thornwall | Hold the Hill | Spirit thorns within 350, taunts and marks them all |
| 38 | The Undying Court | Thornwall | Ward on your whole party |
| 8 | Brehon's Bash | after you block | 3 s stun |
| 28 | Rooted in Tara | Brehon's Bash | The target and enemies within 250 are **rooted** 8 s |
| 40 | Guardian of Tara | Rooted in Tara | Party absorption +15% for 10 s |
| 12 | **Thornslam** | anytime | **9-second stun** (normal immunity), 1 mark |
| 18 | Shield Rush | beside target | 40% snare |
| 23 | Crushing Rim | Shield Rush | 2 s stun |
| 20 | Hill-Fort Slam | in front | Interrupts, 1 mark |
| 25 | Wall of Tara | Hill-Fort Slam | +30% block for 8 s |
| 43 | Throne of the Sidhe | Wall of Tara | Capstone: taunts everything within 500 and **detonates** them |
| 15 | Glamoured Ambush | stealth | 4 s stun, 2 marks |
| 30 | Ambush Guard | Glamoured Ambush | Ward on you |
| 33 | Rowan Bulwark | anytime | Heals you 5%, thorns 10 s |
| 35 | Embrace of the Court | Rowan Bulwark | Heals your party 10% |
| 45 | Oath of the Fianna | anytime | Party +10% evade and block for 15 s |
| 48 | Thornwall of Tara | Oath of the Fianna | Signature: party ward and a thorns aura around you for 15 s |

## Spells and abilities (15 buttons, down from 23)

| Ability | Unlock | What it does |
|---|---:|---|
| Spirit Lance | 1 | 2.5 s spirit bolt that **detonates** its target's marks; instant if the target has 3+ |
| Court of the Sidhe | 1 | Maintained party chant (speed, regeneration, resists); the court adds its rider |
| Armor of the Crown | 1 | Self buff, stats and resists |
| Faerie Drake | 5 | One companion (one at a time); its breath puts 1 mark on what it hits |
| Turn of the Courts | 10 | Swap Seelie and Unseelie (10 s reuse) |
| Bramble Bind | 11 | Roots the target and enemies within 300 for 15 s; 1 mark each |
| Tir na Nog Renewal | 11 | Party heal over time, 15% over 12 s; 30 s reuse |
| Song of Dreams | 20 | **Mesmerizes** enemies within 350 for 20 s (breaks on damage); 60 s reuse |
| Faerie Step | 15 | Step behind an enemy within 1,500 (opens back styles); 1 mark; 15 s reuse |
| Wildwood Armament | 11 | Maintained: scythe hits splash 1 mark, blade hits open bleeds, thorns on every block, parry or evade; extra magic resist |
| Veil of the Sidhe | 30 | Maintained stealth benefit |
| Otherworld Burst | 31 | Spirit burst within 350 that **detonates** every marked enemy hit |
| Otherworld Ascendant | 40 | 60 s Sidhe lord form: marks land double, detonations echo once, both courts' riders at once |
| Fairy Ring | 45 | Ground ring: heals allies (Seelie) or drains enemies (Unseelie) inside, in Hibernian green |
| Will of the Tuatha | 45 | Break crowd control (unchanged) |

Gone from the old list: the three decrees, Sovereign's Renewal, Rally to the Crown, Sovereign's Challenge, Glamoured Ambush,
Glamour Ward and Thorn Reversal (their jobs move to the courts, Tir na Nog Renewal, the shield styles and Wildwood Armament);
Tara's Judgement and Ring of the Sidhe fold into Spirit Lance and Otherworld Burst; Step Between Worlds becomes Faerie Step.

## Realm abilities

Drop the caster picks (trained ones stay trained), keep the Tuatha_* Sovereign abilities, add:
- **Morrigan's Edge:** detonations +5/10/15%.
- **Heart of the Court:** marks last 20/30/40 s and each detonation leaves 0/1/1 mark (2 at rank 3).
- **Evasion of the Sidhe:** evade +3/6/9% and thorns 10/20/30% stronger.

## Regalia of Tara (armor set)

Seven level 51 pieces, Tuatha only, not tradeable, from the Brehon of Tara at level 50 (whisper "Regalia of Tara").
**Scale at AF 102 / 27%**, Hibernia's best. Each piece: stats, resists, a skill (+4 Scythe, Blades, Shields or Parry) and the
ToA lines a Tuatha rolls on loot (11 lines), so the set reaches the ToA caps.

| Piece | Ability |
|---|---|
| Helm | Use: **Eyes of the Sidhe**: every enemy within 1,000 gets 2 marks (3 min) |
| Hauberk | When struck (10%): **Briarward**, a 200-point ward |
| Sleeves | When struck (10%): the attacker is **rooted** 6 s and marked |
| Cloak | Use: **Mantle of Mists**: 30% evade and stealth-speed for 20 s (5 min) |

Set bonus: 2 pieces +5% all resistances; 4 pieces +10% melee and style damage; 6 pieces +10% evade and spell damage;
all 7: +10% maximum health and 2 marks from every block, parry or evade.

### Art

New private art for the modern test client only, made like the Stormforged set: painted textures on the stock Hibernian
scale meshes under new IDs, a shoulder-pad body variant, a recoloured stock helm and a cloak skin. Proposed look:
**a Sidhe king of Tir na Nog**: dark emerald and moonsilver scale, gold knotwork and leaf engraving in the polished
"black crack" style, a silver antlered helm, and a deep green cloak with a moon-and-stag emblem and a pale fur collar.
Concept previews first; nothing installs until the owner picks a look. The items can go in on stock scale art first and
switch to the new art later.

## How it gets built

1. Shared groundwork with the Storm code (the orb packet, the style-table loader, the set-bonus and trainer-grant helpers),
   with the Tuatha's own mechanic: marks per enemy, courts, detonations. Own ID ranges, checked free first.
2. `tuatha_remake_styles.json`, the effects, the spells, three realm abilities, Evade, loot and bot specs.
3. World edits through World Workshop (plan, copy, then the real world with the owner's OK), with undo.
4. Unit tests for every style, mark, court and spell rule; a private-copy boot; an AI tester run before handing over.
5. The armor art in its own pass after the class works.

## Vampiir hover (owner, 2026-11-06)

The owner wants the Tuatha to hover like a Vampiir **at any speed**, without the Vampiir skin. The hover-only client patch
(`TUATHA_HOVER` in `patch_class_slots.py`, written 2026-10-25, checked in emulation, never played) is the starting point;
first the client's animation code is traced for hover while moving, then one test build goes to the owner.

## Choices for the owner

1. **Mark effects:** decided, effect flashes (the stock effects get picked and shown for a look before building).
2. **Stealth:** kept. Say if the Tuatha should drop it.
3. **Armor look:** the emerald and moonsilver Sidhe king above, or another theme.

## As built (2026-11-06, commit 16ed6b453)

| Part | Where |
|---|---|
| Style table (61) | `source/tools/sovereign/tuatha_remake_styles.json`; styles 7200-7260, effects 63800-63860 (`TuathaRemakeStyles.cs`) |
| Sidhe Marks, courts, Regalia of Tara set | `gameutils/TuathaRemake.cs` (`TuathaMarks`, `TuathaCourt`, `TaraRegalia`); sets share `gameutils/RegaliaSet.cs` with the Storm |
| Spells (65600 + tier x 16 + key; lines `Tuatha.Remake00-08`; courts 65609/65610) | `spells/Stormforged/TuathaRemakeSpells.cs` |
| Regalia item abilities 63750-63753 | `TaraRegaliaSpells` in `StormRegaliaSpells.cs` |
| Realm abilities (InternalID 1944-1946) | `TuathaMorrigansEdge`, `TuathaHeartOfTheCourt`, `TuathaEvasionOfTheSidhe` |
| Block/parry/evade marks, drake breath marks, Wildwood procs | `GamePlayer.OnAttackedByEnemy`, `StormWolf.DealDamage`, `StormSovereignEffect` |
| Loot and bots | Scythe, Blades, Shield only (`GeneratedUniqueItem`, `PendragonRules.IsTuathaSkill`, `SovereignBotSpec`) |
| World edits | `tuatha_remake_edits.py` (495 rows) and `tara_regalia_edits.py` (11 rows), through World Workshop |
| Hover | `TUATHA_HOVER` in `source/tools/classslots/patch_class_slots.py` (client, installed and confirmed 2026-11-06) |

Mark flashes use stock effects 4501/4505/4509 (Ethereal Concussion, Detonation, Devastation) and 4510 for a detonation;
change the constants in `TuathaMarks` to try others. The court rider on the Court of the Sidhe chant was not built (the
courts carry their own bonuses instead): Seelie +10% evade and +5% absorption, Unseelie +10% melee and spell critical chance.
The Regalia of Tara uses the stock osnadurtha scale models and a stock Hibernian cloak until the art pass.
Tested: 5,976 unit tests; both world edits applied to a snapshot of the test world; a private copy booted 4 minutes with
bots, no exceptions. Not played.
