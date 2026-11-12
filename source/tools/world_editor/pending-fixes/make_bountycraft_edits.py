"""Bountycraft (client trade 5) for the world: materials, products, item spells, recipes and merchant stock.

    python -I make_bountycraft_edits.py <opendaoc.sqlite3.db> <client app folder> <daoc_item_database_combined.zip> <out dir>
        [online-herald-*.json ...]

The optional files are newer item records from the online Herald (tools.camelotherald.com/item/view/<id>, the page's
"Raw Item JSON"), for products the 1.126 export lacks; they are matched by the official id alone, since the client's
recipe names are older than the items' ("Cabalist - Unyielding Majestic Vest" is official 24081 "Unyielding Majestic
Robe").

Writes <out dir>/bountycraft-<date>.edits.json, report.json and summary.json. Applies nothing. Needs the server
build with eCraftingSkill.BountyCrafting (feature/bountycraft): older DLLs ignore skill 5 recipes.

Live rules (patch notes 1.125 and 1.126, darkageofcamelot.com):
  - every character has Bountycraft at 1, it never rises, recipes never fail and make 100% quality (server code);
  - worked at a forge; ingredients are Stardust, a Bounty Weapon/Armor/Accessory Mold, a "Recipe: <set>" item and
    rare alchemy components (Ancient Giant Blood ..., already in the world as drops);
  - Stardust: alchemy merchants, 2.5 platinum, or Alchemy 1000 ("Bountycraft Components" in the client);
  - Bounty Masters sell the molds (1.126: accessory 5,000, armor 6,250, weapon 7,500 BP; not tradable, bought in
    stacks no bigger than the realm rank: server code), the recipe items (1.126: Pictslayer 50,000, Darkness Falls
    75,000, Seasonal 100,000, Aurulite 100,000, Otherworldly 125,000, Frozen 150,000, Curse 175,000 BP) and a
    Box of Rare Alch. Components (2,500 BP; 2-5 of one of eight components: server code);
  - bountycrafted gear cannot be sold to merchants.

Products: the client's recipe record holds the official item id where other trades hold a model id, so each
product is the official Herald item of that id (name checked). A world item of the same name and realm is used as
it is; otherwise a new ItemTemplate is made from the official record (bonuses, charges, procs, class limits, DPS/AF,
the official model) on a donor row of the same object and item type, the way the ToA named drop pass did. Champion
level requirements are left off (owner rule). Item spells follow the existing convention 800000 + official id.
"""
import collections
import json
import os
import re
import sqlite3
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from herald_items import Herald  # noqa: E402
from make_craft_recipes_130_edits import read_crf  # noqa: E402

DATE = '2026-11-08'
PKG = 'OfflineBountycraft'
NS = uuid.UUID('9d6c1f52-5a77-4b1e-8f3e-b0c7a7d10b05')
BOUNTYCRAFT, ALCHEMY = 5, 4
PLATINUM = 10_000_000  # copper
REALM_TAG = {0: 'all', 1: 'alb', 2: 'mid', 3: 'hib'}

# id, name, price (BP at a Bounty Master, copper at a normal merchant), tradable, model (stock: 605 Dust of Creation,
# 2913 heroic armor mold, 498 pattern, 505 chest; the official records are not in the 1.126 export)
MATERIALS = [
    ('stardust', 'Stardust', 25 * PLATINUM // 10, 1, 605),   # model of Dust of Creation
    ('bounty_weapon_mold', 'Bounty Weapon Mold', 7500, 0, 2913),
    ('bounty_armor_mold', 'Bounty Armor Mold', 6250, 0, 2913),
    ('bounty_accessory_mold', 'Bounty Accessory Mold', 5000, 0, 2913),
    ('bounty_recipe_pictslayer', 'Recipe: Pictslayer', 50000, 1, 498),
    ('bounty_recipe_darkness_falls', 'Recipe: Darkness Falls', 75000, 1, 498),
    ('bounty_recipe_seasonal', 'Recipe: Seasonal', 100000, 1, 498),
    ('bounty_recipe_aurulite', 'Recipe: Aurulite', 100000, 1, 498),
    ('bounty_recipe_otherworldly', 'Recipe: Otherworldly', 125000, 1, 498),
    ('bounty_recipe_frozen', 'Recipe: Frozen', 150000, 1, 498),
    ('bounty_recipe_curse', 'Recipe: Curse', 175000, 1, 498),
    ('bounty_rare_alchemy_components_box', 'Box of Rare Alch. Components', 2500, 1, 505),
]
BOUNTY_MASTER_ITEMS = [m[0] for m in MATERIALS if m[0] != 'stardust']
# Armor type in a client name "Otherworldly Mighty Eyes (Cloth)" -> official absorption
ABSORPTION = {'cloth': 0, 'leather': 10, 'studded': 19, 'reinforced': 19, 'chain': 27, 'scale': 27, 'plate': 34}


def uid(*parts):
    return str(uuid.uuid5(NS, '|'.join(str(p) for p in parts)))


def slug(s):
    return re.sub(r'[^a-z0-9]+', '_', s.lower()).strip('_')


def main(db_path, app_folder, zip_path, out_dir, *extra):
    con = sqlite3.connect(f'file:{db_path}?mode=ro', uri=True)
    con.row_factory = sqlite3.Row
    W = {r['Id_nb'].lower(): dict(r) for r in con.execute('SELECT * FROM ItemTemplate')}
    by_name = collections.defaultdict(list)
    for w in W.values():
        by_name[w['Name'].lower()].append(w)
    spells = {r['SpellID']: dict(r) for r in con.execute('SELECT * FROM Spell')}
    line_x = {r[0] for r in con.execute("SELECT SpellID FROM LineXSpell WHERE LineName = 'Item Effects'")}
    world_recipes = {str(r[0]) for r in con.execute('SELECT CraftedItemID FROM CraftedItem')}
    attached = collections.defaultdict(list)
    for p, i, c in con.execute('SELECT CraftedItemId_nb, IngredientId_nb, Count FROM CraftedXItem'):
        attached[p.lower()].append((i.lower(), c))
    h = Herald(zip_path, spells=spells, package=PKG, date=DATE, extra=extra)
    off_by_id = {i['id']: i for i in h.items}
    off_by_name = collections.defaultdict(list)
    for i in h.items:
        off_by_name[i['name'].lower().replace('’', "'")].append(i)
    class_ids = {v['name'].lower(): int(k) for k, v in h.META['classes'].items()}

    edits, report = [], []
    recipes = read_crf(app_folder)

    # ---------------- materials ----------------
    donor_mat = 'Ancient_Giant_Blood' if 'ancient_giant_blood' in W else next(iter(W))
    for id_nb, name, price, tradable, model in MATERIALS:
        if id_nb in W:
            continue
        box = id_nb.endswith('_box')
        edits.append({'table': 'ItemTemplate', 'copy': W[donor_mat.lower()]['Id_nb'], 'key': id_nb, 'set': {
            'Name': name, 'Model': model, 'Price': price, 'IsTradable': tradable, 'IsDropable': tradable,
            'MaxCount': 20 if box else 200, 'PackSize': 1, 'Realm': 0, 'Level': 0, 'Object_Type': 0, 'Item_Type': 40,
            'PackageID': PKG, 'Description': 'Bountycraft material' if not box else 'Use it: 2-5 of one rare alchemy component',
            'ClassType': '', 'TranslationId': '', 'LastTimeRowUpdated': f'{DATE} 00:00:00'}})
        W[id_nb] = {'Id_nb': id_nb, 'Name': name, 'Realm': 0}
        by_name[name.lower()].append(W[id_nb])

    def ingredient_id(name, realm):
        found = [w for w in by_name.get(name.lower(), []) if w['Realm'] in (0, realm)]
        if not found and name.endswith('s'):
            found = [w for w in by_name.get(name[:-1].lower(), []) if w['Realm'] in (0, realm)]
        return sorted(found, key=lambda w: (w['Realm'] != realm, w['Id_nb']))[0]['Id_nb'] if found else None

    # ---------------- products ----------------
    def official_for(r):
        """The official item for a client recipe: by the id in the record, else by name (class / armor type)."""
        name = r['name']
        o = off_by_id.get(r['model'])
        base = re.sub(r'\s*\((cloth|leather|studded|reinforced|chain|scale|plate)\)\s*$', '', name, flags=re.I)
        m = re.match(r'^([A-Za-z ]+?) - (.+)$', base)
        cls, base = (m.group(1).lower(), m.group(2)) if m else (None, base)
        if o and o.get('_online'):
            return o, 'id (online Herald)'
        if o and o['name'].lower().replace('’', "'") == base.lower().replace('’', "'"):
            return o, 'id'
        cands = [i for i in off_by_name.get(base.lower().replace('’', "'"), []) if i['realm'] in (0, r['realm'])]
        armor = re.search(r'\((cloth|leather|studded|reinforced|chain|scale|plate)\)\s*$', name, flags=re.I)
        if armor:
            cands = [i for i in cands if (i.get('type_data') or {}).get('absorption') == ABSORPTION[armor.group(1).lower()]]
        if cls and cls in class_ids:
            narrowed = [i for i in cands if class_ids[cls] in ((i.get('requirements') or {}).get('usable_by') or [])]
            cands = narrowed or cands
        if not cands:
            return None, 'no official item'
        return sorted(cands, key=lambda i: (i['realm'] != r['realm'], i['id']))[0], 'name'

    def donor_for(o, realm):
        ot, it, _ = h.official_type(o)
        if o.get('slot') == 17:
            ot, it = 41, 37   # Mythirian
        best = None
        for w in W.values():
            if 'Object_Type' not in w or (ot is not None and w['Object_Type'] != ot) or (it is not None and w['Item_Type'] != it):
                continue
            if w['Realm'] not in (0, realm) or not w.get('Model') or (w.get('Level') or 0) < 40:
                continue
            key = (w['Realm'] != realm, w.get('ClassType') not in (None, ''), w['Id_nb'])
            if best is None or key < best[0]:
                best = (key, w)
        if best is None and it is not None:
            # e.g. a left-hand Albion flail: no such row, so any flail gives the defaults (Item_Type is set anyway)
            for w in W.values():
                if 'Object_Type' in w and w['Object_Type'] == ot and w['Realm'] in (0, realm) and w.get('Model'):
                    key = (w['Realm'] != realm, w.get('ClassType') not in (None, ''), w['Id_nb'])
                    if best is None or key < best[0]:
                        best = (key, w)
        return best[1] if best else None, ot, it

    made = {}       # (official id, realm or 0) -> new id
    products = {}   # recipe id -> product id
    spells_needed_by = collections.defaultdict(list)
    for r in recipes:
        if r['trade'] != BOUNTYCRAFT or str(r['id']) in world_recipes:
            continue
        o, how = official_for(r)
        world = [w for w in by_name.get((o['name'] if o else r['name']).lower(), [])
                 if w['Realm'] in (0, r['realm']) and 'Object_Type' in w]
        if world:
            products[r['id']] = sorted(world, key=lambda w: (w['Realm'] != r['realm'], -(w.get('Level') or 0), w['Id_nb']))[0]['Id_nb']
            report.append({**r, 'result': 'world item', 'product': products[r['id']], 'official': o and o['id']})
            continue
        if not o:
            report.append({**r, 'result': 'skipped: ' + how})
            continue
        # one item per official record, but a realm-0 item whose realms take different ingredients gets one per set
        key = (o['id'], o['realm'], tuple(sorted(r['ingredients'])))
        if key in made:
            products[r['id']] = made[key]
            report.append({**r, 'result': 'new item (shared)', 'product': made[key], 'official': o['id']})
            continue
        donor, ot, it = donor_for(o, r['realm'])
        if not donor:
            report.append({**r, 'result': f'skipped: no donor of type {ot}/{it}', 'official': o['id']})
            continue
        s, notes = h.item_set(o)
        for sid in list(h.need_spells):
            spells_needed_by[sid] = h.need_spells[sid]
        td = o.get('type_data') or {}
        req = o.get('requirements') or {}
        nid = f"bounty_{slug(o['name'])}_{REALM_TAG[o['realm']]}"
        n, k = nid, 2
        while n.lower() in W:
            n, k = f'{nid}{k}', k + 1
        s.update({'Name': o['name'], 'Realm': o['realm'], 'PackageID': PKG, 'Price': 0, 'Quality': 100,
                  'Description': f"Bountycraft (official item {o['id']})", 'IsPickable': 1, 'CanDropAsLoot': 0,
                  'Model': o.get('icon') or donor['Model'], 'Level': 50 if o.get('slot') == 17 else 51,
                  'LevelRequirement': req.get('level_required', 0),
                  'AllowedClasses': ';'.join(str(c) for c in req.get('usable_by') or []) or '0',
                  'Condition': 50000, 'MaxCondition': 50000, 'Durability': 50000, 'MaxDurability': 50000,
                  'TranslationId': '', 'ClassType': '', 'SalvageYieldID': 0,
                  'LastTimeRowUpdated': f'{DATE} 00:00:00'})
        if ot is not None:
            s['Object_Type'] = ot
        if it is not None:
            s['Item_Type'] = it
        if o['category'] in (1, 3, 4):
            s['DPS_AF'] = int(round((td.get('dps') or 16.5) * 10))
            s['SPD_ABS'] = int(round((td.get('speed') or donor['SPD_ABS'] / 10) * 10))
            if o['category'] == 3:
                s['Type_Damage'] = td.get('shield_size', donor['Type_Damage'])
            elif td.get('damage_type') in (1, 2, 3):
                s['Type_Damage'] = td['damage_type']
            _, _, hand = h.official_type(o)
            if hand is not None:
                s['Hand'] = hand
        elif o['category'] == 2:
            s['SPD_ABS'] = td.get('absorption', donor['SPD_ABS'])
            s['DPS_AF'] = 51 if s.get('Object_Type', donor['Object_Type']) == 32 else (td.get('armor_factor') or 102)
        else:
            s['Type_Damage'] = 0   # a Mythirian's Type_Damage would be a Champion level requirement
        if o['flags'].get('no_destroy'):
            s['IsIndestructible'] = 1
        edits.append({'table': 'ItemTemplate', 'copy': donor['Id_nb'], 'key': n, 'set': s})
        W[n.lower()] = {'Id_nb': n, 'Name': o['name'], 'Realm': o['realm']}
        made[key] = n
        products[r['id']] = n
        report.append({**r, 'result': f'new item (donor {donor["Id_nb"]}, matched by {how})', 'product': n,
                       'official': o['id'], 'notes': notes})

    # ---------------- item spells ----------------
    failed = {}
    for sid in sorted(spells_needed_by):
        ours = 800000 + sid
        if ours not in spells:
            row, why = h.build_spell(sid)
            if row is None:
                failed[ours] = why
                continue
            row['Spell_ID'] = f'offline-item-{ours}'
            edits.append({'table': 'Spell', 'insert': row})
            spells[ours] = row
        if ours not in line_x:
            edits.append({'table': 'LineXSpell', 'insert': {'LineXSpell_ID': uid('lxs', ours), 'LineName': 'Item Effects',
                                                             'SpellID': ours, 'Level': 1}})
            line_x.add(ours)
    for e in edits:
        if e['table'] == 'ItemTemplate' and 'copy' in e:
            for col, charges in (('SpellID', ('Charges', 'MaxCharges')), ('SpellID1', ('Charges1', 'MaxCharges1')),
                                 ('ProcSpellID', ()), ('ProcSpellID1', ())):
                if e['set'].get(col) in failed:
                    e['set'][col] = 0
                    for c in charges:
                        e['set'][c] = 0

    # ---------------- recipes ----------------
    recipe_rows = 0
    alchemy = [r for r in recipes if r['trade'] == ALCHEMY and r['category'] == 'Bountycraft Components']
    for r in [r for r in recipes if r['trade'] == BOUNTYCRAFT] + alchemy:
        rid = str(r['id'])
        if rid in world_recipes:
            continue
        product = products.get(r['id']) if r['trade'] == BOUNTYCRAFT else 'stardust'
        if not product:
            continue
        rows = [(ingredient_id(n, r['realm']), c) for n, c in r['ingredients']]
        if any(i is None for i, _ in rows):
            report.append({**r, 'result': 'skipped: missing ingredient ' + ', '.join(n for (i, _), (n, _) in zip(rows, r['ingredients']) if i is None)})
            continue
        want = sorted((i.lower(), c) for i, c in rows)
        have = attached.get(product.lower())
        if have is not None and sorted(have) != want:
            report.append({**r, 'result': f'skipped: {product} already made with other ingredients'})
            continue
        edits.append({'table': 'CraftedItem', 'insert': {
            'CraftedItemID': rid, 'CraftedItem_ID': rid, 'Id_nb': product, 'CraftingLevel': r['skill'],
            'CraftingSkillType': r['trade'], 'MakeTemplated': 1}})
        if have is None:
            for k, (i, c) in enumerate(rows):
                edits.append({'table': 'CraftedXItem', 'insert': {
                    'CraftedXItem_ID': uid('cx', rid, k), 'CraftedItemId_nb': product, 'IngredientId_nb': i, 'Count': c}})
            attached[product.lower()] = want
        world_recipes.add(rid)
        recipe_rows += 1

    # ---------------- merchants ----------------
    page_use = collections.defaultdict(set)
    sells = collections.defaultdict(set)
    for lst, item, page, slot in con.execute('SELECT ItemListID, ItemTemplateID, PageNumber, SlotPosition FROM MerchantItem'):
        page_use[(lst, page)].add(slot)
        sells[lst].add(item.lower())
    lists = {r[0] for r in con.execute(
        "SELECT DISTINCT ItemsListTemplateID FROM Mob WHERE ClassType = 'DOL.GS.GameBountyMerchant' "
        "AND ItemsListTemplateID IS NOT NULL AND ItemsListTemplateID <> ''")}
    seen_lists = set()

    def add_to(lst, items):
        page = 0
        while page_use.get((lst, page)):
            page += 1
        if page and len(page_use[(lst, page - 1)]) + len(items) <= 30:
            page -= 1
        free = (s for s in range(30) if s not in page_use[(lst, page)])
        for item in items:
            if item.lower() in sells[lst]:
                continue
            slot = next(free)
            page_use[(lst, page)].add(slot)
            sells[lst].add(item.lower())
            edits.append({'table': 'MerchantItem', 'insert': {'MerchantItem_ID': uid('mi', lst, item), 'ItemListID': lst,
                                                               'ItemTemplateID': item, 'PageNumber': page, 'SlotPosition': slot}})

    # MerchantItem.ItemListID is COLLATE NOCASE: midBPTotem and MidBPTotem are the same list
    for lst in sorted(lists):
        if lst in seen_lists:
            continue
        seen_lists.add(lst)
        add_to(lst, BOUNTY_MASTER_ITEMS)
    # Stardust at the alchemy merchants: the lists selling alchemy-only ingredients
    alch_only = set()
    by_skill = collections.defaultdict(set)
    for ing, sk in con.execute('SELECT DISTINCT lower(x.IngredientId_nb), c.CraftingSkillType FROM CraftedXItem x '
                               'JOIN CraftedItem c ON c.Id_nb = x.CraftedItemId_nb'):
        by_skill[ing].add(sk)
    alch_only = {i for i, s in by_skill.items() if s == {ALCHEMY}}
    alchemy_lists = sorted(l for l, items in sells.items() if len(items & alch_only) >= 3)
    for lst in alchemy_lists:
        add_to(lst, ['stardust'])

    # The 1.130 client lists realm-0 products (shared Bountycraft items, Mauler staffs, horse gear...) in every
    # realm's crafting window, but NormalServerRules refuses a realm-0 product unless this is on.
    row = con.execute("SELECT Value FROM ServerProperty WHERE Key = 'allow_craft_norealm_items'").fetchone()
    if row is not None and row[0] != 'True':
        edits.append({'table': 'ServerProperty', 'where': {'Key': 'allow_craft_norealm_items'}, 'set': {'Value': 'True'}})

    os.makedirs(out_dir, exist_ok=True)
    json.dump(edits, open(os.path.join(out_dir, f'bountycraft-{DATE}.edits.json'), 'w', encoding='utf-8'), indent=1)
    json.dump(report, open(os.path.join(out_dir, 'report.json'), 'w', encoding='utf-8'), indent=1, default=str)
    results = collections.Counter(x['result'].split(' (')[0].split(':')[0] for x in report)
    summary = {'bountycraft recipes in the client': sum(1 for r in recipes if r['trade'] == BOUNTYCRAFT),
               'recipes added (incl. stardust alchemy)': recipe_rows, 'products': results,
               'new items': len(made), 'spells not built': failed, 'bounty master lists': sorted(seen_lists),
               'stardust lists': alchemy_lists, 'edits': len(edits),
               'by table': collections.Counter(e['table'] for e in edits),
               'skipped': [(x['realm'], x['id'], x['name'], x['result']) for x in report if x['result'].startswith('skipped')]}
    json.dump(summary, open(os.path.join(out_dir, 'summary.json'), 'w', encoding='utf-8'), indent=1, default=str)
    print(json.dumps(summary, indent=1, default=str)[:6000])


if __name__ == '__main__':
    main(*sys.argv[1:])
