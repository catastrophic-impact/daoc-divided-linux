"""Checks a world that has the craft-families patches applied (make_craft_families_edits.py), read-only.

    python check_craft_families.py <opendaoc.sqlite3.db> <client app folder> <prefix>.report.json

For every recipe the report says was added, by the server's own rules (Recipe.LoadFromDB, NormalServerRules):
  - the recipe row is there with the client's trade and skill, and its product exists
  - the product is the crafter's realm or realm 0 (allow_craft_norealm_items)
  - it has ingredients, each exists, counts are the client's, none is listed twice
  - each ingredient can be had in that realm: sold by a merchant, a drop, or made by a recipe of that realm
  - a built weapon has the crafted DPS for its level, and a Mauler weapon is held by that realm's Mauler
Prints the problems and exits 1 when there are any.
"""
import collections
import json
import sqlite3
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import make_craft_recipes_130_edits as base  # noqa: E402

MAULER_CLASS = {1: '60', 2: '61', 3: '62'}


def main(db_path, app_folder, report_path):
    db = sqlite3.connect(f'file:{db_path}?mode=ro', uri=True)
    client = {r['id']: r for r in base.read_crf(app_folder)}
    report = [r for r in json.loads(Path(report_path).read_text(encoding='utf-8')) if r['why'].startswith('added')]
    items = {r[0].lower(): r for r in db.execute(
        'SELECT Id_nb, Name, Realm, Level, Object_Type, DPS_AF, AllowedClasses, PackageID FROM ItemTemplate')}
    recipes = {str(r[0]): r for r in db.execute(
        'SELECT CraftedItemID, Id_nb, CraftingLevel, CraftingSkillType FROM CraftedItem')}
    rows = collections.defaultdict(list)
    for product, ingredient, count in db.execute('SELECT CraftedItemId_nb, IngredientId_nb, Count FROM CraftedXItem'):
        rows[product.lower()].append((ingredient.lower(), count))
    sold = {r[0].lower() for r in db.execute('SELECT ItemTemplateID FROM MerchantItem')}
    dropped = {r[0].lower() for r in db.execute('SELECT ItemTemplateID FROM LootTemplate')}
    made_in = collections.defaultdict(set)
    for rid, row in recipes.items():
        if rid.isdigit() and int(rid) in client:
            made_in[row[1].lower()].add(client[int(rid)]['realm'])

    problems = []
    for r in report:
        def bad(text):
            problems.append(f'{r["realm"]} {r["id"]} {r["name"]}: {text}')

        row = recipes.get(str(r['id']))
        if row is None:
            bad('no CraftedItem row')
            continue
        if (row[2], row[3]) != (r['skill'], r['trade']):
            bad(f'skill/trade {row[2]}/{row[3]}, client {r["skill"]}/{r["trade"]}')
        product = items.get(row[1].lower())
        if product is None:
            bad(f'product {row[1]} missing')
            continue
        if product[2] not in (r['realm'], 0):
            bad(f'product {row[1]} is realm {product[2]}')
        have = rows.get(row[1].lower(), [])
        if not have:
            bad('no ingredients')
        if len({i for i, _ in have}) != len(have):
            bad('an ingredient is listed twice')
        if sorted(c for _, c in have) != sorted(c for _, c in r['ingredients']):
            bad(f'counts {sorted(c for _, c in have)}, client {sorted(c for _, c in r["ingredients"])}')
        for ingredient, count in have:
            if ingredient not in items:
                bad(f'ingredient {ingredient} missing')
            elif count <= 0:
                bad(f'ingredient {ingredient} count {count}')
            elif not (ingredient in sold or ingredient in dropped or r['realm'] in made_in[ingredient]):
                bad(f'ingredient {ingredient} is not sold, dropped or made in realm {r["realm"]}')
        if product[7] == 'OfflineCraft130' and 1 <= (product[4] or 0) <= 28:
            if r['family'] == 'mauler' and product[5] != 12 + 3 * product[3]:
                bad(f'DPS_AF {product[5]} at level {product[3]}')
            if product[4] in (27, 28) and str(product[6]) not in (MAULER_CLASS[r['realm']], '0', '', 'None'):
                bad(f'AllowedClasses {product[6]} in realm {r["realm"]}')
    print(f'{len(report)} added recipes checked, {len(problems)} problems')
    for p in problems:
        print(' ', p)
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main(*sys.argv[1:4]))
