"""Writes the World Workshop edits that add the alchemy recipes the 1.130 client offers but the world lacks.

    python make_alchemy_missing_recipes_edits.py <opendaoc.sqlite3.db> <client app folder> <out.edits.json>

The client builds its crafting window from its own recipe file (data/ifd.mpk -> tdl.crf) and sends the recipe id
(packet 0xED); the server looks that id up in CraftedItem. The world only has the Elixir tier of most potion lines,
so every Potion / Draught (and the Greater / Superior instant and stat elixirs) the client lists answered
"That recipe is not known here." (silently before fc6d2a959). Player report 2026-11-08: level 40 endurance regen
(Invigoration) potions.

For each such recipe this adds a CraftedItem row (the client's id and skill level, Alchemy) and its CraftedXItem
ingredient rows (the client's names and counts). Only recipes are added whose product ItemTemplate already exists for
that realm (unused by another recipe) and whose every ingredient is already an alchemy ingredient in the world; the
rest are printed as skipped. Nothing is invented: products, spells and ingredients are the world's own rows.

tdl.crf v103 layout (found 2026-11-08): header ints [version, string bytes, string count, 0, then per realm
(count, ?, recipe offset, ?, ?)]; strings from byte 76; string offset table after them; 88-byte recipe records
(id, name string, model, skill << 16 | ?, tier, item level, up to 8 (ingredient string, count) pairs).
"""
import collections
import json
import sqlite3
import struct
import sys
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[4] / 'tools' / 'asset-tool'))
import archive  # noqa: E402

ALCHEMY = 4
NAMESPACE = uuid.UUID('6f1d6c2e-8f5a-4c55-9a8e-2e6b1b0a1c04')


def client_recipes(app_folder):
    _, entries = archive.read((Path(app_folder) / 'data' / 'ifd.mpk').read_bytes())
    blob = next(e.data for e in entries if e.name.lower() == 'tdl.crf')
    header = struct.unpack('<19I', blob[:76])
    if header[0] != 103:
        raise SystemExit(f'tdl.crf version {header[0]}, this reader knows 103')
    string_bytes, string_count = header[1], header[2]
    table_at = 76 + string_bytes
    offsets = struct.unpack(f'<{string_count}I', blob[table_at:table_at + 4 * string_count])

    def text(i):
        start = 76 + offsets[i]
        return blob[start:blob.index(b'\0', start)].decode('latin1')

    recipes = {}
    for realm, base in ((1, 4), (2, 9), (3, 14)):
        count, at = header[base], header[base + 2]
        for r in range(count):
            v = struct.unpack('<22i', blob[at + r * 88:at + r * 88 + 88])
            ingredients = [(text(v[6 + 2 * k]), v[7 + 2 * k]) for k in range(8) if v[6 + 2 * k]]
            recipes.setdefault(v[0], (realm, text(v[1]), (v[3] >> 16) & 0xffff, v[5], ingredients))
    return recipes


def main(db_path, app_folder, out_path):
    recipes = client_recipes(app_folder)
    db = sqlite3.connect(f'file:{db_path}?mode=ro', uri=True)
    known = {r[0] for r in db.execute('SELECT CraftedItemID FROM CraftedItem')}
    used_products = {r[0].lower() for r in db.execute('SELECT Id_nb FROM CraftedItem')}
    sold = {r[0].lower(): r[1] for r in db.execute(
        'SELECT ItemTemplateID, count(*) FROM MerchantItem GROUP BY lower(ItemTemplateID)')}
    ingredient_ids = collections.defaultdict(set)
    for ingredient, name in db.execute(
            'SELECT DISTINCT x.IngredientId_nb, i.Name FROM CraftedXItem x '
            'JOIN CraftedItem c ON c.Id_nb = x.CraftedItemId_nb JOIN ItemTemplate i ON i.Id_nb = x.IngredientId_nb '
            'WHERE c.CraftingSkillType = ?', (ALCHEMY,)):
        ingredient_ids[name.lower()].add(ingredient)
    templates = collections.defaultdict(list)
    for id_nb, name, realm in db.execute('SELECT Id_nb, Name, Realm FROM ItemTemplate'):
        templates[(name.lower(), realm)].append(id_nb)

    edits, added, skipped = [], [], []
    for rid, (realm, name, skill, level, ingredients) in sorted(recipes.items()):
        if str(rid) in known or skill == 9999 or not ingredients:
            continue
        if not all(n.lower() in ingredient_ids for n, _ in ingredients):
            continue  # another trade's recipe
        products = [t for t in templates[(name.lower(), realm)] if t.lower() not in used_products]
        if len(products) != 1:
            skipped.append((rid, realm, name, f'{len(products)} unused product templates'))
            continue
        rows = []
        for n, count in ingredients:
            # same-named twins (boiled St. John's wort) match the same way in CheckRawMaterials; take the one sold
            choice = sorted(ingredient_ids[n.lower()], key=lambda i: (-sold.get(i.lower(), 0), i))[0]
            rows.append((choice, count))
        edits.append({'table': 'CraftedItem', 'insert': {
            'CraftedItemID': str(rid), 'CraftedItem_ID': str(rid), 'Id_nb': products[0],
            'CraftingLevel': skill, 'CraftingSkillType': ALCHEMY, 'MakeTemplated': 0}})
        for i, (ingredient, count) in enumerate(rows):
            edits.append({'table': 'CraftedXItem', 'insert': {
                'CraftedXItem_ID': str(uuid.uuid5(NAMESPACE, f'{rid}-{i}')), 'CraftedItemId_nb': products[0],
                'IngredientId_nb': ingredient, 'Count': count}})
        added.append((rid, realm, name, level, skill))

    Path(out_path).write_text(json.dumps(edits, indent=1) + '\n', encoding='utf-8')
    for row in added:
        print('add ', *row)
    for row in skipped:
        print('skip', *row)
    print(f'{len(added)} recipes added ({len(edits)} rows), {len(skipped)} skipped -> {out_path}')


if __name__ == '__main__':
    main(*sys.argv[1:4])
