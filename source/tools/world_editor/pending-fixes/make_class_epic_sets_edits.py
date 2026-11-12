"""DR-209 / DR-210: the live epic armour ("kings armor") and the Cloak of the Loyal <class> for every class.

Source: the official Herald 1.126 export. Epic sets = the 6 single-class armour pieces sold in the
"<REALM> EPIC <armour>" stores / given by Camelot's, Jordheim's and Tir na Nog's Finest. Cloaks = "Cloak of the Loyal
<class>" (one per class; three Mauler realm versions).

Rules:
- Every official bonus line is given outright: no Champion Level requirement and no artifact level staging
  (owner rule, no-CL-gating). Untradeable, level requirement 45 (armour) as on live.
- Models: the class's own epic look. Our older epic set's piece for that class and slot where it exists,
  otherwise the stock 1.130 epic art (client objects.csv), otherwise a donor piece of the same realm and armour
  type. Cloaks use their official icon id, which is the client model (cloak_<class>_Epic01, 4604-4650).
- Abilities: official spell X = our spell 800000+X; a spell missing from our Spell table is left off and listed.
- Items already in the world under the same name for that class or for no class (Bainshee's old set, open to every
  class; Cleric's Crown of Intrepid Souls) are updated in place and locked to the class.

usage: python make_class_epic_sets_edits.py <world.sqlite3 (copy)> <daoc_item_database_combined.zip> <out.edits.json>
Read-only on the world. Writes <out>.edits.json and <out>.report.json.
"""
import collections
import json
import re
import sqlite3
import sys
import os
import uuid
import zipfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from herald_items import Herald  # noqa: E402

db_path, zip_path, out_path = sys.argv[1:4]
z = zipfile.ZipFile(zip_path)
ALL = json.loads(z.read('static_objects.json').decode('utf8', 'replace'))['items']
META = json.loads(z.read('daoc_item_metadata.json').decode('utf8', 'replace'))

# ---------------- converter (copied from Research\Audits\Missing-Items-2026-11-08\albion\make_albion_missing_items.py) ----------------
# ---------------- converter (official_bonus_map.py + mythical codes) ----------------
STAT = {0: 1, 1: 2, 2: 3, 3: 4, 4: 5, 5: 6, 6: 7, 7: 8, 10: 156}
CAP = {0: 201, 1: 202, 2: 203, 3: 204, 4: 205, 5: 206, 6: 207, 7: 208, 10: 209}
MYTH_CAP = {0: 236, 1: 237, 2: 238, 3: 239, 4: 240, 5: 241, 6: 242, 7: 243, 10: 244}
RESN = {'Body': 11, 'Cold': 12, 'Crush': 13, 'Energy': 14, 'Heat': 15, 'Matter': 16, 'Slash': 17, 'Spirit': 18, 'Thrust': 19,
        'Essence': 249}
RESCAP = {'Body': 221, 'Cold': 222, 'Crush': 223, 'Energy': 224, 'Heat': 225, 'Matter': 226, 'Slash': 227, 'Spirit': 228, 'Thrust': 229}
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
         'Hexing': 161, 'Witchcraft': 162, 'All Focus': 165, 'All Casting': 165}
FLAT = {4: 10, 8: 173, 9: 198, 10: 200, 11: 154, 12: 153, 13: 199, 14: 190, 15: 193, 16: 195, 17: 194, 19: 155, 20: 188,
        21: 191, 22: 148, 27: 174, 29: 210, 30: 211, 32: 197, 34: 196, 36: 189, 37: 185, 38: 147, 40: 150, 41: 151, 42: 183,
        46: 182, 47: 186, 48: 187, 49: 180, 50: 179, 51: 171, 52: 170, 53: 169, 54: 184,
        # mythical (LotM): eProperty enum names; parry/evade/block as in the world's Mythirian rows (block = 170)
        39: 233, 55: 234, 60: 171, 61: 169, 62: 170, 63: 235, 66: 260, 67: 249, 71: 230, 72: 253, 74: 146, 76: 257, 77: 258,
        78: 259, 80: 261}
ART = {1: 254, 2: 251, 4: 248, 5: 235, 6: 253, 7: 247}
NO_PROPERTY = {23: 'crafting min quality', 24: 'crafting quality', 25: 'crafting speed', 26: 'crafting skill gain',
               31: 'ToA fatigue cap', 44: 'death XP loss reduction', 58: 'mythical siege speed', 69: 'mythical siege ablative',
               73: 'mythical spell focus'}


def bonus_lines(item):
    out, unknown = collections.OrderedDict(), []

    def add(p, v):
        out[p] = out.get(p, 0) + v
    for b in item.get('bonuses') or []:
        t, v, sid = b['type'], b['value'], b.get('id')
        sub = META['bonus_types'].get(str(t), {}).get('sub_types', {}).get(str(sid))
        if t == 1:
            p = STAT.get(sid)
        elif t == 28:
            p = CAP.get(sid)
        elif t == 64:
            p = MYTH_CAP.get(sid)
        elif t == 75:      # mythical stat and cap
            if STAT.get(sid) and MYTH_CAP.get(sid):
                add(STAT[sid], v)
                add(MYTH_CAP[sid], v)
                continue
            p = None
        elif t == 2:
            p = SKILL.get(sub)
        elif t == 5:
            p = RESN.get(sub)
        elif t == 57:
            p = RESCAP.get(sub)
        elif t == 68:      # mythical resist and cap
            if RESN.get(sub) and RESCAP.get(sub):
                add(RESN[sub], v)
                add(RESCAP[sub], v)
                continue
            p = None
        elif t == 6:
            p = FOCUS.get(sub)
        elif t == 35:
            p = ART.get(sid)
        else:
            p = FLAT.get(t)
        if p:
            add(p, v)
        else:
            unknown.append({'type': t, 'id': sid, 'value': v, 'name': META['bonus_types'].get(str(t), {}).get('name'),
                            'sub': sub, 'why': NO_PROPERTY.get(t, 'no eProperty mapping')})
    return list(out.items()), unknown



def ability_cols(i):
    s = {'SpellID': 0, 'Charges': 0, 'MaxCharges': 0, 'SpellID1': 0, 'Charges1': 0, 'MaxCharges1': 0,
         'ProcSpellID': 0, 'ProcSpellID1': 0, 'CanUseEvery': int(i.get('use_duration') or 0)}
    notes = []
    for a in i.get('abilities') or []:
        sid, pos, mt = a['spell'], a.get('position', 1), a.get('magic_type')
        ours = 800000 + sid
        if mt in (6, 7, 0):
            c = a.get('max_charges') or 0
            if mt == 0:
                notes.append(f'use ability {sid} (no charges in the record; reuse {i.get("use_duration") or 0}s)')
            k = '' if pos == 1 or s['SpellID'] == 0 else '1'
            s.update({f'SpellID{k}': ours, f'Charges{k}': c, f'MaxCharges{k}': c})
        elif mt in (1, 2):
            k = 'ProcSpellID' if pos == 1 or s['ProcSpellID'] == 0 else 'ProcSpellID1'
            s[k] = ours
        elif mt == 3:
            notes.append(f'passive while wielded {sid} (no column; DOL needs a script)')
        elif mt == 4:
            notes.append(f'sub-spell {sid} of the item\'s other ability')
    s['_notes'] = notes
    return s




# ---------------- epic sets + loyalty cloaks ----------------
EPIC_STORE = re.compile(r'^(ALB|MID|HIB) EPIC (CLOTH|STUDDED|LEATHER|CHAIN|PLATE|SCALE)', re.I)
SLOT_IT = {1: 21, 2: 22, 3: 23, 5: 25, 7: 27, 8: 28, 6: 26}
SLOT_KEY = {21: 'helm', 22: 'gloves', 23: 'boots', 25: 'vest', 27: 'legs', 28: 'arms', 26: 'cloak'}
ARMOR_OT = {1: {0: 32, 10: 33, 19: 34, 27: 35, 34: 36}, 2: {0: 32, 10: 33, 19: 34, 27: 35},
            3: {0: 32, 10: 33, 19: 37, 27: 38}}
CLASS_KEY = {1: 'paladin', 2: 'armsman', 3: 'scout', 4: 'minstrel', 5: 'theurgist', 6: 'cleric', 7: 'wizard',
             8: 'sorcerer', 9: 'infiltrator', 10: 'friar', 11: 'mercenary', 12: 'necromancer', 13: 'cabalist',
             19: 'reaver', 21: 'thane', 22: 'warrior', 23: 'shadowblade', 24: 'skald', 25: 'hunter', 26: 'healer',
             27: 'spiritmaster', 28: 'shaman', 29: 'runemaster', 30: 'bonedancer', 31: 'berserker', 32: 'savage',
             33: 'heretic', 34: 'valkyrie', 39: 'bainshee', 40: 'eldritch', 41: 'enchanter', 42: 'mentalist',
             43: 'blademaster', 44: 'hero', 45: 'champion', 46: 'warden', 47: 'druid', 48: 'bard', 49: 'nightshade',
             50: 'ranger', 55: 'animist', 56: 'valewalker', 58: 'vampiir', 59: 'warlock', 60: 'mauler_alb',
             61: 'mauler_mid', 62: 'mauler_hib'}
MAULER = {1: 60, 2: 61, 3: 62}
# Stock 1.130 epic art for classes without an older epic set in our world (client objects.csv ids).
CLIENT_ART = {
    33: {25: 2921},                                                   # Heretic robe (arms in the robe)
    39: {25: 2922, 28: 2948, 27: 2949, 22: 2950, 23: 2952},           # Bainshee
    58: {25: 2923, 27: 2924, 28: 2925, 22: 2926, 23: 2927},           # Vampiir
    34: {25: 2928, 27: 2929, 28: 2930, 22: 2931, 23: 2932},           # Valkyrie
    59: {25: 2933, 27: 2934, 22: 2936, 23: 2937},                     # Warlock (no arms piece in the client)
    60: {25: 3638, 27: 3639, 28: 3640, 22: 3641, 23: 3642},           # Mauler (Albion)
    61: {25: 3570, 27: 3571, 28: 3572, 22: 3573, 23: 3574},           # Mauler (Midgard)
    62: {25: 3560, 27: 3561, 28: 3562, 22: 3563, 23: 3564},           # Mauler (Hibernia)
    11: {25: 718},                                                    # Mercenary vest (old set lacks it)
    25: {28: 758},                                                    # Hunter arms (old set lacks it)
}

con = sqlite3.connect('file:' + db_path.replace('\\', '/') + '?mode=ro', uri=True)
con.row_factory = sqlite3.Row
SPELL_IDS = {r[0] for r in con.execute('SELECT SpellID FROM Spell')}
_spells = {r['SpellID']: dict(r) for r in con.execute('SELECT * FROM Spell')}
H = Herald(zip_path, spells=_spells, package='DR-209 live epic sets', date='2026-11-09')
# Herald() drops artifact records, and the loyalty cloaks are artifacts whose delve tags each line "[L5]: ".
for _i in ALL:
    if _i['name'].startswith('Cloak of the Loyal '):
        H.items.append(dict(_i, delve_text=re.sub(r'(?m)^\[L\d+\]:\s*', '', _i.get('delve_text') or '')))
H.OFF = H.items
LINE_X = {r[0] for r in con.execute("SELECT SpellID FROM LineXSpell WHERE LineName = 'Item Effects'")}
NEXT_PARAM = [(con.execute('SELECT max(CustomParamID) FROM SpellXCustomValues').fetchone()[0] or 0) + 1]
NS = uuid.UUID('9d2b6c41-7e0a-4f55-b8a3-1c6e2f7d9a20')
spell_edits = []


def uid(*parts):
    return str(uuid.uuid5(NS, '|'.join(str(p) for p in parts)))


# ---------------- hand-made cloak abilities (DR-216) ----------------
# Official spells herald_items has no rule for. Types marked * are new handlers in
# GameServer/spells/LoyaltyCloakHandlers.cs (feature/loyalty-cloaks). Durations the delve leaves out are our choice
# and say so in the description; the delve's 6 s / 5 s group pulses are given as plain 10 minute group buffs, since
# a pulsing item spell would never end. DB Frequency is in 1/10 s (procs: % chance).
T = 'DR-216'
HAND = {
    6171: dict(Name='Ward of Health', Type='DiseaseWard', Target='Group', Range=1500, Duration=600, Frequency=150,      # *
               ClientEffect=4320, Icon=4320, Description='Protects the group from disease, curing it every 15 seconds for 10 minutes.'),
    6173: dict(Name='Battle Stride', Type='InCombatSpeed', Target='Self', Duration=30, Value=225, ClientEffect=2086,    # *
               Icon=2086, Description='Raises your speed to 225% even in combat for 30 seconds (duration ours). Roots, snares, mezzes and stuns still land.'),
    5893: dict(Name='Bladeturn Breaker', Type='IgnoreBladeturn', Target='Self', Duration=30, ClientEffect=1081,          # *
               Icon=1081, Description='Your melee attacks ignore bladeturn magic for 30 seconds (duration ours).'),
    6091: dict(Name='Bladeturn Breaker', Type='IgnoreBladeturn', Target='Self', Duration=30, ClientEffect=1081,          # *
               Icon=1081, Description='Your melee attacks ignore bladeturn magic for 30 seconds (duration ours).'),
    5891: dict(Name='Call of the Wild', Type='CharmableDecoy', Target='Self', Duration=600, Value=56,                   # *
               ClientEffect=4320, Icon=4320, Description='Summons a wild wolf that the caster, allies or enemies may charm. Lasts 10 minutes or until killed.'),
    9735: dict(Name='Morning Light', Type='CureMezz', Target='Realm', Range=1500, Radius=150, CastTime=3.0,
               ClientEffect=1081, Icon=1081, Description='All mesmerization effects are removed from the target and allies near it.'),
    5887: dict(Name='Loyal Elemental', Type='SummonElemental', Target='Enemy', Range=1750, Duration=20, Radius=400,
               LifeDrainReturn=12837, DamageType=12, ClientEffect=5435, Icon=4107,
               Description='Summons an elemental spirit that attacks the target for 20 seconds.'),
    5889: dict(Name='Arcane Renewal', Type='RefreshClassTimers', Target='Self', ClientEffect=1081, Icon=1081,          # *
               Description='Refreshes all class-based reuse timers (not realm abilities or items).'),
    5890: dict(Name='Mindfulness', Type='ResetQuickcast', Target='Self', ClientEffect=1081, Icon=1081,
               Description='Refreshes Quickcast.'),
    5895: dict(Name='Shadow Step', Type='EvadeBuff', Target='Self', Duration=15, Value=20, ClientEffect=1021, Icon=1021,
               Description='Your chance to evade is increased by 20% for 15 seconds.'),
    5888: dict(Name='Arcane Leadership', Type='LoyalArcaneLeadership', Target='Group', Range=1500, Duration=600,       # *
               Value=10, ClientEffect=1031, Icon=1031, Description='The group gains 10% spell range and 10% resist piercing for 10 minutes.'),
    6071: dict(Name='Terrify Minions', Type='PetScareArea', Target='Enemy', Duration=60, Radius=2000, DamageType=10,
               ClientEffect=15201, Icon=15201, Description='Enemy pets within range flee in terror; it cannot be resisted.'),
    6078: dict(Name='Arcane Fury', Type='ArtifactSpellDamage', Target='Self', Duration=600, Value=10, ClientEffect=1031,
               Icon=1031, Description='Increases your magic damage by 10% for 10 minutes (duration ours).'),
    6179: dict(Name='Arcane Fury', Type='ArtifactSpellDamage', Target='Self', Duration=600, Value=10, ClientEffect=1031,
               Icon=1031, Description='Increases your magic damage by 10% for 10 minutes (duration ours).'),
    6086: dict(Name='Runic Fury', Type='ArtifactSpellDamage', Target='Self', Duration=30, Value=25, ClientEffect=1031,
               Icon=1031, Description='Increases your magic damage by 25% for 30 seconds (duration ours).'),
    6074: dict(Name='Storm Insight', Type='SpellCritAura', Target='Group', Range=1500, Duration=20, Value=25,
               ClientEffect=1031, Icon=15231, Description="The group's chance to critically hit with spells is increased by 25% for 20 seconds."),
    6076: dict(Name='Loyal Bulwark', Type='ControlShrug', Target='Group', Range=1500, Duration=10, Value=100,          # *
               ClientEffect=1001, Icon=1587, Description='The group gains 100% magic resistance and shrugs off all crowd control for 10 seconds.'),
    6187: dict(Name='Loyal Guard', Type='ControlShrug', Target='Group', Range=1500, Duration=600, Value=10,            # *
               ClientEffect=1001, Icon=1587, Description='The group gains 10% magic resistance and a 10% chance to shrug off crowd control for 10 minutes.'),
    6077: dict(Name='Killing Edge', Type='MeleeCritBuff', Target='Self', Duration=30, Value=20, ClientEffect=1021,      # *
               Icon=1021, Description='Increases your melee critical hit chance by 20% for 30 seconds (duration ours).'),
    6080: dict(Name='Pack Speed', Type='SpeedEnhancement', Target='Pet', Range=1000, Duration=45, Value=200,
               ClientEffect=2086, Icon=2086, Description="Your pet's speed is raised to 200% for 45 seconds (speed ours)."),
    6085: dict(Name='Healing Torrent', Type='HealOverTime', Target='Group', Range=1500, Duration=4, Frequency=10, Value=200,
               ClientEffect=1340, Icon=1340, Description='The group regenerates 200 hit points every second for 4 seconds.'),
    6090: dict(Name='Hunter Senses', Type='StealthDetectionAura', Target='Self', Duration=300, Value=40, ClientEffect=2874,
               Icon=2874, Description='Your chance to uncover stealthed enemies is increased by 40 for 5 minutes.'),
    6093: dict(Name='Spirit Veil', Type='DamageReduction', Target='Group', Range=1500, Duration=20, Value=35,
               ClientEffect=1001, Icon=1587, Description='Reduces all damage taken by you and your group by 35% for 20 seconds.'),
    6094: dict(Name='Mistwalk', Type='SelfStealth', Target='Self', ClientEffect=1081, Icon=1081,                        # *
               Description='You slip into stealth.'),
    6095: dict(Name='Spreading Flames', Type='GroupDDRadius', Target='Group', Range=1500, Duration=60, Value=300,        # *
               ClientEffect=1031, Icon=1031, Description="The group's single target direct damage spells hit everything within 300 of the target for 60 seconds."),
    6099: dict(Name='Warden Stance', Type='ParryBlockBuff', Target='Group', Range=1500, Duration=600, Value=10,         # *
               ClientEffect=1021, Icon=1021, Description="The group's chance to parry and block is increased by 10% for 10 minutes."),
    6180: dict(Name='Chain of Thorns', Type='ChainDirectDamage', Target='Enemy', Range=2000, Damage=3000, DamageType=12,
               ClientEffect=2501, Icon=2501, Description='Damages the target, then up to four other enemies near it, each jump doing less.'),
    5897: dict(Name='Healing Strikes', Type='OffensiveProc', Target='Group', Range=1500, Duration=600, Value=855897,
               Frequency=10, ClientEffect=1340, Icon=1340, Description="The group's melee attacks have a 10% chance to heal the group for 75."),
    6083: dict(Name='Plague Ward', Type='DefensiveProc', Target='Self', Duration=600, Value=856083, Frequency=10,
               ClientEffect=520, Icon=520, Description='When hit in melee, 10% chance to infect the attacker with a wasting disease for 30 seconds.'),
    6182: dict(Name='Soul Thirst', Type='OffensiveProc', Target='Self', Duration=600, Value=856182, Frequency=10,
               ClientEffect=2874, Icon=2874, Description='Your melee attacks have a 10% chance to drain 35 power from the target.'),
}
HAND_SUBS = {
    855897: dict(Name='Healing Strikes', Type='Heal', Target='Group', Range=1500, Value=75, ClientEffect=1340, Icon=1340,
                 Description='Heals the group for 75.'),
    856083: dict(Name='Wasting Plague', Type='Disease', Target='Enemy', Range=1000, Duration=30, DamageType=14,
                 ClientEffect=520, Icon=520, Description='A wasting disease slows the target, weakens it and hinders healing.'),
    856182: dict(Name='Soul Thirst', Type='PowerDrain', Target='Enemy', Range=250, Damage=35, LifeDrainReturn=35,
                 DamageType=10, ClientEffect=2874, Icon=2874, Description='Damages the target and returns power to the caster.'),
}
# Animist: the strongest damage turret (Forest's Core, spell 11357), lasting 3 minutes.
TURRET = 6178


def hand_row(sid, d):
    row = {'SpellID': sid, 'Spell_ID': f'offline-item-{sid}', 'PackageID': T, 'Power': 0, 'CastTime': 0.0,
           'Message1': '', 'Message2': '', 'Message3': '', 'Message4': ''}
    row.update(d)
    if d.get('Type') == 'CharmableDecoy':
        row['Message1'] = 'wild wolf'   # the creature's name (CharmableDecoySpellHandler)
    return row


def hand_spell(ours, report):
    official = ours - 800000
    if official not in HAND and official != TURRET:
        return False
    if official == TURRET:
        base = dict(next(con.execute('SELECT * FROM Spell WHERE SpellID = 11357')))
        row = {k: v for k, v in base.items() if k != 'LastTimeRowUpdated'}
        row.update({'SpellID': ours, 'Spell_ID': f'offline-item-{ours}', 'Name': 'Loyal Turret', 'Duration': 180,
                    'Power': 0, 'RecastDelay': 0, 'SpellGroup': 0, 'EffectGroup': 0, 'PackageID': T,
                    'Description': "Summons the Animist's strongest damage turret for 3 minutes."})
    else:
        row = hand_row(ours, HAND[official])
    spell_edits.append({'table': 'Spell', 'insert': row})
    SPELL_IDS.add(ours)
    ids = [ours]
    if row['Type'] in ('OffensiveProc', 'DefensiveProc'):
        sub = int(row['Value'])
        if sub not in SPELL_IDS:
            spell_edits.append({'table': 'Spell', 'insert': hand_row(sub, HAND_SUBS[sub])})
            SPELL_IDS.add(sub)
        ids.append(sub)
    for sid in ids:
        if sid not in LINE_X:
            spell_edits.append({'table': 'LineXSpell', 'insert': {'LineXSpell_ID': uid('lxs', sid), 'LineName': 'Item Effects',
                                                                  'SpellID': sid, 'Level': 1}})
            LINE_X.add(sid)
    report['spells_built'].append([ours, row['Name'], row['Type'], row.get('Value'), row.get('Duration'), 'hand-made'])
    return True


def ensure_spell(ours, report):
    """Build official spell ours-800000 (and its sub spells) with herald_items' rules; True when it exists after."""
    if ours in SPELL_IDS:
        return True
    if hand_spell(ours, report):
        return True
    row, why = H.build_spell(ours - 800000)
    if row is None:
        report['spells_not_built'][ours] = why
        return False
    row['Spell_ID'] = f'offline-item-{row["SpellID"]}'
    spell_edits.append({'table': 'Spell', 'insert': row})
    SPELL_IDS.add(row['SpellID'])
    extra = H.extra_rows.get(row['SpellID'], {})
    for sub in extra.get('spells', []):
        if sub['SpellID'] not in SPELL_IDS:
            spell_edits.append({'table': 'Spell', 'insert': sub})
            SPELL_IDS.add(sub['SpellID'])
    for sid in [row['SpellID']] + [x['SpellID'] for x in extra.get('spells', [])]:
        if sid not in LINE_X:
            spell_edits.append({'table': 'LineXSpell', 'insert': {'LineXSpell_ID': uid('lxs', sid), 'LineName': 'Item Effects',
                                                                  'SpellID': sid, 'Level': 1}})
            LINE_X.add(sid)
    for sub_id in extra.get('custom', []):
        spell_edits.append({'table': 'SpellXCustomValues', 'insert': {'CustomParamID': NEXT_PARAM[0], 'SpellID': row['SpellID'],
                                                                      'KeyName': 'MultipleSubSpells', 'Value': str(sub_id)}})
        NEXT_PARAM[0] += 1
    for key, value in extra.get('params', []):
        spell_edits.append({'table': 'SpellXCustomValues', 'insert': {'CustomParamID': NEXT_PARAM[0], 'SpellID': row['SpellID'],
                                                                      'KeyName': key, 'Value': str(value)}})
        NEXT_PARAM[0] += 1
    report['spells_built'].append([row['SpellID'], row['Name'], row['Type'], row.get('Value'), row.get('Duration')])
    return True
ITEM_COLS = {r[1] for r in con.execute('PRAGMA table_info(ItemTemplate)')}
old = collections.defaultdict(dict)      # class -> item type -> (model, object type, extension)
for r in con.execute("SELECT AllowedClasses, Item_Type, Model, Object_Type, Extension, Realm FROM ItemTemplate "
                     "WHERE Id_nb LIKE '%Epic%' AND Item_Type IN (21,22,23,25,27,28) AND Level >= 45"):
    for c in str(r['AllowedClasses']).split(';'):
        if c.isdigit():
            old[int(c)][r['Item_Type']] = (r['Model'], r['Object_Type'], r['Extension'])
by_name = collections.defaultdict(list)
for r in con.execute('SELECT Id_nb, Name, AllowedClasses FROM ItemTemplate'):
    by_name[r['Name']].append(dict(r))


def classes_of(i):
    out = []
    for c in (i.get('requirements') or {}).get('usable_by') or []:
        out.append(MAULER[i['realm']] if c == 60 else c)
    return out


def model_for(cls, it, ot, realm, report):
    if it in CLIENT_ART.get(cls, {}):
        return CLIENT_ART[cls][it], 0, 'client epic art'
    if it in old.get(cls, {}):
        m, _, ext = old[cls][it]
        return m, ext, 'older epic set'
    # donor: same realm, same armour type, same slot from another class's older epic set
    for c, pieces in sorted(old.items()):
        if it in pieces and pieces[it][1] == ot and REALM_OF.get(c) == realm:
            report['donor_models'].append(f'{CLASS_KEY[cls]} {SLOT_KEY[it]}: model {pieces[it][0]} from {CLASS_KEY[c]}')
            return pieces[it][0], pieces[it][2], f'donor {CLASS_KEY[c]}'
    report['no_model'].append(f'{CLASS_KEY[cls]} {SLOT_KEY[it]}')
    return 0, 0, 'none'


REALM_OF = {c: (1 if c in (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 19, 33, 60) else
                2 if c in (21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 34, 59, 61) else 3) for c in CLASS_KEY}

report = {'items': [], 'missing_spells': [], 'unconverted_bonuses': [], 'donor_models': [], 'no_model': [],
          'updated_existing': [], 'skipped': [], 'spells_built': [], 'spells_not_built': {}}
edits = []
seen = set()
for i in ALL:
    src = i.get('sources') or {}
    is_cloak = i['name'].startswith('Cloak of the Loyal ')
    is_epic = i.get('category') == 2 and i.get('slot') in SLOT_IT and (
        any(EPIC_STORE.match(s) for s in src.get('stores', [])) or
        any(q.endswith('Finest') for q in src.get('quests', [])))
    if not (is_cloak or is_epic):
        continue
    cls = classes_of(i)
    if len(cls) != 1 or cls[0] not in CLASS_KEY:
        if is_epic:
            continue
        report['skipped'].append(f"{i['name']}: classes {cls}")
        continue
    cls = cls[0]
    realm = i['realm'] if i['realm'] in (1, 2, 3) else REALM_OF[cls]   # Berserker's set is listed as realm 0
    it = SLOT_IT[i['slot']]
    key = f'live_{"loyal_cloak" if is_cloak else "epic"}_{CLASS_KEY[cls]}' + ('' if is_cloak else f'_{SLOT_KEY[it]}')
    if key in seen:
        report['skipped'].append(f"{i['name']}: duplicate {key} (herald {i['id']})")
        continue
    seen.add(key)

    lines, unknown = bonus_lines(i)
    if i['name'] == 'Chausses of Intrepid Souls':   # 1.127 patch notes: +2% cast speed, not crafting speed
        unknown = [u for u in unknown if u['type'] != 25]
        lines.append((191, 2))
    if unknown:
        report['unconverted_bonuses'].append({'item': i['name'], 'bonuses': unknown})
    td = i.get('type_data') or {}
    ab = ability_cols(i)
    notes = ab.pop('_notes')
    for col in ('SpellID', 'SpellID1', 'ProcSpellID', 'ProcSpellID1'):
        if ab[col] and not ensure_spell(ab[col], report):
            report['missing_spells'].append(f"{i['name']}: {col} {ab[col]} (official {ab[col] - 800000})")
            ab[col] = 0
            if col.startswith('SpellID'):
                k = col[len('SpellID'):]
                ab[f'Charges{k}'] = ab[f'MaxCharges{k}'] = 0
    for k in ('', '1'):   # a use ability with no charge count is unlimited (-1), like the Storm Regalia
        if ab[f'SpellID{k}'] and not ab[f'MaxCharges{k}']:
            ab[f'Charges{k}'] = ab[f'MaxCharges{k}'] = -1
    if is_cloak:
        ot, model, ext, model_from = 41, i.get('icon') or 0, 0, 'official icon = client cloak model'
        level, af, absorb, req = 50, 0, 0, 0
    else:
        ot = ARMOR_OT[realm].get(td.get('absorption'))
        model, ext, model_from = model_for(cls, it, ot, realm, report)
        level, af, absorb = 51, td.get('armor_factor') or 0, td.get('absorption') or 0
        req = (i.get('requirements') or {}).get('level_required') or 45
    cols = {'Name': i['name'], 'Realm': realm, 'Object_Type': ot, 'Item_Type': it, 'Hand': 0, 'Level': level,
            'DPS_AF': af, 'SPD_ABS': absorb, 'Quality': 100, 'Condition': 50000, 'MaxCondition': 50000,
            'Durability': 50000, 'MaxDurability': 50000, 'Model': model, 'Extension': ext,
            'BonusLevel': i.get('bonus_level') or 0, 'LevelRequirement': req, 'AllowedClasses': str(cls),
            'IsTradable': 0, 'IsDropable': 1, 'IsPickable': 1, 'CanDropAsLoot': 0, 'Price': 0,
            'Weight': 10 if is_cloak else {32: 10, 33: 15, 34: 22, 35: 32, 36: 40, 37: 22, 38: 32}.get(ot, 20),
            'PackageID': 'DR-209 live epic sets', 'Description': 'Live class epic' if not is_cloak else 'Live class cloak'}
    for n in range(1, 11):
        p, v = lines[n - 1] if n <= len(lines) else (0, 0)
        cols[f'Bonus{n}Type'], cols[f'Bonus{n}'] = p, v
    cols['ExtraBonusType'], cols['ExtraBonus'] = lines[10] if len(lines) > 10 else (0, 0)
    if len(lines) > 11:
        report['unconverted_bonuses'].append({'item': i['name'], 'bonuses': f'{len(lines) - 11} lines past ExtraBonus'})
    cols.update(ab)
    cols = {k: v for k, v in cols.items() if k in ITEM_COLS}

    same = [r for r in by_name.get(i['name'], []) if str(cls) in str(r['AllowedClasses']).split(';')
            or str(r['AllowedClasses']) in ('0', '')]   # e.g. the old Bainshee epics are open to every class
    if same:
        keep = {k: v for k, v in cols.items() if k != 'Name'}   # PackageID too: trainers find the set by it (ClassEpicGear)
        edits.append({'table': 'ItemTemplate', 'key': same[0]['Id_nb'], 'set': keep})
        report['updated_existing'].append(f"{same[0]['Id_nb']} ({i['name']})")
        target = same[0]['Id_nb']
    else:
        edits.append({'table': 'ItemTemplate', 'insert': {'Id_nb': key, **cols}})
        target = key
    report['items'].append({'Id_nb': target, 'name': i['name'], 'class': CLASS_KEY[cls], 'slot': SLOT_KEY[it],
                            'herald_id': i['id'], 'model': model, 'model_from': model_from,
                            'bonus_lines': len(lines), 'notes': notes})

# Death Knight (66, Albion) reuses the Necromancer set and cloak (owner, 2026-11-09).
for e in [e for e in edits if 'insert' in e and e['insert']['AllowedClasses'] == '12']:
    row = dict(e['insert'], Id_nb=e['insert']['Id_nb'].replace('necromancer', 'deathknight'), AllowedClasses='66')
    edits.append({'table': 'ItemTemplate', 'insert': row})
    src = next(r for r in report['items'] if r['Id_nb'] == e['insert']['Id_nb'])
    report['items'].append(dict(src, Id_nb=row['Id_nb'], **{'class': 'deathknight', 'model_from': 'copy of Necromancer'}))
CLASS_KEY[66] = 'deathknight'

per_class = collections.Counter(r['class'] for r in report['items'])
report['per_class'] = dict(sorted(per_class.items()))
edits = spell_edits + edits
json.dump(edits, open(out_path, 'w', encoding='utf8'), indent=1)
json.dump(report, open(out_path.replace('.edits.json', '') + '.report.json', 'w', encoding='utf8'), indent=1)
print(f"{len(edits)} edits ({len(report['updated_existing'])} updates) -> {out_path}")
print('classes:', len(per_class), 'not 7 pieces:', {k: v for k, v in per_class.items() if v != 7})
print('spells built:', len(report['spells_built']), 'not built:', report['spells_not_built'])
for k in ('missing_spells', 'donor_models', 'no_model', 'skipped'):
    print(f'{k}: {len(report[k])}', *report[k][:60], sep='\n  ')
print('unconverted bonuses:', len(report['unconverted_bonuses']))
