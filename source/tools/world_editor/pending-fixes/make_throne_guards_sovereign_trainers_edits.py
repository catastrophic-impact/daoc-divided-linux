"""DR-211: spawn the three capital throne-room guards and move the Sovereign trainers beside them, in Regalia.

- Royal Guards (ThroneRoomTeleporter) from EoD db-public, at their live spots outside the king's throne room.
  Their NpcTemplate and NPCEquipment rows already exist in our world.
- Crownkeeper / Crownwarden / Brehon of Tara move ~180 units to the side of their guard (the side with the most
  room from other NPCs) and face the same way.
- New NPCEquipment templates sovereign_trainer_{storm,camelot,tara}: the Regalia armour models + class weapons.

usage: python make_throne_guards_sovereign_trainers_edits.py <world.sqlite3> <out.edits.json>
Read-only on the world.
"""
import json, math, sqlite3, sys

db_path, out_path = sys.argv[1:3]
con = sqlite3.connect('file:' + db_path.replace('\\', '/') + '?mode=ro', uri=True)
con.row_factory = sqlite3.Row

GUARD_COMMON = {
    'ClassType': 'DOL.GS.ThroneRoomTeleporter', 'Guild': '', 'Level': 50, 'Speed': 0, 'RespawnInterval': 0,
    'RoamingRange': 0, 'AggroLevel': 0, 'AggroRange': 0, 'Flags': 0, 'MeleeDamageType': 2,
    'Strength': 350, 'Constitution': 50, 'Dexterity': 100, 'Quickness': 50, 'Intelligence': 30, 'Piety': 30,
    'Empathy': 30, 'Charisma': 30, 'PackageID': 'DR-211 throne guards', 'BodyType': 0, 'Race': 0,
}
GUARDS = [  # EoD db-public rows (Mob_ID kept)
    {'Mob_ID': 'ef5e8bc9-2cac-4523-b5fd-e369f7cb8cff', 'Name': "King's Royal Guard", 'Realm': 1, 'Region': 10,
     'X': 32547, 'Y': 24320, 'Z': 8447, 'Heading': 3060, 'Model': 33, 'Size': 52, 'Gender': 1, 'Level': 52,
     'NPCTemplateID': 60162911, 'EquipmentTemplateID': '82553ae8-ccbe-49c2-9aa8-ec66edf002cd'},
    {'Mob_ID': '426dd8cc-93a9-4c9a-b197-9409099d7aee', 'Name': 'Royal Guard', 'Realm': 2, 'Region': 101,
     'X': 31763, 'Y': 27001, 'Z': 8830, 'Heading': 70, 'Model': 209, 'Size': 51, 'Gender': 0,
     'NPCTemplateID': 60165450, 'EquipmentTemplateID': 'c292b020-c3c7-40ee-8137-11f95cabe162'},
    {'Mob_ID': 'e373e658-0874-4696-98df-951f521ee498', 'Name': 'Royal Guard', 'Realm': 3, 'Region': 201,
     'X': 33177, 'Y': 33003, 'Z': 8008, 'Heading': 2040, 'Model': 346, 'Size': 48, 'Gender': 0,
     'NPCTemplateID': 60165451, 'EquipmentTemplateID': '5e18bcd7-2028-4428-8c00-d71ccee331b8'},
]
# realm -> (trainer Mob_ID, equipment template, armour Id_nb prefix, weapons {slot: model})
TRAINERS = {
    1: ('pendragon-trainer', 'sovereign_trainer_camelot', 'camelot_regalia', {12: 3818}),
    2: ('stormforged-trainer', 'sovereign_trainer_storm', 'storm_regalia', {10: 3837, 11: 1138}),
    3: ('tuatha-trainer', 'sovereign_trainer_tara', 'tara_regalia', {12: 3821}),
}
ARMOUR_SLOT = {21: 21, 22: 22, 23: 23, 25: 25, 26: 26, 27: 27, 28: 28}  # Item_Type == NPCEquipment slot
SIDE = 180


def point(x, y, heading, dist):
    a = heading * 2 * math.pi / 4096
    return round(x - math.sin(a) * dist), round(y + math.cos(a) * dist)


edits = []
for g in GUARDS:
    if con.execute('SELECT 1 FROM Mob WHERE Mob_ID=?', (g['Mob_ID'],)).fetchone():
        sys.exit(f"guard {g['Mob_ID']} already in the world")
    edits.append({'table': 'Mob', 'insert': {**GUARD_COMMON, **g}})

    mob_id, tmpl, prefix, weapons = TRAINERS[g['Realm']]
    others = [(r['X'], r['Y']) for r in con.execute(
        'SELECT X, Y FROM Mob WHERE Region=? AND ABS(X-?)<800 AND ABS(Y-?)<800 AND Mob_ID<>?',
        (g['Region'], g['X'], g['Y'], mob_id))]
    best = None
    # Camelot's guard stands in a passage: neither side has navmesh floor (cli.py audit "no floor"); only the hall
    # in front does, so the trainer stands 120 units front-left of the guard (navmesh probe, 2026-11-09).
    for heading, dist in (((3840, 120),) if g['Realm'] == 1 else (((g['Heading'] + 1024) % 4096, SIDE),
                                                                  ((g['Heading'] - 1024) % 4096, SIDE))):
        x, y = point(g['X'], g['Y'], heading, dist)
        room = min((math.dist((x, y), o) for o in others), default=9999)
        if best is None or room > best[0]:
            best = (room, x, y)
    edits.append({'table': 'Mob', 'key': mob_id, 'set': {
        'X': best[1], 'Y': best[2], 'Z': g['Z'], 'Heading': g['Heading'], 'EquipmentTemplateID': tmpl}})
    print(f"realm {g['Realm']}: {mob_id} -> {best[1]},{best[2]},{g['Z']} (nearest other NPC {best[0]:.0f})")

    if con.execute('SELECT 1 FROM NPCEquipment WHERE TemplateID=?', (tmpl,)).fetchone():
        sys.exit(f'{tmpl} already exists')
    for r in con.execute('SELECT Item_Type, Model, Extension, Color FROM ItemTemplate WHERE Id_nb LIKE ?',
                         (prefix + '_%',)):
        edits.append({'table': 'NPCEquipment', 'insert': {
            'NPCEquipment_ID': f"{tmpl}-{r['Item_Type']}", 'TemplateID': tmpl, 'Slot': ARMOUR_SLOT[r['Item_Type']], 'Model': r['Model'],
            'Extension': r['Extension'], 'Color': r['Color'], 'Effect': 0, 'Emblem': 0}})
    for slot, model in weapons.items():
        edits.append({'table': 'NPCEquipment', 'insert': {
            'NPCEquipment_ID': f'{tmpl}-{slot}', 'TemplateID': tmpl, 'Slot': slot, 'Model': model, 'Extension': 0, 'Color': 0, 'Effect': 0, 'Emblem': 0}})

json.dump(edits, open(out_path, 'w', encoding='utf8'), indent=1)
print(len(edits), 'edits ->', out_path)
