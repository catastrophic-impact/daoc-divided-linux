"""Writes the World Workshop edits that bring every trade's recipes up to the 1.130 client, where the world already
has the items (phase 1 of the 2026-11-08 crafting pass).

    python make_craft_recipes_130_edits.py <opendaoc.sqlite3.db> <client app folder> <out prefix>

Writes <prefix>.recipes.edits.json (new CraftedItem + CraftedXItem rows), <prefix>.levels.edits.json (existing
recipes whose skill level differs from the client) and <prefix>.report.json (every client recipe the world lacks,
with why it was not added).

The client builds every crafting window from data/ifd.mpk -> tdl.crf and sends the recipe id; the server looks that
id up in CraftedItem (Recipe.LoadFromDB). A recipe is added only when:
  - its product ItemTemplate exists (same name, the recipe's realm or realm 0, item level within 3 of the client's)
    and either no recipe uses it yet or the recipes using it take the same ingredients (the world shares 222),
  - every ingredient exists as an ItemTemplate (same rule; tiered names are resolved, see below).
Nothing is invented: products, ingredients and counts are the world's rows and the client's numbers.

tdl.crf v103 (0x67), found 2026-11-08 (atom0s/craft_extract v67.hpp is the same layout):
  header 19 ints: version, string bytes, string count, 0, then per realm (recipe count, category count,
    recipe offset, category offset, trade offset). Strings start at byte 76, the offset table follows them.
  recipe, 88 bytes / 22 ints: id, name string, model, skill << 16 | icon, tier << 16 | ?, material << 16 | item
    level, then up to 8 (ingredient string, material << 16 | count) pairs. Skill 9999 rows are category headers.
  category, 104 bytes: name string, then up to 50 uint16 recipe indices.
  trade, 408 bytes: name string, (trade id, label), then uint16 category indices. Trade ids match eCraftingSkill;
    5 is Bountycraft (the server has no such skill yet).
Material ids are the tier word put in front of a generic name ("metal bars" + 7 -> "bronze metal bars"); the table
below was learned from the recipes the world already has (every id agreed on one word).
"""
import collections
import json
import re
import sqlite3
import struct
import sys
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[4] / 'tools' / 'asset-tool'))
import archive  # noqa: E402

NAMESPACE = uuid.UUID('3b0c8d4e-1f6a-4f7e-9b52-7d1c130a2e11')
BOUNTYCRAFT = 5
MATERIALS = {
    7: 'bronze', 8: 'iron', 9: 'mithril', 10: 'asterite', 14: 'alloy', 15: 'steel', 17: 'rawhide', 18: 'tanned',
    19: 'cured', 20: 'hard', 21: 'rigid', 22: 'embossed', 23: 'imbued', 24: 'runed', 25: 'eldritch',
    26: 'fine alloy', 29: 'adamantium', 31: 'rowan', 32: 'elm', 33: 'oaken', 34: 'ironwood', 35: 'heartwood',
    36: 'runewood', 37: 'stonewood', 38: 'ebonwood', 39: 'dyrwood', 41: 'woolen', 42: 'linen', 43: 'brocade',
    44: 'silk', 45: 'gossamer', 46: 'sylvan', 47: 'seamist', 48: 'nightshade', 49: 'wyvernskin', 50: 'leaf',
    51: 'bone', 52: 'vine', 53: 'shell', 54: 'fossil', 55: 'amber', 56: 'coral', 57: 'chitin', 58: 'copper',
    59: 'ferrite', 60: 'quartz', 61: 'dolomite', 62: 'cobalt', 63: 'carbide', 64: 'sapphire', 65: 'diamond',
    66: 'netherite', 67: 'arcanite', 68: 'netherium', 69: 'arcanium', 70: 'tempered', 71: 'duskwood',
    72: 'silksteel', 73: 'petrified', 74: 'crystalized', 101: 'rough clout', 102: 'rough', 103: 'clout',
    104: 'rough flight', 105: 'standard', 106: 'footed clout', 107: 'flight', 108: 'footed', 109: 'footed flight',
    110: 'footed flight',
    89: 'perfect',   # Legendary weapons: "3 Perfect Fiery Essence Jewel" (Allakhazam recipe 6424); no world recipe uses it
}

# Legendary weapon materials the world lacks. Live: gathered in the Otherworlds (not in this world) or bought at the
# Legendary Items Merchant in Oceanus Haven (Pendragon hot fix, 2015); 20 ore per weapon, an Orb of Entropy for
# Entropic and a Pound of Flesh for Somatic ones. Sold here at each realm's high-tier craft merchants (AlbExpensiveTrade, MidExpensiveTrade,
# HibExpensiveCraftComponents).
# Prices are this project's choice (live gives none): ore 1 gold, orb and pound 50 gold.
EXTRA_MATERIALS = [
    # id, name, copper, donor (stack size), model. Not the metal bar model 519: the server picks an ingredient's
    # secondary skill by its model, so a 519 ore asked for Metalworking "to create the Otherworldly Ore".
    ('otherworldly_ore', 'Otherworldly Ore', 10_000, 'arcanium_metal_bars', 488),
    ('orb_of_entropy', 'Orb of Entropy', 500_000, 'Dust_of_Creation', 605),
    ('pound_of_flesh', 'Pound of Flesh', 500_000, 'Dust_of_Creation', 605),
]
GEM_TIER = re.compile(r'^(Raw|Uncut|Rough|Flawed|Imperfect|Polished|Faceted|Precious|Flawless|Perfect)\s*\+\d+$', re.I)


def product_names(r):
    """Names the product may have in the world, best first. The client often names a product only inside its
    category: "Faceted +7" in "Glacial War Rune (Fist Wraps Skill)" is "faceted glacial war rune"; "war hammer" in
    "Dragonsworn Weapons" is "Dragonsworn War Hammer"."""
    category = re.sub(r'\s*\(.*\)$', '', r['category']).strip()
    names = [r['name']]
    gem = GEM_TIER.match(r['name'])
    if gem:
        names.append(f'{gem.group(1)} {category}')
    if category.split() and category.split()[0].lower() in ('dragonsworn', 'legendary'):
        names.append(f'{category.split()[0]} {r["name"]}')
    return names


def read_crf(app_folder):
    _, entries = archive.read((Path(app_folder) / 'data' / 'ifd.mpk').read_bytes())
    blob = next(e.data for e in entries if e.name.lower() == 'tdl.crf')
    h = struct.unpack('<19I', blob[:76])
    if h[0] != 103:
        raise SystemExit(f'tdl.crf version {h[0]}, this reader knows 103')
    table_at = 76 + h[1]
    offsets = struct.unpack(f'<{h[2]}I', blob[table_at:table_at + 4 * h[2]])

    def text(i):
        start = 76 + offsets[i]
        return blob[start:blob.index(b'\0', start)].decode('latin1')

    def full(name, material):
        return f'{MATERIALS[material]} {name}' if material in MATERIALS else name

    recipes, seen = [], set()
    for realm, base in ((1, 4), (2, 9), (3, 14)):
        count, ncat, at, cat_at, trade_at = h[base:base + 5]
        end = h[base + 7] if realm < 3 else len(blob)
        records = [struct.unpack('<22i', blob[at + r * 88:at + r * 88 + 88]) for r in range(count)]
        cats = []
        for c in range(ncat):
            b = blob[cat_at + c * 104:cat_at + c * 104 + 104]
            cats.append((text(struct.unpack('<i', b[:4])[0]), [s for s in struct.unpack('<50H', b[4:]) if s]))
        for t in range((end - trade_at) // 408):
            b = blob[trade_at + t * 408:trade_at + t * 408 + 408]
            if not struct.unpack('<i', b[:4])[0]:
                continue
            for c in [s for s in struct.unpack('<202H', b[4:])[2:] if s]:
                for r in cats[c][1]:
                    v = records[r]
                    if v[3] >> 16 == 9999 or v[0] in seen:
                        continue
                    seen.add(v[0])
                    recipes.append({
                        'realm': realm, 'trade': t, 'id': v[0], 'category': cats[c][0],
                        'name': full(text(v[1]), v[5] >> 16), 'skill': v[3] >> 16, 'level': v[5] & 0xffff, 'model': v[2],
                        'ingredients': [(full(text(v[6 + 2 * k]), v[7 + 2 * k] >> 16), v[7 + 2 * k] & 0xffff)
                                        for k in range(8) if v[6 + 2 * k]]})
    return recipes


def main(db_path, app_folder, prefix):
    recipes = read_crf(app_folder)
    db = sqlite3.connect(f'file:{db_path}?mode=ro', uri=True)
    world = {str(r[0]): (r[1], r[2], r[3]) for r in db.execute(
        'SELECT CraftedItemID, Id_nb, CraftingLevel, CraftingSkillType FROM CraftedItem')}
    as_ingredient = collections.Counter(r[0].lower() for r in db.execute('SELECT IngredientId_nb FROM CraftedXItem'))
    sold = collections.Counter(r[0].lower() for r in db.execute('SELECT ItemTemplateID FROM MerchantItem'))
    templates = collections.defaultdict(list)
    for id_nb, name, realm, level in db.execute('SELECT Id_nb, Name, Realm, Level FROM ItemTemplate'):
        templates[name.lower()].append((id_nb, realm, level))

    def lookup(name, realm):
        for n in (name, name[:-1] if name.endswith('s') else name + 's'):
            found = [t for t in templates[n.lower()] if t[1] in (realm, 0)]
            if found:
                return found
        return []

    attached = collections.defaultdict(list)  # product -> sorted (ingredient, count), world rows and ours
    for product, ingredient, count in db.execute('SELECT CraftedItemId_nb, IngredientId_nb, Count FROM CraftedXItem'):
        attached[product.lower()].append((ingredient.lower(), count))
    attached = {k: sorted(v) for k, v in attached.items()}

    material_edits = []
    if not templates['otherworldly ore']:
        lists = [row[0] for row in db.execute(
            "SELECT DISTINCT ItemsListTemplateID FROM Mob WHERE ItemsListTemplateID LIKE '%Expensive%'")]
        pages = collections.defaultdict(set)
        for lst, page, slot in db.execute('SELECT ItemListID, PageNumber, SlotPosition FROM MerchantItem'):
            pages[(lst.lower(), page)].add(slot)
        donors = {row[0].lower(): row[0] for row in db.execute('SELECT Id_nb FROM ItemTemplate')}
        for id_nb, name, price, donor, model in EXTRA_MATERIALS:
            material_edits.append({'table': 'ItemTemplate', 'copy': donors[donor.lower()], 'key': id_nb, 'set': {
                'Name': name, 'Price': price, 'Model': model, 'Realm': 0, 'Level': 51, 'PackageID': 'OfflineCraft130',
                'Description': 'Legendary weapon material (Otherworlds)', 'TranslationId': '',
                'LastTimeRowUpdated': '2026-11-08 00:00:00'}})
            templates[name.lower()].append((id_nb, 0, 51))
            sold[id_nb] += 1
        for lst in sorted(lists):
            page = max([p for (l, p) in pages if l == lst.lower()] or [0])
            free = [slot for slot in range(30) if slot not in pages[(lst.lower(), page)]]
            if len(free) < len(EXTRA_MATERIALS):
                page, free = page + 1, list(range(30))
            for (id_nb, *_), slot in zip(EXTRA_MATERIALS, free):
                material_edits.append({'table': 'MerchantItem', 'insert': {
                    'MerchantItem_ID': str(uuid.uuid5(NAMESPACE, f'mi-{lst}-{id_nb}')), 'ItemListID': lst,
                    'ItemTemplateID': id_nb, 'PageNumber': page, 'SlotPosition': slot}})

    def pick_ingredient(name, realm):
        return sorted(lookup(name, realm), key=lambda t: (
            -as_ingredient[t[0].lower()], -sold[t[0].lower()], t[1] != r['realm'], t[0]))[0][0]

    level_edits, insert_edits, report = [], [], []
    for r in recipes:
        rid = str(r['id'])
        if rid in world:
            id_nb, level, trade = world[rid]
            if trade == r['trade'] and level != r['skill'] and r['trade'] != BOUNTYCRAFT:
                level_edits.append({'table': 'CraftedItem', 'key': rid, 'set': {'CraftingLevel': r['skill']}})
            continue
        why, product, add_rows = None, None, []
        missing = [n for n, _ in r['ingredients'] if not lookup(n, r['realm'])]
        named, fitting = [], []
        for candidate in product_names(r):
            found = lookup(candidate, r['realm'])
            # a generic name ("war hammer" for a level 51 Dragonsworn weapon) must not land on an unrelated item
            fit = [t for t in found if r['trade'] == 3 or not r['level'] or not t[2] or abs(t[2] - r['level']) <= 3]
            named = named or found
            if fit:
                named, fitting = found, fit
                break
        if r['trade'] == BOUNTYCRAFT:
            why = 'bountycraft (no server skill yet)'
        elif not r['ingredients']:
            why = 'no ingredients in the client'
        elif missing:
            why = 'missing ingredient: ' + ', '.join(missing)
        elif not named:
            why = 'no product item'
        elif not fitting:
            why = 'no product item at the right level'
        else:
            wanted = sorted((pick_ingredient(n, r['realm']).lower(), c) for n, c in r['ingredients'])
            # a product may serve several recipes (the world shares 222) only with the same ingredients
            for t in sorted(fitting, key=lambda t: (t[0].lower() in attached, t[1] != r['realm'],
                                                    abs((t[2] or 0) - r['level']), t[0])):
                have = attached.get(t[0].lower())
                if have is None:
                    product, add_rows = t[0], r['ingredients']
                    break
                if have == wanted:
                    product = t[0]
                    break
            if product is None:
                why = 'product used by another recipe with other ingredients'
        if why:
            report.append({**r, 'why': why})
            continue
        insert_edits.append({'table': 'CraftedItem', 'insert': {
            'CraftedItemID': rid, 'CraftedItem_ID': rid, 'Id_nb': product, 'CraftingLevel': r['skill'],
            'CraftingSkillType': r['trade'], 'MakeTemplated': 0}})
        rows = []
        for i, (name, count) in enumerate(add_rows):
            choice = pick_ingredient(name, r['realm'])
            rows.append((choice.lower(), count))
            insert_edits.append({'table': 'CraftedXItem', 'insert': {
                'CraftedXItem_ID': str(uuid.uuid5(NAMESPACE, f'{rid}-{i}')), 'CraftedItemId_nb': product,
                'IngredientId_nb': choice, 'Count': count}})
        if add_rows:
            attached[product.lower()] = sorted(rows)
        report.append({**r, 'why': 'added' if add_rows else 'added (shared product)', 'product': product})

    Path(prefix + '.recipes.edits.json').write_text(json.dumps(material_edits + insert_edits, indent=1) + '\n', encoding='utf-8')
    Path(prefix + '.levels.edits.json').write_text(json.dumps(level_edits, indent=1) + '\n', encoding='utf-8')
    Path(prefix + '.report.json').write_text(json.dumps(report, indent=1) + '\n', encoding='utf-8')
    summary = collections.Counter((r['realm'], r['trade'], r['why'].split(':')[0]) for r in report)
    for key in sorted(summary):
        print(*key, summary[key])
    added = sum(1 for r in report if r['why'].startswith('added'))
    print(f'{added} recipes added ({len(insert_edits)} rows), {len(level_edits)} skill levels fixed')


if __name__ == '__main__':
    main(*sys.argv[1:4])
