"""ToA named drops: tables, official stats, missing items and item spells, as World Workshop edits.

Reads the world, the official Herald item export and Eve of Darkness db-public read-only, and writes
toa-named-drops-<date>.edits.json plus a summary and a per-item CSV beside this script. Applies nothing.

    python -I make_toa_named_drops_edits.py <world.db> <daoc_item_database_combined.zip> <eod-db-public/data> <ArtifactEncounters.Data.cs>

What it does (found 2026-11-08):
1. The world has MobXLootTemplate rows for 359 ToA monsters (from EoD) that name LootTemplate tables which were
   never imported, so those monsters drop only the generic family junk. The tables are added from EoD.
2. Every official 1.126 item the Herald export lists as dropped by a ToA monster is added to that monster's table.
3. Items in those tables that have an official record get the official bonus lines, procs and charges. The old
   rows came from an import that stored ToA bonuses on the wrong properties (e.g. Con cap as Power pool, cast speed
   as buff effectiveness, Hits cap as Hits) and had almost no procs or charges.
4. Official drops the world never had are made by copying the closest world item of the same type and realm
   (for the model) and giving it the official name, bonuses and abilities.
5. Item spells use the existing convention: official spell X is our spell 800000 + X in the Item Effects line.
   Missing ones are built from the official delve text, copying a spell row of the same type for visuals/messages.
"""
import collections
import csv
import json
import os
import re
import sqlite3
import sys
import uuid
import zipfile

DATE = os.environ.get('DR_PATCH_DATE', '2026-11-08')   # a later run (e.g. after new zones) writes its own dated folder
NS = uuid.UUID('3b2b9a3e-6a55-4d1e-9a52-6f0d5d7e0b11')
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, f'toa-named-drops-{DATE}')
PKG = 'OfflineToANamedDrops'
TOA = set([30, 73, 130, 70, 71, 72] + list(range(35, 48)) + list(range(78, 91)) + list(range(135, 148)))
# Names in the official ToA set that belong to other content (classic, SI, Camelot); not touched here.
NOT_TOA = {'knight', 'mordred', 'muryan', 'cailean', 'cailleach uragaig', 'morgana', 'sister blythe'}

db_path, zip_path, eod_dir, enc_path = sys.argv[1:5]
con = sqlite3.connect('file:' + db_path.replace('\\', '/') + '?mode=ro', uri=True)
con.row_factory = sqlite3.Row
z = zipfile.ZipFile(zip_path)
OFF = [i for i in json.loads(z.read('static_objects.json').decode('utf8', 'replace'))['items'] if not i.get('artifact')]
META = json.loads(z.read('daoc_item_metadata.json').decode('utf8', 'replace'))


def eod(name):
    return json.load(open(os.path.join(eod_dir, name), encoding='utf-8'))


def uid(*parts):
    return str(uuid.uuid5(NS, '|'.join(str(p) for p in parts)))


# ---------------- official bonus codes -> eProperty ----------------
STAT = {0: 1, 1: 2, 2: 3, 3: 4, 4: 5, 5: 6, 6: 7, 7: 8, 10: 156}
CAP = {0: 201, 1: 202, 2: 203, 3: 204, 4: 205, 5: 206, 6: 207, 7: 208, 10: 209}
RESN = {'Body': 11, 'Cold': 12, 'Crush': 13, 'Energy': 14, 'Heat': 15, 'Matter': 16, 'Slash': 17, 'Spirit': 18, 'Thrust': 19}
SKILL = {'Slash': 44, 'Thrust': 50, 'Bow': 93, 'Parry': 40, 'Sword': 52, 'Hammer': 53, 'Axe': 54, 'LeftAxe': 55, 'Stealth': 49,
         'Spear': 56, 'Mending': 57, 'Augmentation': 58, 'Crush': 25, 'Pacification': 94, 'Cave Magic': 85, 'Darkness': 60,
         'Suppression': 61, 'Runecarving': 62, 'Shield': 43, 'Thrown': 91, 'Flexible': 33, 'Staff': 48, 'Summoning': 98,
         'Stormcalling': 63, 'Beastcraft': 64, 'Polearm': 41, 'Two Handed': 20, 'Fire Magic': 32, 'Wind Magic': 51,
         'Cold Magic': 34, 'Earth Magic': 29, 'Light Magic': 65, 'Matter Magic': 37, 'Body Magic': 21, 'Spirit Magic': 47,
         'Mind Magic': 38, 'Void Magic': 66, 'Mana Magic': 67, 'Dual Wield': 28, 'Composite Bow': 68, 'Envenom': 31,
         'Battlesongs': 69, 'Enhancements': 30, 'Enchantments': 70, 'Rejuvenation': 42, 'Smiting': 45, 'Longbow': 36,
         'Crossbow': 24, 'Chants': 22, 'Instruments': 35, 'Blades': 72, 'Blunt': 73, 'Piercing': 74, 'Large Weaponry': 75,
         'Mentalism': 76, 'Regrowth': 77, 'Nurture': 78, 'Nature Affinity': 79, 'Music': 80, 'Celtic Dual': 81,
         'Celtic Spear': 82, 'Recurve Bow': 83, 'Valor': 84, 'Critical Strike': 23, 'Nightshade': 96, 'Deathsight': 27,
         'Painworking': 39, 'Death Servant': 26, 'Soulrending': 46, 'Hand to Hand': 92, 'Scythe': 90, 'Bone Army': 86,
         'Arboreal Path': 89, 'Savagery': 95, 'Creeping Path': 88, 'Verdant Path': 87, "Odin's Will": 105,
         'Spectral Guard': 114, 'Phantasmal Wail': 103, 'Ethereal Shriek': 102, 'Shadow Mastery': 100,
         'Vampiiric Embrace': 101, 'Dementia': 99, 'Witchcraft': 108, 'Cursing': 106, 'Hexing': 107, 'Power Strikes': 111,
         'Magnetism': 112, 'Aura Manipulation': 113, 'Fist Wraps': 110, 'Mauler Staff': 109,
         'All Melee': 164, 'All Duel': 167, 'All Archery': 168, 'All Casting': 163, 'All Focus': 165}
FOCUS = {'Darkness': 120, 'Suppression': 121, 'Runecarving': 122, 'Spirit Magic': 123, 'Fire Magic': 124, 'Wind Magic': 125,
         'Cold Magic': 126, 'Earth Magic': 127, 'Light Magic': 128, 'Body Magic': 129, 'Matter Magic': 130, 'Mind Magic': 132,
         'Void Magic': 133, 'Mana Magic': 134, 'Enchantments': 135, 'Mentalism': 136, 'Summoning': 137, 'Bone Army': 138,
         'Painworking': 139, 'Deathsight': 140, 'Death Servant': 141, 'Verdant Path': 142, 'Creeping Path': 143,
         'Arboreal Path': 144, 'Ethereal Shriek': 157, 'Phantasmal Wail': 158, 'Spectral Guard': 159, 'Cursing': 160,
         'Hexing': 161, 'Witchcraft': 162, 'All Focus': 165}
FLAT = {4: 10, 8: 173, 9: 198, 10: 200, 11: 154, 12: 153, 13: 199, 14: 190, 15: 193, 16: 195, 17: 194, 19: 155, 20: 188,
        21: 191, 22: 148, 27: 174, 29: 210, 30: 211, 32: 197, 34: 196, 36: 189, 37: 185, 38: 147, 40: 150, 41: 151, 42: 183,
        46: 182, 47: 186, 48: 187, 49: 180, 50: 179, 51: 171, 52: 170, 53: 169, 54: 184}
ART = {1: 254, 2: 251, 4: 248, 5: 235, 6: 253, 7: 247}


def bonus_lines(item):
    """Official bonuses -> ordered [(eProperty, value)], merging repeats; unknown codes reported."""
    out, unknown = collections.OrderedDict(), []
    for b in item.get('bonuses') or []:
        t, v, sid = b['type'], b['value'], b.get('id')
        sub = META['bonus_types'].get(str(t), {}).get('sub_types', {}).get(str(sid))
        p = (STAT.get(sid) if t == 1 else CAP.get(sid) if t == 28 else SKILL.get(sub) if t == 2 else
             RESN.get(sub) if t == 5 else FOCUS.get(sub) if t == 6 else ART.get(sid) if t == 35 else FLAT.get(t))
        if p:
            out[p] = out.get(p, 0) + v
        else:
            unknown.append(b)
    return list(out.items()), unknown


# ---------------- type rules learned from matched pairs (see summary) ----------------
SKILL_OT = {(1, 1): 3, (2, 1): 4, (33, 1): 2, (46, 1): 24, (47, 1): 8, (47, 2): 8, (47, 3): 8, (64, 1): 7, (65, 1): 6,
            (90, 1): 9, (91, 1): 10, (4, 1): 5, (147, 1): 27, (148, 1): 28, (14, 2): 11, (16, 2): 12, (17, 2): 13,
            (18, 2): 17, (26, 2): 14, (78, 2): 15, (124, 2): 25, (124, 0): 25, (101, 3): 19, (102, 3): 20, (103, 3): 21,
            (104, 3): 22, (112, 3): 23, (113, 3): 18, (125, 3): 26, (44, 1): 16, (44, 2): 16, (44, 3): 16}
ARMOR_OT = {1: {0: 32, 10: 33, 19: 34, 27: 35, 34: 36}, 2: {0: 32, 10: 33, 19: 34, 27: 35},
            3: {0: 32, 10: 33, 19: 37, 27: 38}}
SLOT_IT = {1: 21, 2: 22, 3: 23, 4: 24, 5: 25, 6: 26, 7: 27, 8: 28, 9: 29, 12: 32, 13: 33, 15: 35, 16: 35}


def official_type(i):
    """(Object_Type, Item_Type, Hand) the world would use, None where it cannot be told."""
    td, cat, realm = i.get('type_data') or {}, i['category'], i['realm']
    if cat == 1:
        ot = SKILL_OT.get((td.get('skill_used'), realm)) or next((v for (s, _), v in SKILL_OT.items() if s == td.get('skill_used')), None)
        if td.get('range') is not None or ot in (5, 9, 10, 15, 18):
            return ot, 13, 1
        if td.get('two_handed'):
            return ot, 12, 1
        if td.get('left_handed'):
            return ot, 11, 2
        return ot, 10, 0
    if cat == 3:
        return 42, 11, 2
    if cat == 4:
        return 45, None, None
    if cat == 2:
        return ARMOR_OT.get(realm, {}).get(td.get('absorption')), SLOT_IT.get(i.get('slot')), 0
    if cat == 5:
        return 41, SLOT_IT.get(i.get('slot')), 0
    return None, None, None


MAT = r'^(arcanium|arcanite|duskwood|silksteel|tempered|\d_)\s*'


def norm(n):
    n = (n or '').lower().replace('’', "'")
    n = re.sub(MAT, '', n)
    n = re.sub(MAT, '', n)
    return re.sub(r'\s+', ' ', re.sub(r"[^a-z' -]", '', n)).strip()


# A few world names are misspelt copies of the official one.
SPELLING = {"archaic assassin vest": "archaic assassin's vest", "great sword of the detonation": "great sword of detonation",
            "legs of the ascendant": "legs of the ascendent", "leviathin tooth necklace": "leviathan tooth necklace",
            "mangus staff of the blaze": "magus staff of the blaze", "perplexitas gantlet": "perplexitas gauntelet"}

# ---------------- world ----------------
W = {r['Id_nb'].lower(): dict(r) for r in con.execute('SELECT * FROM ItemTemplate')}
w_by_name = collections.defaultdict(list)
for w in W.values():
    n = norm(w['Name'])
    w_by_name[SPELLING.get(n, n)].append(w)
toa_names, other_names = collections.Counter(), collections.Counter()
for n, r in con.execute('SELECT Name, Region FROM Mob WHERE Realm = 0'):
    (toa_names if r in TOA else other_names)[n.lower()] += 1
enc_names = set(x.lower() for x in re.findall(r'new\("([^"]+)", \d+, \d+, \d+, \d+, (?:true|false)\)', open(enc_path, encoding='utf8').read()))
w_lt = set(r[0].lower() for r in con.execute('SELECT DISTINCT TemplateName FROM LootTemplate'))
w_mx = collections.defaultdict(list)
for r in con.execute('SELECT * FROM MobXLootTemplate'):
    w_mx[r['MobName'].lower()].append(dict(r))
w_otd = set((r[0].lower(), r[1].lower()) for r in con.execute('SELECT MobName, ItemTemplateID FROM LootOTD'))
spells = {r['SpellID']: dict(r) for r in con.execute('SELECT * FROM Spell')}
line_x = set(r[0] for r in con.execute("SELECT SpellID FROM LineXSpell WHERE LineName = 'Item Effects'"))
# Artifacts have their own pass (Class-Research artifact-pass); never touched here.
ARTIFACT_ITEMS = set(r[0].lower() for r in con.execute('SELECT ItemID FROM ArtifactXItem'))

# ---------------- EoD ToA tables ----------------
eod_toa = set()
for f in sorted(os.listdir(eod_dir)):
    if f.startswith('Mob.') and f.endswith('.json'):
        for m in eod(f):
            if int(m['Region']) in TOA and int(m.get('Realm', 0)) == 0:
                eod_toa.add(m['Name'].lower())
e_lt = collections.defaultdict(list)
for r in eod('LootTemplate.json'):
    e_lt[r['TemplateName'].lower()].append(r)
e_mx = collections.defaultdict(list)
for r in eod('MobXLootTemplate.json'):
    e_mx[r['MobName'].lower()].append(r)

all_toa = (set(toa_names) | eod_toa | enc_names) - NOT_TOA

# ---------------- official ToA drops ----------------
off_drops = collections.defaultdict(list)   # mob -> [(kind, item)]
for i in OFF:
    for kind, lst in ((i.get('sources') or {}).get('monsters') or {}).items():
        for m in lst:
            if m.lower() in all_toa:
                off_drops[m.lower()].append((kind, i))
off_by_name = collections.defaultdict(list)
for i in OFF:
    off_by_name[norm(i['name'])].append(i)


def realm_ok(o_realm, w_realm):
    return o_realm == 0 or w_realm == 0 or o_realm == w_realm


def wlines(w):
    r = collections.Counter()
    for k in [str(n) for n in range(1, 11)]:
        if w[f'Bonus{k}Type'] and w[f'Bonus{k}']:
            r[w[f'Bonus{k}Type']] += w[f'Bonus{k}']
    if w['ExtraBonusType'] and w['ExtraBonus']:
        r[w['ExtraBonusType']] += w['ExtraBonus']
    return r


def type_ok(i, w):
    ot, it, _ = official_type(i)
    if ot is not None and w['Object_Type'] != ot:
        return False
    if it is not None and i['category'] in (1, 2) and w['Item_Type'] != it:
        return False
    return True


def distance(i, w):
    o = collections.Counter(dict(bonus_lines(i)[0]))
    x = wlines(w)
    return sum(abs(o[k] - x[k]) for k in set(o) | set(x))


def best_official_for(w):
    cands = [i for i in off_by_name.get(SPELLING.get(norm(w['Name']), norm(w['Name'])), []) if realm_ok(i['realm'], w['Realm']) and type_ok(i, w)]
    if not cands:
        return None
    return min(cands, key=lambda i: (i['realm'] != w['Realm'], distance(i, w)))


def best_world_for(i):
    cands = [w for w in w_by_name.get(norm(i['name']), []) if realm_ok(i['realm'], w['Realm']) and type_ok(i, w)
             and w['Id_nb'].lower() not in ARTIFACT_ITEMS]
    if not cands:
        return None
    return min(cands, key=lambda w: (w['Realm'] != i['realm'], not w['Id_nb'].lower() in eod_items, distance(i, w)))


# ---------------- tables to build ----------------
tables = collections.OrderedDict()   # template name -> {item id: (chance, count, source)}
mob_table = {}                       # mob -> template name
eod_items = set()
missing_eod_items = []
for mob in sorted(all_toa):
    names = [r['LootTemplateName'] for r in w_mx.get(mob, []) if r['LootTemplateName'].lower() not in w_lt]
    names += [r['LootTemplateName'] for r in e_mx.get(mob, []) if r['LootTemplateName'].lower() not in w_lt and r['LootTemplateName'] not in names]
    if not names and mob in e_lt and mob not in w_lt:
        names = [mob]
    for t in names:
        rows = e_lt.get(t.lower(), [])
        if not rows:
            continue
        tab = tables.setdefault(t.lower(), {})
        for r in rows:
            iid = r['ItemTemplateID'].lower()
            if iid not in W:
                missing_eod_items.append((t, r['ItemTemplateID']))
                continue
            eod_items.add(iid)
            tab.setdefault(iid, (int(r['Chance']), int(r.get('Count') or 1), 'eod'))
        mob_table.setdefault(mob, t.lower())

# ---------------- items: match official records, plan new ones ----------------
updates = {}        # world id -> official item
created = {}        # new id -> (official item, donor id)
off_world = {}      # official id -> world id
for mob, lst in off_drops.items():
    for kind, i in lst:
        w = best_world_for(i)
        if w:
            off_world[i['id']] = w['Id_nb'].lower()
for iid in sorted(eod_items):
    if iid in ARTIFACT_ITEMS:
        continue
    o = best_official_for(W[iid])
    if o:
        updates[iid] = o
off_index = {i['id']: i for i in OFF}
for oid, wid in off_world.items():
    updates.setdefault(wid, off_index[oid])

REALM_TAG = {0: 'all', 1: 'alb', 2: 'mid', 3: 'hib'}


def slug(s):
    return re.sub(r'[^a-z0-9]+', '_', s.lower()).strip('_')


def donor_for(i):
    ot, it, _ = official_type(i)
    words = set(w for w in norm(i['name']).split() if w not in ('of', 'the'))
    head = norm(i['name']).split(' of ')[0]
    best, score = None, None
    for w in W.values():
        if ot is not None and w['Object_Type'] != ot:
            continue
        if it is not None and w['Item_Type'] != it:
            continue
        if not realm_ok(i['realm'], w['Realm']) or not w['Model']:
            continue
        if w['Level'] < 40:
            continue
        n = norm(w['Name'])
        s = (3 if n.split(' of ')[0] == head else 0) + len(words & set(n.split())) + (1 if w['Id_nb'].lower() in eod_items else 0) + (1 if w['Realm'] == i['realm'] else 0)
        if score is None or s > score or (s == score and w['Id_nb'] < best['Id_nb']):
            best, score = w, s
    return best


unmade = []
seen_new = {}
for mob, lst in sorted(off_drops.items()):
    for kind, i in lst:
        if i['id'] in off_world:
            continue
        key = (norm(i['name']), i['realm'], official_type(i)[:2])
        if key in seen_new:
            off_world[i['id']] = seen_new[key]
            continue
        d = donor_for(i)
        if not d:
            unmade.append((mob, i['name'], i['realm']))
            continue
        nid = f"toa_{slug(i['name'])}_{REALM_TAG[i['realm']]}"
        n, k = nid, 2
        while n.lower() in W or n.lower() in created:
            n, k = f'{nid}{k}', k + 1
        created[n.lower()] = (i, d['Id_nb'])
        seen_new[key] = n.lower()
        off_world[i['id']] = n.lower()

# ---------------- spells ----------------
DT = {'Crush': 1, 'Slash': 2, 'Thrust': 3, 'Body': 10, 'Cold': 11, 'Energy': 12, 'Heat': 13, 'Matter': 14, 'Spirit': 15}
RESIST_DEBUFF = {'Slash': 'SlashResistDebuff', 'Thrust': 'ThrustResistDebuff', 'Crush': 'CrushResistDebuff'}


HELPFUL = {'EnduranceHeal', 'PowerHeal', 'Heal', 'AblativeArmor', 'CombatSpeedBuff', 'AcuityBuff', 'StrengthConstitutionBuff',
           'DexterityQuicknessBuff', 'HealthRegenBuff', 'DamageAdd', 'BaseArmorFactorBuff', 'ToHitBuff', 'FlexibleSkillBuff',
           'StealthSkillBuff', 'HealAllOverTime'}


def ability_block(item, spell):
    """The delve block of this ability: blocks follow the order of the abilities list.

    KNOWN FAULT (found 2026-11-08): "Secondary Charged Magic Item:" is not split as a header, so a second ability can
    read the first one's text. herald_items.Herald.ability_block is the fixed version; the ToA spells built wrong
    (801592, 803403, 812051) are corrected by item-spells-2026-11-08. Use herald_items before rerunning this script."""
    t = item['delve_text'].replace('\r', '')
    parts = re.split(r'\n(?=[A-Za-z ]*Ability:)', t)[1:]
    order = [a['spell'] for a in item.get('abilities') or []]
    if spell in order and order.index(spell) < len(parts):
        return parts[order.index(spell)]
    return parts[0] if parts else ''


def seconds(s):
    m = re.match(r'\s*(\d+):(\d+) min', s)
    if m:
        return int(m.group(1)) * 60 + int(m.group(2))
    m = re.match(r'\s*([\d.]+) sec', s)
    return int(float(m.group(1))) if m else 0


def parse_spell(sid):
    holders = [i for i in OFF for a in (i.get('abilities') or []) if a['spell'] == sid]
    holders.sort(key=lambda i: len(i.get('abilities') or []))
    blk = ability_block(holders[0], sid) if holders else ''
    f = {}
    for line in blk.split('\n'):
        m = re.match(r'^([A-Za-z/ -]+):\s*(.+)$', line.strip())
        if m and m.group(1) not in f:
            f[m.group(1).strip()] = m.group(2).strip()
    return f, META['abilities']['spell'].get(str(sid), '')


def build_spell(sid):
    """A Spell row for official spell sid as 800000+sid, or (None, reason)."""
    f, desc = parse_spell(sid)
    fn = f.get('Function', '').lower() or ('disorient' if f.get('Effect') == 'Disoriented' else '')
    num = lambda k: float(re.match(r'[\d.]+', f[k]).group(0)) if k in f and re.match(r'[\d.]+', f[k]) else 0
    target = 'Self' if f.get('Target', 'Self') == 'Self' else 'Enemy'
    row = {'Target': target, 'Range': int(num('Range')) if target == 'Enemy' else 0, 'Duration': seconds(f.get('Duration', '')),
           'CastTime': float(re.match(r'\s*([\d.]+)', f['Casting time']).group(1)) if re.match(r'\s*([\d.]+)', f.get('Casting time', '')) else 0.0,
           'DamageType': DT.get(f.get('Damage', '').split()[0] if f.get('Damage', '') and not f['Damage'][0].isdigit() else '', 0)}
    dmg_type_word = re.search(r'(Body|Cold|Energy|Heat|Matter|Spirit|Crush|Slash|Thrust) damage', desc)
    if dmg_type_word:
        row['DamageType'] = DT[dmg_type_word.group(1)]
    if fn == 'endurance heal':
        typ, row['Value'] = 'EnduranceHeal', num('Value')
    elif fn == 'lifedrain':
        typ = 'Lifedrain'
        row['Damage'] = num('Damage')
        pct = int(num('Health returned'))
        row['LifeDrainReturn'], row['Value'] = pct, -pct
    elif fn == 'replenish power':
        typ = 'PowerHeal'
        row['Value'] = -num('Amount') if '%' in f.get('Amount', '') or '%' in desc else num('Amount')
    elif 'health buffer' in desc:
        typ = 'AblativeArmor'
        row['Damage'], row['Value'] = num('Absorption'), num('Value')
    elif fn == 'heal':
        typ = 'Heal'
        row['Value'] = -num('Value') if '%' in desc else (num('Value') or float(re.search(r'regains (\d+)', desc).group(1)))
    elif fn == 'dmg-over-time':
        typ, row['Damage'], row['Frequency'] = 'DamageOverTime', num('Damage per tick'), int(num('Frequency') * 10)
    elif fn == 'haste':
        typ, row['Value'] = 'CombatSpeedBuff', num('Value')
    elif fn == 'direct damage':
        typ, row['Damage'] = 'DirectDamage', num('Damage')
    elif fn == 'improved stat decrease':
        typ = 'StrengthConstitutionDebuff' if 'Str/Con decrease' in f else 'DexterityQuicknessDebuff'
        row['Value'] = num('Str/Con decrease') or num('Dex/Qui decrease')
    elif fn == 'improved stat enhancement':
        typ = 'AcuityBuff' if 'Acuity bonus' in f else 'StrengthConstitutionBuff' if 'Str/Con bonus' in f else 'DexterityQuicknessBuff'
        row['Value'] = num('Acuity bonus') or num('Str/Con bonus') or num('Dex/Qui bonus')
    elif fn == 'health regen':
        typ, row['Value'] = 'HealthRegenBuff', num('Value')
    elif fn == 'resistance decrease':
        typ = RESIST_DEBUFF.get(f.get('Resist decreased', '').strip())
        row['Value'] = num('Base value of spell')
        row['DamageType'] = DT.get(f.get('Resist decreased', '').strip(), 0)
    elif fn == 'disorient':
        typ, row['Value'] = 'ToHitDebuff', num('Melee to-hit penalty')
    elif fn == 'damage add':
        typ, row['Damage'] = 'DamageAdd', num('Damage')
        row['Duration'] = row['Duration'] or 30   # some official damage add procs print no duration
    elif fn == 'shield':
        typ, row['Value'] = 'BaseArmorFactorBuff', num('Value')
    elif fn == 'accuracy boost':
        typ, row['Value'] = 'ToHitBuff', num('Value')
    elif fn == 'skill buff':
        # "Skill raised: Flexible, Bonus: 10%": the skill buff handlers add Value to the skill.
        typ = {'Flexible': 'FlexibleSkillBuff', 'Stealth': 'StealthSkillBuff'}.get(f.get('Skill raised', '').strip())
        row['Value'] = num('Bonus')
        row.update({'Message1': f"Your {f.get('Skill raised', 'skill').strip().lower()} skill improves!",
                    'Message2': '{0} looks more skilled.', 'Message3': 'Your skill returns to normal.',
                    'Message4': "{0}'s skill returns to normal."})
    elif fn == 'heal all over time':
        # Needs the HealAllOverTime handler (feature/toa-named-drops): percent of health, power and endurance a pulse.
        typ, row['Value'], row['Frequency'] = 'HealAllOverTime', num('Value'), int(num('Frequency') * 10)
        row['Message1'] = 'You feel your body, mind and stamina renew.'
    else:
        return None, f'no rule for "{fn or f}"'
    if not typ:
        return None, f'no spell type for {f.get("Resist decreased") or f.get("Skill raised")}'
    # Visuals and messages come from a row of the same type, or of a similar one for types no row uses yet.
    look = {'FlexibleSkillBuff': 'StrengthConstitutionBuff', 'StealthSkillBuff': 'StrengthConstitutionBuff',
            'HealAllOverTime': 'HealOverTime'}.get(typ, typ)
    donor = next((s for k, s in sorted(spells.items()) if 800000 <= k < 820000 and s['Type'] in (typ, look)), None) or \
        next((s for k, s in sorted(spells.items()) if s['Type'] == typ and s['Target'] == target), None)
    if not donor:
        return None, f'no spell row of type {typ} to copy'
    title = (f.get('Function') or f.get('Effect') or typ).strip().title()
    new = {k: donor[k] for k in donor if k not in ('LastTimeRowUpdated',)}
    for k in ('Damage', 'Value', 'Duration', 'Frequency', 'LifeDrainReturn', 'Radius', 'Pulse', 'Power', 'RecastDelay',
              'SubSpellID', 'SharedTimerGroup', 'ResurrectHealth', 'ResurrectMana', 'Concentration', 'SpellGroup', 'EffectGroup'):
        new[k] = 0 if not isinstance(donor[k], float) else 0.0
    if typ in HELPFUL and row['Target'] == 'Enemy':
        # "Target: Targeted" on a buff or heal means a friendly target, never the enemy.
        row['Target'] = 'Group' if f.get('Target', '').strip() == 'Group' else 'Realm'
        row['Range'] = row['Range'] or 1500
    new.update(row)
    new.update({'SpellID': 800000 + sid, 'Name': f'{title} ({sid})', 'Description': desc or title, 'Type': typ,
                'PackageID': PKG, 'Spell_ID': f'offline-toa-item-{800000 + sid}', 'TooltipId': 0,
                'LastTimeRowUpdated': f'{DATE} 00:00:00'})
    if typ == 'HealAllOverTime':   # the export's summary line says 5%; the item delve says 3% every 5 seconds
        new['Description'] = f"You regenerate {row['Value']:g}% health, endurance and power every {row['Frequency'] / 10:g} sec."
    return new, donor['SpellID']


# ---------------- edits ----------------
edits, report, spell_report = [], [], []
need_spells = {}
CHARGE_TYPES = (6,)


def ability_set(i):
    """ItemTemplate ability columns from the official abilities, and spells needed."""
    s = {'SpellID': 0, 'Charges': 0, 'MaxCharges': 0, 'SpellID1': 0, 'Charges1': 0, 'MaxCharges1': 0,
         'ProcSpellID': 0, 'ProcSpellID1': 0, 'CanUseEvery': 0}
    notes = []
    for a in i.get('abilities') or []:
        sid, pos, mt = a['spell'], a.get('position', 1), a.get('magic_type')
        need_spells.setdefault(sid, []).append(i['name'])
        ours = 800000 + sid
        if mt == 6:
            c = a.get('max_charges') or 0
            if not c:
                notes.append(f'use ability {sid} has no charges in the official record; given 30')
                c = 30
            if pos == 1:
                s.update({'SpellID': ours, 'Charges': c, 'MaxCharges': c})
            else:
                s.update({'SpellID1': ours, 'Charges1': c, 'MaxCharges1': c})
            s['CanUseEvery'] = int(i.get('use_duration') or 0)
        elif mt in (1, 2):
            s['ProcSpellID' if pos == 1 else 'ProcSpellID1'] = ours
        else:
            notes.append(f'ability {sid} magic type {mt} skipped')
    return s, notes


def item_set(i):
    lines, unknown = bonus_lines(i)
    s = {}
    for n in range(1, 11):
        p, v = lines[n - 1] if n <= len(lines) else (0, 0)
        s[f'Bonus{n}Type'], s[f'Bonus{n}'] = p, v
    s['ExtraBonusType'], s['ExtraBonus'] = 0, 0
    ab, notes = ability_set(i)
    s.update(ab)
    s['IsTradable'] = 0 if i['flags'].get('no_trade') else 1
    s['IsDropable'] = 0 if i['flags'].get('no_drop') else 1
    if i.get('bonus_level') is not None:
        s['BonusLevel'] = i['bonus_level']
    if unknown:
        notes.append(f'unknown bonus codes {unknown}')
    return s, notes


changed_items = 0
for wid, i in sorted(updates.items()):
    w = W[wid]
    s, notes = item_set(i)
    diff = {k: v for k, v in s.items() if w.get(k) != v}
    if diff:
        edits.append({'table': 'ItemTemplate', 'key': w['Id_nb'], 'set': diff})
        changed_items += 1
    report.append({'Id_nb': w['Id_nb'], 'Name': w['Name'], 'Realm': w['Realm'], 'Result': 'stats set to official' if diff else 'already official',
                   'Official id': i['id'], 'Official name': i['name'], 'Before': ' '.join(f'{k}:{v}' for k, v in sorted(wlines(w).items())),
                   'After': ' '.join(f'{k}:{v}' for k, v in bonus_lines(i)[0]), 'Notes': '; '.join(notes)})

for nid, (i, donor) in sorted(created.items()):
    d = W[donor.lower()]
    ot, it, hand = official_type(i)
    td = i.get('type_data') or {}
    s, notes = item_set(i)
    s.update({'Name': i['name'], 'Realm': i['realm'], 'PackageID': PKG, 'Description': f'ToA drop (official item {i["id"]})',
              'IsPickable': 1, 'CanDropAsLoot': 1, 'Quality': 100,
              'LevelRequirement': (i.get('requirements') or {}).get('level_required', d['LevelRequirement']),
              'Condition': 50000, 'MaxCondition': 50000, 'Durability': 50000, 'MaxDurability': 50000, 'TranslationId': ''})
    if ot is not None:
        s['Object_Type'] = ot
    if it is not None:
        s['Item_Type'] = it
    # Matched ToA drops in the world are item level 51 with DPS 16.5 (165) and AF 102 (cloth 51); new ones match.
    s['Level'] = 51
    if i['category'] == 1:
        s['SPD_ABS'] = int(round(td.get('speed', d['SPD_ABS'] / 10) * 10))
        s['DPS_AF'] = 165
        if td.get('damage_type') in (1, 2, 3):
            s['Type_Damage'] = td['damage_type']
        s['Hand'] = hand if hand is not None else d['Hand']
    elif i['category'] == 2:
        s['SPD_ABS'] = td.get('absorption', d['SPD_ABS'])
        s['DPS_AF'] = 51 if s.get('Object_Type', d['Object_Type']) == 32 else 102
    edits.append({'table': 'ItemTemplate', 'copy': d['Id_nb'], 'key': nid, 'set': s})
    report.append({'Id_nb': nid, 'Name': i['name'], 'Realm': i['realm'], 'Result': f'new (model and type from {d["Id_nb"]})',
                   'Official id': i['id'], 'Official name': i['name'], 'Before': '', 'After': ' '.join(f'{k}:{v}' for k, v in bonus_lines(i)[0]),
                   'Notes': '; '.join(notes)})

# official drops into tables
for mob, lst in sorted(off_drops.items()):
    t = mob_table.get(mob)
    if t is None:
        t = mob
        mob_table[mob] = t
    tab = tables.setdefault(t, {})
    for kind, i in lst:
        wid = off_world.get(i['id'])
        if not wid:
            continue
        if kind == 'one_time_drop':
            if (mob, wid) not in w_otd:
                w_otd.add((mob, wid))
                edits.append({'table': 'LootOTD', 'insert': {'LootOTD_ID': uid('otd', mob, wid), 'MobName': mob, 'ItemTemplateID': created.get(wid, (None, None))[0] and wid or W[wid]['Id_nb'], 'MinLevel': 0}})
            continue
        tab.setdefault(wid, (15, 1, 'official'))

lt_rows = mx_rows = 0
for t, tab in tables.items():
    if t in w_lt:
        continue
    for wid, (ch, cnt, src) in sorted(tab.items()):
        idnb = W[wid]['Id_nb'] if wid in W else wid
        edits.append({'table': 'LootTemplate', 'insert': {'LootTemplate_ID': uid('lt', t, wid), 'TemplateName': t, 'ItemTemplateID': idnb, 'Chance': ch, 'Count': cnt}})
        lt_rows += 1
for mob, t in sorted(mob_table.items()):
    if t not in tables or not tables[t]:
        continue
    rows = w_mx.get(mob, [])
    if any(r['LootTemplateName'].lower() == t for r in rows):
        continue
    if rows or mob in toa_names or mob in enc_names:
        dc = next((int(r['DropCount']) for r in e_mx.get(mob, []) if r['LootTemplateName'].lower() == t), 2)
        edits.append({'table': 'MobXLootTemplate', 'insert': {'MobXLootTemplate_ID': uid('mx', mob, t), 'MobName': mob, 'LootTemplateName': t, 'DropCount': dc}})
        mx_rows += 1

made_spells, failed_spells = 0, []
for sid in sorted(need_spells):
    ours = 800000 + sid
    if ours in spells:
        continue
    row, why = build_spell(sid)
    if row is None:
        failed_spells.append((sid, why, sorted(set(need_spells[sid]))[:5]))
        continue
    edits.append({'table': 'Spell', 'insert': row})
    made_spells += 1
    spell_report.append({'Official spell': sid, 'Our spell': ours, 'Type': row['Type'], 'Copied from': why,
                         'Damage': row['Damage'], 'Value': row['Value'], 'Duration': row['Duration'], 'Target': row['Target'],
                         'Description': row['Description']})
    spells[ours] = row
for sid in sorted(need_spells):
    ours = 800000 + sid
    if ours in spells and ours not in line_x:
        edits.append({'table': 'LineXSpell', 'insert': {'LineXSpell_ID': uid('lxs', ours), 'LineName': 'Item Effects', 'SpellID': ours, 'Level': 1}})
        line_x.add(ours)
# an item pointing at a spell we could not build keeps nothing rather than a dead id
dead = {800000 + sid for sid, _, _ in failed_spells}
for e in edits:
    if e['table'] == 'ItemTemplate':
        for k in ('SpellID', 'SpellID1', 'ProcSpellID', 'ProcSpellID1'):
            if e['set'].get(k) in dead:
                e['set'][k] = 0
                if k == 'SpellID':
                    e['set'].update({'Charges': 0, 'MaxCharges': 0})
                if k == 'SpellID1':
                    e['set'].update({'Charges1': 0, 'MaxCharges1': 0})


def write_outputs():
    os.makedirs(OUT, exist_ok=True)
    json.dump(edits, open(os.path.join(OUT, f'toa-named-drops-{DATE}.edits.json'), 'w'), indent=1)
    with open(os.path.join(OUT, 'items.csv'), 'w', newline='', encoding='utf8') as fh:
        wr = csv.DictWriter(fh, fieldnames=list(report[0].keys()))
        wr.writeheader()
        wr.writerows(report)
    with open(os.path.join(OUT, 'spells.csv'), 'w', newline='', encoding='utf8') as fh:
        wr = csv.DictWriter(fh, fieldnames=['Official spell', 'Our spell', 'Type', 'Copied from', 'Damage', 'Value', 'Duration', 'Target', 'Description'])
        wr.writeheader()
        wr.writerows(spell_report)
    summary = {
        'toa monster names': len(all_toa), 'official ToA drop monsters': len(off_drops),
        'tables added': sum(1 for t in tables if t not in w_lt and tables[t]), 'LootTemplate rows': lt_rows, 'MobXLootTemplate rows': mx_rows,
        'items set to official stats': changed_items, 'items already official': len(updates) - changed_items, 'items created': len(created),
        'spells created': made_spells, 'spells not built': failed_spells, 'official drops not made (no donor)': unmade,
        'EoD rows naming items missing from the world': missing_eod_items, 'edits': len(edits),
        'by table': collections.Counter(e['table'] for e in edits),
    }
    json.dump(summary, open(os.path.join(OUT, 'summary.json'), 'w'), indent=1, default=str)
    print(json.dumps(summary, indent=1, default=str)[:4000])


if __name__ == '__main__':
    write_outputs()
