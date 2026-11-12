"""Live (1.130a) spells missing from the classic hybrid spell lines, added beside the existing ranks.

Classes: Thane (pilot), Skald, Champion, Reaver, Minstrel, Paladin. Values are live's: the levels, values, durations,
ranges, radii, power and timers of daoc.gg's 1.130a data (a 1.123b client extraction with the dated patches applied).
Units: direct damage and lifedrain are stored in tenths (live 125 = 12.5); live's 1000 cast timer is this server's
3.0 s for buffs and debuffs (the existing ranks), and heals use the healer calibration (x0.0046); negative live power
costs are percentages, as here. The live client art IDs don't match this client's catalog, so each family borrows the
effect and icon of an existing local spell of the same kind (ART names it). Every new family gets its own SpellGroup:
the hybrid spell list shows only the best rank per group or per spell kind, and a new family must not hide, or be
hidden by, an older local family of the same kind. Only families an existing handler supports are added; the rest
are listed in SKIPPED with the reason. RENAMES corrects local misspellings of live spells instead of adding duplicates.
"""
import sqlite3

PACKAGE = 'local-hybrid-live-v1'
FIRST, LAST = 74000, 74899  # tooltips 22100-22999, all free below the Death Knight's 23200
TOOLTIP_BASE = 22100  # The client reads tooltip IDs as signed 16-bit: keep every one at or below 32767.

# (line, level, name, fields). Cast times: live's 1000 cast timer is this server's 3.0 s, as on the existing ranks.
THANE = [
    # AoE energy resist debuffs at the target (live DEBUFFENERGYAE).
    *[('Stormcalling', lv, name, dict(Type='EnergyResistDebuff', Target='Enemy', Value=value, Duration=10, Radius=radius,
        Range=1500, Power=power, CastTime=3.0, DamageType=12, EffectGroup=74000, ClientEffect=art, Icon=art))
      for lv, name, value, radius, power, art in [(14, 'Dissipate Energy', 6, 350, 10, 2637), (20, 'Rout Energy', 12, 350, 13, 2638),
                                                  (33, 'Sunder Energy', 18, 350, 22, 2639), (43, 'Banish Energy', 30, 400, 29, 2639)]],
    # Instant acuity debuffs with a shared 20 s timer (live DEBUFFACU).
    *[('Stormcalling', lv, name, dict(Type='AcuityDebuff', Target='Enemy', Value=value, Duration=45, Range=1000, Power=power,
        CastTime=0, RecastDelay=20, DamageType=12, EffectGroup=74010, SharedTimerGroup=74010, ClientEffect=art, Icon=art))
      for lv, name, value, power, art in [(8, 'Bewildering', 21, 5, 2621), (16, 'Major Bewildering', 33, 11, 2622),
                                          (24, 'Greater Bewildering', 43, 16, 2623), (33, 'Superior Bewildering', 58, 22, 2624),
                                          (45, 'Ultimate Bewildering', 69, 29, 2625)]],
    # Pulsing group endurance-cost chants, like the Paladin's chants: 6 s effect renewed every 5 s (live FATIGUEREDUCTION).
    *[('Stormcalling', lv, name, dict(Type='FatigueConsumptionBuff', Target='Group', Value=value, Duration=6, Range=1500,
        Pulse=1, Frequency=50, Power=0, CastTime=0, RecastDelay=8, EffectGroup=74020, SharedTimerGroup=74020, ClientEffect=art, Icon=art))
      for lv, name, value, art in [(11, "Thor's Energy", 10, 1712), (21, "Thor's Stamina", 15, 1713),
                                   (31, "Thor's Persistence", 20, 1714), (41, "Thor's Tenacity", 25, 1715)]],
    # Self resist-piercing buffs (live PIERCE).
    *[('Stormcalling', lv, name, dict(Type='ResiPierceBuff', Target='Self', Value=value, Duration=1200, Range=0, Power=power,
        CastTime=3.0, EffectGroup=74030, ClientEffect=art, Icon=art))
      for lv, name, value, power, art in [(10, "Mjollnir's Precision", 1, 5, 3531), (20, "Mjollnir's Accuracy", 3, 10, 3532),
                                          (30, "Mjollnir's Clarity", 5, 15, 3533), (40, "Mjollnir's Acuity", 7, 20, 3535),
                                          (50, "Mjollnir's Sagacity", 10, 25, 3537)]],
]


def family(line, group, art, ranks, **common):
    """One spell family: ranks are (level, name, fields); ART names the local spell(s) whose effect/icon to borrow."""
    arts = art if isinstance(art, list) else [art] * len(ranks)
    return [(line, lv, name, dict(common, **fields, EffectGroup=group, SpellGroup=group, ART=arts[min(i, len(arts) - 1)]))
            for i, (lv, name, fields) in enumerate(ranks)]


def chants(line, group, art, ranks, frequency):
    """Pulsing group endurance-cost chants (live FATIGUEREDUCTION)."""
    return family(line, group, art, [(lv, name, dict(Value=v)) for lv, name, v in ranks], Type='FatigueConsumptionBuff',
                  Target='Group', Duration=6, Range=1500, Pulse=1, Frequency=frequency, Power=0, CastTime=0, RecastDelay=8,
                  SharedTimerGroup=group)


def acuity(line, group, ranks, recast, damage_type, radius_top=0):
    """Instant acuity debuffs (live DEBUFFACU); ranks are (level, name, value, power)."""
    rows = [(lv, name, dict(Value=v, Power=p, Radius=radius_top if i == len(ranks) - 1 else 0)) for i, (lv, name, v, p) in enumerate(ranks)]
    return family(line, group, ['Cover Eyes', 'Dull Sight', 'Obscure View', 'Darken Vision', 'Ban Sight'], rows, Type='AcuityDebuff',
                  Target='Enemy', Duration=45, Range=1000, CastTime=0, RecastDelay=recast, DamageType=damage_type, SharedTimerGroup=group)


def water(line, group):
    """Group water breathing with swim speed (live WATTERBUFF): 30 minutes."""
    return family(line, group, 'Simple Song of Travel', [(lv, name, dict(Value=v, Power=p)) for lv, name, v, p in
                  [(10, "Neriad's Call", 70, 10), (20, "Neriad's Breath", 80, 15), (30, "Neriad's Blessing", 90, 20),
                   (40, 'Breath of Leviathan', 100, 25)]], Type='WaterBreathing', Target='Group', Duration=1800, Range=1500, CastTime=3.0)


SKALD = [
    *chants('Battlesongs', 74100, "Saint's Energy", [(2, "Bragi's Bellow", 5), (12, "Bragi's Energy", 10), (22, "Bragi's Stamina", 15),
                                                     (32, "Bragi's Persistence", 20), (42, "Bragi's Tenacity", 25)], 60),
    *water('Battlesongs', 74110),
    # Body, spirit and energy resist chants, pulsing like the older single-resist songs they replace on live.
    *family('Battlesongs', 74120, ['Spirit Bolstering Chant', 'Spirit Bolstering Song'],
            [(27, 'Soul Bolstering Chant', dict(Value=8)), (46, 'Soul Bolstering Song', dict(Value=12))],
            Type='BodySpiritEnergyBuff', Target='Group', Duration=5, Frequency=50, Pulse=1, Range=1500, CastTime=0, RecastDelay=8),
]

CHAMPION = [
    *chants('Valor', 74200, ["Saint's Energy", "Saint's Energy", "Saint's Stamina", "Saint's Persistence", "Saint's Tenacity"],
            [(2, 'Chant of Reduction', 5), (12, 'Chant of Energy', 10), (22, 'Chant of Stamina', 15), (32, 'Chant of Persistence', 20),
             (42, 'Chant of Tenacity', 25)], 50),
    *acuity('Valor', 74210, [(5, 'Dazing', 21, 5), (15, 'Major Dazing', 33, 11), (25, 'Greater Dazing', 43, 16),
                             (35, 'Superior Dazing', 58, 22), (45, 'Ultimate Dazing', 69, 29)], 15, 10, radius_top=350),
    *family('Valor', 74220, 'Slowness', [(1, 'Decelerate', dict(Value=75, Duration=51, Power=8))],
            Type='SpeedDecrease', Target='Enemy', Range=1500, CastTime=0, RecastDelay=20, DamageType=15),
]

REAVER = [
    *acuity('Soulrending', 74300, [(6, "Arawn's Minor Curse", 21, 5), (14, "Arawn's Lesser Curse", 33, 11), (24, "Arawn's Curse", 43, 16),
                                   (35, "Arawn's Major Curse", 58, 22), (46, "Arawn's Greater Curse", 69, 29)], 20, 15),
    # Single-target absorption debuff shouts (the Auras of Foreboding are their area chants).
    *family('Soulrending', 74310, ['Aura of Foreboding', 'Aura of Destiny', 'Aura of Fate', 'Aura of Death', 'Aura of the Inevitable'],
            [(lv, name, dict(Value=v, Power=p)) for lv, name, v, p in [(6, 'Shout of Darkness', 7, 8), (16, 'Shout of Destiny', 9, 13),
             (26, 'Shout of Fate', 11, 18), (36, 'Shout of Death', 13, 23), (46, 'Shout of the Inevitable', 15, 28)]],
            Type='ArmorAbsorptionDebuff', Target='Enemy', Duration=20, Range=1500, CastTime=0, RecastDelay=8, DamageType=15),
    *family('Soulrending', 74320, 'Slowness',
            [(lv, name, dict(Duration=d, Power=p)) for lv, name, d, p in [(11, 'Curse of Slowness', 49, 7), (17, 'Curse of Languishing', 59, 10),
             (22, 'Curse of Lumbering', 68, 13), (33, 'Curse of Sluggishness', 89, 21), (43, 'Curse of Crawling', 108, 29)]],
            Type='SpeedDecrease', Target='Enemy', Value=60, Range=1500, CastTime=0, RecastDelay=20, DamageType=15),
    # Instant spirit strikes (live replaced the Spirit Wrench damage-over-time line with these).
    *family('Soulrending', 74330, ['Spirit Wrench', 'Spirit Rot', 'Spirit Decay'],
            [(1, 'Soul Wench', dict(Damage=20.0, Power=3)), (12, 'Soul Rot', dict(Damage=28.1, Power=8)), (18, 'Soul Decay', dict(Damage=40.0, Power=11))],
            Type='DirectDamage', Target='Enemy', Range=1000, CastTime=0, RecastDelay=15, DamageType=15),
    *family('Soulrending', 74340, 'Murder Aura', [(41, 'Circle of Despair', dict(Damage=25.0, Power=-15))],
            Type='DirectDamage', Target='Enemy', Range=0, Radius=750, CastTime=0, RecastDelay=60, DamageType=15),
    # Instant lifedrains beside the Siphons; the return matches the Siphons'.
    *family('Soulrending', 74350, ['Vigor Siphon', 'Health Siphon', 'Vitality Siphon', 'Spirit Siphon', 'Soul Siphon'],
            [(lv, name, dict(Damage=d, Power=p)) for lv, name, d, p in [(3, 'Vigor Drain', 12.5, 3), (12, 'Health Drain', 35.5, 8),
             (21, 'Vitality Drain', 55.0, 13), (31, 'Spirit Drain', 81.5, 23), (45, 'Soul Drain', 109.0, 30)]],
            Type='Lifedrain', Target='Enemy', Value=-70, LifeDrainReturn=60, Range=1500, CastTime=0, RecastDelay=20, DamageType=15),
]

MINSTREL = [
    *water('Instruments', 74400),
]

PALADIN = [
    # Instant group heals, 60 s shared reuse, percentage power (live "Holy" chants).
    *family('Chants', 74500, ['Minor Refreshment', 'Lesser Refreshment', 'Major Refreshment', "Crusader's Refreshment", "Angel's Refreshment",
                              "Archangel's Refreshment"],
            [(lv, name, dict(Value=v, Power=p)) for lv, name, v, p in [(2, 'Holy Recovery', 25, -5), (5, 'Holy Renewal', 50, -5),
             (12, 'Holy Refreshment', 100, -10), (20, 'Holy Restoration', 180, -20), (35, 'Holy Rejuvenation', 250, -25),
             (45, 'Holy Revitalization', 300, -35)]],
            Type='Heal', Target='Group', Range=1500, CastTime=0, RecastDelay=60, SharedTimerGroup=74500),
    # Single-target heals on any realm member (live "Major" heals).
    *family('Chants', 74510, 'Minor Refreshment',
            [(lv, name, dict(Value=v, Power=p, CastTime=c)) for lv, name, v, p, c in [(5, 'Major Restoration', 30, 5, 5.5),
             (8, 'Major Recuperation', 55, 7, 5.1), (11, 'Major Renewal', 82, 9, 4.6), (14, 'Major Revival', 109, 11, 4.1),
             (18, 'Major Resuscitation', 137, 14, 3.7), (25, 'Major Reviction', 172, 19, 3.2), (33, 'Major Refection', 235, 24, 2.8),
             (43, 'Major Refocillation', 307, 30, 2.3)]],
            Type='Heal', Target='Realm', Range=2000),
    # Fast group heals for a large flat power cost (live "Heaven's" heals).
    *family('Chants', 74520, 'Greater Refreshment',
            [(lv, name, dict(Value=v, Power=p, CastTime=c)) for lv, name, v, p, c in [(15, "Heaven's Commendation", 58, 51, 2.8),
             (26, "Heaven's Benediction", 97, 75, 1.8), (36, "Heaven's Blessing", 163, 96, 0.9), (46, "Heaven's Approbation", 223, 112, 0)]],
            Type='Heal', Target='Group', Range=2000),
    # Self strength and dexterity buffs.
    *family('Chants', 74530, 'Strength of the Champion',
            [(lv, name, dict(Value=v, Power=p)) for lv, name, v, p in [(3, 'Righteous Strength', 16, 3), (7, 'Righteous Potency', 20, 6),
             (14, 'Righteous Power', 26, 11), (20, 'Virtuous Strength', 31, 16), (30, 'Virtuous Potency', 39, 25), (41, 'Virtuous Power', 47, 33),
             (50, 'Virtuous Might', 55, 43)]],
            Type='StrengthBuff', Target='Self', Duration=1200, Range=0, CastTime=3.0),
    *family('Chants', 74540, 'Strength of the Champion',
            [(lv, name, dict(Value=v, Power=p)) for lv, name, v, p in [(4, 'Righteous Dexterity', 12, 3), (13, 'Righteous Deftness', 22, 11),
             (21, 'Righteous Proficiency', 32, 16), (31, 'Virtuous Agility', 42, 25), (47, 'Virtuous Fluidity', 52, 43)]],
            Type='DexterityBuff', Target='Self', Duration=1200, Range=0, CastTime=3.0),
    # Pulsing group damage-absorbing barriers, like the Minstrel's (live ABLATIVECHANT: 30% absorbed up to the amount).
    *family('Chants', 74550, ['Barrier of Sound', 'Shield of Melody', 'Wall of Song'],
            [(30, 'Barrier of Faith', dict(Damage=60)), (40, 'Barrier of Virtue', dict(Damage=90)), (50, 'Barrier of Temperance', dict(Damage=120))],
            Type='AblativeArmor', Target='Group', Value=30, Duration=6, Frequency=60, Pulse=1, Range=1500, CastTime=0, RecastDelay=8, DamageType=10),
    *family('Chants', 74560, 'Raise Fallen', [(35, 'Reincarnate', dict(ResurrectHealth=25, ResurrectMana=25))],
            Type='Resurrect', Target='Corpse', Range=1500, CastTime=0, RecastDelay=300),
]

def siege_songs(line, group, art, names):
    """Group siege-protection songs (1.122B): 10-50% less siege damage, an 8 s effect renewed every 7 s (SiegeDefense)."""
    return family(line, group, art, [(lv, name, dict(Value=v)) for (lv, v), name in zip(((6, 10), (16, 20), (26, 30), (36, 40), (46, 50)), names)],
                  Type='SiegeProtection', Target='Group', Duration=8, Range=1500, Pulse=1, Frequency=70, Power=0, CastTime=0, RecastDelay=5,
                  SharedTimerGroup=group)


# Later additions, kept after the first six classes so their spell IDs stay put (2026-10-25 research pass).
LATER = [
    *siege_songs('Battlesongs', 74130, 'Spirit Bolstering Chant',
                 ['Rhythm of the Siege', 'Accent of the Siege', 'Intonation of the Siege', 'Pulse of the Siege', 'Count of the Siege']),
    # 1.129: the Valor auras are a 2/4/6/8/10% chance for spells and styles to cost no power or endurance, not siege
    # songs (DR-101, world fix open-issues-1108; server CostFreeChance).
    *family('Valor', 74230, "Saint's Energy",
            [(lv, name, dict(Value=v)) for (lv, v), name in zip(((6, 2), (16, 4), (26, 6), (36, 8), (46, 10)),
             ["Champion's Ambience", "Champion's Demeanor", "Champion's Presence", "Champion's Aspect", "Champion's Aura"])],
            Type='CostFreeChance', Target='Group', Duration=8, Range=1500, Pulse=1, Frequency=70, Power=0, CastTime=0, RecastDelay=5,
            SharedTimerGroup=74230),
    *siege_songs('Instruments', 74410, 'Simple Song of Travel',
                 ['Ballad of Albion', 'Chant of Albion', 'Chorus of Albion', 'Song of Albion', 'Anthem of Albion']),
    # Skald wards (1.121): the next mesmerize (1) or magical stun (2) on another realm member is stopped and the ward used up.
    *family('Battlesongs', 74140, 'Spirit Bolstering Chant', [(38, 'Sleep Guard', dict(Value=1, Power=-10))],
            Type='CrowdControlGuard', Target='Realm', Duration=300, Range=1000, CastTime=0, RecastDelay=300),
    *family('Battlesongs', 74150, 'Spirit Bolstering Chant', [(48, 'Stun Guard', dict(Value=2, Power=-25))],
            Type='CrowdControlGuard', Target='Realm', Duration=300, Range=1000, CastTime=0, RecastDelay=300),
]

SKIPPED = {
    'Thane': [
        ('Health of Thor (5 ranks)', "live 'aura' group function; its effect isn't defined in the data"),
        ('Gift of Lightning', "damage-type change on a target: needs a handler"),
        ('Chain Lightning', 'already the Thane RR5 realm ability on this server'),
    ],
    'Skald': [
        ('Siegebreaker', 'siege damage add: needs a handler'),
        ('Crescendo, Great Crescendo, March of Bragi', "live 'combat' speed bursts: their exact effect isn't defined in the data"),
        ('Disorienting Yelp, Disorienting Cry', "live 'combat' confusion: its values aren't defined in the data"),
    ],
    'Champion': [
        ('Stoutness/Girth/Resilience/Vigor/Vitality of Valor', "live 'aura' group function; its effect isn't defined in the data"),
        ('Siegebreaker', 'siege damage add: needs a handler'),
    ],
    'Reaver': [('Malefic Horror', 'cower: needs a handler')],
    'Minstrel': [
        ('Siegebreaker', 'siege damage add: needs a handler'),
        ('Song of Magic/Arcane/Wizardic/Supreme/Sorcerous Reach', "live 'aura' song; its effect isn't defined in the data"),
        ('Song of Inspiring/Rallying/Awakening/Everlasting Fervor', 'pulsing haste songs: pulse timing is not in the data'),
        ('Song of Spell Protection/Warding/Shielding/Degradation/Invulnerability', 'pulsing resist songs: pulse timing is not in the data'),
        ('Engaging/Engrossing/Enthralling Harmony', 'charm: needs the charm pulse and level fields live does not give'),
        ('Crescendo, Great Crescendo', "live 'combat' speed bursts: their exact effect isn't defined in the data"),
    ],
    'Paladin': [
        ('Cure of the Righteous/Virtuous/Faithful', 'area cure: which effects it removes is not in the data'),
    ],
}

# Local misspellings of live spells: (line, local name, live name, live field values in this server's units).
RENAMES = [
    ('Instruments', 'Dischordant Note', 'Discordant Note', dict(Damage=9.0, Range=1000)),
    ('Instruments', 'Dischordant Tone', 'Discordant Tone', dict(Damage=26.0, Range=1000)),
    ('Instruments', 'Dischordant Wail', 'Discordant Wail', dict(Damage=58.0, Range=1000)),
    ('Instruments', 'Dischordant Shriek', 'Discordant Shriek', dict(Damage=115.0, Range=1000)),
    ('Instruments', 'Dischordant Howl', 'Discordant Howl', dict(Damage=139.0, Range=1000)),
    ('Soulrending', 'Annullling Aura', 'Annulling Aura', dict(Radius=400)),
    ('Battlesongs', 'Compel Captulation', 'Compel Capitulation', dict(Value=60, RecastDelay=20)),
    ('Chants', 'Aura of Indestructability', 'Aura of Indestructibility', dict()),
]

CLASSES = {'Thane': THANE, 'Skald': SKALD, 'Champion': CHAMPION, 'Reaver': REAVER, 'Minstrel': MINSTREL, 'Paladin': PALADIN,
           'Later additions': LATER}

# Live class abilities missing or unreachable here: (key, icon or None, internal ID or None, description or None,
# [(career spec, level)]). Only the listed careers' grants of the ability are replaced.
# Call of a Thousand Storms was in the data already, granted at ThaneCareer 51 (unreachable) with no handler.
TANKS = ('ArmsmanCareer', 'HeroCareer', 'WarriorCareer')
STEALTHERS = ('HunterCareer', 'InfiltratorCareer', 'NightshadeCareer', 'RangerCareer', 'ScoutCareer', 'ShadowbladeCareer')
# (key, icon|None, internal|None, description|None, grants). A grant is (spec, level[, ability level[, class]]); only those
# specs' grants (for that class, or class 0) are replaced, so other classes keep theirs. Live levels are 1.130a's.
ABILITIES = [
    ('Call of a thousand storms', 3066, 3150,
     'Adds 8 to the number of attackers you can face at once, against monsters of level 50 and up. '
     'Self only, 2 minute duration, 3 minute reuse.',
     [('ThaneCareer', 40, 1), ('ValkyrieCareer', 40, 1)]),
    ('Intercept', None, None, None, [('ReaverCareer', 25), ('MaulerCareer', 12)]),
    ('Stoicism', None, None, None, [('ReaverCareer', 35), ('MaulerCareer', 35)]),
    # The Armsman, Hero and Warrior shouts and level 41 passives.
    ('Taunting Shout', None, None, None, [(c, 5) for c in TANKS]),
    ('Metal Guard', None, None, None, [(c, 15) for c in TANKS]),
    ('Bolstering Roar', None, None, None, [(c, 30) for c in TANKS]),
    ('Rampage', None, None, 'A group shout: a 100% chance to resist debuff spells for 20 seconds. Re-usable every 5 minutes.',
     [(c, 40) for c in TANKS]),
    ('Fury', None, None, 'A self-only shout: a 45% bonus to all magic resistances and a 45% chance to shrug off crowd control spells, '
     'for 20 seconds. Re-usable every 15 minutes.', [(c, 50) for c in TANKS]),
    ('Siege Resist', None, None, 'Passive: siege weapons deal 33% less damage to you, boiling oil included.', [(c, 5, 1) for c in TANKS]),
    ('Snapshot', 485, None, 'Fire ranged weapons while moving, without the shot being interrupted, for 60 seconds. Re-usable every 5 minutes.',
     [('ArmsmanCareer', 25)]),
    ('Scars of Battle', None, None, None, [(c, 41, 1) for c in TANKS]),
    # 2% spell damage resistance per level from 41: 20% at 50.
    ('Memories of War', None, None, None, [(c, lv, lv - 40) for c in TANKS for lv in range(41, 51)]),
    # The Warden has no Shields line here; live gives it Engage at 7 and full Guard at 15.
    ('Engage', None, None, None, [('WardenCareer', 7)]),
    ('Guard', None, None, None, [('WardenCareer', 15, 3)]),
    ('Remedy', None, None, None, [(c, 50) for c in STEALTHERS]),
    ('Protect', None, None, None, [('SavageCareer', 15, 2)]),  # live's Protect II
    ('Climb Walls', None, None, None, [('Savagery', 45, 0, 32)]),  # live 1.130a: the Savage climbs at Savagery 45
    # Safe Fall I for the archers; the assassins' IV and V come at 38 and 48.
    ('Safe Fall', None, None, None, [('Stealth', 10, 1, c) for c in (3, 25, 50)] +
     [('Stealth', lv, rank + 1, c) for c in (9, 23, 49) for rank, lv in enumerate((10, 20, 30, 38, 48))]),
]

# The tank shouts' spells, at the IDs their ability handlers cast (live values; timers as the shared DOL data).
ABILITY_PACKAGE = PACKAGE + '-abilities'
ABILITY_SPELLS = [
    dict(SpellID=14373, Name='Rampage', Type='Rampage', Target='Group', Value=100, Duration=20, Radius=2000, RecastDelay=300,
         Icon=2384, TooltipId=5295, Description='Group shout: a 100% chance to resist debuff spells for 20 seconds.'),
    dict(SpellID=14374, Name='Fury', Type='Fury', Target='Self', Value=45, Duration=20, RecastDelay=900, Icon=2044, TooltipId=5297,
         Description='A 45% bonus to all magic resistances and a 45% chance to shrug off crowd control spells, for 20 seconds.'),
    dict(SpellID=14375, Name='Metal Guard', Type='MetalGuard', Target='Self', Value=3, Duration=1200, Radius=2000, Range=2000,
         RecastDelay=600, Icon=2779, TooltipId=5296, Description='Increases the absorption (ABS) of your group members by 3%.'),
    dict(SpellID=14376, Name='Bolstering Roar', Type='BolsteringRoar', Target='Self', Radius=250, Range=250, RecastDelay=600,
         Icon=974, TooltipId=5294, Description='Removes root, snare and mesmerize effects from your group members.'),
    dict(SpellID=14377, Name='Taunting Shout', Type='Taunt', Target='Cone', Value=97, Radius=90, Range=350, RecastDelay=20,
         Icon=3464, TooltipId=5298, Description='Taunts the enemies in a 90 degree, 350 radius cone, increasing your threat against them by 486.'),
]


def definitions():
    rows = []
    spell_id = FIRST
    for cls, spells in CLASSES.items():
        for line, level, name, fields in spells:
            rows.append((line, level, dict(fields, SpellID=spell_id, Spell_ID=f'{PACKAGE}-{spell_id}', PackageID=PACKAGE,
                                           TooltipId=TOOLTIP_BASE + spell_id - FIRST, Name=name,
                                           Description=f'{name} (live 1.130a {line}).')))
            spell_id += 1
    assert spell_id <= LAST
    return rows


def apply(db):
    db.row_factory = sqlite3.Row
    if db.execute('select 1 from Spell where (SpellID between ? and ? or TooltipId between ? and ?) and coalesce(PackageID,\'\')<>?',
                  (FIRST, LAST, TOOLTIP_BASE, TOOLTIP_BASE + LAST - FIRST, PACKAGE)).fetchone():
        raise ValueError('Hybrid line spell/tooltip collision')
    rows = definitions()
    for line, _, _ in rows:
        if not db.execute('select 1 from SpellLine where KeyName=?', (line,)).fetchone():
            raise ValueError('Missing spell line ' + line)
    renamed = apply_renames(db)
    for line in {line for line, _, _ in rows}:
        names = {row['Name'] for l, _, row in rows if l == line}
        clash = db.execute(f'select s.Name from LineXSpell l join Spell s on s.SpellID=l.SpellID where l.LineName=? and coalesce(s.PackageID,\'\')<>? '
                           f'and s.Name in ({",".join("?" * len(names))})', (line, PACKAGE, *names)).fetchone()
        if clash:
            raise ValueError(f'{line} already has {clash[0]}')
    db.execute('delete from LineXSpell where SpellID between ? and ?', (FIRST, LAST))
    db.execute('delete from Spell where PackageID=?', (PACKAGE,))
    template = blank_spell(db)
    for line, level, fields in rows:
        fields = dict(fields)
        art = fields.pop('ART', None)
        if art is not None:
            source = db.execute('select ClientEffect, Icon from Spell where Name=? and coalesce(PackageID,\'\')<>? order by SpellID limit 1',
                                (art, PACKAGE)).fetchone()
            if source is None:
                raise ValueError(f"No local spell '{art}' to borrow art from for {fields['Name']}")
            fields.update(ClientEffect=source['ClientEffect'], Icon=source['Icon'])
        row = dict(template, **fields)
        row.update(Message1=f"You are affected by {row['Name']}.", Message2=f"{{0}} is affected by {row['Name']}.",
                   Message3=f"{row['Name']} ends.", Message4=f"{row['Name']} ends on {{0}}.")
        db.execute('insert into Spell (' + ','.join(f'"{k}"' for k in row) + ') values (' + ','.join('?' * len(row)) + ')', list(row.values()))
        db.execute('insert into LineXSpell (LineName,SpellID,Level,LineXSpell_ID) values (?,?,?,?)', (line, row['SpellID'], level, row['Spell_ID']))
    abilities = apply_abilities(db)
    return dict(package=PACKAGE, spells=len(rows), renamed=renamed, abilities=abilities, classes=list(CLASSES),
                skipped={k: len(v) for k, v in SKIPPED.items()})


def apply_renames(db):
    """Renames local misspellings to their live names (again harmlessly on a re-run) and sets the live values given."""
    done = 0
    for line, old, new, fields in RENAMES:
        found = db.execute('select s.SpellID from LineXSpell l join Spell s on s.SpellID=l.SpellID where l.LineName=? and s.Name in (?,?)',
                           (line, old, new)).fetchall()
        if len(found) != 1:
            raise ValueError(f'{line}: expected one {old} to rename, found {len(found)}')
        assigns = dict(fields, Name=new)
        db.execute('update Spell set ' + ','.join(f'"{k}"=?' for k in assigns) + ' where SpellID=?', (*assigns.values(), found[0]['SpellID']))
        done += 1
    return done


# Display names that differ from the key the server code uses (SiegeDefense.SIEGE_RESISTANCE).
ABILITY_NAMES = {'Siege Resist': 'Siege Resistance'}


def apply_abilities(db):
    ids = [r['SpellID'] for r in ABILITY_SPELLS]
    marks = ','.join('?' * len(ids))
    if db.execute(f"select 1 from Spell where (SpellID in ({marks}) or TooltipId in ({marks})) and coalesce(PackageID,'')<>?",
                  (*ids, *[r['TooltipId'] for r in ABILITY_SPELLS], ABILITY_PACKAGE)).fetchone():
        raise ValueError('Ability spell/tooltip collision')
    db.execute('delete from Spell where PackageID=?', (ABILITY_PACKAGE,))
    template = blank_spell(db)
    for fields in ABILITY_SPELLS:
        row = dict(template, **fields, Spell_ID=f"{ABILITY_PACKAGE}-{fields['SpellID']}", PackageID=ABILITY_PACKAGE,
                   ClientEffect=fields['SpellID'])
        db.execute('insert into Spell (' + ','.join(f'"{k}"' for k in row) + ') values (' + ','.join('?' * len(row)) + ')', list(row.values()))
    for key, icon, internal, description, grants in ABILITIES:
        if not db.execute('select 1 from Ability where KeyName=?', (key,)).fetchone():
            raise ValueError('Missing ability ' + key)
        if internal is not None and db.execute('select 1 from Ability where InternalID=? and KeyName<>?', (internal, key)).fetchone():
            raise ValueError('Ability internal ID collision for ' + key)
        for column, value in (('IconID', icon), ('InternalID', internal), ('Description', description)):
            if value is not None:
                db.execute(f'update Ability set {column}=? where KeyName=?', (value, key))
        grants = [(g + (0, 0))[:4] for g in grants]
        for spec, class_id in {(g[0], g[3]) for g in grants}:
            if not db.execute('select 1 from Specialization where KeyName=?', (spec,)).fetchone():
                raise ValueError('Missing career ' + spec)
            db.execute('delete from SpecXAbility where AbilityKey=? and Spec=? and ClassId=?', (key, spec, class_id))
        for spec, level, ability_level, class_id in grants:
            db.execute('insert into SpecXAbility (Spec,SpecLevel,AbilityKey,AbilityLevel,ClassId) values (?,?,?,?,?)',
                       (spec, level, key, ability_level, class_id))
    for key, name in ABILITY_NAMES.items():
        db.execute('update Ability set Name=? where KeyName=?', (name, key))
    return len(ABILITIES)


def blank_spell(db):
    """A Spell row with every column the table needs, zeroed."""
    template = dict(db.execute('select * from Spell where SpellID=3531').fetchone())
    for column in list(template):
        if column not in ('LastTimeRowUpdated',):
            template[column] = 0 if isinstance(template[column], (int, float)) else ('' if isinstance(template[column], str) else template[column])
    return template
