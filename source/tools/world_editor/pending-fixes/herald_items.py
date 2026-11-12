"""Official Herald item export (1.126) -> Divided Realms ItemTemplate / Spell values.

Shared by the item generators. The rules were learned and checked in the 2026-11-08 ToA named drop pass
(make_toa_named_drops_edits.py, applied to the test world and played on a sandbox); they are moved here unchanged,
with the mythical bonus codes added for the Bountycraft Mythirians.

    from herald_items import Herald
    h = Herald(zip_path, spells={SpellID: row}, package='OfflineX', date='2026-11-08')
    columns, notes = h.item_set(official_item)          # bonuses, charges, procs, tradable...
    row, donor = h.build_spell(official_spell_id)       # Spell row 800000 + id, or (None, reason)
"""
import collections
import json
import re
import zipfile

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


# Mythical bonuses (1.126+): our MythicalStatCapBonus 236-244, MythicalCoin 235, MythicalDiscumbering 234 and the
# modern regen / crowd control / physical defense 257-261 (read when `modern_item_bonuses` is on).
MYTH_CAP = {0: 236, 1: 237, 2: 238, 3: 239, 4: 240, 5: 241, 6: 242, 7: 243, 10: 244}
RESCAP = {'Body': 221, 'Cold': 222, 'Crush': 223, 'Energy': 224, 'Heat': 225, 'Matter': 226, 'Slash': 227, 'Spirit': 228,
          'Thrust': 229}
MYTH_FLAT = {55: 234, 63: 235, 71: 230, 76: 257, 77: 258, 78: 259, 66: 260, 80: 261}


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



DT = {'Crush': 1, 'Slash': 2, 'Thrust': 3, 'Body': 10, 'Cold': 11, 'Energy': 12, 'Heat': 13, 'Matter': 14, 'Spirit': 15}
RESIST_DEBUFF = {k: f'{k}ResistDebuff' for k in ('Slash', 'Thrust', 'Crush', 'Body', 'Cold', 'Energy', 'Heat', 'Matter', 'Spirit')}


HELPFUL = {'EnduranceHeal', 'PowerHeal', 'Heal', 'AblativeArmor', 'CombatSpeedBuff', 'AcuityBuff', 'StrengthConstitutionBuff',
           'DexterityQuicknessBuff', 'HealthRegenBuff', 'DamageAdd', 'BaseArmorFactorBuff', 'ToHitBuff', 'FlexibleSkillBuff',
           'StealthSkillBuff', 'HealAllOverTime', 'ArtifactAllStats', 'CurePoison', 'Conversion', 'MesmerizeDurationBuff', 'HealingDoneBuff',
           'MeleeDamageBuff', 'DmgReductionAndPowerReturn', 'AllMagicResistsBuff', 'CCResist', 'StyleAbsorbBuff', 'Resurrect',
           'HealingReceivedBuff', 'ParryBuff', 'SpreadHeal', 'ArmorAbsorptionBuff', 'AfHitsBuff', 'AblativeArmor', 'CrushSlashThrustBuff', 'HeatResistBuff', 'ColdResistBuff', 'MatterResistBuff',
           'BodyResistBuff', 'SpiritResistBuff', 'EnergyResistBuff', 'PowerRegenBuff'}



class Herald:
    def __init__(self, zip_path, spells=None, package='OfflineHerald', date='2026-11-08', extra=()):
        z = zipfile.ZipFile(zip_path)
        self.items = [i for i in json.loads(z.read('static_objects.json').decode('utf8', 'replace'))['items']
                      if not i.get('artifact')]
        # Newer records from the online Herald (tools.camelotherald.com/item/view/<id> "Raw Item JSON", same format),
        # as {"items": [...]}; they replace an export record of the same id.
        for path in extra:
            newer = {i['id']: dict(i, _online=True) for i in json.load(open(path, encoding='utf-8'))['items']}
            self.items = [i for i in self.items if i['id'] not in newer] + list(newer.values())
        self.META = json.loads(z.read('daoc_item_metadata.json').decode('utf8', 'replace'))
        self.OFF = self.items
        self.spells = spells if spells is not None else {}
        self.PKG = package
        self.DATE = date
        self.need_spells = {}
        self.extra_rows = {}   # 800000 + id -> {'spells': [sub rows], 'custom': [MultipleSubSpells ids]}

    def bonus_lines(self, item):
        """Official bonuses -> ordered [(eProperty, value)], merging repeats; unknown codes reported."""
        out, unknown = collections.OrderedDict(), []
        for b in item.get('bonuses') or []:
            t, v, sid = b['type'], b['value'], b.get('id')
            sub = self.META['bonus_types'].get(str(t), {}).get('sub_types', {}).get(str(sid))
            p = (STAT.get(sid) if t in (1, 75) else CAP.get(sid) if t == 28 else SKILL.get(sub) if t == 2 else
                 RESN.get(sub) if t in (5, 68) else FOCUS.get(sub) if t == 6 else ART.get(sid) if t == 35 else
                 MYTH_CAP.get(sid) if t == 64 else RESCAP.get(sub) if t == 57 else MYTH_FLAT.get(t) if t in MYTH_FLAT
                 else FLAT.get(t))
            if p:
                out[p] = out.get(p, 0) + v
            else:
                unknown.append(b)
        return list(out.items()), unknown

    @staticmethod
    def official_type(i):
        return official_type(i)

    HEADER = re.compile(r'^(Secondary )?(Charged Magic Ability|Charged Magic Item|Magical Ability|Offensive Proc Ability|'
                        r'Defensive Proc Ability):\s*$')
    BLOCK_END = re.compile(r'^(Damage Modifiers|Armor Modifiers|Can use item every|Cannot be|Bonus Level)')

    def ability_block(self, item, spell):
        """The delve block of this ability: a "Secondary ..." header is position 2, the other position 1.
        (Before 2026-11-08 blocks were split on "...Ability:" only, so after a "Secondary Charged Magic Item:" header
        position 2 read position 1's text, e.g. official 3465 "cures disease and poison" read as Boon of Kings.)"""
        blocks, current = [], None
        for line in item['delve_text'].replace('\r', '').split('\n'):
            m = self.HEADER.match(line.strip())
            if m:
                current = [2 if m.group(1) else 1, []]
                blocks.append(current)
            elif current is not None:
                if self.BLOCK_END.match(line.strip()):
                    current = None
                else:
                    current[1].append(line)
        pos = next((a.get('position', 1) for a in item.get('abilities') or [] if a['spell'] == spell), 1)
        for p, body in blocks:
            if p == pos:
                return '\n'.join(body)
        return '\n'.join(blocks[0][1]) if blocks else ''

    def seconds(self, s):
        m = re.match(r'\s*(\d+):(\d+) min', s)
        if m:
            return int(m.group(1)) * 60 + int(m.group(2))
        m = re.match(r'\s*([\d.]+) sec', s)
        return int(float(m.group(1))) if m else 0

    def parse_spell(self, sid):
        holders = [i for i in self.OFF for a in (i.get('abilities') or []) if a['spell'] == sid]
        holders.sort(key=lambda i: len(i.get('abilities') or []))
        blk = self.ability_block(holders[0], sid) if holders else ''
        f, text, sub, sub_text = {}, [], {}, []
        target, target_text = f, text
        for line in blk.split('\n'):
            # "(Function: omni heal)" and "(Cures both Disease and Poison from the user.)" are short forms
            line = line.strip().lstrip('(').rstrip(')').strip()
            if line == 'Sub-spell:':
                # a proc's second spell has its own fields (2026-11-08: they used to fill the proc's gaps)
                target, target_text = sub, sub_text
                continue
            m = re.match(r'^([A-Za-z/ -]+):\s*(.+)$', line)
            if m and m.group(1) not in target:
                target[m.group(1).strip()] = m.group(2).strip()
            elif line and not line.startswith('-'):
                target_text.append(line)
        f['_text'] = ' '.join(text)
        if sub:
            sub['_text'] = ' '.join(sub_text)
            f['_sub'] = sub
        return f, self.META['abilities']['spell'].get(str(sid), '') or f['_text']

    def build_spell(self, sid):
        """A Spell row for official spell sid as 800000+sid, or (None, reason)."""
        f, desc = self.parse_spell(sid)
        fn = f.get('Function', '').lower() or ('disorient' if f.get('Effect') == 'Disoriented' else '')
        num = lambda k: float(re.match(r'[\d.]+', f[k]).group(0)) if k in f and re.match(r'[\d.]+', f[k]) else 0
        target = 'Self' if f.get('Target', 'Self') == 'Self' else 'Enemy'
        row = {'Target': target, 'Range': int(num('Range')) if target == 'Enemy' else 0, 'Duration': self.seconds(f.get('Duration', '')),
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
        elif fn == 'skill buff' and f.get('Skill raised', '').strip() in ('Flexible', 'Stealth'):
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
        # Added 2026-11-08 for the Bountycraft products: the numbers are in the official description, the same way
        # the world's existing rows of these types were built (e.g. 808762 Shield Debuff, 802340 Melee Absorption
        # Debuff, 810150 Speed Decrease, 802097 accuracy, 806982 to-hit penalty, 32182 Scepter's Boon).
        elif fn == 'shield debuff' and re.search(r'armor factor \(AF\) by (\d+)', desc):
            typ, row['Value'] = 'ArmorFactorDebuff', float(re.search(r'armor factor \(AF\) by (\d+)', desc).group(1))
        elif fn == 'melee absorption debuff' and re.search(r'\(ABS\) by (\d+)%', desc):
            typ, row['Value'] = 'ArmorAbsorptionDebuff', float(re.search(r'\(ABS\) by (\d+)%', desc).group(1))
        elif fn == 'melee absorption' and re.search(r'absorption by (\d+)', desc):
            typ, row['Value'] = 'ArmorAbsorptionBuff', float(re.search(r'absorption by (\d+)', desc).group(1))
        elif fn == 'speed decrease' and re.search(r'slowed by (\d+)%', desc):
            typ, row['Value'] = 'SpeedDecrease', float(re.search(r'slowed by (\d+)%', desc).group(1))
        elif fn == 'decrease casting speed' and re.search(r'cast speed by (\d+)%', desc):
            typ, row['Value'] = 'CastingSpeedDebuff', float(re.search(r'cast speed by (\d+)%', desc).group(1))
        elif fn == 'combat penalty' and re.search(r'(\d+)% penalty to their melee', desc):
            typ, row['Value'] = 'ToHitDebuff', float(re.search(r'(\d+)% penalty to their melee', desc).group(1))
        elif fn == 'boon of kings' and re.search(r'(\d+)% bonus to armor and health', desc):
            # negative Value = percent, as in the world's Scepter's Boon (32182)
            typ, row['Value'] = 'AfHitsBuff', -float(re.search(r'(\d+)% bonus to armor and health', desc).group(1))
            row['Target'] = 'Self'
        elif re.search(r'(\d+)% Accuracy bonus', desc):
            typ, row['Value'] = 'ToHitBuff', float(re.search(r'(\d+)% Accuracy bonus', desc).group(1))
            row['Target'], row['Range'] = 'Self', 0
            row['Duration'] = row['Duration'] or self.seconds(re.search(r'(\d+) sec', desc).group(0) if re.search(r'(\d+) sec', desc) else '') or 30
        else:
            typ = self.rules_item_spells(fn, f, desc, row, num)
            if typ is None:
                return None, f'no rule for "{fn or {k: v for k, v in f.items() if k != "_text"}}"'
        if typ in ('ArmorFactorDebuff', 'ArmorAbsorptionDebuff', 'SpeedDecrease', 'CastingSpeedDebuff', 'ToHitDebuff', 'AfHitsBuff',
                   'ArmorAbsorptionBuff') and not row['Duration']:
            m = re.search(r'for (\d+:\d+ min|[\d.]+ sec)', desc)
            row['Duration'] = self.seconds(m.group(1)) if m else 30
        if not typ:
            return None, f'no spell type for {f.get("Resist decreased") or f.get("Skill raised")}'
        # Visuals and messages come from a row of the same type, or of a similar one for types no row uses yet.
        look = {'FlexibleSkillBuff': 'StrengthConstitutionBuff', 'StealthSkillBuff': 'StrengthConstitutionBuff',
                'HealAllOverTime': 'HealOverTime', 'SpecSkillBuff': 'StrengthConstitutionBuff',
                'DmgReductionAndPowerReturn': 'AblativeArmor'}.get(typ, typ)
        donor = next((s for k, s in sorted(self.spells.items()) if 800000 <= k < 820000 and s['Type'] in (typ, look)), None) or \
            next((s for k, s in sorted(self.spells.items()) if s['Type'] == typ and s['Target'] == target), None) or \
            next((s for k, s in sorted(self.spells.items()) if s['Type'] == typ), None) or             next((s for k, s in sorted(self.spells.items()) if s['Type'] == look), None)
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
        subs, multi = row.pop('_subs', []), row.pop('_multi', False)
        proc, params = row.pop('_proc', False), row.pop('_params', [])
        new.update(row)
        new.update({'SpellID': 800000 + sid, 'Name': f'{title} ({sid})', 'Description': desc or title, 'Type': typ,
                    'PackageID': self.PKG, 'Spell_ID': f'offline-toa-item-{800000 + sid}', 'TooltipId': 0,
                    'LastTimeRowUpdated': f'{self.DATE} 00:00:00'})
        if typ == 'HealAllOverTime':   # the export's summary line says 5%; the item delve says 3% every 5 seconds
            new['Description'] = f"You regenerate {row['Value']:g}% health, endurance and power every {row['Frequency'] / 10:g} sec."
        # Second parts of one official spell (Arrogance's melee immunity, omni heal's power and endurance) are sub
        # spells 850000 + id and 860000 + id, cast with the main one (SubSpellID, or MultipleSubSpells for two).
        extra = {'spells': [], 'custom': []}
        for k, (sub_typ, sub_row) in enumerate(subs):
            sub_donor = next((s for _, s in sorted(self.spells.items()) if s['Type'] == sub_typ), None)
            if not sub_donor:
                return None, f'no spell row of type {sub_typ} to copy for the second part'
            sub = {c: sub_donor[c] for c in sub_donor if c != 'LastTimeRowUpdated'}
            for c in ('Damage', 'Value', 'Duration', 'Frequency', 'Radius', 'Pulse', 'Power', 'RecastDelay', 'SubSpellID',
                      'SharedTimerGroup', 'Concentration', 'SpellGroup', 'EffectGroup'):
                sub[c] = 0 if not isinstance(sub_donor[c], float) else 0.0
            sub_id = (850000 if k == 0 else 860000) + sid
            sub.update({c: new[c] for c in ('Target', 'Range', 'Duration', 'CastTime')})
            sub.update(sub_row)
            sub.update({'SpellID': sub_id, 'Name': f'{title} ({sid}, part {k + 2})', 'Description': desc or title,
                        'Type': sub_typ, 'PackageID': self.PKG, 'Spell_ID': f'offline-item-{sub_id}', 'TooltipId': 0,
                        'LastTimeRowUpdated': f'{self.DATE} 00:00:00'})
            extra['spells'].append(sub)
        if proc:
            # OffensiveProc / DefensiveProc cast the spell whose id is their Value
            new['Value'] = float(extra['spells'][0]['SpellID'])
        elif len(subs) == 1:
            new['SubSpellID'] = extra['spells'][0]['SpellID']
        elif subs:
            extra['custom'] = [s['SpellID'] for s in extra['spells']]
        extra['params'] = list(params)
        self.extra_rows[800000 + sid] = extra
        return new, donor['SpellID']

    def rules_item_spells(self, fn, f, desc, row, num):
        """Rules added 2026-11-08 for the Bountycraft products' item spells. Each type is one the world already uses for
        the same live function (examples named), with the official numbers. Returns the spell type or None."""
        text = desc + ' ' + f.get('_text', '')
        pct = lambda *keys: next((num(k) for k in keys if num(k)), 0.0)
        found = lambda pattern: float(re.search(pattern, text).group(1)) if re.search(pattern, text) else 0.0
        target = f.get('Target', '').strip()
        if target == 'Point-Blank Area':
            row.update({'Target': 'Enemy', 'Range': 0, 'Radius': int(num('Radius'))})
        elif target == 'Group':
            row.update({'Target': 'Group', 'Range': int(num('Range')) or 1500})
        if fn == 'skill buff':
            # "Increases your Sword by 10%": the description names the skill; the delve's "Skill raised" can show the
            # weapon's own skill (3411 says Slash, the spell raises Dual Wield). SpecSkillBuff + BuffProperty.
            m = re.search(r'Increases your ([A-Za-z ]+?) by (\d+)%', desc)
            skill = (m.group(1) if m else f.get('Skill raised', '')).strip()
            prop = SKILL.get(skill) or SKILL.get(skill.replace(' ', ''))
            value = pct('Bonus') or (float(m.group(2)) if m else 0)
            if not prop or not value:
                return None
            row.update({'Value': value, 'Target': 'Self', 'Range': 0,
                        'Message1': f'Your {skill.lower()} skill improves!', 'Message2': '{0} looks more skilled.',
                        'Message3': f'Your {skill.lower()} skill returns to normal.', 'Message4': "{0}'s skill returns to normal."})
            row['_params'] = [('BuffProperty', prop)]
            return 'SpecSkillBuff'
        if fn in ('offensive proc', 'defensive proc') and f.get('_sub'):
            # OffensiveProc / DefensiveProc: Value = the spell cast, Frequency = chance x 100 (826080 Harpy Talons)
            sf = f['_sub']
            snum = lambda k: float(re.match(r'[\d.]+', sf[k]).group(0)) if k in sf and re.match(r'[\d.]+', sf[k]) else 0
            sub = {'Duration': self.seconds(sf.get('Duration', '')), 'CastTime': 0.0}
            if sf.get('Effect') == 'Disarmed':
                sub_typ = 'Disarm'
                sub.update({'Target': 'Enemy', 'Range': int(snum('Range')) or 250})
            elif sf.get('Function', '').lower() == 'resistance decrease' and sf.get('Resist decreased', '').strip() in DT:
                kind = sf['Resist decreased'].strip()
                sub_typ = f'{kind}ResistDebuff'
                sub.update({'Value': snum('Base value of spell'), 'Target': 'Enemy', 'Range': int(snum('Range')) or 1500,
                            'DamageType': DT[kind]})
            elif 'temporarily absorb' in sf.get('_text', '') and snum('Absorption') and snum('Value'):
                sub_typ = 'AblativeArmor'
                sub.update({'Damage': snum('Absorption'), 'Value': snum('Value'), 'Target': 'Self', 'Range': 0})
            else:
                return None
            chance = found(r'with a (\d+)% chance') or 10
            row.update({'Frequency': int(chance * 100), 'Target': 'Self', 'Range': 0})
            row['_subs'] = [(sub_typ, sub)]
            row['_proc'] = True
            return 'OffensiveProc' if fn == 'offensive proc' else 'DefensiveProc'
        if f.get('Effect') == 'Stealth Lore' and num('Stealth bonus'):
            row.update({'Value': num('Stealth bonus'), 'Target': 'Self', 'Range': 0,
                        'Message1': 'Your senses sharpen and your steps grow quieter.', 'Message2': '{0} looks more watchful.',
                        'Message3': 'Your senses return to normal.', 'Message4': "{0}'s watchfulness fades."})
            return 'StealthSkillBuff'
        if fn == 'bedazzled' and num('Chance to miscast') and not num('Chance to fumble'):
            # no miscast handler: the world's own Bedazzled (826042) is a CastingSpeedDebuff of the same strength
            row['Value'] = num('Chance to miscast')
            return 'CastingSpeedDebuff'
        if fn.startswith('arrogance'):
            # 826061 Arrogance: ArtifactAllStats + ArmorAbsorptionBuff (826060) for the melee immunity
            stats = pct('Bonus to all stats') or found(r'a (\d+) bonus to all stats')
            immunity = pct('Immunity to melee') or found(r'(\d+)% immunity to melee')
            if not stats or not immunity:
                return None
            row.update({'Value': stats, 'Target': 'Self', 'Range': 0,
                        'Duration': row['Duration'] or self.seconds(re.search(r'(\d+) sec', text).group(0)) or 30})
            row['_subs'] = [('ArmorAbsorptionBuff', {'Value': immunity})]
            return 'ArtifactAllStats'
        if fn == 'omni heal' or re.search(r'Heals the target for (\d+)% health, power and fatigue', text):
            # no OmniHeal handler: a percent Heal with PowerHeal and EnduranceHeal of the same percent
            value = pct('Value') or found(r'for (\d+)% health, power and fatigue')
            if not value:
                return None
            row.update({'Value': -value, 'Target': 'Self', 'Range': 0})
            row['_subs'] = [('PowerHeal', {'Value': -value}), ('EnduranceHeal', {'Value': -value})]
            return 'Heal'
        if 'Cures both Disease and Poison' in text:
            # 826085 Purify: CurePoison with a CureDisease sub spell
            row.update({'Target': 'Self', 'Range': 0, 'Duration': 0})
            row['_subs'] = [('CureDisease', {})]
            return 'CurePoison'
        if fn == 'damage conversion':
            row['Value'] = pct('Value')        # 810340 Damage Conversion
            return 'Conversion'
        if fn == 'mesmerize duration reduction':
            row['Value'] = pct('Value')
            return 'MesmerizeDurationBuff'
        if fn == 'heal bonus':
            row['Value'] = abs(pct('Modifier') or float(re.sub(r'[^\d.]', '', f.get('Modifier', '0')) or 0))
            return 'HealingDoneBuff'
        if fn == 'melee damage boost':
            row['Value'] = pct('Value')
            return 'MeleeDamageBuff'
        if fn == 'damage to power':
            # DamageReductionAndPowerReturnSpellHandler: Damage = percent absorbed, given back as power
            row.update({'Damage': pct('Blocked'), 'Value': pct('Power given')})
            return 'DmgReductionAndPowerReturn'
        if re.search(r'penalizes the casting time of enemy casters by (\d+)% for (\d+) seconds', text):
            m = re.search(r'penalizes the casting time of enemy casters by (\d+)% for (\d+) seconds', text)
            row.update({'Value': float(m.group(1)), 'Duration': int(m.group(2)), 'Target': 'Enemy', 'Range': 1500})
            return 'CastingSpeedDebuff'
        if fn == 'speed decrease' and pct('Value') and row['Target'] == 'Enemy':
            row['Value'] = pct('Value')
            return 'SpeedDecrease'
        if fn == 'illusion' and num('Melee to-hit penalty'):
            row['Value'] = num('Melee to-hit penalty')     # the shape change itself is left out
            return 'ToHitDebuff'
        if fn == 'resistance enhancement' and num('All magic resistances'):
            row.update({'Value': num('All magic resistances'), 'Target': 'Self', 'Range': 0})
            return 'AllMagicResistsBuff'
        if fn == 'resistance enhancement' and num('Resist crush/slash/thrust'):
            row.update({'Value': num('Resist crush/slash/thrust'), 'Target': 'Self', 'Range': 0})
            return 'CrushSlashThrustBuff'
        one = next((k for k in f if re.match(r'^Resist (Body|Cold|Energy|Heat|Matter|Spirit)$', k)), None)
        if fn == 'resistance enhancement' and one and num(one):
            row.update({'Value': num(one), 'Target': 'Self', 'Range': 0})
            return f'{one.split()[1]}ResistBuff'
        if fn == 'power regen' and num('Value'):
            row.update({'Value': num('Value'), 'Target': 'Self', 'Range': 0})
            return 'PowerRegenBuff'
        if fn == 'bedazzled' and num('Chance to fumble'):
            row['Value'] = num('Chance to fumble')         # 826043 Bedazzled
            return 'FumbleChanceDebuff'
        if fn == 'mesmerization feedback' and 'immune to the next mesmerize' in text:
            row['Value'] = 20                              # as the world's 808711 / 809443 Mesmerization Feedback
            return 'CCResist'
        if fn == 'style damage shield':
            row.update({'Value': pct('Value'), 'Target': 'Self', 'Range': 0})
            return 'StyleAbsorbBuff'
        if fn == 'disease':
            return 'Disease'
        if fn == 'wave of healing' and pct('Value'):
            row.update({'Value': -pct('Value'), 'Target': 'Realm', 'Range': 0, 'Radius': int(num('Radius'))})
            return 'Heal'
        if fn == 'raise dead':
            row.update({'Target': 'Corpse', 'Range': 1500, 'Radius': int(num('Radius')),
                        'ResurrectHealth': int(found(r'restores (\d+)%') or 10), 'ResurrectMana': 0, 'Duration': 0})
            return 'Resurrect'
        if fn == 'efficient healing':
            row['Value'] = pct('Bonus')
            return 'HealingReceivedBuff'
        if fn == 'increase attack speed':
            row.update({'Value': pct('Value'), 'Target': 'Self', 'Range': 0})
            return 'CombatSpeedBuff'
        if fn == 'parry':
            row.update({'Value': pct('Chance to parry'), 'Target': 'Self', 'Range': 0})
            return 'ParryBuff'
        if fn == 'stat decrease' and num('Constitution penalty'):
            row['Value'] = num('Constitution penalty')
            return 'ConstitutionDebuff'
        if fn == 'spreadheal':
            row['Value'] = pct('Value')
            return 'SpreadHeal'
        if fn == 'melee absorption' and pct('Value'):
            row.update({'Value': pct('Value'), 'Target': 'Self', 'Range': 0})
            return 'ArmorAbsorptionBuff'
        if fn == 'attack speed decrease':
            row['Value'] = pct('Value')
            return 'CombatSpeedDebuff'
        if fn == 'boon of kings' and pct('Bonus'):
            row['Value'] = -pct('Bonus')                   # percent, as Scepter's Boon (32182)
            if row['Target'] != 'Group':
                row.update({'Target': 'Self', 'Range': 0})
            return 'AfHitsBuff'
        if 'temporarily absorb' in text and num('Absorption') and num('Value') and not fn.endswith(' proc'):
            row.update({'Damage': num('Absorption'), 'Value': num('Value'), 'Target': 'Self', 'Range': 0})
            return 'AblativeArmor'
        if fn == 'mesmerize':
            return 'Mesmerize'
        if f.get('Effect') == 'Lore Debuff' and pct('Spell damage/resists'):
            row.update({'Value': pct('Spell damage/resists'), 'Target': 'Enemy', 'Range': 0, 'Radius': int(num('Radius'))})
            return 'LoreDebuff'
        return None

    def ability_set(self, i):
        """ItemTemplate ability columns from the official abilities, and self.spells needed."""
        s = {'SpellID': 0, 'Charges': 0, 'MaxCharges': 0, 'SpellID1': 0, 'Charges1': 0, 'MaxCharges1': 0,
             'ProcSpellID': 0, 'ProcSpellID1': 0, 'CanUseEvery': 0}
        notes = []
        for a in i.get('abilities') or []:
            sid, pos, mt = a['spell'], a.get('position', 1), a.get('magic_type')
            self.need_spells.setdefault(sid, []).append(i['name'])
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

    def item_set(self, i):
        lines, unknown = self.bonus_lines(i)
        s = {}
        for n in range(1, 11):
            p, v = lines[n - 1] if n <= len(lines) else (0, 0)
            s[f'Bonus{n}Type'], s[f'Bonus{n}'] = p, v
        s['ExtraBonusType'], s['ExtraBonus'] = 0, 0
        ab, notes = self.ability_set(i)
        s.update(ab)
        s['IsTradable'] = 0 if i['flags'].get('no_trade') else 1
        s['IsDropable'] = 0 if i['flags'].get('no_drop') else 1
        if i.get('bonus_level') is not None:
            s['BonusLevel'] = i['bonus_level']
        if unknown:
            notes.append(f'unknown bonus codes {unknown}')
        return s, notes
