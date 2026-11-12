# Pendragon Sovereign remake: Sword, Shield and Polearm

Status: design for owner review, 2026-11-06 (first draft). Nothing built. Branch `feature/pendragon-remake` (from
bot-ai-trips a362e10bb). Still overpowered on purpose. The Storm (64) and Tuatha (67) kits are not touched.

Owner picks (2026-11-06): weapons **Sword (Slash), Shield and Polearm** ("the ones the game currently has in it"),
core mechanic **Valor**.

## What is wrong today

The Pendragon (class 65) is still the old shared kit: a copy of the pre-remake Storm Sovereign renamed to holy fire.
Six weapon lines (Crush, Slash, Thrust, Polearm, Two Handed, Shields) with 7 generic styles each, 23 spell buttons that
mirror the old Storm table, the Royal Lion pet and the Lion of Albion form. It has no mechanic of its own.

## Identity

The Storm Sovereign is a burst-and-thunder king (Tempest charges on itself). The Tuatha Sovereign is a Sidhe king who
marks and detonates its enemies. The Pendragon is **the king who stands in front**: Arthur's knight-king. It holds the
line with block and parry, guards its allies, and turns every blow it takes into **Valor**, which it spends on
**Judgments**: holy-fire finishers that also heal and shield its party.

| | Storm Sovereign | Tuatha Sovereign | Pendragon Sovereign |
|---|---|---|---|
| Resource | Tempest charges on yourself | Sidhe Marks on enemies | **Valor** on yourself, earned by **defending** |
| Spending | Finishers spend charges | Detonations burst marks | **Judgments**: holy fire + a party heal or ward |
| Defense | Block, parry | Evade, parry, block, thorns | **Block, parry, Guard** (take hits for an ally) |
| Control | Stuns | Roots, mesmerize, stuns | **Taunts**, stuns, knockback, snares |
| Group role | Damage | Damage, party heals in Seelie | **Protector**: guards, party wards, rallies |
| Damage | Energy, cold | Spirit, bleeds, rot | **Heat** (holy fire), burns |

## Weapons

**Slash (sword), Shields, Polearm, Parry, Stealth**, all training with level. Crush, Thrust and Two Handed stop
training and their styles go. Characters keep every item they own; loot and bots stop rolling those weapons for this
class (as was done for the Tuatha).

| Setup | Weapons | Plays as |
|---|---|---|
| Knight of the Shield | Sword (Excalibur) + shield (Pridwen) | The guardian: blocks and parries build Valor fast, guards allies, party wards |
| Lancer | Polearm (Rhongomyniad, Arthur's lance) | The charger: reach, sweeps and charges, the biggest Judgments |

## Valor

- Valor is **on yourself**, **0 to 5**. It decays one point every 8 seconds out of combat.
- You **earn** it by defending: every **block or parry** +1, a **melee hit taken** +1 (at most once every 3 seconds),
  an attack absorbed for a **guarded ally** +1, and the shield and reactive styles +1 or +2.
- You **spend** it with **Judgments** (finishing styles and the Judgment spells). Each Valor spent adds **+20% damage**
  and either **heals your party 3%** (sword and shield Judgments) or **wards your party** for 3% of its health
  (polearm Judgments), and at 5 Valor the Judgment also **stuns for 2 seconds**.
- **Showing Valor (owner's pick):** a gold "Valor" buff icon on your bar whose name shows the count (Valor 3), and a
  holy-light flash on you at 3 and again at 5 (stock paladin/cleric effects). No orbs: the Warlock chamber orbs stay
  the Storm's.

## Styles (61)

Unlock levels follow the Sovereign spacing (1, 3, 5, 8, 10 ... 48). Openers 0.85 growth, steps 1.1, Judgments 1.45-1.6.
**V+n** = earns Valor, **J** = Judgment (spends all Valor).

### Sword (Slash): Excalibur, the duelist knight

| Lvl | Style | Opens from | Effect |
|---|---|---|---|
| 1 | Crowned Cut | anytime | Holy fire burn for 9 s |
| 3 | Lady of the Lake | Crowned Cut | +15% damage, snare 30% |
| 10 | **Judgment of Excalibur** | Lady of the Lake | **J**: holy fire strike |
| 5 | Riposte of Camelot | after you parry | V+1, +10% parry for 10 s |
| 13 | Gawain's Answer | Riposte of Camelot | Damage grows with the sun: +25% |
| 38 | **Sun at Noon** | Gawain's Answer | **J**, 3 s stun |
| 8 | Shield-Breaker | after you block | V+1, target's block -20% for 10 s |
| 28 | Kay's Cut | Shield-Breaker | Attack speed -25% for 12 s |
| 40 | **Caliburn's Oath** | Kay's Cut | **J**, you gain 20% absorption for 10 s |
| 18 | Bedivere's Turn | side | Snare 40%, holy burn |
| 23 | Return the Sword | Bedivere's Turn | Disarms for 5 s |
| 20 | Mordred's Bane | back | Heavy blow, holy burn |
| 25 | Camlann | Mordred's Bane | +25% if the target is below 50% health |
| 43 | **Last Light of Camelot** | Camlann | **J**: capstone, doubled below 25% health |
| 15 | Hidden Blade of Avalon | stealth | A great opener, 3 s stun |
| 30 | Mists of Avalon | Hidden Blade of Avalon | Holy burn, V+2 |
| 33 | Grail Light | anytime | Strike that heals you |
| 35 | Grail Quest | Grail Light | Heals your party 10% |
| 45 | Lancelot's Fury | anytime | Two quick strikes, V+1 |
| 48 | **Excalibur Unsheathed** | Lancelot's Fury | **J**: signature, holy fire nova within 350 |

### Polearm: Rhongomyniad, the lancer

| Lvl | Style | Opens from | Effect |
|---|---|---|---|
| 1 | Couched Lance | anytime | Long-reach thrust |
| 3 | Unhorse | Couched Lance | Knockback, 3 s |
| 10 | **Judgment of the Lance** | Unhorse | **J**: holy fire strike, wards the party |
| 5 | Lancer's Answer | after you parry | V+1 |
| 13 | Sweep of the Field | Lancer's Answer | Sweep: the target and 3 enemies near it |
| 38 | **Field of Camlann** | Sweep of the Field | **J** within 350, every enemy hit burns |
| 8 | The Tilt | front | Interrupts, V+1 |
| 28 | Splinter the Shield | The Tilt | Target's block and parry -20% for 10 s |
| 40 | **Breaker of Hosts** | Splinter the Shield | **J**, 4 s stun |
| 18 | Flank of the Charge | side | Snare 40% |
| 23 | Rider Down | Flank of the Charge | Root 8 s |
| 20 | Dragon's Tail | back | Heavy blow, holy burn |
| 25 | Wyrmfire | Dragon's Tail | Holy fire cone within 300 |
| 43 | **Rhongomyniad** | Wyrmfire | **J**: capstone, doubled below 25% health |
| 15 | Silent Lance | stealth | A great opener, V+2 |
| 30 | Pin to the Earth | Silent Lance | 3 s stun |
| 33 | Charge of Albion | anytime | You gain 30% speed for 6 s, the strike snares |
| 35 | Banner of the Dragon | Charge of Albion | Your party gains 10% melee damage for 15 s |
| 45 | Pendragon's Reach | anytime | Sweep: every enemy within 400, V+1 each (max 3) |
| 48 | **The Red Dragon** | Pendragon's Reach | **J**: signature, holy fire wave within 500, wards the party |

### Shields: Pridwen, the guardian (21)

| Lvl | Style | Opens from | Effect |
|---|---|---|---|
| 1 | King's Rebuke | anytime | Taunt, V+1 |
| 3 | Hold the Gate | King's Rebuke | +30% block for 8 s |
| 10 | Pridwen's Answer | Hold the Gate | 4 s stun |
| 5 | Stand Fast | after you block | V+1, a ward on you |
| 13 | Shield of the Round Table | Stand Fast | Taunts every enemy within 350, V+1 |
| 38 | **Judgment of the Round Table** | Shield of the Round Table | **J**: a ward on your whole party |
| 8 | Crown's Bash | after you block | V+1, 3 s stun |
| 28 | Shatterguard | Crown's Bash | Target's block -20% for 10 s |
| 40 | **Walls of Camelot** | Shatterguard | **J**: your party gains 15% absorption for 10 s |
| 12 | **Pridwen's Bash** | anytime | Anytime shield slam: **9 s stun** (normal immunity), V+1 |
| 18 | Shield Rush | side | Snare 40% |
| 23 | Ram the Line | Shield Rush | Knockback |
| 20 | Hill of Badon | front | Interrupts, V+1 |
| 25 | Unbroken | Hill of Badon | You gain 30% block for 8 s |
| 43 | **The Throne Stands** | Unbroken | **J**: capstone, taunts every enemy within 500 and burns them |
| 15 | Hidden Pridwen | stealth | 4 s stun, V+2 |
| 30 | Guarded Retreat | Hidden Pridwen | A ward on you |
| 33 | Oath of Protection | anytime | Heals you 5% and your guarded ally 10% |
| 35 | Fealty | Oath of Protection | Heals your party 10% |
| 45 | Rally to the King | anytime | Your party gains 10% block and parry for 15 s |
| 48 | **Pendragon's Shield** | Rally to the King | **J**: signature, a ward on your party and you take 50% of their damage for 10 s |

## Spells and abilities (15 buttons, down from 23)

| Unlock | Button | What it does |
|---|---|---|
| 1 | Crownfire Lance | Holy fire bolt; **instant** with 3+ Valor |
| 5 | Guard of the Crown | Guard an ally (maintained): you take 50% of the melee damage it would take; each blocked or taken blow earns Valor |
| 10 | Sovereign's Challenge | Taunts every enemy within 500, V+1 per enemy (max 3) |
| 11 | Rally to the Crown | Party heal 10% now and more over 12 s |
| 11 | Consecrated Armament | Maintained: sword hits burn, polearm hits splash, blocks and parries flash holy fire back |
| 15 | King's Advance | Party speed; breaks snares on you |
| 20 | Decree: Stand Fast | Party ward and +10% block and parry for 15 s |
| 20 | Binding Radiance | Stuns the target 4 s, snares enemies near it |
| 31 | **Judgment** | Spends all Valor: holy fire nova within 350, heals the party 3% per Valor |
| 40 | Field of Judgement | Holy ground at your feet for 30 s: burns enemies, heals allies inside |
| kept | Will of the Pendragon | Maintained party chant |
| kept | Armor of the Crown | Armor buff |
| kept | Royal Lion | The pet (one at a time), now the Black Lion model |
| kept | Lion of Albion | The transformation, stays the Stygian Lion |
| kept | Veil of Avalon | Stealth |

## Realm abilities

Three Pendragon ones replace the caster picks, like the other two Sovereigns:

- **Code of Chivalry** (3 ranks): Valor cap 6/7/8; each Judgment spends its Valor for +20% per point as usual.
- **Knight's Guard** (3 ranks): Guard of the Crown takes 60/70/80% of the blow and returns 10/20/30% of it as holy fire.
- **Grail Blessing** (3 ranks): Judgment heals and wards +25/50/75%.

## Regalia of Camelot (armor set)

Seven level 51 pieces, AF 102 / 27% (as the Storm and Tara sets), class 65 only, no trade or sale, ToA lines, a 2/4/6/7
set bonus (RegaliaSet), two use abilities and two armor procs, granted by the Pendragon trainer on a whispered keyword.
Placeholder: stock Albion plate art until the art pass; then crimson and gold royal plate with the red dragon of the
Pendragon and a lion-crowned cloak, made the same way as Tara (concepts first, owner picks).

## How it gets built

1. This draft, owner OK.
2. C#: Valor (PendragonValor, like StormTempest), the styles (PendragonRemakeStyles), spells (PendragonRemakeSpells),
   Guard of the Crown, the realm abilities and RegaliaSet use. Own ID ranges, checked free: styles 7300-7360, style
   effects 63900-63960, spells 65800-65927.
3. World Workshop edits file (pendragon_remake_edits.py), like the Tuatha's: specs, styles, spells, RAs, Regalia.
4. Unit tests, a private-copy boot and an AI tester run before handing over; install only with the owner's OK.

Players today: the owner's Yberrawin (level 1) and two AI tester characters at 50.

## Owner picks (2026-11-06)

1. **Showing Valor:** a gold Valor buff icon whose name shows the count, plus a holy-light flash on you at 3 and at 5.
2. **The transformation:** keep the Lion of Albion as the Stygian Lion (model 1193).
3. **The pet:** the Royal Lion becomes the Black Lion (model 104, was 100, a plain Tan Lion).

## As built (2026-11-06, commit 152e28821)

- **Valor** (`gameutils/PendragonRemake.cs`, `PendragonValor`): 0-5, +1 per block or parry (2 with the full Regalia), +1
  per melee hit taken (once every 3 s), guarded blows land on the Pendragon so they count; Judgments spend all of it.
  The buff icon is one of seven tier-0 spell rows "Valor 1" to "Valor 7" (65809-65815, icon 1506); holy flashes are
  1465 at 3 and 1506 at 5. Out of combat one point fades every 8 s. The Lion of Albion fills it and Judgments spend
  none while it lasts (as the Storm Crown keeps Tempest).
- **Code of Chivalry** became cap 6/6/7 with slower fading (8/12/16 s), so the icons stop at 7.
- **Guard of the Crown** is an ally buff, not a damage redirect: 20% absorption and a ward of 10% of the ally's health
  for 15 s, +1 Valor; **Knight's Guard** is block +3/6/9% and a 25/50/75% stronger ward. The class keeps the stock
  Guard, Protect and Intercept abilities it already had.
- **Pendragon's Shield** (signature) wards the party and gives it 20% absorption for 10 s (no damage transfer).
- **16 buttons**, not 15: King's Advance stays the old key 21 (throw and teleport), so the old chant (key 6) is kept too.
- Kept old keys: 6 chant, 12 Will, 13 Armor, 19 Royal Lion, 20 Lion of Albion, 21 King's Advance, 22 Veil of Avalon.
- **Regalia of Camelot**: plate, AF 102 / 34% (Albion plate's own absorption), stock arcanium full plate models 86-90
  and 93 and cloak 57 until the art pass; item abilities Crown of Camelot (fill Valor), Mercy of Avalon (party heal
  20%), Holy Bulwark (ward + Valor when struck), Searing Rebuke (burn + taunt when struck).
- World: `pendragon_remake_edits.py` (486 rows incl. the Royal Lion template -> model 104) and
  `camelot_regalia_edits.py` (11). Tooltips 29610-29670 (styles), 29700-29827 (spells and icons), 29840-29843.
- Checked: 5,985 unit tests; both edit sets applied to a private copy of the test world and a 5-minute private boot
  (port 10379) with no new errors. Not installed, not played by the AI tester or the owner yet.
