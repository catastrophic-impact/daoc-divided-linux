"""Loot redesign phase 4 (owner 2026-11-05): shared loot tables for mobs that have none.

About 74% of mob names in the modern world have no LootTemplate or MobXLootTemplate rows, so they drop coin and
random items only. This script gives each of those hostile mobs one MobXLootTemplate row pointing at a shared
"family" table chosen from its race, body type or name, and the tier for its usual level. The family tables drop:

  - vendor junk (new item templates, owner: both junk and materials), and
  - crafting materials that already exist (leather, cloth, wood, metal bars, enchanted dust, bone, fishing catches).

Coin is untouched: LootGeneratorMoney still drops coin on every mob. The existing LootGeneratorTemplate reads the
new rows, so no server code is needed. The output is a World Workshop edits file: plan it, show the owner, apply
with their OK, undo with the id World Workshop prints.

    python -I build_loot_families.py --db <opendaoc.sqlite3.db> --out loot-families.edits.json [--summary summary.json]

The database is opened read-only. Ids are uuid5 of stable names, so running it again yields the same edits.
"""

import argparse
import collections
import json
import pathlib
import re
import sqlite3
import statistics
import sys
import uuid

NAMESPACE = uuid.UUID("6f1d3c2a-0b7e-4c55-9a51-2b8f0c6e4d10")
PACKAGE = "OfflineLootFamilies"
TABLE_PREFIX = "family "

# NPC flags (GameNPC.eFlags): no loot for mobs that cannot be fought.
FLAG_CANTTARGET, FLAG_PEACE, FLAG_STATUE = 0x08, 0x10, 0x80
HOSTILE_CLASSES = {"DOL.GS.GameNPC", "DOL.GS.GameEpicNPC", "DOL.GS.NightSpawn", ""}
SKIP_NAMES = {"new mob"}

RACE_FAMILY = {2000: "animal", 2001: "demon", 2002: "drake", 2003: "elemental", 2004: "giant", 2005: "humanoid",
               2006: "magical", 2007: "plant", 2008: "reptile", 2009: "undead", 2010: "insect"}
BODY_FAMILY = {1: "animal", 2: "demon", 3: "drake", 4: "elemental", 5: "giant", 6: "humanoid", 7: "insect",
               8: "magical", 9: "reptile", 10: "plant", 11: "undead"}

# Name words, checked first for sea creatures (their race says animal), then for mobs with no race or body type.
SEA_WORDS = ["barracuda", "octopus", "stingray", "ray", "eel", "shark", "fish", "crab", "squid", "jellyfish",
             "lobster", "clam", "urchin", "kraken", "piranha", "whale", "dolphin", "angler", "sea serpent", "manta",
             "sea", "seahorse", "nautilus"]
NAME_FAMILY = [
    ("undead", ["skeleton", "skeletal", "zombie", "ghoul", "wraith", "spirit", "ghost", "lich", "banshee", "spectre",
                "specter", "phantom", "revenant", "mummy", "bone", "corpse", "wight", "shade", "haunt"]),
    ("insect", ["spider", "beetle", "scarab", "wasp", "ant", "cicada", "scorpion", "mantis", "fly", "locust", "bee",
                "hornet", "centipede", "tick", "worm", "larva", "bug", "moth"]),
    ("reptile", ["snake", "lizard", "crocodile", "serpent", "viper", "adder", "asp", "cobra", "toad", "frog",
                 "turtle", "tortoise", "basilisk", "salamander", "iguana", "gecko"]),
    ("plant", ["tree", "treant", "vine", "fungus", "mushroom", "bush", "shrub", "bramble", "sapling", "root", "moss",
               "bloom", "thorn", "creeper"]),
    ("giant", ["giant", "ogre", "cyclops", "titan", "ettin", "jotun", "troll"]),     # before "stone": stone giant
    ("elemental", ["elemental", "golem", "wisp", "construct", "gargoyle", "statue", "stone"]),
    ("demon", ["demon", "imp", "fiend", "devil", "daemon", "hellhound", "succubus"]),
    ("drake", ["drake", "wyvern", "wyrm", "dragon", "drakeling"]),
    ("humanoid", ["bandit", "brigand", "thief", "cultist", "soldier", "mercenary", "villager", "orc", "goblin",
                  "kobold", "gnoll", "pirate", "raider", "rogue", "knight", "warrior", "mage", "priest", "acolyte",
                  "scout", "hunter", "poacher", "smuggler", "deserter", "highwayman", "footpad", "outlaw", "exile",
                  "minotaur", "siabra", "fomorian", "fir bolg", "firbolg", "svartalf", "dwarf", "elf", "man", "woman",
                  # Catacombs peoples and their trades (2026-11-09 top-up)
                  "glashtin", "terracite", "outcast", "inconnu", "plutonian", "dark elf", "marauder", "summoner",
                  "sorcerer", "theurgist", "vigilante", "chieftain", "pugilist", "duelist", "spellbinder", "armigerant",
                  "valorant", "shadowblade", "runemaster", "skald", "stalwart", "marksman", "defender", "assassin",
                  "blackguard", "deathseeker", "infernalist", "necromancer", "wizard", "reaver", "infiltrator"]),
    ("animal", ["wolf", "bear", "boar", "cat", "lion", "lioness", "badger", "rat", "hound", "dog", "fox", "stag",
                "deer", "elk", "hare", "rabbit", "bat", "owl", "hawk", "crow", "raven", "eagle", "griffin", "horse",
                "pony", "bull", "cow", "goat", "sheep", "pig", "mole", "weasel", "otter", "lynx", "panther", "tiger",
                "mauler", "mammoth", "yeti", "ape", "monkey", "bird", "vulture", "cockatrice", "harpy", "cub", "pup",
                "warg", "fenrir", "griffon", "gryphon", "jackal", "hyena", "wolverine", "boarhound", "wolfhound"]),
]

GRADES = ["worn", "rough", "sturdy", "fine", "pristine"]          # levels 1-10, 11-20, 21-30, 31-40, 41+
GRADE_PRICE = [10, 40, 120, 350, 800]                              # copper

# Junk per family: (key, name with {g} for the grade, model, weight)
JUNK = {
    "animal":    [("pelt", "{g} animal pelt", 100, 4), ("fang", "{g} animal fang", 106, 1)],
    "reptile":   [("hide", "{g} scaly hide", 107, 3), ("fang", "{g} reptile fang", 106, 1)],
    "insect":    [("chitin", "{g} chitin shard", 516, 1), ("leg", "{g} insect leg", 106, 1)],
    "humanoid":  [("trinket", "{g} trinket", 101, 1), ("pouch", "{g} empty pouch", 488, 1)],
    "giant":     [("knucklebone", "{g} giant knucklebone", 540, 5), ("tooth", "{g} giant tooth", 106, 2)],
    "undead":    [("bone", "{g} bone fragment", 540, 1), ("dust", "{g} grave dust", 541, 1)],
    "plant":     [("bark", "{g} bark strip", 612, 2), ("leaves", "{g} bundle of leaves", 535, 1)],
    "elemental": [("shard", "{g} elemental shard", 592, 2), ("dust", "{g} glittering dust", 541, 1)],
    "magical":   [("residue", "{g} arcane residue", 541, 1), ("shard", "{g} glowing shard", 592, 1)],
    "demon":     [("horn", "{g} demon horn", 106, 2), ("ash", "{g} sulphurous ash", 541, 1)],
    "drake":     [("scale", "{g} drake scale", 107, 2), ("claw", "{g} drake claw", 106, 2)],
    "generic":   [("tooth", "{g} monster tooth", 106, 1), ("hide", "{g} patch of hide", 100, 2)],
}
SEA_JUNK = ["offline_fish_junk_old_boot", "offline_fish_junk_tangled_seaweed"]

# Existing crafting materials, ten tiers by level (1-5, 6-10, ... 46+).
LEATHER = ["rawhide_leather_square", "tanned_leather_squares", "cured_leather_squares", "hard_leather_squares",
           "rigid_leather_squares", "embossed_leather_squares", "imbued_leather_squares", "runed_leather_squares",
           "eldritch_leather_squares", "tempered_leather_squares"]
CLOTH = ["woolen_cloth_squares", "linen_cloth_squares", "brocade_cloth_squares", "silk_cloth_squares",
         "gossamer_cloth_squares", "sylvan_cloth_squares", "seamist_cloth_squares", "nightshade_cloth_squares",
         "wyvernskin_cloth_squares", "silksteel_cloth_squares"]
WOOD = ["elm_wooden_boards", "elm_wooden_boards", "oak_wooden_boards", "ironwood_wooden_boards",
        "heartwood_wooden_boards", "runewood_wooden_boards", "stonewood_wooden_boards", "ebonwood_wooden_boards",
        "dyrwood_wooden_boards", "duskwood_wooden_boards"]
# Albion and Midgard/Hibernia names for the same metal tier; a table carries both at half the chance each.
METAL_A = ["ferrite_metal_bars", "ferrite_metal_bars", "quartz_metal_bars", "dolomite_metal_bars", "cobalt_metal_bars",
           "carbide_metal_bars", "sapphire_metal_bars", "diamond_metal_bars", "netherite_metal_bars", "arcanite_metal_bars"]
METAL_B = ["iron_metal_bars", "iron_metal_bars", "steel_metal_bars", "alloy_metal_bars", "fine_alloy_metal_bars",
           "mithril_metal_bars", "adamantium_metal_bars", "asterite_metal_bars", "netherium_metal_bars", "arcanium_metal_bars"]
# Enchanted dust is costly (golden is 2.5 gold a pinch): only the two cheapest, rarely.
DUST = ["coppery_enchanted_dust"] * 5 + ["silvery_enchanted_dust"] * 5
FISH_SPECIES = ["offline_fish_any_eel", "offline_fish_any_catfish", "offline_fish_any_sturgeon"]

JUNK_CHANCE = 25        # each junk row
MATERIAL_CHANCE = 12    # each material line (metal: 6 per name)
DUST_CHANCE = 3
BONE_CHANCE = 10
FISH_CHANCE = 15        # each fish species row is FISH_CHANCE / 3


def material_rows(family, tier):
    """(item id, chance) rows of existing materials for a family at a tier (0-9)."""
    metal = [(METAL_A[tier], MATERIAL_CHANCE // 2), (METAL_B[tier], MATERIAL_CHANCE // 2)]
    rows = {
        "animal": [(LEATHER[tier], MATERIAL_CHANCE)],
        "reptile": [(LEATHER[tier], MATERIAL_CHANCE)],
        "drake": [(LEATHER[tier], MATERIAL_CHANCE)],
        "insect": [(CLOTH[tier], MATERIAL_CHANCE)],
        "humanoid": [(CLOTH[tier], MATERIAL_CHANCE)] + metal,
        "giant": metal + [("ground_giant_bone", BONE_CHANCE)],
        "undead": [("ground_blessed_undead_bone", BONE_CHANCE), (CLOTH[tier], MATERIAL_CHANCE // 2)],
        "plant": [(WOOD[tier], MATERIAL_CHANCE)],
        "elemental": metal + [(DUST[tier], DUST_CHANCE)],
        "magical": [(DUST[tier], DUST_CHANCE), (CLOTH[tier], MATERIAL_CHANCE // 2)],
        "demon": metal + [(DUST[tier], DUST_CHANCE)],
        "generic": [(LEATHER[tier], MATERIAL_CHANCE // 2)],
    }
    return rows[family]


def fish_rows(tier):
    size = 1 if tier < 4 else 2 if tier < 8 else 3       # small / normal / large; trophies stay with fishing
    return [(f"{species}_{size}", max(1, FISH_CHANCE // len(FISH_SPECIES))) for species in FISH_SPECIES]


def tier_for(level):
    return max(0, min(9, (int(level) - 1) // 5))


def grade_for(tier):
    return min(4, tier // 2)


def words(name):
    return re.findall(r"[a-z']+", name.lower())


def has_word(name, keyword):
    if " " in keyword:
        return keyword in name.lower()
    return keyword in words(name) or any(w.rstrip("s") == keyword for w in words(name))


# Body types an import left as a default and that say nothing (--distrust-body). EoD db-public's Catacombs rows carry
# BodyType 1 on goblins, half-orcs and cultists alike, which made 428 names drop animal pelts in the first top-up.
DISTRUSTED_BODIES = set()


def family_for(name, race, body):
    if any(has_word(name, w) for w in SEA_WORDS):
        return "sea", "name"
    if race in RACE_FAMILY:
        return RACE_FAMILY[race], "race"
    if body in BODY_FAMILY and body not in DISTRUSTED_BODIES:
        return BODY_FAMILY[body], "body type"
    for family, keywords in NAME_FAMILY:
        if any(has_word(name, w) for w in keywords):
            return family, "name"
    return "generic", "fallback"


def stable_id(*parts):
    return str(uuid.uuid5(NAMESPACE, "|".join(str(p) for p in parts)))


def junk_id(family, key, grade):
    return f"loot_junk_{family}_{key}_{grade + 1}"


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--db", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--summary")
    parser.add_argument("--top-up", action="store_true",
                        help="the world already has the family patch: only add tables for mobs it missed")
    parser.add_argument("--distrust-body", type=int, action="append", default=[],
                        help="a BodyType value to ignore (name words decide, else the generic family)")
    args = parser.parse_args()
    DISTRUSTED_BODIES.update(args.distrust_body)

    db = sqlite3.connect(pathlib.Path(args.db).resolve().as_uri() + "?mode=ro", uri=True)
    q = lambda sql, *a: db.execute(sql, a).fetchall()

    items = {row[0].lower() for row in q("SELECT Id_nb FROM ItemTemplate")}
    existing_tables = {row[0].lower() for row in q("SELECT DISTINCT TemplateName FROM LootTemplate")}
    tabled = set(existing_tables)
    # A MobXLootTemplate row only counts when its table has rows: 2,644 rows point at an empty or missing table,
    # and the 2026-11-06 build skipped those mobs as "has a table", so 1,500+ hostile names dropped coin only.
    tabled |= {row[0].lower() for row in q("SELECT MobName, LootTemplateName FROM MobXLootTemplate")
               if (row[1] or "").lower() in existing_tables}
    if not args.top_up:
        if any(t.startswith(TABLE_PREFIX) for t in tabled):
            sys.exit("This world already has family tables: undo the earlier patch first, or use --top-up.")
        if q("SELECT 1 FROM ItemTemplate WHERE Id_nb LIKE 'loot_junk_%' LIMIT 1"):
            sys.exit("This world already has loot_junk_ items: undo the earlier patch first, or use --top-up.")

    # A spawn linked to an NPC template takes its level (often "10-11" or "39;43;42"), race and body type from it.
    templates = {}
    for template_id, level_text, race, body in q("SELECT TemplateId, Level, Race, BodyType FROM NpcTemplate"):
        levels = [int(n) for n in re.findall(r"\d+", str(level_text or ""))]
        templates[template_id] = (int(statistics.median(levels)) if levels else 0, race or 0, body or 0)

    # Every spawn of each exact name (the generator matches Name.ToLower(), spaces and all).
    spawns = collections.defaultdict(list)
    for name, level, race, body, cls, flags, realm, template in q(
            "SELECT Name, Level, Race, BodyType, ClassType, Flags, Realm, NPCTemplateID FROM Mob"):
        if not name:
            continue
        t_level, t_race, t_body = templates.get(template, (0, 0, 0))
        level = t_level if t_level > 0 and (level or 0) <= 1 else (level or 1)
        spawns[name.lower()].append((name, max(1, level), race or t_race, body or t_body, cls or "", flags or 0,
                                     realm or 0, template or 0))

    mobs, skipped = [], collections.Counter()
    for key, rows in spawns.items():
        if key in tabled:
            skipped["has a table"] += 1
            continue
        if key.strip() in SKIP_NAMES:
            skipped["placeholder name"] += 1
            continue
        hostile = [r for r in rows if r[4] in HOSTILE_CLASSES and r[6] == 0 and not r[5] & (FLAG_CANTTARGET | FLAG_PEACE | FLAG_STATUE)]
        if not hostile:
            skipped["not a hostile mob"] += 1
            continue
        if any(str(r[7]).lower() in tabled for r in hostile if r[7]):
            skipped["template has a table"] += 1
            continue
        level = int(statistics.median(r[1] for r in hostile))
        race = (collections.Counter(r[2] for r in hostile if r[2]).most_common(1) or [(0, 0)])[0][0]
        body = (collections.Counter(r[3] for r in hostile if r[3]).most_common(1) or [(0, 0)])[0][0]
        family, why = family_for(hostile[0][0], race, body)
        mobs.append({"name": hostile[0][0], "level": level, "family": family, "why": why, "spawns": len(hostile)})

    edits, tables_used = [], collections.defaultdict(int)
    for mob in mobs:
        table = f"{TABLE_PREFIX}{mob['family']} {tier_for(mob['level']) + 1}"
        tables_used[table] += 1
        edits.append({"table": "MobXLootTemplate", "insert": {
            "MobXLootTemplate_ID": stable_id("mobx", mob["name"]),
            "MobName": mob["name"], "LootTemplateName": table, "DropCount": 1}})

    # New junk item templates, copied from a stock vendor-junk pelt so every other column is sane.
    junk_made = set()
    for family, entries in JUNK.items():
        for key, name, model, weight in entries:
            for grade in range(len(GRADES)):
                item_id = junk_id(family, key, grade)
                junk_made.add(item_id.lower())
                if item_id.lower() in items:
                    continue  # --top-up: the junk item is already in the world
                edits.append({"table": "ItemTemplate", "copy": "tattered_black_canine_pelt", "key": item_id, "set": {
                    "Name": name.format(g=GRADES[grade]), "Level": min(50, grade * 10 + 5), "Model": model,
                    "Weight": weight, "Price": GRADE_PRICE[grade], "MaxCount": 50, "PackSize": 1, "Realm": 0,
                    "Object_Type": 0, "Item_Type": 40, "IsPickable": 1, "IsDropable": 1, "CanDropAsLoot": 1,
                    "IsTradable": 1, "PackageID": PACKAGE, "TranslationId": "",
                    "Description": "Vendor junk from the shared loot tables (loot redesign phase 4)."}})

    missing = set()
    for table in sorted(tables_used):
        if table.lower() in existing_tables:
            continue  # --top-up: the table already has its rows
        family, tier = table[len(TABLE_PREFIX):].rsplit(" ", 1)
        tier = int(tier) - 1
        if family == "sea":
            rows = [(item, JUNK_CHANCE // 2) for item in SEA_JUNK] + fish_rows(tier)
        else:
            rows = [(junk_id(family, key, grade_for(tier)), JUNK_CHANCE) for key, *_ in JUNK[family]] + material_rows(family, tier)
        for item, chance in rows:
            if item.lower() not in items and item.lower() not in junk_made:
                missing.add(item)
                continue
            edits.append({"table": "LootTemplate", "insert": {
                "LootTemplate_ID": stable_id("loot", table, item), "TemplateName": table,
                "ItemTemplateID": item, "Chance": chance, "Count": 1}})

    if missing:
        sys.exit("Items missing from this world: " + ", ".join(sorted(missing)))

    pathlib.Path(args.out).write_text(json.dumps(edits, indent=1, ensure_ascii=False), encoding="utf-8")

    by_family = collections.Counter(m["family"] for m in mobs)
    spawns_by_family = collections.Counter()
    for m in mobs:
        spawns_by_family[m["family"]] += m["spawns"]
    summary = {
        "mob names given a table": len(mobs),
        "spawns covered": sum(m["spawns"] for m in mobs),
        "skipped": dict(skipped),
        "by family (names, spawns)": {f: [by_family[f], spawns_by_family[f]] for f in sorted(by_family)},
        "chosen by": dict(collections.Counter(m["why"] for m in mobs)),
        "tables": len(tables_used),
        "junk items": len(junk_made),
        "edits": len(edits),
        "samples": {f: [f"{m['name']} (L{m['level']}, {m['why']})" for m in mobs if m["family"] == f][:6] for f in sorted(by_family)},
    }
    text = json.dumps(summary, indent=1, ensure_ascii=False)
    if args.summary:
        pathlib.Path(args.summary).write_text(text, encoding="utf-8")
    print(text)


if __name__ == "__main__":
    main()
