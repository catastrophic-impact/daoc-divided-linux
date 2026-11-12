# Storm Sovereign remake: Hammer, Axe and Shield

Status: owner approved 2026-11-04; built on feature/storm-remake. Orb colour chosen in game: **0x77** (blue ring, gold runes).
Not yet installed on the modern test world; not played.
Builds on `SOVEREIGN-IDENTITY-REDESIGN-PROPOSAL.md` (2026-10-25), narrowed to the owner's pick:
**Hammer, Axe and Shield**, with real offensive and defensive chains, still overpowered.

## What is wrong today

- Five weapon lines (Sword, Hammer, Axe, Spear, Shields), each with the same seven-style template:
  anytime, one follow-up, one reaction, one positional, a stun, a level-50 style that chains from nothing,
  and a stealth opener. No line has a real three-step chain, a block chain or a defensive branch.
- Every line's style effects come from one shared `index % 7` switch, so Hammer and Axe play the same.
- Pendragon (65) and Tuatha (67) reuse that same handler. The remake must not change them.
- Realm abilities: 36 inherited from Thane, including caster abilities the class can't use well
  (Mastery of Focus, Mastery of Concentration, Dual Threat, Raging Power, Serenity, Ethereal Bond).
- 23 spell buttons, several overlapping (three decrees, two self-heals, a ward split from Armor of the Crown).

## Weapons and setups

| Setup | Weapons | Strength |
|---|---|---|
| Offensive | Two-handed hammer or two-handed axe | Biggest style damage, Tempest finishers, cleaves |
| Defensive | One-handed hammer or axe plus a large shield | Block chains, wards, retaliation, taunts, stuns |

Trained lines: **Hammer, Axe, Shields, Parry, Stealth**, all still matching character level automatically.
Sword and Spear stop being trained for this class. Characters keep any swords and spears they own (nothing is
deleted), but those lines' styles go away. New characters start with a one-handed hammer and a large shield.

## The core mechanic: Tempest

Up to **5 Tempest charges** (shown as a buff with a stack count). Gained from landing chain steps, blocks
and parries, at most one per swing. Lasts 30 seconds without fighting. Splash hits, procs and echoes never
build charges, so it can't loop.

Every landed melee hit also builds one charge (owner, 2026-11-04). Finishers and two spells **spend all charges** for a bigger effect: each charge adds +20% to the finisher's
lightning and +1 target to its arcs. Everything still works with zero charges; charges make it explode.

### How Tempest shows in game: Warlock chamber orbs

The classic Warlock chamber display (the orbs over the Warlock's head) is its own server packet
(`VisualEffect` sub-code 3, `PacketLib173.SendWarlockChamberEffect`) with **exactly five slots**, one byte each.
Tempest reuses it: each charge lights one orb over the Sovereign's head, every nearby player sees them, and
spending charges clears them. The stock packet colour codes are 0x03, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66 and 0x77
(one per old chamber type); we pick the one that looks blue and lightning-like.

Nothing in the client changes: no new art, no patched files. Two things are not known yet and get checked first:

1. Whether the 1.130 client still draws the orbs: **yes** (owner's screenshot, 2026-11-04).
2. Which colour code looks most like blue lightning: **0x77**, a blue lightning ring around gold runes. The GM command
   `/tempest cycle|show <code>|set <n>|color <code>|clear` stays for testing.

The orbs show the count, so no separate buff icon was added.

## Styles: 20 per line, 60 total

Unlocks every 2-3 levels, same schedule on each line so it's easy to learn. All three-step chains are real
`after style` chains; reactions use the engine's parry/block checks. Damage growth: openers 0.85, second
steps 1.1, finishers 1.45 (well above stock Thane). Stuns are 3 seconds (Shields 4) and use normal immunity.

### Hammer: thunder and packs (burst, area, armor breaking)

| Lvl | Style | Opens from | Effect |
|---:|---|---|---|
| 1 | Thunderstroke | anytime | Lightning hit, +1 Tempest |
| 3 | Shatterguard | Thunderstroke | Target armor -15% for 12 s |
| 10 | Thor's Judgement | Shatterguard | Heavy hit; spends Tempest: lightning arc to 3 + charges nearby enemies |
| 5 | Storm Counter | after you parry | Lightning hit, +1 Tempest |
| 13 | Rolling Thunder | Storm Counter | 350-radius thunderclap around the target |
| 38 | Mjolnir's Answer | Rolling Thunder | 3 s stun, big lightning hit |
| 8 | Thunder Aegis | after you block | Ablative shield on you (absorbs level x 4) |
| 28 | Hammer of Reprisal | Thunder Aegis | Hit returns the ablative's absorbed damage as lightning |
| 40 | Wall of Thunder | Hammer of Reprisal | Party ablative; 350-radius lightning around you |
| 18 | Earthshaker | beside target | 40% snare 8 s |
| 23 | Quake | Earthshaker | 250-radius knockdown (2 s stun) |
| 20 | Backbreaker | behind target | Heavy hit, armor -10% |
| 25 | Spinecrack | Backbreaker | Attack speed -25% 12 s |
| 43 | Thunderfall | Spinecrack | Capstone: huge hit, spends Tempest, lightning splash |
| 15 | Hidden Thunder | stealth | Big opener, +2 Tempest |
| 30 | Silent Storm | Hidden Thunder | 3 s stun |
| 33 | Stormcaller | anytime | Hit restores 10% power |
| 35 | Skybreaker | Stormcaller | 350-radius lightning burst around you |
| 45 | Gathering Storm | anytime | Fills Tempest to 5 |
| 48 | Ragnarok's Hammer | Gathering Storm | Signature: spends Tempest, 500-radius strike, armor break on all hit |

### Axe: winter executioner (bleeds, frost, single-target kills)

| Lvl | Style | Opens from | Effect |
|---:|---|---|---|
| 1 | Winterbite | anytime | Frost wound (cold DoT, 12 s), +1 Tempest |
| 3 | Bloodmark | Winterbite | Bleed (body DoT, 12 s) |
| 10 | Executioner's Storm | Bloodmark | +25% damage per your wound on the target; doubled under 25% health |
| 5 | Frost Rend | after you parry | Cold hit, 30% snare |
| 13 | Royal Fury | Frost Rend | Two quick strikes (second as cold), +1 Tempest |
| 38 | Wolf's Reckoning | Royal Fury | Heavy hit, your Storm Wolf also strikes |
| 8 | Iceguard | after you block | Ablative ice; attacker slowed 20% |
| 28 | Winter's Reprisal | Iceguard | Cold hit, heals you 5% |
| 40 | Glacier Wall | Winter's Reprisal | 4 s stun, you get +20% absorb 10 s |
| 18 | Frost Hook | beside target | Cold hit, 40% snare |
| 23 | Shiver | Frost Hook | Attack speed -25% 12 s |
| 20 | Hamstring | behind target | Bleed, 50% snare |
| 25 | Gutting Frost | Hamstring | Heavy hit, +25% if bleeding |
| 43 | Widowmaker | Gutting Frost | Capstone: execute, spends Tempest, crit +15 |
| 15 | Hidden Wrath | stealth | Huge opener plus bleed |
| 30 | Silent Winter | Hidden Wrath | 3 s stun |
| 33 | Hungering Frost | anytime | Steals 30% of damage as health |
| 35 | Frozen Heart | Hungering Frost | Heals 10%, cleanses snares |
| 45 | Mark of Fenrir | anytime | Target takes +15% from you for 15 s, fills Tempest to 5 |
| 48 | Fimbulwinter | Mark of Fenrir | Signature: spends Tempest, detonates your wounds on the target for all their remaining damage |

### Shields: stormguard (defense, control, protecting the group)

Shield styles need a shield in hand. There is no parry chain here; shields get two block chains.

| Lvl | Style | Opens from | Effect |
|---:|---|---|---|
| 1 | Royal Rebuke | anytime | Taunt, +1 Tempest |
| 3 | Break Their Guard | Royal Rebuke | You get +10% block 10 s |
| 10 | Crownfall | Break Their Guard | 4 s stun |
| 5 | Hold the Throne | after you block | Ablative on you |
| 13 | Thunder Wall | Hold the Throne | 350-radius lightning, taunts everything hit |
| 38 | The Unbroken Crown | Thunder Wall | Party ablative; spends Tempest for extra lightning |
| 8 | Stormguard Bash | after you block | 3 s stun |
| 28 | Shieldbreaker | Stormguard Bash | Armor -15%, attack speed -20% |
| 40 | Guardian's Oath | Shieldbreaker | Party absorb +15% 10 s |
| 18 | Shield Rush | beside target | Knockback hit, 40% snare |
| 23 | Crushing Rim | Shield Rush | 2 s stun |
| 20 | Rampart Slam | in front of target | Interrupts casting, +1 Tempest |
| 25 | Wall of Midgard | Rampart Slam | +30% block 8 s |
| 43 | Throne of Storms | Wall of Midgard | Capstone: 500-radius taunt and lightning, spends Tempest |
| 15 | Hidden Crown | stealth | 4 s stun opener |
| 30 | Ambush Guard | Hidden Crown | Ablative on you |
| 33 | Bulwark | anytime | Heals you 5%, +1 Tempest |
| 35 | Crown's Embrace | Bulwark | Party heal 10% |
| 45 | Oathbinding | anytime | Fills Tempest to 5 |
| 48 | Stormwall | Oathbinding | Signature: spends Tempest; 10 s party ablative plus a 350-radius retaliation aura |

## Spells and abilities: 23 buttons down to 19

| Keep / change | Ability | Change |
|---|---|---|
| Keep | Lightning Spear, Winter's Grasp, Royal Thunder, Chain of the Crown, Stormbreak, Eye of the Storm | Royal Thunder and Stormbreak **spend Tempest** (+20% per charge) |
| Merge | Royal Presence | unchanged chant |
| Merge | Runic Ward into Runic Armament | one maintained buff: weapon procs plus the extra magic resist (Armor of the Crown stays) |
| Merge | Decree: Advance / Stand Fast / Strike True | one **Royal Decree**: all three effects, 10 s, 30 s reuse |
| Merge | Sovereign's Renewal + Rally to the Crown | **Rally to the Crown**: 25% self, 15% party, 20% endurance, 30 s reuse |
| Keep | Will of the Sovereign, Sovereign's Challenge, Storm Wolf, Storm Crown, Hammerfall, Veil of Winter | Storm Crown also fills Tempest to 5 and makes finishers cost nothing |
| Rework | Runic Armament | Hammer: lightning proc splash. Axe: frost wound proc. Shield: blocks can raise a small ward (every block or parry gives Tempest anyway) |
| Rework | Runic Reversal | Parries **and blocks** return lightning for 8 s |
| Rework | Hunter's Mark | From stealth: +30% on the next hit **and** +3 Tempest |

Old buttons for merged spells stop working; drag the new ones from the spell list after install.

## Realm abilities

Cut the list from 36 to the ones a hammer/axe/shield storm king uses. Abilities a character has **already
trained stay trained**; they just stop appearing for new purchases.

- **Keep:** Augmented Str/Con/Dex/Qui/Acuity, Toughness, Mastery of Pain, Mastery of Blocking, Mastery of Parrying,
  Determination, Avoidance of Magic, Purge, Second Wind, Ignore Pain, First Aid, Long Wind, Mastery of Magery,
  Wild Minion (wolf), Mastery of Stealth, Static Tempest, Wrath of the Champion.
- **Drop:** Serenity, Ethereal Bond, Mastery of Focus, Mastery of Concentration, Dual Threat, Raging Power, MCL,
  Wild Power, Lifter, Veil Recovery.
- **Keep the four Sovereign RAs:** Royal Fortitude, Storm Mastery, Crown's Resolve, Wrath of the Crown (free at RR5).
- **New, 3 ranks each (2/4/6 points):**
  - *Thunderlord:* style damage +5/10/15%.
  - *Eye of Tempest:* finishers refund 1/1/2 charges and charges last 30/45/60 s (the cap stays 5: the client has five orbs).
  - *Shieldwall of Asgard:* block chance +3/6/9% and block chains add 10/20/30% to their wards.

## How it gets built

1. New C# handler `StormRemakeStyles` with one explicit entry per style (no shared index switch).
   New style IDs and spell IDs in a fresh private range; the old 1800-1834 styles and 62700-62734 effects stay for
   Pendragon and Tuatha, which are not touched.
2. A `TempestEffect` (stacking 0-5 charges, 30 s decay, no gain from splash/procs/echoes) that sends the chamber
   orb packet on every change and when a player comes into view. Built first, with the `/tempest show` test,
   so the owner can check the orbs and pick a colour before the styles go on top.
3. `seed_storm_remake.py`: idempotent, preview by default. It replaces class-64 styles, sets the
   trained lines, merges the spells, rebinds the RA list, and adds the three new RAs. It never writes player
   tables; trained RAs and inventories are kept.
4. Bots: update `BotClassKitPolicy`/`BotSpec` so Sovereign bots carry a hammer or axe plus a shield and use the new chains.
5. Tests: unit tests for every chain link and Tempest rule; then boot and a scripted fight on a **private copy**.
   Only then ask before installing on the modern test client.

## For the owner to check later in game

Each chain fires in order; block chains with a shield; parry chains with a two-hander; Tempest stack count
shows; finishers visibly hit harder with charges; Storm Crown fills Tempest; old sword/spear gear is still in bags.

## As built (2026-11-04)

| Part | Where |
|---|---|
| Style table (names, levels, chains, growth, descriptions) | `source/tools/sovereign/storm_remake_styles.json` |
| Style effects, by table index (proc spells 63500-63559, styles 7100-7159) | `spells/Stormforged/StormRemakeStyles.cs` |
| Merged/reworked spells (63600 + tier x 16 + key, lines `Stormforged.Remake00-06`) | `spells/Stormforged/StormRemakeSpells.cs` |
| Tempest charges and orbs | `gameutils/StormTempest.cs`, `PacketLib173.SendChamberOrbs` |
| Blocks/parries build Tempest | `GamePlayer.OnAttackedByEnemy` |
| Storm Crown fills Tempest, finishers free | `StormforgedAvatarEffect`, `StormTempest.Spend` |
| New RAs | `SovereignThunderlord`, `SovereignEyeOfTempest`, `SovereignShieldwall` (InternalID 1904-1906) |
| Loot and bots: no Sword/Spear | `GeneratedUniqueItem`, `StormforgedRules.IsRogSkill`, `SovereignBotSpec` |
| World edits (World Workshop plan/apply/undo) | `source/tools/sovereign/storm_remake_edits.py` |

Install: build the edits from the stopped world (`storm_remake_edits.py <db> edits.json`), `cli.py plan`, then `apply`
with the owner's OK. Undo with the id `apply` prints. Old hotbar buttons for removed styles and spells stop working:
drag new ones from the style and spell lists. Characters who had the old Runic Ward or Runic Armament switched on keep
those icons until they cancel them.

## Second pass (2026-11-04, owner after first play)

- **One Storm Wolf.** Recasting while a wolf lived replaced the pet link and left the old wolf in the world; it is now refused.
- **Styles that kill still build Tempest.** The effect needs a living target, so one-shot kills built nothing.
- **14 spell buttons** (from 19): Royal Thunder absorbs Lightning Spear; Stormbreak absorbs Chain of the Crown; Runic Armament
  absorbs Runic Reversal (every parry or block returns lightning); Hunter's Mark and Sovereign's Challenge are cut (stealth
  openers build Tempest, shield styles taunt). The list: Royal Presence, Armor of the Crown, Runic Armament, Veil of Winter,
  Storm Wolf, Royal Decree, Rally to the Crown, Royal Thunder, Winter's Grasp, Hammerfall, Stormbreak, Eye of the Storm,
  Storm Crown, Will of the Sovereign.
- **Tempest pays for casts.** With any charge, Royal Thunder (2.5 s bolt from level 1) and Stormbreak (2.3 s burst from 31)
  are instant, free and spend every charge (+20% each); Winter's Grasp and Hammerfall spend 1 charge instead of power.
- **Stormslam** (Shields, level 12, anytime): a 9-second stun with normal stun immunity; builds 1 Tempest.
- World: `storm_remake_edits.py <db> edits.json --update` takes a world with the first remake to this one;
  without `--update` it builds the whole remake on an old-kit world. Both give identical class rows (checked on copies).

## Storm Regalia: the class armor as real items (2026-11-04)

Seven level 51 pieces on the Stormforged art, Storm Sovereign only, not tradeable or sellable. Chain armor at
AF 102 / 27% absorption (the best chain in the game); the hauberk draws the shoulder pads (extension 5).

| Piece | Model | Stats | ToA lines | Ability |
|---|---|---|---|---|
| Stormcrown of the Sovereign | 4942 | Str/Con 18, Pie 15, 50 hits, energy/spirit | Str/Con cap +5, spell damage 5%, casting speed 5%, fatigue 5% | Use: Crown of Tempest, Tempest to 5 (3 min) |
| Hauberk of the Storm Sovereign | 4929 | Str/Con 20, Dex 15, 70 hits, crush/slash/thrust 10, Shields +4 | melee 5%, style 5%, buff effectiveness 10% | When struck (10%): Stormguard Ward |
| Thunderbound Sleeves | 4931 | Str 16, Qui 15, Dex 12, 45 hits, heat/cold, Axe +4 | melee speed 5%, Dex/Qui cap +5, resist pierce 5% | When struck (10%): Thunder Rebuke |
| Gauntlets of the Thunder King | 4932 | Str 15, Dex 18, Qui 15, 40 hits, matter/body, Hammer +4 | melee speed 5%, resist pierce 5%, melee crit 5%, Str cap +5 | |
| Stormforged Leggings | 4930 | Con 20, Str/Pie 15, 60 hits, energy/body, Parry +4 | Pie cap +5, hits cap +50, casting speed 5%, power pool 10% | |
| Boots of the Rolling Thunder | 4933 | Dex 15, Qui 18, Con 15, 40 hits, cold/matter/heat | style 5%, Qui cap +5, fatigue 10%, debuff effectiveness 10% | |
| Wolfmantle of the Storm King | 4937 | Str/Con/Pie 15, 50 hits, physical resists 5 | melee 5%, spell damage 5%, spell duration 10%, fatigue 10% | Use: Wolfmantle (5 min) |

Set totals (2026-11-06, every ToA line generated loot can roll for this class): melee, style and spell damage, melee and
casting speed, resist pierce 10% each; fatigue 25%; buff and debuff effectiveness, spell duration, power pool 10% each;
Str/Qui cap +10, Con/Dex/Pie cap +5, hits cap +50. Each piece uses all 11 lines (the 11th is the item's extra bonus line).
Existing worlds: `storm_regalia_edits.py <db> edits.json --update` rewrites only the bonus lines; owned pieces follow the template.

Set bonus (`gameutils/StormRegalia.cs`, held while worn, re-read at login): 2 pieces +5% all resistances; 4 pieces +10% melee
and style damage; 6 pieces +10% armor absorption and spell damage; all 7 +10% maximum health and every block or parry
builds 2 Tempest. A level 50 Storm Sovereign whispers **Storm Regalia** to the Crownkeeper for any pieces they don't carry.
World rows: `source/tools/sovereign/storm_regalia_edits.py` (7 items, 4 ability spells 63740-63743), through World Workshop.
