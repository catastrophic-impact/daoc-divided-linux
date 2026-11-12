"""Housing decorations: stock the market Decorators, add the 1.130 client's missing decorations and trophies,
give Siegecraft Supplies realm siege kits (owner request 2026-11-08, issues DR-161/DR-162).

    python make_housing_decor_edits.py --db <world.db> --catalog <Housing-Audit/client-catalog/client-housing-assets.csv>

Writes housing-decor-2026-11-08.edits.json beside this script. Reads the world read-only; World Workshop applies it.

What it builds:
- Decorators (9 per realm in the housing markets) sold nothing: their 27-item lists pointed at templates that
  never existed (also missing in OpenDAoC's db-public), and the 11-07 orphan clean-up left them empty. Hibernia's
  items already exist in the house shop; Albion and Midgard get new templates. Painting models follow the client
  catalog order (Alb 1382-1391, Hib 1392-1401 matches our existing Hib items exactly, Mid 1402-1411). Tapestry and
  arch models are matched from the catalog renders (best guesses, see TAPESTRY/ARCH notes below).
- Every client housing model with no server item becomes one: ordinary decor and seasonal items go into the house
  buy menus (indoor / garden); trophies go on the Taxidermy Supplies house NPC (owner: sell trophies there).
- Siegecraft Supplies gets one NPC template per realm that sells that realm's Siege Master kits, and the realm house
  NPC shops sell the realm version.
Prices are our own choice (no live data): they follow the existing house shop prices for similar items.
"""
import argparse
import csv
import json
import re
import sqlite3
from pathlib import Path

HERE = Path(__file__).resolve().parent
OUT = HERE / 'housing-decor-2026-11-08.edits.json'

DECORATORS = {'alb': '2c84ce8d-21b4-462b-bd81-24e07757401e', 'mid': '75e0a81e-6140-4a25-82b8-ba7eeac1ffef',
              'hib': '410c36c1-1913-4dc8-b073-5486eb3e876b'}

ALB_PAINTINGS = ['Icons of Caer Sidi', 'Gothwaite Harbor at Night', 'Sunset Over Avalon Isle', 'Agony of Defeat',
                 'Caer Gothwaite', 'Stonehenge', 'Sunset at Gothwaite Harbor', 'Bridge to Clifton', 'Anniogel at Night',
                 'Safety in the Distance']
MID_PAINTINGS = ['Glacial Resting Place', 'Frozen Horizon', 'Aegirhamn Docks', 'Darkness Falls', 'Skyline of Jordheim',
                 "Queen Kula's Splendor", "Hazards of Iarnvidiur's Lair", 'Wintry Forest Sunrise', 'Sanctuary',
                 'Fog Over Hagall']
HIB_PAINTING_IDS = ['housing_indoor_shop55', 'housing_indoor_shop56', 'housing_indoor_shop57', 'housing_indoor_shop58',
                    'housing_indoor_shop59', 'housing_indoor_shop60', 'housing_indoor_shop61', 'housing_indoor_shop62',
                    'housing_indoor_shop63', 'housing_indoor_shop64']
HIB_GRANDIOSE_IDS = ['housing_indoor_shop66', 'housing_indoor_shop67', 'housing_indoor_shop68', 'housing_indoor_shop69',
                     'housing_indoor_shop70', 'housing_indoor_shop71', 'housing_indoor_shop72', 'housing_indoor_shop73',
                     'housing_indoor_shop74', 'housing_indoor_shop75']
# TAPESTRY: 01 snow + wolf warrior = Midgard hero; 01a robed caster = Albion adept; 01b = A Hero of Hibernia (existing);
# 02 = Hibernian Horseman (existing); 02b blue/red knight = A Hero of Albion; 02a = Three Defenders of Midgard (left over).
TAPESTRIES = {'alb': [('A Hero of Albion wall tapestry', 1593), ('An Albion Adept wall tapestry', 1576)],
              'mid': [('A Hero of Midgard wall tapestry', 1572), ('Three Defenders of Midgard wall tapestry', 1592)]}
# ARCH: 01 iron-braced stone = granite (Alb), 02 = stag (Hib, existing), 03 beams + rope + lanterns = crossed-beam (Mid).
ARCHES = {'alb': [('granite arch', 1453, 500000), ('walled granite arch', 1463, 1000000)],
          'mid': [('crossed beam arch', 1466, 500000), ('walled crossed-beam arch', 1467, 1000000)]}

DECOR_ASSETS_DONE_ABOVE = {'ext_arch_01', 'ext_arch_01_wall', 'ext_arch_03', 'ext_arch_03_wall', 'int_wall_tapestry_01',
                           'int_wall_tapestry_01a', 'int_wall_tapestry_02a', 'int_wall_tapestry_02b'}
SKIP_ASSETS = {'alot', 'hlot', 'mlot', 'tool_leather'}  # lots are the lot marker, the leather tool is a crafting hookpoint
TROPHY_ASSETS = {'egg_dragon_a', 'egg_dragon_h', 'egg_dragon_m', 'int_mauler_unlock', 'roundtable'}

NAMES = {  # client names that do not read well
    'int_floor_chamber_pot': 'chamber pot', 'int_floor_fishtank': 'fish tank', 'int_floor_foot_stool': 'foot stool',
    'int_floor_table_frontier_albion': 'Albion frontier table', 'int_floor_table_frontier_hibernia': 'Hibernia frontier table',
    'int_floor_table_frontier_midgard': 'Midgard frontier table',
    'int_floor_table_frontier_ellan_vannin': 'Ellan Vannin frontier table',
    'ext_15year': '15 year statue', 'ext_market_barrow': 'market barrow', 'message_brd': 'message board',
    'int_wall_5yrshield': 'Camelot 5 year wall trophy', 'int_wall_10yrshield': 'Camelot 10 year wall trophy',
    'int_wall_vegasroundtable2006': 'Round Table 2006 wall plaque', 'int_wall_tapestry_04': 'anniversary wall tapestry',
    'int_wall_painting-anniversary-large': 'anniversary grandiose painting',
    'int_wall_painting-anniversary-large-02': 'second anniversary grandiose painting',
    'int_wall_7yrtrophyknot': 'seven year knot', 'roundtable': 'The Round Table',
    'egg_dragon_a': 'Albion dragon egg', 'egg_dragon_h': 'Hibernia dragon egg', 'egg_dragon_m': 'Midgard dragon egg',
    'clas_alb_horsetrophy_5years': 'Albion 5 year horse trophy', 'clas_hib_horsetrophy_5years': 'Hibernia 5 year horse trophy',
    'clas_mid_horsetrophy_5years': 'Midgard 5 year horse trophy', 'dracolich_trophy': 'Dracolich trophy',
    'gargoyle_trophy': 'Round Table gargoyle trophy', 'glimmer_knight_trophy': 'Glimmer Knight trophy',
    'guildbannertrophy_alb': 'Albion guild banner trophy', 'guildbannertrophy_hib': 'Hibernia guild banner trophy',
    'guildbannertrophy_mid': 'Midgard guild banner trophy',
    'int_floor_trophy_headhuntingdisplay-albion01': 'Albion headhunting display',
    'int_floor_trophy_headhuntingdisplay-hibernia01': 'Hibernia headhunting display',
    'int_floor_trophy_headhuntingdisplay-midgard01': 'Midgard headhunting display',
    'ext_trophy_garden_headhuntingdisplay-albion': 'Albion garden headhunting display',
    'ext_trophy_garden_headhuntingdisplay-hibernia': 'Hibernia garden headhunting display',
    'ext_trophy_garden_headhuntingdisplay-midgard': 'Midgard garden headhunting display',
    'int_wall_trophy_alb_dragon_02': 'Golestandt wall trophy', 'int_wall_trophy_hib_dragon_02': 'Cuuldurach wall trophy',
    'int_wall_trophy_mid_dragon_02': 'Gjalpinulva wall trophy', 'int_wall_trophy_rockguardian': 'Rock Guardian trophy',
    'int_wall_trophy_werewolf_revamp': 'werewolf trophy', 'int_floor_trophy_snow_cat_01': 'snow cat trophy',
    'int_floor_trophy_ant_01': 'giant ant trophy', 'int_floor_trophy_minoconstruct': 'minotaur construct trophy',
    'int_floor_trophy_goblinchief': 'goblin chief trophy', 'int_floor_trophy_doppleganger': 'doppelganger trophy',
    'int_floor_trophy_manticor': 'manticore trophy', 'int_floor_trophy_headhunting_trolll': 'troll headhunting trophy',
    'trophy_terracite_bose': 'terracite boss trophy', 'trophy_xaphan': 'Xaphan trophy',
    # seasonal
    'ext_springbench': 'spring garden bench', 'int_floor_springbench': 'spring bench',
    'ext_springflowerbox': 'spring flower box', 'ext_springfowers': 'spring flowers', 'ext_springnestegg': 'spring nest egg',
    'ext_springshrubwhite': 'white spring shrub', 'ext_springshrubyellow': 'yellow spring shrub',
    'ext_springtreepink': 'pink spring garden tree', 'ext_springtreewhite': 'white spring garden tree',
    'int_floor_springtree': 'spring tree', 'int_floor_springtree_pink': 'pink spring tree',
    'int_floor_springtree_tall': 'tall spring tree', 'int_wall_springgarland': 'spring garland',
    'int_floor_trophy_springbunny': 'spring bunny', 'int_floor_trophy_goldeneggs': 'golden eggs',
    'ext_statue_cupid': 'cupid garden statue', 'int_floor_trophy_cupid': 'cupid statue',
    'ext_fishbasket': 'garden fish basket', 'int_floor_trophy_fishbasket': 'fish basket',
    'int_floor_trophy_marketbasket': 'market basket', 'int_floor_trophy_pie': 'pie',
    'ext_winterbanner_alb': 'Albion winter banner', 'ext_winterbanner_hib': 'Hibernia winter banner',
    'ext_winterbanner_mid': 'Midgard winter banner', 'ext_wintercandlestand': 'winter garden candle stand',
    'int_floor_trophy_wintercandlestand': 'winter candle stand', 'int_floor_trophy_winterbrazier': 'winter brazier',
    'ext_winterpresents_lg': 'large garden winter presents', 'ext_winterpresents_sm': 'small garden winter presents',
    'int_floor_winterpresents_lg': 'large winter presents', 'int_floor_winterpresents_sm': 'small winter presents',
    'ext_wintersleigh': 'winter sleigh', 'ext_wintertree_alb': 'Albion garden winter tree',
    'ext_wintertree_hib': 'Hibernia garden winter tree', 'ext_wintertree_mid': 'Midgard garden winter tree',
    'int_floor_trophy_wintertreehib': 'Hibernia winter tree', 'int_floor_trophy_wintertreemid': 'Midgard winter tree',
    'int_floor_trophy_wintertreetall_a': 'tall Albion winter tree', 'int_floor_trophy_wintertreetall_h': 'tall Hibernia winter tree',
    'int_floor_trophy_wintertreetall_m': 'tall Midgard winter tree', 'int_wall_trophy_knotcandle': 'knotwork candle',
    'int_wall_trophy_wreathgold': 'gold wreath', 'int_wall_trophy_wreathknot': 'knotwork wreath',
    'int_wall_trophy_wreathred': 'red wreath', 'int_wall_trophy_wreathsnow': 'snowy wreath',
    'int_wall_trophy_wreathspray': 'wreath spray', 'int_wall_trophy_wreathswag': 'wreath swag',
    'int_wall_trophy_mega_01': 'megalodon trophy', 'int_wall_trophy_fishing': 'fishing trophy',
}
PIXIE_COLOURS = {'b': 'blue', 'g': 'green', 'r': 'red', 'w': 'white'}

PRICE = {'Wall decoration': 500000, 'Floor decoration': 300000, 'Garden / exterior': 500000,
         'Seasonal or event candidate': 150000, 'Other housing asset': 250000, 'Trophy': 500000}
PRICE_BY_ASSET = {'int_floor_chamber_pot': 20000, 'int_floor_foot_stool': 50000, 'int_floor_fishtank': 500000,
                  'ext_15year': 1000000, 'ext_market_barrow': 250000, 'message_brd': 100000, 'roundtable': 2000000}


def nice(asset, client_name):
    if asset in NAMES:
        return NAMES[asset]
    s = re.sub(r'^(ext_trophy_garden_|ext_|int_floor_trophy_|int_wall_trophy_|int_wall_singletrophy_|int_floor_|int_wall_|trophy_)',
               '', asset)
    s = re.sub(r'_0\d$|01$', '', s).replace('-', ' ').replace('_', ' ')
    s = re.sub(r'([a-z])([A-Z])', r'\1 \2', s).strip()
    s = re.sub(r'\blt\b', 'lieutenant', s)
    s = re.sub(r'\bheadhunting (\w+)', r'\1 headhunting', s)
    if asset.startswith(('int_wall_trophy_', 'int_floor_trophy_', 'trophy_', 'int_wall_singletrophy_')) and 'trophy' not in s:
        s += ' trophy'
    if asset.startswith('int_wall_trophy_pixie_'):
        cols = []
        for ch in asset.split('pixie_')[1].split('_'):
            if PIXIE_COLOURS[ch] not in cols:
                cols.append(PIXIE_COLOURS[ch])
        s = 'four pixie trophy (' + ', '.join(cols) + ')'
    if asset.startswith('int_wall_singletrophy_pixie_'):
        s = asset.rsplit('_', 1)[1] + ' pixie trophy'
    if asset.startswith(('int_wall_trophy_', 'int_floor_trophy_', 'trophy_')) and 'headhunting' not in s and 'pixie' not in s:
        s = s[0].upper() + s[1:]  # named monsters
    return s


def object_type(asset, category):
    if asset.startswith('ext_') or category == 'Garden / exterior' or asset == 'message_brd':
        return 49
    if asset.startswith('int_wall') or category == 'Wall decoration' or asset.startswith('guildbannertrophy'):
        return 50
    return 51


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--db', required=True)
    ap.add_argument('--catalog', required=True)
    a = ap.parse_args()
    con = sqlite3.connect(f'file:{a.db}?mode=ro', uri=True)
    have = {r[0] for r in con.execute('SELECT Id_nb FROM ItemTemplate')}
    model_used = {r[0] for r in con.execute('SELECT DISTINCT Model FROM ItemTemplate WHERE Object_Type IN (49,50,51)')}
    edits, new_ids = [], set()
    used_slots = {}

    def slots(lst):
        if lst not in used_slots:
            used_slots[lst] = {(p, s) for p, s in con.execute(
                'SELECT PageNumber, SlotPosition FROM MerchantItem WHERE ItemListID=?', (lst,))}
        taken = used_slots[lst]
        for p in range(5):
            for s in range(30):
                if (p, s) not in taken:
                    taken.add((p, s))
                    return p, s
        raise SystemExit(f'{lst} is full (150 items)')

    def sell(lst, item_id):
        p, s = slots(lst)
        edits.append({'table': 'MerchantItem', 'insert': {'ItemListID': lst, 'ItemTemplateID': item_id,
                                                          'PageNumber': p, 'SlotPosition': s}})

    def make(item_id, source, name, model, price, otype=None):
        assert item_id not in have and item_id not in new_ids, item_id
        new_ids.add(item_id)
        st = {'Name': name, 'Model': model, 'Price': price}
        if otype is not None:
            st['Object_Type'] = otype
        edits.append({'table': 'ItemTemplate', 'copy': source, 'key': item_id, 'set': st})
        return item_id

    WALL, FLOOR, GARDEN = 'housing_indoor_shop66', 'housing_indoor_shop10', 'housing_outdoor_shop98'

    # ---- Decorators
    def slug(n):
        return re.sub(r'[^a-z0-9]+', '_', n.lower()).strip('_')

    for realm, names, base in (('alb', ALB_PAINTINGS, 1382), ('mid', MID_PAINTINGS, 1402)):
        plain = [make(f'housing_decor_{slug(n)}', WALL, n, base + i, 250000) for i, n in enumerate(names)]
        grand = [make(f'housing_decor_{slug(n)}_grandiose', WALL, f'{n} grandiose painting', base + i, 400000)
                 for i, n in enumerate(names)]
        taps = [make(f'housing_decor_{slug(n)}', WALL, n, m, 750000) for n, m in TAPESTRIES[realm]]
        arch = [make(f'housing_decor_{slug(n)}', GARDEN, n, m, p) for n, m, p in ARCHES[realm]]
        if realm == 'alb':
            maps = ['housing_indoor_albmap', 'housing_indoor_large_alb_map']
            out = 'housing_outdoor_albouthouse'
        else:
            maps = [make('housing_decor_midgard_wall_map', 'housing_indoor_albmap', 'Midgard wall map', 1381, 50000),
                    'housing_indoor_large_mid_map']
            out = 'housing_outdoor_midouthouse'
        for item in [maps[0]] + plain + [maps[1]] + grand + taps + arch + [out]:
            sell(DECORATORS[realm], item)
    for item in (['housing_indoor_shop54'] + HIB_PAINTING_IDS + ['housing_indoor_shop65'] + HIB_GRANDIOSE_IDS +
                 ['housing_indoor_shop86', 'housing_indoor_shop85', 'housing_outdoor_shop98', 'housing_outdoor_shop99',
                  'housing_outdoor_shop100']):
        assert item in have, item
        sell(DECORATORS['hib'], item)
    # The new arches also go in the garden menu, like the stag archways.
    for n, _, _ in ARCHES['alb'] + ARCHES['mid']:
        sell('housing_outdoor_shop', f'housing_decor_{slug(n)}')

    # ---- every other client housing model with no server item
    rows = list(csv.DictReader(open(a.catalog, encoding='utf-8-sig')))
    trophies, added = [], {'indoor': 0, 'garden': 0, 'trophy': 0}
    for r in rows:
        asset, cat = r['Asset'], r['Category']
        if not r['ModelIDs'] or r['ServerItems'] or asset in DECOR_ASSETS_DONE_ABOVE or asset in SKIP_ASSETS:
            continue
        if asset.startswith(('int_wall_paint_alb', 'int_wall_paint_mid')):
            continue  # the Decorator paintings above
        models = [int(m) for m in r['ModelIDs'].split(';')]
        names = [n.strip() for n in r['ClientNames'].split(';')]
        if asset == 'int_mauler_unlock':
            parts = [('housing_trophy_mauler_unlock_inside', 'Mauler unlock trophy', models[0], 51),
                     ('housing_trophy_mauler_unlock_outside', 'Mauler unlock garden trophy', models[1], 49)]
        elif asset == 'int_floor_trophy_wintertreealb':
            parts = [('housing_decor_winter_light', 'winter light', models[0], 51),
                     ('housing_decor_winter_tree_alb', 'Albion winter tree', models[1], 51)]
        else:
            ot = object_type(asset, cat)
            is_trophy = cat == 'Trophy' or asset in TROPHY_ASSETS
            parts = [((('housing_trophy_' if is_trophy else 'housing_decor_') + slug(asset)), nice(asset, names[0]), models[0], ot)]
        for item_id, name, model, ot in parts:
            if model in model_used:
                continue
            is_trophy = item_id.startswith('housing_trophy_')
            price = PRICE_BY_ASSET.get(asset, PRICE[cat])
            if is_trophy and ('dragon' in asset or 'dracolich' in asset):
                price = 1000000
            src = {49: GARDEN, 50: WALL, 51: FLOOR}[ot]
            make(item_id, src, name, model, price, ot)
            if is_trophy:
                trophies.append(item_id)
                added['trophy'] += 1
            elif ot == 49:
                sell('housing_outdoor_shop', item_id)
                added['garden'] += 1
            else:
                sell('housing_indoor_shop', item_id)
                added['indoor'] += 1
    for item in sorted(trophies, key=lambda i: next(e['set']['Name'].lower() for e in edits
                                                    if e.get('key') == i and 'copy' in e)):
        sell('housing_taxidermy', item)

    # ---- Taxidermy Supplies sells the trophies; Siegecraft Supplies gets a realm version per realm
    edits.append({'table': 'NpcTemplate', 'key': '545492cf-fbcc-47ca-99b6-34542ec9f903',
                  'set': {'ItemsListTemplateID': 'housing_taxidermy'}})
    free = [t for t in range(842, 900) if not con.execute('SELECT 1 FROM NpcTemplate WHERE TemplateId=?', (t,)).fetchone()]
    for (realm, rname, lst), tid in zip((('alb', 'Albion', 'SiegeMerchantAlb'), ('mid', 'Midgard', 'SiegeMerchantMid'),
                                         ('hib', 'Hibernia', 'SiegeMerchantHib')), free):
        edits.append({'table': 'NpcTemplate', 'copy': 'f3fc9335-2a1b-4c65-a539-459dc6723165',
                      'key': f'housing_siegecraft_{realm}', 'set': {'TemplateId': tid, 'ItemsListTemplateID': lst}})
        item = make(f'housing_{realm}_siegecraft_supplies', 'housing_siegecraft_supplies', 'Siegecraft Supplies', None, None)
        edits[-1]['set'] = {'Bonus': tid}
        for (mid,) in con.execute("SELECT MerchantItem_ID FROM MerchantItem WHERE ItemListID=? AND "
                                  "ItemTemplateID='housing_siegecraft_supplies'", (f'housing_indoor_{realm}_npc',)):
            edits.append({'table': 'MerchantItem', 'key': mid, 'set': {'ItemTemplateID': item}})

    OUT.write_text(json.dumps(edits, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')
    counts = {}
    for e in edits:
        k = (e['table'], 'insert' if 'insert' in e or 'copy' in e else 'update')
        counts[k] = counts.get(k, 0) + 1
    print(OUT.name, len(edits), 'edits', counts, added)
    for lst, taken in used_slots.items():
        print(f'  {lst}: {len(taken)} of 150 slots')


if __name__ == '__main__':
    main()
