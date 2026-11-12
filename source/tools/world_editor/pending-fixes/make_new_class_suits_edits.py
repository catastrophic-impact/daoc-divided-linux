"""DR-251: the capital armor merchants (Orbs*Armor lists) sell a level 51 six-piece suit for every classic
class, and none for the newer classes. Each missing class gets a copy of a sibling class's suit: same realm,
same armor type, same models and prices, with the sibling's skill bonuses moved to the new class's own lines
(and its mana stat where it differs). Death Knight shares the Necromancer's lines, so it is simply allowed on
the Necromancer suit.

    python -I make_new_class_suits_edits.py <opendaoc.sqlite3.db>   (reads only; writes the edits file beside it)
"""
import json
import sqlite3
import sys
import uuid
from pathlib import Path

OUT = Path(__file__).with_name('new-class-suits-2026-11-10.edits.json')
NAMESPACE = uuid.UUID('6f0c3a52-2c3e-4f53-9c1b-5d2b7e0a2511')
PAGE_SLOTS = 30
PAGES = 5

# eProperty ids
STR, DEX, CON, QUI, INT, PIE = 1, 2, 3, 4, 5, 6
INT_CAP, PIE_CAP = 205, 206
CRIT, EARTH, ENHANCEMENT, ENVENOM, FLEXIBLE, COLD = 23, 29, 30, 31, 33, 34
REJUVENATION, SLASH, STEALTH, WIND = 42, 44, 49, 51
SWORD, HAMMER, LEFT_AXE, DARKNESS, STORMCALLING = 52, 53, 55, 60, 63
PIERCING, CELTIC_DUAL = 74, 81
DEMENTIA, SHADOW_MASTERY, VAMPIIRIC_EMBRACE, ODINS_WILL, CURSING = 99, 100, 101, 105, 106
MAULER_STAFF, FIST_WRAPS, POWER_STRIKES, MAGNETISM, AURA = 109, 110, 111, 112, 113

# new class: (merchant list, sibling class, name prefix, {old property: [new property, next occurrence, ...]})
SUITS = {
    33: ('OrbsAlbionArmor', 5, 'Apostate', {INT: [PIE], INT_CAP: [PIE_CAP], EARTH: [REJUVENATION],
                                            COLD: [ENHANCEMENT], WIND: [FLEXIBLE]}),
    60: ('OrbsAlbionArmor', 9, 'Ironfist', {DEX: [QUI], QUI: [DEX], CRIT: [POWER_STRIKES], SLASH: [FIST_WRAPS],
                                            STEALTH: [MAGNETISM, AURA], ENVENOM: [MAULER_STAFF]}),
    34: ('OrbsMidgardArmor', 21, 'Shieldmaiden', {HAMMER: [SWORD], STORMCALLING: [ODINS_WILL]}),
    59: ('OrbsMidgardArmor', 29, 'Hexwoven', {DARKNESS: [CURSING]}),
    61: ('OrbsMidgardArmor', 23, 'Stormknuckle', {STEALTH: [MAGNETISM, AURA], CRIT: [POWER_STRIKES],
                                                  ENVENOM: [MAULER_STAFF, FIST_WRAPS], LEFT_AXE: [FIST_WRAPS]}),
    39: ('OrbsHiberniaArmor', 40, 'Keening', {}),
    58: ('OrbsHiberniaArmor', 49, 'Bloodbound', {ENVENOM: [VAMPIIRIC_EMBRACE], STEALTH: [DEMENTIA, SHADOW_MASTERY],
                                                CELTIC_DUAL: [SHADOW_MASTERY], CRIT: [DEMENTIA]}),
    62: ('OrbsHiberniaArmor', 49, 'Thornfist', {ENVENOM: [MAULER_STAFF], PIERCING: [FIST_WRAPS],
                                               STEALTH: [MAGNETISM, AURA], CELTIC_DUAL: [POWER_STRIKES],
                                               CRIT: [POWER_STRIKES]}),
}
# Each sibling has one six-piece suit on the list; these pick it when the class has more armor there.
SIBLING_SUIT = {23: 'Ice-Shadow', 49: 'Cured Blackthorn'}
DEATH_KNIGHT, NECROMANCER = 66, 12

def piece_word(item_type, object_type):
    if item_type == 25:
        return {32: 'Robe', 35: 'Hauberk'}.get(object_type, 'Vest')
    return {21: 'Crown' if object_type == 32 else 'Helm', 22: 'Gloves', 23: 'Boots',
            27: 'Pants' if object_type == 32 else 'Leggings', 28: 'Sleeves'}[item_type]


BONUS_TYPES =[f'Bonus{i}Type' for i in range(1, 11)] + ['ExtraBonusType']


def main(db_path):
    db = sqlite3.connect(f'file:{db_path}?mode=ro', uri=True)
    db.row_factory = sqlite3.Row
    used = {}
    for row in db.execute("select ItemListID, PageNumber, SlotPosition from merchantitem where ItemListID like 'Orbs%Armor'"):
        used.setdefault(row['ItemListID'], set()).add((row['PageNumber'], row['SlotPosition']))

    def free_run(item_list, count):
        taken = used.setdefault(item_list, set())
        for page in range(PAGES):
            for start in range(PAGE_SLOTS - count + 1):
                if all((page, slot) not in taken for slot in range(start, start + count)):
                    taken.update((page, slot) for slot in range(start, start + count))
                    return page, start
        raise SystemExit(f'{item_list}: no room for {count} more items')

    edits = []
    for new_class, (item_list, sibling, prefix, remap) in SUITS.items():
        pieces = db.execute("""select t.* from merchantitem m join itemtemplate t on lower(t.Id_nb) = lower(m.ItemTemplateID)
                               where m.ItemListID = ? and t.AllowedClasses = ? order by m.PageNumber, m.SlotPosition""",
                            (item_list, str(sibling))).fetchall()
        stem = SIBLING_SUIT.get(sibling)
        if stem:
            pieces = [p for p in pieces if p['Name'].startswith(stem)]
        if sorted(p['Item_Type'] for p in pieces) != [21, 22, 23, 25, 27, 28]:
            raise SystemExit(f'class {sibling} on {item_list}: expected one six-piece suit, got {[p["Name"] for p in pieces]}')
        if db.execute("select 1 from itemtemplate where AllowedClasses = ? and Level >= 50 and Object_Type between 32 and 38",
                      (str(new_class),)).fetchone():
            print(f'note: class {new_class} already has class-only level 50+ armor somewhere')

        seen = {}
        page, start = free_run(item_list, len(pieces))
        for offset, piece in enumerate(sorted(pieces, key=lambda p: [21, 25, 28, 22, 27, 23].index(p['Item_Type']))):
            suffix = piece_word(piece['Item_Type'], piece['Object_Type'])
            new_id = f'dr_{prefix.lower()}_{suffix.lower()}'
            changes = {'Name': f'{prefix} {suffix}', 'AllowedClasses': str(new_class)}
            for column in BONUS_TYPES:
                old = piece[column]
                if old in remap:
                    targets = remap[old]
                    changes[column] = targets[min(seen.get(old, 0), len(targets) - 1)]
                    seen[old] = seen.get(old, 0) + 1
            edits.append({'table': 'ItemTemplate', 'copy': piece['Id_nb'], 'key': new_id, 'set': changes})
            edits.append({'table': 'MerchantItem', 'insert': {
                'MerchantItem_ID': str(uuid.uuid5(NAMESPACE, f'{item_list}/{new_id}')),
                'ItemListID': item_list, 'ItemTemplateID': new_id,
                'PageNumber': page, 'SlotPosition': start + offset}})

    for piece in db.execute("""select t.Id_nb, t.AllowedClasses from merchantitem m join itemtemplate t on lower(t.Id_nb) = lower(m.ItemTemplateID)
                               where m.ItemListID = 'OrbsAlbionArmor' and t.AllowedClasses = ?""", (str(NECROMANCER),)):
        edits.append({'table': 'ItemTemplate', 'key': piece['Id_nb'],
                      'set': {'AllowedClasses': f'{NECROMANCER};{DEATH_KNIGHT}'}})

    OUT.write_text(json.dumps(edits, indent=1) + '\n', encoding='utf-8')
    print(f'{len(edits)} edits -> {OUT}')


if __name__ == '__main__':
    main(sys.argv[1])
