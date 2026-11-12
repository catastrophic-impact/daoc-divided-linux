"""World data cleanup from the 2026-11-07 server log review (read-only on the world).

    python make_playtest_1107_cleanup_edits.py <world.db>

Writes, next to this script:
- playtest-1107-merchants.edits.json: merchant rows on spawned merchants that name an item id the world does not
  have, where the same item exists under the realm-suffixed id (Hibernian lists ask for quartz_dagger, the world has
  quartz_dagger3). The row is repointed when exactly one candidate fits the merchant's realm (or realm 0) and the
  list does not already hold it. Housing items (paintings, deeds, linings) have no candidate and are left.
- playtest-1107-spells.edits.json: LineXSpell rows whose spell does not exist, and missing spell ids in NPC template
  spell lists. Both did nothing already; this only removes the boot errors ("Tried to load a null spell", "LineXSpell
  Spell Adding Error").
Then: python ../cli.py --game <game> plan <file>  (applies nothing).
"""
import collections
import json
import re
import sqlite3
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent


def stem(item_id):
    return re.sub(r"\d+$", "", item_id.lower())


def merchant_edits(db):
    items = {row[0].lower(): row for row in db.execute("SELECT Id_nb, Realm FROM ItemTemplate")}
    by_stem = collections.defaultdict(list)
    for key in items:
        by_stem[stem(key)].append(key)
    list_realm = collections.defaultdict(collections.Counter)
    for list_id, realm in db.execute("SELECT ItemsListTemplateID, Realm FROM Mob WHERE ItemsListTemplateID <> ''"):
        list_realm[list_id][realm] += 1
    rows = db.execute("SELECT MerchantItem_ID, ItemListID, ItemTemplateID FROM MerchantItem").fetchall()
    in_list = collections.defaultdict(set)
    for _, list_id, item_id in rows:
        in_list[list_id].add((item_id or "").lower())

    edits = []
    for row_id, list_id, item_id in rows:
        if list_id not in list_realm or (item_id or "").lower() in items:
            continue
        realm = list_realm[list_id].most_common(1)[0][0]
        fits = [k for k in by_stem.get(stem(item_id), []) if items[k][1] in (realm, 0)]
        exact = [k for k in fits if items[k][1] == realm] or fits
        if len(exact) != 1 or exact[0] in in_list[list_id]:
            continue
        in_list[list_id].add(exact[0])
        edits.append({"table": "MerchantItem", "key": row_id, "set": {"ItemTemplateID": items[exact[0]][0]}})
    return edits


def spell_edits(db):
    spells = {row[0] for row in db.execute("SELECT SpellID FROM Spell")}
    edits = [{"table": "LineXSpell", "delete": row_id}
             for row_id, spell_id in db.execute("SELECT LineXSpell_ID, SpellID FROM LineXSpell")
             if spell_id not in spells]
    for key, template_spells in db.execute("SELECT NpcTemplate_ID, Spells FROM NpcTemplate WHERE Spells <> ''"):
        parts = [p for p in re.split(r"[;,]", template_spells) if p.strip()]
        kept = [p for p in parts if p.strip().isdigit() and int(p) in spells]
        if len(kept) != len(parts):
            edits.append({"table": "NpcTemplate", "key": key, "set": {"Spells": ";".join(kept)}})
    return edits


def main():
    world = Path(sys.argv[1])
    db = sqlite3.connect(f"file:{world.as_posix()}?mode=ro", uri=True)
    for name, edits in (("playtest-1107-merchants", merchant_edits(db)), ("playtest-1107-spells", spell_edits(db))):
        out = HERE / f"{name}.edits.json"
        out.write_text(json.dumps(edits, indent=1, ensure_ascii=False), encoding="utf-8")
        print(f"{out.name}: {len(edits)} edits")


if __name__ == "__main__":
    main()
