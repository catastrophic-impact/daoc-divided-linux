"""Weapon damage types: melee weapons whose Type_Damage is a magic type (10-15) get their official crush/slash/thrust.

    python -I make_weapon_damage_types_edits.py <world.db> <daoc_item_database_combined.zip> [out-dir]

Why: AttackComponent.GetMeleeDamageType returns (eDamageType)weapon.Type_Damage, so a weapon stored as 13 hits as Heat.
An old import copied each weapon's proc element into Type_Damage (Pyroclasmic = Heat, Lithic = Matter, ...). Officially
every one of these is crush, slash or thrust. Found by the 2026-11-08 missing-items survey
(Research\\Audits\\Missing-Items-2026-11-08).

Where the new value comes from, in order:
  herald-realm  the official 1.126 item with the same name and realm
  herald-any    the official item with the same name in another realm (all copies agree)
  objtype       the only damage type that weapon type has (Blunt = crush, Piercing = thrust, ...)
  herald-stem   the official items named the same apart from the material word (Arcanium/Arcanite ...) agree
  model         most of our other weapons with the same model and object type use this physical type
  name-word     the weapon noun in the name (Hammer = crush, Sword = slash, Spear = thrust), nearest the end wins
Rows none of these settle are left alone and listed in the report.

Skipped on purpose: Stormforged and the Sovereign remakes (owner: intentionally overpowered, fix correctness only),
bows and crossbows (they take the arrow's damage type), shields, and anything outside the weapon object types.
Applies nothing; writes an edits file for `cli.py plan` and a report.
"""
import collections
import json
import os
import sqlite3
import sys
import zipfile

db_path, herald_zip = sys.argv[1], sys.argv[2]
out_dir = sys.argv[3] if len(sys.argv) > 3 else os.path.dirname(os.path.abspath(__file__))
TOPIC = 'weapon-damage-types-2026-11-08'

MAGIC = range(10, 16)
NAMES = {1: 'Crush', 2: 'Slash', 3: 'Thrust'}
# Object types that use weapon.Type_Damage for melee (not bows/crossbows: those use the ammo).
MELEE = {1, 2, 3, 4, 6, 7, 8, 11, 12, 13, 14, 16, 17, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28}
# Weapon types with only one possible damage type.
ONLY = {2: 1, 3: 2, 4: 3, 8: 1, 11: 2, 12: 1, 13: 2, 17: 2, 19: 2, 20: 1, 21: 3, 23: 3, 26: 2, 27: 1, 28: 1}
# Owner-protected items: Stormforged (class 64) and the Sovereign classes (65-67), by class or by their own id prefixes.
PROTECT_CLASSES = {'64', '65', '66', '67'}
PROTECT_IDS = ('storm_', 'stormforged', 'sovereign', 'tara_', 'camelot_', 'lugh_')
# Last resort: the weapon noun in the name. Which type each noun means is learned from the official export below,
# and a noun is trusted only when at least 5 official weapons carry it and 90% of them share one type.
NOUNS = ('spear', 'trident', 'pike', 'lance', 'fork', 'stiletto', 'rapier', 'dirk', 'dagger', 'gladius', 'spike',
         'impaler', 'harpoon', 'javelin', 'needle', 'stinger', 'piercer', 'hammer', 'mace', 'club', 'cudgel', 'maul',
         'flail', 'star', 'morningstar', 'grinder', 'crusher', 'smasher', 'basher', 'mallet', 'mattock', 'staff',
         'chain', 'bludgeon', 'scepter', 'sceptre', 'rod', 'wrap', 'wraps', 'knuckles', 'sword', 'blade', 'axe',
         'claymore', 'scythe', 'cleaver', 'splitter', 'sunderer', 'falchion', 'sabre', 'saber', 'scimitar', 'katana',
         'glaive', 'halberd', 'bill', 'whip', 'claw', 'greatsword', 'longsword', 'chopper', 'reaver', 'razor',
         'slicer', 'cutter', 'edge', 'adze', 'pick', 'sickle', 'harvester', 'reaper', 'greave', 'fang', 'tail')


def protected(row):
    classes = set(str(row['AllowedClasses'] or '').replace(',', ';').split(';'))
    return bool(classes & PROTECT_CLASSES) or str(row['Id_nb'] or '').lower().startswith(PROTECT_IDS)


def strip(name):
    """Drop a leading material word (Arcanium/Arcanite/...) and trailing digits: the realm/level copy names."""
    parts = name.replace('  ', ' ').split(' ')
    while parts and parts[-1].isdigit():
        parts.pop()
    return ' '.join(parts[1:]) if len(parts) > 2 else ' '.join(parts)


def noun(name):
    """The weapon noun nearest the end of the name ("Hammer of the Blade" is a hammer)."""
    text = ' ' + ' '.join(name.lower().replace("'", ' ').replace('-', ' ').split()) + ' '
    found = [(text.rfind(' ' + n + ' '), n) for n in NOUNS if text.rfind(' ' + n + ' ') >= 0]
    return max(found)[1] if found else None


db = sqlite3.connect(f'file:{db_path}?mode=ro', uri=True)
db.row_factory = sqlite3.Row
rows = db.execute('SELECT Id_nb, Name, Realm, Level, Object_Type, Item_Type, Type_Damage, Model, PackageID, '
                  'AllowedClasses FROM ItemTemplate').fetchall()

z = zipfile.ZipFile(herald_zip)
herald = json.loads(z.read('static_objects.json').decode('utf8', 'replace'))['items']
official = collections.defaultdict(lambda: collections.defaultdict(set))   # name -> realm -> {damage types}
by_stem = collections.defaultdict(set)                                       # name minus material word -> types
noun_types = collections.defaultdict(collections.Counter)                    # weapon noun -> official type counts
for it in herald:
    td = it.get('type_data') or {}
    dt = td.get('damage_type') if isinstance(td, dict) else None
    if dt in NAMES and it.get('name'):
        official[it['name'].strip().lower()][it.get('realm')].add(dt)
        by_stem[strip(it['name'].strip().lower())].add(dt)
        n = noun(it['name'])
        if n:
            noun_types[n][dt] += 1

TRUSTED = {}
for n, c in noun_types.items():
    (top, k), = c.most_common(1)
    if k >= 5 and k >= 0.9 * sum(c.values()):
        TRUSTED[n] = top

# Same model + object type majority among our physical weapons.
by_model = collections.defaultdict(collections.Counter)
for r in rows:
    if r['Object_Type'] in MELEE and r['Type_Damage'] in NAMES:
        by_model[(r['Model'], r['Object_Type'])][r['Type_Damage']] += 1

edits, report, left, skipped = [], [], [], []
for r in rows:
    if r['Object_Type'] not in MELEE or r['Type_Damage'] not in MAGIC:
        continue
    if protected(r):
        skipped.append({'Id_nb': r['Id_nb'], 'Name': r['Name'], 'Type_Damage': r['Type_Damage'], 'why': 'protected'})
        continue
    name = (r['Name'] or '').strip().lower()
    new, how = None, None
    off = official.get(name)
    if off:
        same = off.get(r['Realm'], set())
        allv = set().union(*off.values())
        if len(same) == 1:
            new, how = next(iter(same)), 'herald-realm'
        elif not same and len(allv) == 1:
            new, how = next(iter(allv)), 'herald-any'
    if new is None and r['Object_Type'] in ONLY:
        new, how = ONLY[r['Object_Type']], 'objtype'
    if new is None:
        stem = by_stem.get(strip(name), set())
        if len(stem) == 1:
            new, how = next(iter(stem)), 'herald-stem'
    if new is None:
        c = by_model.get((r['Model'], r['Object_Type']))
        if c:
            (top, n), = c.most_common(1)
            if n >= 2 and n >= 0.75 * sum(c.values()):
                new, how = top, 'model'
    if new is None:
        n = noun(name)
        if n in TRUSTED:
            new, how = TRUSTED[n], 'name-word'
    entry = {'Id_nb': r['Id_nb'], 'Name': r['Name'], 'Realm': r['Realm'], 'Object_Type': r['Object_Type'],
             'old': r['Type_Damage'], 'new': new, 'how': how}
    if new is None:
        left.append(entry)
        continue
    edits.append({'table': 'ItemTemplate', 'key': r['Id_nb'], 'set': {'Type_Damage': new}})
    report.append(entry)

with open(os.path.join(out_dir, f'{TOPIC}.edits.json'), 'w', encoding='utf8') as f:
    json.dump(edits, f, indent=1)
summary = {
    'changed': len(edits),
    'by_source': collections.Counter(e['how'] for e in report),
    'by_realm': collections.Counter(e['Realm'] for e in report),
    'by_new_type': collections.Counter(NAMES[e['new']] for e in report),
    'by_object_type': collections.Counter(e['Object_Type'] for e in report),
    'left_alone_unresolved': len(left),
    'skipped_protected': len(skipped),
    'trusted_nouns': TRUSTED,
}
with open(os.path.join(out_dir, f'{TOPIC}.report.json'), 'w', encoding='utf8') as f:
    json.dump({'summary': summary, 'changed': report, 'unresolved': left, 'protected': skipped}, f, indent=1)
print(json.dumps(summary, indent=1))
