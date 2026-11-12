"""Throne rooms, Weaponmasters and Darkness Rising champion weapons (DR-215, DR-062), 2026-11-09.

Owner: players get the champion weapons from the Weaponmasters in the throne room, and Champion training starts
at the King. This writes champion-weaponmasters-2026-11-09.edits.json:

- Teleport rows Alb/Mid/HibThroneRoom and *ThroneExit (dolpubdb coordinates), so the Royal Guards can port.
- The 12 class-guild Weaponmasters in regions 394/360/395 become DOL.GS.ChampionWeaponmaster.
- Every champion weapon from the official Herald 1.126 export (ids 57001-57599): the base version and the two
  Weaponmaster versions (+200 / +400, a second spell), built fresh as champion_<name>[_v2|_v3] with PackageID
  DR_ChampionWeapons, class-locked, no drop/trade, unlimited use charges, no Champion Level requirement.
  The older imported copies (dolpubdb and EoD) are left alone; nothing hands them out.
- The weapon spells that were missing (811086-811090, 811093, 811094) and fixes to 811081 (heal all over time
  4%) and 811098 (pet AF shield).

Usage (read-only on both databases):
    py -I make_champion_weaponmasters_edits.py --world <opendaoc.sqlite3.db> --reference <reference.sqlite3>
The reference database is private (Research/Reference); it is only read here.
"""
import argparse
import json
import re
import sqlite3
import sys
from pathlib import Path

PACKAGE = "DR_ChampionWeapons"
TEMPLATE_ITEM = "Upsilon_Cabalist_Staff"

TELEPORTS = [
    ("AlbThroneRoom", 1, 394, 32328, 31795, 15901, 17),
    ("AlbThroneExit", 1, 10, 32689, 24326, 8447, 3080),
    ("MidThroneRoom", 2, 360, 32331, 30410, 15563, 3),
    ("MidThroneExit", 2, 101, 31753, 27073, 8830, 6),
    ("HibThroneRoom", 3, 395, 32329, 31779, 15715, 12),
    ("HibThroneExit", 3, 201, 33176, 32914, 8008, 2048),
]
WEAPONMASTER_REGIONS = (394, 360, 395)

# Herald type_data (realm, skill_used) -> our Object_Type.
OBJECT_TYPE = {
    (1, 1): 3, (1, 2): 4, (1, 33): 2, (1, 46): 24, (1, 47): 8, (1, 64): 7, (1, 65): 6, (1, 90): 9,
    (2, 14): 11, (2, 16): 12, (2, 17): 13, (2, 26): 14, (2, 47): 8, (2, 78): 15, (2, 124): 25,
    (3, 101): 19, (3, 102): 20, (3, 103): 21, (3, 104): 22, (3, 112): 23, (3, 113): 18, (3, 125): 26, (3, 47): 8,
}
FIST_WRAPS, MAULER_STAFF = 27, 28
PREFIX_REALM = {"upsilon": 1, "dextera": 1, "laevus": 1, "satago": 1, "ansuz": 2, "thurisaz": 2,
                "dochar": 3, "draiocht": 3, "fuar": 3}
CLASS_IDS = {
    1: {"Paladin": 1, "Armsman": 2, "Scout": 3, "Minstrel": 4, "Theurgist": 5, "Cleric": 6, "Wizard": 7, "Sorcerer": 8,
        "Infiltrator": 9, "Friar": 10, "Mercenary": 11, "Necromancer": 12, "Cabalist": 13, "Reaver": 19,
        "Heretic": 33, "Mauler": 60},
    2: {"Thane": 21, "Warrior": 22, "Shadowblade": 23, "Skald": 24, "Hunter": 25, "Healer": 26, "Spiritmaster": 27,
        "Shaman": 28, "Runemaster": 29, "Bonedancer": 30, "Berserker": 31, "Savage": 32, "Valkyrie": 34,
        "Warlock": 59, "Mauler": 61},
    3: {"Eldritch": 40, "Enchanter": 41, "Mentalist": 42, "Blademaster": 43, "Hero": 44, "Champion": 45,
        "Warden": 46, "Druid": 47, "Bard": 48, "Nightshade": 49, "Ranger": 50, "Animist": 55, "Valewalker": 56,
        "Vampiir": 58, "Bainshee": 39, "Mauler": 62},
}
# Weapons whose name carries no class.
NAME_CLASS = {"Upsilon Harp": "Minstrel", "Draiocht Harp": "Bard", "Dochar Recurve Bow": "Ranger", "Thurisaz Compound Bow": "Hunter"}

# Herald bonus type -> eProperty, from the class pass tools (Class-Research/.../albion/work/canon.py).
STAT = {0: 1, 1: 2, 2: 3, 3: 4, 4: 5, 5: 6, 6: 7, 7: 8, 10: 156}
CAP = {0: 201, 1: 202, 2: 203, 3: 204, 4: 205, 5: 206, 6: 207, 7: 208, 10: 209}
SIMPLE = {4: 10, 8: 173, 9: 198, 10: 200, 12: 153, 13: 199, 14: 190, 15: 193, 16: 195, 17: 194, 19: 155, 20: 188,
          21: 191, 22: 148, 27: 174, 29: 210, 30: 211, 34: 196, 37: 185, 38: 147, 46: 182, 47: 186, 48: 187, 49: 180,
          50: 179, 54: 184, 42: 183}
RES = {"Crush": 13, "Slash": 17, "Thrust": 19, "Heat": 15, "Spirit": 18, "Cold": 12, "Matter": 16, "Body": 11,
       "Energy": 14, "Essence": 249}
SKILL = {'Slash': 44, 'Thrust': 50, 'Crush': 25, 'Parry': 40, 'Shield': 43, 'Flexible': 33, 'Staff': 48, 'Polearm': 41,
         'Two Handed': 20, 'Dual Wield': 28, 'Critical Strike': 23, 'Stealth': 49, 'Envenom': 31, 'Crossbow': 24,
         'Longbow': 36, 'Chants': 22, 'Instruments': 35, 'Smiting': 45, 'Rejuvenation': 42, 'Enhancements': 30,
         'Body Magic': 21, 'Mind Magic': 38, 'Matter Magic': 37, 'Spirit Magic': 47, 'Fire Magic': 32,
         'Cold Magic': 34, 'Earth Magic': 29, 'Wind Magic': 51, 'Death Servant': 26, 'Deathsight': 27,
         'Painworking': 39, 'Soulrending': 46, 'Cursing': 106, 'Hexing': 107, 'Witchcraft': 108, 'Fist Wraps': 110,
         'Mauler Staff': 109, 'Power Strikes': 111, 'Magnetism': 112, 'Aura Manipulation': 113, 'All Melee': 164,
         'All Casting': 163, 'All Archery': 168, 'All Duel': 167, 'Valor': 84, 'Pacification': 94,
         'Composite Bow': 68, 'Recurve Bow': 83, 'Thrown': 91, 'Sword': 52, 'Hammer': 53, 'Axe': 54, 'LeftAxe': 55,
         'Left Axe': 55, 'Spear': 56, 'Mending': 57, 'Augmentation': 58, 'Darkness': 60, 'Suppression': 61,
         'Runecarving': 62, 'Stormcalling': 63, 'Beastcraft': 64, 'Light Magic': 65, 'Void Magic': 66,
         'Mana Magic': 67, 'Battlesongs': 69, 'Enchantments': 70, 'Blades': 72, 'Blunt': 73, 'Piercing': 74,
         'Large Weaponry': 75, 'Mentalism': 76, 'Regrowth': 77, 'Nurture': 78, 'Nature Affinity': 79, 'Music': 80,
         'Celtic Dual': 81, 'Celtic Spear': 82, 'Bone Army': 86, 'Verdant Path': 87, 'Creeping Path': 88,
         'Arboreal Path': 89, 'Scythe': 90, 'Hand to Hand': 92, 'Savagery': 95, 'Nightshade': 96, 'Summoning': 98,
         'Dementia': 99, 'Shadow Mastery': 100, 'Vampiiric Embrace': 101, 'Ethereal Shriek': 102,
         'Phantasmal Wail': 103, "Odin's Will": 105, 'Spectral Guard': 114, 'Cave Magic': 85}
RESIST_PASSIVE = 11083  # wielded: +5 crush/slash/thrust resist, stored as item bonuses


def herald_bonuses(bonuses, typenames, problems, name):
    out = {}
    for b in bonuses:
        t, sub, v = b["type"], b.get("id"), b["value"]
        k = None
        if t == 1:
            k = STAT.get(sub)
        elif t == 2:
            k = SKILL.get(typenames[2]["sub_types"].get(str(sub)))
        elif t == 5:
            k = RES.get(typenames[5]["sub_types"].get(str(sub)))
        elif t == 6:
            k = 165 if sub == 304 else None
        elif t == 28:
            k = CAP.get(sub)
        elif t in SIMPLE:
            k = SIMPLE[t]
        if k is None:
            problems.append(f"{name}: bonus type {t}/{sub} value {v} has no mapping, left out")
            continue
        out[k] = out.get(k, 0) + v
    return out


def class_ids(name, realm, requirements):
    usable = requirements.get("usable_by") if requirements else None
    if usable:
        return sorted(set(usable))
    cls = NAME_CLASS.get(name)
    if cls is None:
        cls = next((c for c in CLASS_IDS[realm] if re.search(r"\b%s\b" % c, name)), None)
    return [CLASS_IDS[realm][cls]] if cls else []


def realm_of(name, herald_realm):
    if herald_realm in (1, 2, 3):
        return herald_realm
    for word in re.findall(r"[a-z]+", name.lower()):
        if word in PREFIX_REALM:
            return PREFIX_REALM[word]
    return 0


def slug(name):
    return "champion_" + re.sub(r"[^a-z0-9]+", "_", name.lower()).strip("_")


def norm(name):
    return re.sub(r"[^a-z0-9]", "", name.lower())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--world", required=True)
    ap.add_argument("--reference", required=True)
    ap.add_argument("--out", default=str(Path(__file__).with_name("champion-weaponmasters-2026-11-09.edits.json")))
    a = ap.parse_args()
    w = sqlite3.connect(f"file:{a.world}?mode=ro", uri=True)
    r = sqlite3.connect(f"file:{a.reference}?mode=ro", uri=True)
    edits, problems = [], []

    # Teleports.
    have_tp = {x[0] for x in w.execute("select TeleportID from Teleport")}
    for tid, realm, region, x, y, z, heading in TELEPORTS:
        if tid in have_tp:
            problems.append(f"Teleport {tid} already exists, left alone")
            continue
        edits.append({"table": "Teleport", "key": f"dr215-{tid}", "insert": {
            "Type": "", "TeleportID": tid, "Realm": realm, "RegionID": region, "X": x, "Y": y, "Z": z,
            "Heading": heading, "Teleport_ID": f"dr215-{tid}"}})

    # Weaponmasters.
    masters = w.execute("select Mob_ID, Name, Region, ClassType from Mob where Region in (394,360,395) "
                        "and Name like '%Weaponmaster%' order by Region, Name").fetchall()
    for mob_id, name, region, ctype in masters:
        if ctype != "DOL.GS.ChampionWeaponmaster":
            edits.append({"table": "Mob", "key": mob_id, "set": {"ClassType": "DOL.GS.ChampionWeaponmaster"}})
    if len(masters) != 12:
        problems.append(f"expected 12 throne room Weaponmasters, found {len(masters)}")

    # Spells.
    spells = {x[0] for x in w.execute("select SpellID from Spell where SpellID between 811000 and 812000")}
    def new_spell(source, sid, fields):
        if sid in spells:
            edits.append({"table": "Spell", "where": {"SpellID": sid}, "set": fields})
            return
        source_key = w.execute("select Spell_ID from Spell where SpellID = ?", (source,)).fetchone()[0]
        edits.append({"table": "Spell", "copy": source_key, "key": f"offline-item-official-{sid - 800000}",
                      "set": dict(fields, SpellID=sid, PackageID="OfflineItemEffects")})
        edits.append({"table": "LineXSpell", "key": f"dr215-line-{sid}",
                      "insert": {"LineName": "Item Effects", "SpellID": sid, "Level": 1, "LineXSpell_ID": f"dr215-line-{sid}"}})
    new_spell(811080, 811086, {"Name": "Melee Absorption Debuff (11086)", "Type": "ArmorAbsorptionDebuff", "Value": 10.0,
              "Duration": 15, "Range": 300, "DamageType": 10, "Target": "Enemy",
              "Description": "Reduces the target's melee absorption by 10% for 15 seconds.",
              "Message1": "Your armor feels weaker!", "Message2": "{0}'s armor looks weaker!",
              "Message3": "Your armor feels stronger again.", "Message4": "{0}'s armor looks stronger again."})
    new_spell(811077, 811087, {"Name": "Speed Decrease (11087)", "Type": "SpeedDecrease", "Value": 30.0, "Duration": 20,
              "Range": 2100, "Frequency": 0, "DamageType": 10, "Target": "Enemy",
              "Description": "The target moves 30% slower for 20 seconds."})
    for sid, dtype, label in ((811088, 11, "Cold"), (811089, 12, "Energy"), (811090, 13, "Heat")):
        new_spell(803701, sid, {"Name": f"Direct Damage ({sid - 800000})", "Type": "DirectDamage", "Damage": 75.0,
                  "DamageType": dtype, "Range": 2100, "Radius": 300, "CastTime": 0.0, "Target": "Enemy",
                  "Description": f"Deals 75 {label.lower()} damage to the target and enemies within 300 units."})
    new_spell(74015, 811093, {"Name": "Arch Magery (11093)", "Type": "ResiPierceBuff", "Value": 10.0, "Duration": 30,
              "Target": "Self", "Description": "Your spells pierce 10% of the target's resistances for 30 seconds."})
    new_spell(803464, 811094, {"Name": "Boon of Kings (11094)", "Type": "AfHitsBuff", "Value": -5.0, "Duration": 300,
              "Target": "Self", "Description": "Your armor and health are improved by 5% for 5 minutes."})
    edits.append({"table": "Spell", "where": {"SpellID": 811081}, "set": {
        "Type": "HealAllOverTime", "Value": 4.0,
        "Description": "You regenerate 4% health, endurance and power every 5 seconds for 30 seconds."}})
    edits.append({"table": "Spell", "where": {"SpellID": 811098}, "set": {
        "Type": "BaseArmorFactorBuff", "Target": "Pet", "Value": 15.0, "Damage": 0.0, "Duration": 300, "CastTime": 2.0,
        "Description": "Your pet's armor factor is increased by 15 for 5 minutes."}})
    created_spells = spells | {811086, 811087, 811088, 811089, 811090, 811093, 811094}

    # Weapons.
    typenames = {t: json.loads(j) for t, j in r.execute("select distinct type, type_name from src_herald_bonuses")}
    ours = {}
    for row in w.execute("select Id_nb, Name, PackageID, Object_Type, Item_Type, Hand, Weight from ItemTemplate where Level >= 50"):
        if row[2] != "dolpubdb":
            ours.setdefault(norm(row[1]), row)
    existing_ids = {x[0] for x in w.execute("select Id_nb from ItemTemplate where PackageID = ?", (PACKAGE,))}
    rows = r.execute("select id, name, realm, type_data, bonuses, abilities, requirements, icon, use_duration, "
                     "bonus_level from src_herald_items where id between 57000 and 57599 order by id").fetchall()
    tiers = {}
    for row in rows:
        version = (row[0] - 57000) // 200
        tiers.setdefault(row[1], {})[version] = row
    report = []
    for name, versions in sorted(tiers.items()):
        if 0 not in versions:
            problems.append(f"{name}: no base version in Herald")
            continue
        base = versions[0]
        realm = realm_of(name, base[2])
        reqs = json.loads(base[6]) if base[6] else {}
        classes = class_ids(name, realm, reqs)
        if not realm or not classes:
            problems.append(f"{name}: realm {realm} classes {classes} unknown, skipped")
            continue
        td = json.loads(base[3]) if base[3] else {}
        skill = td.get("skill_used")
        if skill == 147:
            obj = FIST_WRAPS
        elif skill == 148:
            obj = MAULER_STAFF
        else:
            obj = OBJECT_TYPE.get((realm, skill))
        existing = ours.get(norm(name))
        if td and obj is None:
            problems.append(f"{name}: skill_used {skill} has no object type, skipped")
            continue
        if not td:  # instruments
            obj, item_type, hand = existing[3:6] if existing else (45, 13, 1)
        elif td.get("two_handed"):
            item_type, hand = (13, 1) if obj in (9, 15, 18) else (12, 1)
        elif td.get("left_handed"):
            item_type, hand = 11, 2
        else:
            item_type, hand = 10, 0
        bonuses = herald_bonuses(json.loads(base[4] or "[]"), typenames, problems, name)
        for vnum, suffix in ((0, ""), (1, "_v2"), (2, "_v3")):
            if vnum not in versions:
                if vnum:
                    problems.append(f"{name}: Herald has no version {vnum + 1}")
                continue
            h = versions[vnum]
            abilities = sorted(json.loads(h[5] or "[]"), key=lambda x: x.get("position", 0))
            uses = [x["spell"] for x in abilities if x["magic_type"] == 6]
            procs = [x["spell"] for x in abilities if x["magic_type"] == 1]
            passive = [x["spell"] for x in abilities if x["magic_type"] == 3]
            b = dict(bonuses)
            if RESIST_PASSIVE in passive:
                for k in (13, 17, 19):
                    b[k] = b.get(k, 0) + 5
            for s in passive:
                if s != RESIST_PASSIVE:
                    problems.append(f"{name} v{vnum + 1}: passive spell {s} not supported, left out")
            missing = [s for s in uses + procs if 800000 + s not in created_spells]
            if missing:
                problems.append(f"{name} v{vnum + 1}: spells {missing} do not exist, left out")
            uses = [s for s in uses if 800000 + s in created_spells]
            procs = [s for s in procs if 800000 + s in created_spells]
            items = sorted(b.items(), key=lambda kv: kv[0])
            if len(items) > 11:
                problems.append(f"{name} v{vnum + 1}: {len(items)} bonuses, only 11 fit; dropped {items[11:]}")
            fields = {
                "Name": name, "Level": 50, "LevelRequirement": 0, "Realm": realm,
                "AllowedClasses": ";".join(str(c) for c in classes),
                "Object_Type": obj, "Item_Type": item_type, "Hand": hand,
                "Model": h[7] or 0,
                "Type_Damage": td.get("damage_type", 0) if td else 0,
                "DPS_AF": round(td["dps"] * 10) if td.get("dps") else 0,
                "SPD_ABS": round(td["speed"] * 10) if td.get("speed") else 0,
                "Bonus": h[9] or 35, "Quality": 100,
                "SpellID": 800000 + uses[0] if uses else 0,
                "Charges": -1 if uses else 0, "MaxCharges": -1 if uses else 0,
                "SpellID1": 800000 + uses[1] if len(uses) > 1 else 0,
                "Charges1": -1 if len(uses) > 1 else 0, "MaxCharges1": -1 if len(uses) > 1 else 0,
                "CanUseEvery": (h[8] or 720) if uses else 0,
                "ProcSpellID": 800000 + procs[0] if procs else 0,
                "ProcSpellID1": 800000 + procs[1] if len(procs) > 1 else 0,
                "ProcChance": 0, "PoisonSpellID": 0, "PoisonCharges": 0, "PoisonMaxCharges": 0,
                "IsPickable": 1, "IsDropable": 0, "CanDropAsLoot": 0, "IsTradable": 0, "IsIndestructible": 1,
                "Price": 0, "PackageID": PACKAGE, "BonusLevel": 0, "Flags": 0, "TranslationId": None,
                "Description": "Darkness Rising champion weapon. Swap it at a Weaponmaster in your throne room.",
            }
            if existing and existing[6]:
                fields["Weight"] = existing[6]
            for i in range(1, 11):
                k, v = items[i - 1] if i <= len(items) else (0, 0)
                fields[f"Bonus{i}Type"], fields[f"Bonus{i}"] = k, v
            fields["ExtraBonusType"], fields["ExtraBonus"] = items[10] if len(items) > 10 else (0, 0)
            key = slug(name) + suffix
            if key in existing_ids:
                edits.append({"table": "ItemTemplate", "key": key, "set": fields})
            else:
                edits.append({"table": "ItemTemplate", "copy": TEMPLATE_ITEM, "key": key, "set": fields})
            report.append((key, name, realm, fields["AllowedClasses"], fields["SpellID"], fields["SpellID1"],
                           fields["ProcSpellID"], fields["ProcSpellID1"]))

    Path(a.out).write_text(json.dumps(edits, indent=1), encoding="utf-8")
    report_path = Path(a.out).with_suffix("").with_suffix(".report.txt")
    lines = [f"{len(edits)} edits, {len(report)} weapons rows", "", "Problems:"] + ["  " + p for p in problems]
    lines += ["", "Weapons (key, name, realm, classes, use, use2, proc, proc2):"] + ["  " + " | ".join(map(str, x)) for x in report]
    report_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines[:3 + len(problems)]))
    print(f"wrote {a.out} and {report_path}")


if __name__ == "__main__":
    sys.exit(main())
