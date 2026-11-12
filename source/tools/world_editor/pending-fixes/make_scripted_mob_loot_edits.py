"""Loot for hostile scripted monsters that drop coin and random items only (2026-11-09).

build_loot_families.py only looks at plain GameNPC / GameEpicNPC / NightSpawn spawns, so monsters whose spawn row
names a script class (DOL.GS.Scripts.Myling, DOL.GS.Scripts.Strangler, named bosses such as DOL.GS.Loken) were never
given a family table. They have no LootTemplate of their own either, and no source we hold (Herald 1.126, EoD
db-public, dolpubdb, Prima) lists drops for them, except Watcher Rylie (Herald: four Empyrean Mist one-time drops).

What this writes:
- each monster gets one MobXLootTemplate row to the shared "family <family> <tier>" table its race/body/level gives
  (same rule as build_loot_families.py);
- greater/hostile myling also link to the "Myling" table and choker/shackler/Throttler to "Strangler" (their kin's
  XP item rows);
- Watcher Rylie gets the four live one-time drops (skipped when present: the 2026-11-08 missing drops patch already
  added them, so the 2026-11-09 run wrote 18 MobX rows and no OTD rows).

Left alone on purpose: keep guards, dummies, merchants, falling ice, Raging Tornado (artifact encounter rewards) and
the 41 names whose MobX row names a missing table but who have a table of their own name (the server falls back to
it, LootGeneratorTemplate.cs "falls back to the mob's own table").

    python -I make_scripted_mob_loot_edits.py <world.sqlite3.db>   -> scripted-mob-loot-2026-11-09.edits.json
"""
import json
import pathlib
import sqlite3
import sys
import uuid

NAMESPACE = uuid.UUID("4d1c6a0e-6f0e-4b52-9a51-2f7f3f9b7c11")
OUT = pathlib.Path(__file__).with_name("scripted-mob-loot-2026-11-09.edits.json")

# name -> (family, why). Family from race/body (build_loot_families rules) or, where both are 0, from what it is.
MOBS = {
    "greater myling": "magical",          # race 2006
    "hostile myling": "undead",           # race 2009
    "choker": "plant",                    # race 2007
    "shackler": "plant",                  # race 2007
    "Throttler": "plant",                 # no race/body; the strangler vines' boss
    "undead soldier": "undead",           # race 2009
    "Loken": "humanoid",                  # race 2005
    "Oona": "undead",                     # no race/body; ghost queen of Nisse's Lair
    "Basilius": "humanoid",               # body 6
    "Develin": "animal",                  # race 2000
    "Lord Sanguis": "undead",             # body 11
    "Abomos the Soultrapper": "humanoid",  # body 6
    "Luriquay": "humanoid",               # body 6
}
KIN = {"greater myling": "Myling", "hostile myling": "Myling",
       "choker": "Strangler", "shackler": "Strangler", "Throttler": "Strangler"}
OTD = {"Watcher Rylie": ["cq_hib_empyrean_mist_robes", "cq_hib_empyrean_mist_leggings",
                         "cq_hib_empyrean_mist_hauberk", "cq_hib_empyrean_mist_cloak"]}


def tier_for(level):
    return max(0, min(9, (int(level) - 1) // 5))


def main():
    db = sqlite3.connect(pathlib.Path(sys.argv[1]).resolve().as_uri() + "?mode=ro", uri=True)
    q = lambda sql, *a: db.execute(sql, a).fetchall()
    tables = {r[0].lower() for r in q("SELECT DISTINCT TemplateName FROM LootTemplate")}
    have = {(m.lower(), (t or "").lower()) for m, t in q("SELECT MobName, LootTemplateName FROM MobXLootTemplate")}
    edits = []
    for name, family in MOBS.items():
        levels = sorted(r[0] or 1 for r in q("SELECT Level FROM Mob WHERE Name = ? AND Realm = 0", name))
        if not levels:
            sys.exit(f"{name}: no spawn")
        table = f"family {family} {tier_for(levels[len(levels) // 2]) + 1}"
        for target in [table] + ([KIN[name]] if name in KIN else []):
            if target.lower() not in tables:
                sys.exit(f"{name}: table {target} missing")
            if (name.lower(), target.lower()) in have:
                continue
            edits.append({"table": "MobXLootTemplate", "insert": {
                "MobXLootTemplate_ID": str(uuid.uuid5(NAMESPACE, f"mobx|{name}|{target}")),
                "MobName": name, "LootTemplateName": target, "DropCount": 1}})
    for name, item_ids in OTD.items():
        for item_id in item_ids:
            if not q("SELECT 1 FROM ItemTemplate WHERE Id_nb = ?", item_id):
                sys.exit(f"{item_id} missing")
            if q("SELECT 1 FROM LootOTD WHERE MobName = ? AND ItemTemplateID = ?", name, item_id):
                continue
            edits.append({"table": "LootOTD", "insert": {
                "LootOTD_ID": str(uuid.uuid5(NAMESPACE, f"otd|{name}|{item_id}")),
                "MobName": name, "ItemTemplateID": item_id, "MinLevel": 0}})
    OUT.write_text(json.dumps(edits, indent=1), encoding="utf-8")
    print(f"{len(edits)} edits -> {OUT.name}")


if __name__ == "__main__":
    main()
