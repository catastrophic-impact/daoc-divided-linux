"""World data clean-up from the 2026-11-06 setup review (read-only on the world).

    python make_setup_review_1107_edits.py <world.db>

Writes setup-review-1107.edits.json next to this script; then
    python ../cli.py --game <game> plan setup-review-1107.edits.json  (applies nothing)
Each edit removes a boot error and changes nothing a player could use:
- the seven Void Merchants (DOL.GS.Scripts.RPTradeInMerchant, an Atlas currency vendor upstream removed in 2025):
  they load as plain NPCs that do nothing;
- the three Parthanan Farm Controllers name a class that does not exist; the code has one per farm
  (Controller1 Lough Derg, Controller2 Connacht, Controller3 Lough Gur), so the farms never spawn their boss;
- three NpcTemplate class names without their namespace (QanrisDuros, ScoutArgyle) or of a class that is commented
  out (DOL.GS.Scholar, the Scholar Nenet template, which already loads as a plain GameNPC);
- spell 14800 Comforting Flames: target "ground" is not a value (the ground target is "Area");
- merchant rows whose item exists nowhere (left after playtest-1107-merchants repointed what it could);
- loot template rows whose item exists nowhere;
- monsters that name an NpcTemplate that does not exist (they keep their own row values).
"""
import json
import sqlite3
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
FARMS = {"ParthananFarmLoughDerg": "DOL.GS.ParthananFarmController1",
         "ParthananFarmConnacht": "DOL.GS.ParthananFarmController2",
         "ParthananFarmLoughGur": "DOL.GS.ParthananFarmController3"}
TEMPLATE_CLASSES = {"QanrisDuros": "DOL.GS.QanrisDuros", "ScoutArgyle": "DOL.GS.ScoutArgyle", "DOL.GS.Scholar": "DOL.GS.GameNPC"}


def edits(db):
    out = []
    for (mob_id,) in db.execute("SELECT Mob_ID FROM Mob WHERE ClassType = 'DOL.GS.Scripts.RPTradeInMerchant'"):
        out.append({"table": "Mob", "delete": mob_id})
    for mob_id, package in db.execute("SELECT Mob_ID, PackageID FROM Mob WHERE ClassType = 'DOL.GS.ParthananFarmController'"):
        out.append({"table": "Mob", "key": mob_id, "set": {"ClassType": FARMS[package]}})
    for key, cls in db.execute("SELECT NpcTemplate_ID, ClassType FROM NpcTemplate"):
        if cls in TEMPLATE_CLASSES:
            out.append({"table": "NpcTemplate", "key": key, "set": {"ClassType": TEMPLATE_CLASSES[cls]}})
    for (key,) in db.execute("SELECT Spell_ID FROM Spell WHERE SpellID = 14800 AND Target = 'ground'"):
        out.append({"table": "Spell", "key": key, "set": {"Target": "Area"}})
    items = {row[0].lower() for row in db.execute("SELECT Id_nb FROM ItemTemplate")}
    items |= {row[0].lower() for row in db.execute("SELECT Id_nb FROM ItemUnique")}
    for key, item in db.execute("SELECT MerchantItem_ID, ItemTemplateID FROM MerchantItem"):
        if (item or "").lower() not in items:
            out.append({"table": "MerchantItem", "delete": key})
    for key, item in db.execute("SELECT LootTemplate_ID, ItemTemplateID FROM LootTemplate"):
        if (item or "").lower() not in items:
            out.append({"table": "LootTemplate", "delete": key})
    templates = {row[0] for row in db.execute("SELECT TemplateId FROM NpcTemplate")}
    for mob_id, template in db.execute("SELECT Mob_ID, NPCTemplateID FROM Mob WHERE NPCTemplateID > 0"):
        if template not in templates:
            out.append({"table": "Mob", "key": mob_id, "set": {"NPCTemplateID": -1}})
    return out


def main():
    world = Path(sys.argv[1])
    db = sqlite3.connect(f"file:{world.as_posix()}?mode=ro", uri=True)
    result = edits(db)
    out = HERE / "setup-review-1107.edits.json"
    out.write_text(json.dumps(result, indent=1, ensure_ascii=False), encoding="utf-8")
    counts = {}
    for e in result:
        kind = e["table"] + (" delete" if "delete" in e else " set")
        counts[kind] = counts.get(kind, 0) + 1
    print(f"{out.name}: {len(result)} edits {counts}")


if __name__ == "__main__":
    main()
