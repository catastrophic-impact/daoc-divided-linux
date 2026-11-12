"""Writes the World Workshop edits for two recipe families the 1.130 client has and the world lacks (DR-168, phase 3):
the Mauler fist wraps, hand wraps and staves, and the Dragonsworn armor, shields and weapons.

    python make_craft_families_edits.py <opendaoc.sqlite3.db> <client app folder> <out prefix>

Writes <prefix>-ingredients.edits.json (recipes already in the world that ask for a row no one can get),
<prefix>-mauler.edits.json, <prefix>-dragonsworn.edits.json and <prefix>.report.json (every client recipe of
the two families the world lacks: what was done, or why nothing was).

Run it after make_craft_recipes_130_edits.py has nothing left to add: this one covers what plain name matching
cannot. The client recipe reader, the tier words and the row formats are that script's.

Rules
  Ingredients are the world's items by name. The realm is not asked for: the world's own Hibernian recipes already
  take the realm 1 "copper metal bars" and "rowan wooden boards" (the only rows of those names), and the server does
  not check an ingredient's realm.

  A product is an existing item when one fits: the client's name (for Dragonsworn also with the word put in, the
  client's "Enchanted Mail Gloves" in "Dragonsworn Chain Armor" is the world's "Enchanted Dragonsworn Mail Gloves"),
  the recipe's realm or realm 0, level within 3, and no other recipe with other ingredients on it (CraftedXItem
  hangs on the product, so a product has one ingredient list).

  Otherwise the product is built as a copy of a real item, never from nothing:
    Mauler      the same item of another realm (Midgard's "arcanium fist wrap" is Albion's), else Albion's item of
                the same category and skill (Hibernia's "cobalt exceptional hand wrap" is Albion's "fine alloy"
                one), else the nearest tier of that category. Name, realm, level and the Mauler class of that realm
                are set; DPS is the crafted weapon rule 1.2 + 0.3 per level.
    Dragonsworn weapons only: a Dragonsworn weapon of the same name from another realm, or of the same weapon type in
                this realm, reshaped by the realm's plain top tier weapon of that name (type, hand, damage type,
                speed). "alloyed" ones are the same weapon under the other name (the client only changes the look).
  A built item costs what its ingredients cost at a merchant (legendary and Dragonsworn copies keep their source's
  price).

  Not built, listed in the report: Entropic and Somatic weapons (the world has none of the 106, so no proc to copy),
  Dragonsworn shields named for an effect, robes and mandolins (no sibling with real numbers).
"""
import collections
import json
import re
import sqlite3
import sys
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import make_craft_recipes_130_edits as base  # noqa: E402

REALM_TAG = {1: 'alb', 2: 'mid', 3: 'hib'}
MAULER_CLASS = {1: '60', 2: '61', 3: '62'}
TOP_TIERS = ('arcanium', 'arcanite', 'duskwood')
PACKAGE = 'OfflineCraft130'
STAMP = '2026-11-10 00:00:00'
COLUMNS = ('Id_nb', 'Name', 'Realm', 'Level', 'Object_Type', 'Item_Type', 'Hand', 'Type_Damage', 'SPD_ABS', 'DPS_AF',
           'Model', 'Price', 'Weight', 'Quality', 'AllowedClasses') + tuple(f'Bonus{n}Type' for n in range(1, 11))
WEAPON_TYPES = set(range(1, 29))
BOW_TYPES = {5, 9, 10, 15, 18}
MELEE_SKILLS, ARCHERY_SKILLS = 164, 168  # eProperty.AllMeleeWeaponSkills, AllArcherySkills
# The legendary wraps' looks run 3576 Tempestuous, 3577 Lithic, 3579 Benthic; Pyroclasmic had a sword's.
REPAIR_MODELS = {'pyroclasmic_fist_wrap': 3578, 'pyroclasmic_hand_wrap': 3578}
# Albion's Dragonsworn Fist Wrap is a crushing weapon (type 2) only a Mauler may hold, so no one can use it.
FIST_WRAP_FIX = ('1Dragonsworn_Fist_Wrap_Alb', {'Object_Type': 27})


def slug(name):
    return re.sub(r'[^a-z0-9]+', '_', name.lower()).strip('_')


def clean(name):
    return re.sub(r'\s+', ' ', name.strip().lower())


def squash(name):
    """A name without spaces and marks: "Dragonsworn Shortbow" is the client's "short bow"."""
    return re.sub(r'[^a-z0-9]', '', name.lower())


class World:
    def __init__(self, db):
        self.recipes = {str(r[0]): r[1] for r in db.execute('SELECT CraftedItemID, Id_nb FROM CraftedItem')}
        self.as_ingredient = collections.Counter(r[0].lower() for r in db.execute('SELECT IngredientId_nb FROM CraftedXItem'))
        self.sold = collections.Counter(r[0].lower() for r in db.execute('SELECT ItemTemplateID FROM MerchantItem'))
        self.dropped = {r[0].lower() for r in db.execute('SELECT ItemTemplateID FROM LootTemplate') if r[0]}
        self.ingredient_rows = list(db.execute(
            'SELECT CraftedXItem_ID, CraftedItemId_nb, IngredientId_nb, Count FROM CraftedXItem'))
        self.by_name = collections.defaultdict(list)
        self.by_squash = collections.defaultdict(list)
        self.by_id = {}
        for row in db.execute(f'SELECT {", ".join(COLUMNS)} FROM ItemTemplate'):
            self.add(dict(zip(COLUMNS, row)))
        self.attached = collections.defaultdict(list)
        for product, ingredient, count in db.execute('SELECT CraftedItemId_nb, IngredientId_nb, Count FROM CraftedXItem'):
            self.attached[product.lower()].append((ingredient.lower(), count))
        self.attached = {k: sorted(v) for k, v in self.attached.items()}
        self.crafted_in = collections.defaultdict(set)  # product -> realms with a recipe for it
        self.source = {}  # built key -> (real donor key, every column set on it), so a copy of a copy names a real row

    def add(self, item):
        self.by_name[clean(item['Name'])].append(item)
        self.by_squash[squash(item['Name'])].append(item)
        self.by_id[item['Id_nb'].lower()] = item

    def named(self, name):
        """Items of this name, singular or plural: the leather the merchants sell is "tanned leather square", and
        the rows named "tanned leather squares" are unsold leftovers."""
        return [t for n in (name, name[:-1] if name.endswith('s') else name + 's') for t in self.by_name.get(clean(n), [])]

    def ingredient(self, name, realm):
        found = self.named(name)
        if not found:
            return None
        # What this realm's crafters make themselves comes first (a legendary wrap is made from the realm's own
        # exceptional wrap, not Albion's), then the row the world's recipes already take (Midgard's take the realm 1
        # "bronze metal bars").
        return sorted(found, key=lambda t: (realm not in self.crafted_in[t['Id_nb'].lower()],
                                            not self.had(t['Id_nb']),
                                            -self.as_ingredient[t['Id_nb'].lower()], -self.sold[t['Id_nb'].lower()],
                                            t['Realm'] not in (realm, 0), t['Id_nb']))[0]

    def had(self, key):
        """A player can come by this item without crafting it: a merchant sells it or a monster drops it."""
        return bool(self.sold[key.lower()]) or key.lower() in self.dropped

    def free_key(self, name, realm):
        key = f'{slug(name)}_{REALM_TAG[realm]}'
        n = 1
        while key.lower() in self.by_id:
            n += 1
            key = f'{slug(name)}_{REALM_TAG[realm]}{n}'
        return key


def run(db_path, app_folder, prefix):
    recipes = base.read_crf(app_folder)
    db = sqlite3.connect(f'file:{db_path}?mode=ro', uri=True)
    world = World(db)
    report = []
    edits = {'ingredients': [], 'mauler': [], 'dragonsworn': []}
    for r in recipes:
        if str(r['id']) in world.recipes:
            world.crafted_in[world.recipes[str(r['id'])].lower()].add(r['realm'])

    def family(r):
        if r['trade'] == base.BOUNTYCRAFT:
            return None
        if 'dragonsworn' in r['category'].lower():
            return 'dragonsworn'
        return 'mauler' if re.search(r'wrap|mauler', r['category'], re.I) else None

    def ingredients(r):
        """(rows, missing names) of a recipe's ingredients."""
        rows, missing = [], []
        for name, count in r['ingredients']:
            item = world.ingredient(name, r['realm'])
            if item:
                rows.append((item['Id_nb'], count))
            else:
                missing.append(name)
        return rows, missing

    def usable(item, r, rows):
        """An existing item may be this recipe's product: right realm and level, and free or with these ingredients."""
        if item['Realm'] not in (r['realm'], 0) or abs((item['Level'] or 0) - r['level']) > 3:
            return False
        have = world.attached.get(item['Id_nb'].lower())
        return have is None or have == sorted((i.lower(), c) for i, c in rows)

    def add_recipe(fam, r, product, rows, how):
        rid = str(r['id'])
        edits[fam].append({'table': 'CraftedItem', 'insert': {
            'CraftedItemID': rid, 'CraftedItem_ID': rid, 'Id_nb': product, 'CraftingLevel': r['skill'],
            'CraftingSkillType': r['trade'], 'MakeTemplated': 0}})
        if product.lower() not in world.attached:
            for i, (ingredient, count) in enumerate(rows):
                edits[fam].append({'table': 'CraftedXItem', 'insert': {
                    'CraftedXItem_ID': str(uuid.uuid5(base.NAMESPACE, f'{rid}-{i}')), 'CraftedItemId_nb': product,
                    'IngredientId_nb': ingredient, 'Count': count}})
            world.attached[product.lower()] = sorted((i.lower(), c) for i, c in rows)
        world.recipes[rid] = product
        world.crafted_in[product.lower()].add(r['realm'])
        report.append({**r, 'family': fam, 'why': how, 'product': product})

    def build(fam, r, donor, name, sets):
        """A new item: a copy of `donor` with `sets`. Returns its key."""
        key = world.free_key(name, r['realm'])
        real, inherited = world.source.get(donor['Id_nb'].lower(), (donor['Id_nb'], {}))
        full = {**inherited, **sets, 'Name': name, 'Realm': r['realm'], 'Level': r['level'], 'PackageID': PACKAGE,
                'LastTimeRowUpdated': STAMP}
        edits[fam].append({'table': 'ItemTemplate', 'copy': real, 'key': key, 'set': full})
        world.source[key.lower()] = (real, full)
        world.add({**donor, **{k: v for k, v in full.items() if k in COLUMNS}, 'Id_nb': key})
        return key

    def cost(rows):
        return sum((world.by_id[i.lower()]['Price'] or 0) * c for i, c in rows)

    # ---------------------------------------------------------------- Mauler wraps and staves
    def mauler_sibling(r):
        """Albion's product of the same category and skill, else of the nearest skill."""
        same = [x for x in recipes if x['realm'] == 1 and x['trade'] == r['trade'] and x['category'] == r['category']
                and str(x['id']) in world.recipes
                and world.by_id.get(world.recipes[str(x['id'])].lower(), {}).get('Object_Type') in (27, 28)]
        same.sort(key=lambda x: (abs(x['skill'] - r['skill']), x['id']))
        return world.by_id.get(world.recipes[str(same[0]['id'])].lower()) if same else None

    def mauler_repairs():
        """A Mauler recipe already in the world whose product is not a Mauler weapon ("Arcanium Exceptional Fist
        Wrap" is a type 40 trinket with a bag model): shape the product like the nearest good tier."""
        seen = set()
        for r in recipes:
            product = world.by_id.get(world.recipes.get(str(r['id']), '').lower())
            if family(r) != 'mauler' or product is None or product['Object_Type'] in (27, 28) or product['Id_nb'] in seen:
                continue
            sibling = mauler_sibling(r)
            if sibling is None:
                continue
            seen.add(product['Id_nb'])
            fix = {c: sibling[c] for c in ('Object_Type', 'Item_Type', 'Hand', 'Type_Damage')}
            fix.update({'Level': r['level'], 'DPS_AF': 12 + 3 * r['level'], 'Weight': product['Weight'] or 15,
                        'Model': REPAIR_MODELS.get(product['Id_nb'].lower(), sibling['Model'])})
            if product['Realm'] in MAULER_CLASS:
                fix['AllowedClasses'] = MAULER_CLASS[product['Realm']]
            edits['mauler'].append({'table': 'ItemTemplate', 'key': product['Id_nb'], 'set': fix})
            product.update(fix)
            world.source[product['Id_nb'].lower()] = (product['Id_nb'], fix)  # a copy of it is planned before the fix lands
            print(f'repaired {product["Id_nb"]} (was not a Mauler weapon) from {sibling["Id_nb"]}')

    def mauler(r):
        if r['name'].split()[0] in ('Entropic', 'Somatic'):
            return 'not built: the world has no Entropic or Somatic weapon to take the proc from'
        rows, missing = ingredients(r)
        if missing:
            return 'missing ingredient: ' + ', '.join(missing)
        named = [t for t in world.named(r['name']) if t['Object_Type'] in (27, 28)]
        for item in sorted(named, key=lambda t: (t['Realm'] != r['realm'], t['Id_nb'])):
            if usable(item, r, rows):
                add_recipe('mauler', r, item['Id_nb'], rows, 'added (existing item)')
                return None
        donor = (sorted(named, key=lambda t: (abs((t['Level'] or 0) - r['level']), t['Realm'] != 1, t['Id_nb']))[0]
                 if named else mauler_sibling(r))
        if donor is None:
            return 'not built: no item of this kind in any realm'
        legendary = r['category'].startswith('Legendary')
        sets = {'DPS_AF': 12 + 3 * r['level'], 'Quality': 100}
        if not legendary:
            sets['Price'] = cost(rows)
        if str(donor['AllowedClasses']) in MAULER_CLASS.values():
            sets['AllowedClasses'] = MAULER_CLASS[r['realm']]
        if donor['Object_Type'] == 27 and not donor['Weight']:
            sets['Weight'] = 15
        key = build('mauler', r, donor, r['name'], sets)
        add_recipe('mauler', r, key, rows, f'added (built from {donor["Id_nb"]})')
        return None

    # ---------------------------------------------------------------- Dragonsworn
    def variants(name):
        words = name.split()
        yield 'dragonsworn ' + name
        for i in range(1, len(words)):
            yield ' '.join(words[:i] + ['dragonsworn'] + words[i:])
        for tier in TOP_TIERS:  # "arcanium dragonsworn tuck"
            yield f'{tier} dragonsworn {name}'

    def dragon_named(name):
        for v in variants(clean(name)):
            if world.by_squash.get(squash(v)):
                return world.by_squash[squash(v)]
        return []

    def plain_weapon(name, realm):
        """The realm's top tier crafted weapon of this name: what kind of weapon the name is."""
        for tier in TOP_TIERS:
            for item in world.named(f'{tier} {name}'):
                if item['Realm'] in (realm, 0) and item['Object_Type'] in WEAPON_TYPES and (item['Level'] or 0) >= 46:
                    return item
        return None

    def dragonsworn(r):
        rows, missing = ingredients(r)
        if missing:
            return 'missing ingredient: ' + ', '.join(missing)
        named = dragon_named(r['name'])
        for item in sorted(named, key=lambda t: (t['Realm'] != r['realm'], t['Id_nb'])):
            if usable(item, r, rows):
                add_recipe('dragonsworn', r, item['Id_nb'], rows, 'added (existing item)')
                return None
        if any(t['Realm'] in (r['realm'], 0) for t in named):
            return 'product used by another recipe with other ingredients'
        if 'Weapons' not in r['category']:
            return 'not built: no Dragonsworn item of this name and no sibling with real numbers'
        plain_name = re.sub(r'^alloyed ', '', r['name'])
        shape = plain_weapon(plain_name, r['realm'])
        if shape is None:
            return f'not built: the realm has no plain top tier "{plain_name}" to shape it'
        weapons = [t for items in world.by_name.values() for t in items
                   if clean(t['Name']).startswith('dragonsworn ') and t['Object_Type'] in WEAPON_TYPES
                   and (t['Level'] or 0) >= 50 and (t['DPS_AF'] or 0) >= 160]
        # a weapon's bonuses must fit how it is used: no caster staff becomes a Friar's quarterstaff
        skill = ARCHERY_SKILLS if shape['Object_Type'] in BOW_TYPES else MELEE_SKILLS
        fitting = [t for t in weapons if skill in [t[f'Bonus{n}Type'] for n in range(1, 11)]]
        same_name = [t for t in dragon_named(plain_name) if t in fitting]
        same_type = [t for t in fitting if t['Object_Type'] == shape['Object_Type']]
        pool = same_name + [t for t in same_type if t not in same_name]
        if not pool:
            return 'not built: no Dragonsworn weapon of this name or type to copy'
        donor = sorted(pool, key=lambda t: (
            not (t['Realm'] == r['realm'] and t['Object_Type'] == shape['Object_Type'] and t['Hand'] == shape['Hand']),
            t not in same_name, t['Realm'] != r['realm'], t['Hand'] != shape['Hand'],
            abs((t['SPD_ABS'] or 0) - (shape['SPD_ABS'] or 0)), t['Id_nb']))[0]
        sets = {c: shape[c] for c in ('Object_Type', 'Item_Type', 'Hand', 'Type_Damage', 'SPD_ABS')}
        if str(donor['AllowedClasses']) in MAULER_CLASS.values():
            sets['AllowedClasses'] = MAULER_CLASS[r['realm']]
        elif donor['Realm'] != r['realm']:
            sets['AllowedClasses'] = shape['AllowedClasses']  # the other realm's class list means nothing here
        name = 'Dragonsworn ' + ' '.join(w.capitalize() for w in r['name'].split())
        key = build('dragonsworn', r, donor, name, sets)
        add_recipe('dragonsworn', r, key, rows, f'added (built from {donor["Id_nb"]}, shaped by {shape["Id_nb"]})')
        return None

    # ---------------------------------------------------------------- ingredients no one can get
    # Recipes already in the world that ask for a leftover row ("rawhide_to_tanned1", named "tanned leather squares",
    # sold nowhere) when the same thing under the merchants' row exists: point them at that row.
    products = {p.lower() for p in world.recipes.values()}
    repointed, skipped = collections.Counter(), collections.Counter()
    for row_id, product, ingredient, count in world.ingredient_rows:
        item = world.by_id.get(ingredient.lower())
        if item is None or world.had(ingredient) or ingredient.lower() in products:
            continue
        twins = [t for t in world.named(item['Name']) if world.had(t['Id_nb'])]
        if not twins:
            continue
        if all(t['Id_nb'].lower().startswith('housing_') for t in twins):
            skipped[item['Name']] += 1  # furniture as an ingredient: not looked into, left alone
            continue
        twin = sorted(twins, key=lambda t: (-world.as_ingredient[t['Id_nb'].lower()], t['Id_nb']))[0]['Id_nb']
        have = world.attached[product.lower()]
        if any(i == twin.lower() for i, _ in have):
            continue  # the recipe lists both rows: leave it, two rows of one ingredient would not load
        edits['ingredients'].append({'table': 'CraftedXItem', 'key': row_id, 'set': {'IngredientId_nb': twin}})
        world.attached[product.lower()] = sorted((twin.lower() if i == ingredient.lower() else i, c) for i, c in have)
        repointed[f'{ingredient} -> {twin}'] += 1
    for change, n in sorted(repointed.items()):
        print(f'ingredient rows repointed: {change} x{n}')
    print(f'left alone: {sum(skipped.values())} rows asking for {len(skipped)} kinds of unsold furniture rows')

    fix_key, fix = FIST_WRAP_FIX
    if fix_key.lower() in world.by_id and world.by_id[fix_key.lower()]['Object_Type'] != fix['Object_Type']:
        edits['dragonsworn'].append({'table': 'ItemTemplate', 'key': fix_key, 'set': fix})
        world.by_id[fix_key.lower()].update(fix)

    mauler_repairs()
    todo = [r for r in recipes if family(r) and str(r['id']) not in world.recipes]
    # low skill first, and a plain weapon before its "alloyed" twin: a recipe may need what an earlier one built
    todo.sort(key=lambda r: (family(r) != 'mauler', r['name'].startswith('alloyed'), r['skill'], r['id']))
    for r in todo:
        why = mauler(r) if family(r) == 'mauler' else dragonsworn(r)
        if why:
            report.append({**r, 'family': family(r), 'why': why})

    for fam, rows in edits.items():
        Path(f'{prefix}-{fam}.edits.json').write_text(json.dumps(rows, indent=1) + '\n', encoding='utf-8')
    Path(prefix + '.report.json').write_text(json.dumps(report, indent=1) + '\n', encoding='utf-8')
    summary = collections.Counter((r['family'], r['realm'], re.sub(r'\(built from.*', '(built)', r['why']).split(':')[0])
                                  for r in report)
    for key in sorted(summary):
        print(*key, summary[key])
    print(f'ingredients: {len(edits["ingredients"])} rows repointed')
    for fam, rows in list(edits.items())[1:]:
        print(f'{fam}: {sum(1 for e in rows if e["table"] == "CraftedItem")} recipes, '
              f'{sum(1 for e in rows if "copy" in e)} new items, {len(rows)} rows')


if __name__ == '__main__':
    run(*sys.argv[1:4])
