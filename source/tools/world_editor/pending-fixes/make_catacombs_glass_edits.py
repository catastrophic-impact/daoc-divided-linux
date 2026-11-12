"""Plan: Atlantean Glass is the Catacombs and Darkness Rising currency too (owner decision 2026-11-09).

One PvE currency instead of a separate aurulite economy (live merged them through the 1.124 exchange and stopped all
of them dropping in 1.126). Read-only generator.

 1. `artifact_glass_regions` gets every Catacombs / Darkness Rising region that has monsters. The other glass rules
    stay: only monsters of level 45 and up (`artifact_glass_min_level`), 20% a kill, named monsters always (5).
    So low-level Catacombs monsters drop no currency.
 2. Tymirre (Shar Labyrinth), the one aurulite merchant in the data, becomes a glass merchant.
 3. Her 26 Spellbound Cobalt pieces (level 45) cost 64,152, a coin price copied into a currency list. They become
    10 glass (15 for chest and leg pieces). Glass merchants elsewhere sell artifact credits for 100-360.
The aurulite item stays in the world, unused.

usage: python -I make_catacombs_glass_edits.py <world.sqlite3.db> <out.edits.json>
"""
import json
import sqlite3
import sys

TYMIRRE_LIST = '77793022'
# Brimstone Caverns (339) and Cave of Cruachan (341) were held back in the first run: a capitalised name always dropped
# 5 glass and those dungeons are full of capitalised trash (Uamhas' Brood x47). The server now counts such a name as a
# boss only with at most artifact_glass_named_max_spawns spawns in its region (DR-238), so nothing is held back.
# Apply a run that adds them only to a world whose server has that rule.
HELD_BACK = set()
BIG_PIECES = {25, 27}     # Item_Type: torso, legs


def main():
    world, out = sys.argv[1], sys.argv[2]
    db = sqlite3.connect(f'file:{world}?mode=ro', uri=True)
    db.row_factory = sqlite3.Row
    edits = []
    now = db.execute("SELECT Value FROM ServerProperty WHERE Key = 'artifact_glass_regions'").fetchone()[0]
    have = [r.strip() for r in now.split(',') if r.strip()]
    new = [str(r[0]) for r in db.execute(
        "SELECT m.Region FROM Mob m JOIN Regions r ON r.RegionID = m.Region WHERE r.Expansion IN (3, 4) AND m.Realm = 0 "
        "GROUP BY m.Region HAVING count(*) > 5 ORDER BY m.Region") if str(r[0]) not in have and r[0] not in HELD_BACK]
    if new:
        edits.append({'table': 'ServerProperty', 'where': {'Category': 'loot', 'Key': 'artifact_glass_regions'},
                      'set': {'Value': ','.join(have + new),
                              'Description': 'Comma separated regions where mobs can drop Atlantean Glass (Atlantis; Catacombs and Darkness Rising since 2026-11-09).'}})
    for m in db.execute("SELECT Mob_ID FROM Mob WHERE ClassType = 'DOL.GS.GameAuruliteMerchant'"):
        edits.append({'table': 'Mob', 'key': m['Mob_ID'], 'set': {'ClassType': 'DOL.GS.GameAtlanteanGlassMerchant'}})
    # Her NPC template names the class too, and the spawn takes it from there (she kept greeting with "aurulite").
    for t in db.execute("SELECT NpcTemplate_ID FROM NpcTemplate WHERE ClassType = 'DOL.GS.GameAuruliteMerchant'"):
        edits.append({'table': 'NpcTemplate', 'key': t['NpcTemplate_ID'], 'set': {'ClassType': 'DOL.GS.GameAtlanteanGlassMerchant'}})
    priced = 0
    for mi in db.execute('SELECT ItemTemplateID FROM MerchantItem WHERE ItemListID = ?', (TYMIRRE_LIST,)):
        it = db.execute('SELECT Id_nb, Item_Type, Price FROM ItemTemplate WHERE Id_nb = ? COLLATE NOCASE', (mi[0],)).fetchone()
        if it is None:
            continue
        price = 15 if int(it['Item_Type'] or 0) in BIG_PIECES else 10
        if int(it['Price'] or 0) != price:
            edits.append({'table': 'ItemTemplate', 'key': it['Id_nb'], 'set': {'Price': price}})
            priced += 1
    with open(out, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(edits, f, indent=0, ensure_ascii=False)
        f.write('\n')
    print(len(edits), 'edits | regions added:', len(new), new, '| items repriced:', priced)


if __name__ == '__main__':
    main()
