"""Tuatha Sovereign remake (2026-11-06): writes a World Workshop edits file for an installed world.

Scythe, Blades and Shields with 61 styles (tuatha_remake_styles.json), Sidhe Marks and the two courts (C#: TuathaRemake*.cs),
15 spell buttons, Evade as a class ability, Blunt/Piercing/Large Weapons/Celtic Spear retired, a trimmed realm ability list
and three Tuatha realm abilities. Only class-67 rows change; Storm (64) and Pendragon (65) are untouched. No player table
is written. See docs/TUATHA-SOVEREIGN-REMAKE.md.

    python tuatha_remake_edits.py <world.db> edits.json
    python ../world_editor/cli.py --db <world.db> --client <app> plan edits.json --out tuatha.patch.json
"""
import json
from pathlib import Path
import sqlite3
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from storm_remake_edits import World, DROPPED_RAS, LEVELS

HERE = Path(__file__).resolve().parent
CLASS, CAREER, PACKAGE = 67, "TuathaCareer", "OfflineDAoC.Tuatha.v1"
FIRST_STYLE, FIRST_EFFECT, FIRST_SPELL = 7200, 63800, 65600   # TuathaRemakeStyles / TuathaRemakeSpells in C#
STYLE_TOOLTIP, SPELL_TOOLTIP = 28500, 28600
WEAPON = {"Scythe": 26, "Blades": 19, "Shields": 42}
POSITION = {"back": 0, "side": 1, "front": 2}
RESULT = {"chain": 7, "parry": 3, "block": 4, "evade": 5}

# key: name, unlock, target, settings at level 50 (damage scales by rank), description. Keys match TuathaRemakeSpells.
TURN_KEY = 1
SPELLS = {
    0: ("Spirit Lance", 1, "Enemy", dict(Range=1500, Damage=250, CastTime=2.5, Power=18, ClientEffect=5433),
        "Spirit bolt that detonates your Sidhe Marks on its target (+25% each). Instant on a target with 3 or more marks."),
    TURN_KEY: ("Turn of the Courts", 10, "Self", dict(RecastDelay=10, Power=0, ClientEffect=5445),
        "Take your seat in the other faerie court for 60 seconds. Seelie: detonations heal your party, wards are stronger, +10% evade. "
        "Unseelie: marks rot, detonations drain, +10% critical chance. 10-second reuse."),
    2: ("Bramble Bind", 11, "Enemy", dict(Range=1500, Radius=300, RecastDelay=30, Power=15, ClientEffect=5439),
        "Roots the target and enemies within 300 for 15 seconds and marks each once. 30-second reuse."),
    3: ("Tir na Nog Renewal", 11, "Self", dict(Radius=1500, RecastDelay=30, Power=15, ClientEffect=4608),
        "Your party heals 5% at once and more over 12 seconds. 30-second reuse."),
    4: ("Song of Dreams", 20, "Self", dict(Radius=350, RecastDelay=60, Power=20, ClientEffect=5393),
        "Mesmerizes enemies within 350 for 20 seconds; damage wakes them. 60-second reuse."),
    5: ("Faerie Step", 15, "Enemy", dict(Range=1500, RecastDelay=15, Power=10, ClientEffect=11314),
        "Step behind an enemy within 1,500 (your back styles open) and mark it once. 15-second reuse."),
    6: ("Wildwood Armament", 11, "Self", dict(Duration=65535, Power=0, ClientEffect=5005),
        "Maintained: scythe hits mark a second enemy, blade hits open bleeds, every block, parry or evade returns spirit thorns; "
        "extra magic resistance outside the buff cap."),
    7: ("Otherworld Burst", 31, "Self", dict(Radius=350, Damage=250, CastTime=2.3, Power=25, ClientEffect=11025),
        "Spirit burst within 350 that detonates every marked enemy it hits."),
    8: ("Fairy Ring", 45, "Self", dict(Radius=350, Damage=100, Duration=30, RecastDelay=30, Power=20, ClientEffect=11338),
        "A ring at your feet for 30 seconds: in the Seelie Court it heals allies inside, in the Unseelie Court it drains enemies."),
}
# The two court effects (tier 0 only, not in the spellbook; Turn of the Courts starts them for COURT_SECONDS, as
# TuathaRemakeSpells.CourtSeconds in C#; owner 2026-11-06: not permanent).
COURT_SECONDS = 60
COURTS = {
    9: ("Seelie Court", "Summer court for 60 seconds: detonations heal your party 3% per mark, style wards 50% stronger, +10% evade, +5% absorption.", 5445),
    10: ("Unseelie Court", "Winter court for 60 seconds: your marks rot their enemies, detonations drain half their damage to you, +10% critical chance.", 11008),
}
# Kept old key 20: seed_tuatha.py's renames left the Storm Crown's "Royal storm transformation" text on it.
ASCENDANT_LINE = "Tuatha.Sovereign20"
ASCENDANT_MODEL = 1904      # the Morrigan (1.130 monsters.csv); was 339, "Elf Male 6" (owner 2026-11-06: looked basic)
ASCENDANT = ("The Morrigan's form for one minute: +50% melee damage, double royal spell damage, royal spells cast in at most 0.25 seconds, "
             "+20% absorption, +20% melee haste, 10% health/power/endurance every 4 seconds, 1.5x life/power/endurance steal and "
             "spirit pulses. Your marks land double and you hold both courts' riders at once. Three-minute reuse.")
# Old Tuatha keys replaced, merged away or folded in (kept: 6 chant, 12 will, 13 armor, 19 drake, 20 ascendant, 22 veil).
RETIRED_LINES = (0, 1, 2, 3, 4, 5, 7, 8, 9, 10, 11, 14, 15, 16, 17, 18, 21)
RETIRED_SPECS = ("Blunt", "Piercing", "Large Weapons", "Celtic Spear")
RETIRED_WEAPONRY = ("Weaponry: Blunt", "Weaponry: Piercing", "Weaponry: Large Weapons", "Weaponry: Celtic Spears")
EVADE = ((1, 1), (10, 2), (20, 3), (30, 4), (40, 5), (45, 6), (50, 7))   # (level, Evade rank), like Hibernia's blade classes
NEW_RAS = (
    ("Tuatha_MorrigansEdge", "Morrigan's Edge", 1944, 5433, "TuathaMorrigansEdge",
     "Detonations of your Sidhe Marks deal 5/10/15% more. Three ranks, incremental costs 2/4/6 realm skill points."),
    ("Tuatha_HeartOfTheCourt", "Heart of the Court", 1945, 5445, "TuathaHeartOfTheCourt",
     "Your marks last 20/30/40 seconds, and detonations leave 0/1/2 marks behind. Three ranks, incremental costs 2/4/6."),
    ("Tuatha_EvasionOfTheSidhe", "Evasion of the Sidhe", 1946, 11008, "TuathaEvasionOfTheSidhe",
     "Evade +3/6/9%, and your style thorns are 10/20/30% stronger. Three ranks, incremental costs 2/4/6."),
)


def styles():
    return json.loads((HERE / "tuatha_remake_styles.json").read_text())


def edits_for(db):
    w = World(db)
    if w.one("SELECT 1 FROM Style WHERE ClassId=? AND ID BETWEEN ? AND ?", CLASS, FIRST_STYLE, FIRST_STYLE + 99):
        raise ValueError("The Tuatha remake is already in this world")
    for lo, hi in ((FIRST_EFFECT, FIRST_EFFECT + 99), (FIRST_SPELL, FIRST_SPELL + 127)):
        if w.one("SELECT 1 FROM Spell WHERE SpellID BETWEEN ? AND ?", lo, hi):
            raise ValueError(f"Spell IDs {lo}-{hi} are taken")
    if w.one("SELECT 1 FROM Spell WHERE TooltipId BETWEEN ? AND ?", STYLE_TOOLTIP, SPELL_TOOLTIP + 127):
        raise ValueError("Tooltip IDs taken")
    spell_src = w.one("SELECT Spell_ID FROM Spell WHERE PackageID=? AND Type='StormSovereignAbility' ORDER BY SpellID LIMIT 1", PACKAGE)[0]

    # --- styles: replace the class's 42 old styles with the 61 new ones ---
    style_src = w.one("SELECT StyleID FROM Style WHERE ClassId=? ORDER BY ID LIMIT 1", CLASS)[0]
    link_src = w.one("SELECT StyleXSpell_ID FROM StyleXSpell WHERE ClassID=? LIMIT 1", CLASS)[0]
    old_links = [r[0] for r in w.rows("SELECT StyleXSpell_ID FROM StyleXSpell WHERE ClassID=?", CLASS)]
    old_styles = [r[0] for r in w.rows("SELECT StyleID FROM Style WHERE ClassId=?", CLASS)]
    every = styles()
    ids = {s["name"]: FIRST_STYLE + s["index"] for s in every}
    icons, two_hand = {}, {}
    for spec in WEAPON:
        found = [r[0] for r in w.rows("SELECT DISTINCT Icon FROM Style WHERE SpecKeyName=? AND ClassId<>? ORDER BY SpecLevelRequirement", spec, CLASS)]
        icons[spec] = found
        two_hand[spec] = w.one("SELECT TwoHandAnimation FROM Style WHERE SpecKeyName=? AND ClassId<>? ORDER BY SpecLevelRequirement LIMIT 1", spec, CLASS)[0]
    position = {spec: 0 for spec in WEAPON}
    for s in every:
        spec, op, index = s["line"], s["open"], s["index"]
        finisher = s["growth"] >= 1.45
        w.copy("Style", style_src, dict(
            ID=ids[s["name"]], ClassId=CLASS, Name=s["name"], SpecKeyName=spec, SpecLevelRequirement=s["level"],
            Icon=icons[spec][position[spec] % len(icons[spec])], EnduranceCost=14 if finisher else 8 if op == "anytime" else 10,
            StealthRequirement=int(op == "stealth"),
            OpeningRequirementType=1 if op in ("parry", "block", "evade") else 2 if op in POSITION else 0,
            OpeningRequirementValue=ids[s["after"]] if op == "chain" else POSITION.get(op, 0),
            AttackResultRequirement=RESULT.get(op, 0), WeaponTypeRequirement=WEAPON[spec],
            GrowthOffset=0, GrowthRate=s["growth"], BonusToHit=10, BonusToDefense=5,
            TwoHandAnimation=two_hand[spec], RandomProc=0, ArmorHitLocation=0))
        position[spec] += 1
        effect = FIRST_EFFECT + index
        w.copy("Spell", spell_src, dict(
            SpellID=effect, Spell_ID=f"tuatha-remake-{effect}", Name=s["name"], Description=s["description"],
            Type="StormSovereignAbility", Target="Enemy", Range=0, Radius=0, Power=0, CastTime=0, Damage=0, Duration=10,
            Frequency=0, RecastDelay=0, Value=0, SubSpellID=0, SpellGroup=0, EffectGroup=0, ClientEffect=5433, Icon=5433,
            TooltipId=STYLE_TOOLTIP + index, PackageID=PACKAGE))
        w.copy("StyleXSpell", link_src, dict(SpellID=effect, ClassID=CLASS, StyleID=ids[s["name"]], Chance=100))
    for key in old_links:
        w.delete("StyleXSpell", key)
    for key in old_styles:
        w.delete("Style", key)

    # --- spells: the remake lines and the two court effects, then retire the old lines ---
    line_src = w.one("SELECT SpellLineID FROM SpellLine WHERE KeyName='Tuatha.Sovereign00'")[0]
    lxs_src = w.one("SELECT LineXSpell_ID FROM LineXSpell WHERE LineName='Tuatha.Sovereign00' LIMIT 1")[0]
    base = dict(Range=0, Radius=0, Power=0, CastTime=0, Damage=0, Duration=0, Frequency=0, RecastDelay=0, Value=0,
                SubSpellID=0, EffectGroup=0, Pulse=0, PulsePower=0, Concentration=0)
    for key, (name, unlock, target, settings, description) in SPELLS.items():
        line = f"Tuatha.Remake{key:02}"
        w.copy("SpellLine", line_src, dict(KeyName=line, Name=name, Spec=CAREER, IsBaseLine=1, ClassIDHint=CLASS))
        for level in sorted({unlock, *[n for n in LEVELS if n >= unlock]}):
            tier = max(i for i, n in enumerate(LEVELS) if n <= level)
            spell_id = FIRST_SPELL + tier * 16 + key
            fields = dict(base, **settings)
            if fields["Damage"] > 0:
                fields["Damage"] = max(5, int(fields["Damage"] * level / 50))
            if fields["Power"] > 0:
                fields["Power"] = max(1, int(fields["Power"] * max(.2, level / 50)))
            fields.update(SpellID=spell_id, Spell_ID=f"tuatha-remake-{spell_id}", Name=name, Description=description,
                          Type="StormSovereignAbility", Target=target, SpellGroup=FIRST_SPELL + key,
                          Icon=settings["ClientEffect"], TooltipId=SPELL_TOOLTIP + spell_id - FIRST_SPELL, PackageID=PACKAGE)
            w.copy("Spell", spell_src, fields)
            w.copy("LineXSpell", lxs_src, dict(LineName=line, SpellID=spell_id, Level=level))
    for key, (name, description, effect) in COURTS.items():
        spell_id = FIRST_SPELL + key
        w.copy("Spell", spell_src, dict(base, SpellID=spell_id, Spell_ID=f"tuatha-remake-{spell_id}", Name=name, Description=description,
                                        Type="StormSovereignAbility", Target="Self", Duration=COURT_SECONDS, SpellGroup=FIRST_SPELL + key,
                                        ClientEffect=effect, Icon=effect, TooltipId=SPELL_TOOLTIP + key, PackageID=PACKAGE))
    for key in RETIRED_LINES:
        w.retire_line(f"Tuatha.Sovereign{key:02}")
    for r in ascendant_rows(w):
        w.out.append({"table": "Spell", "key": r[0], "set": dict(Description=ASCENDANT, Value=ASCENDANT_MODEL)})

    # --- weapons: Scythe, Blades and Shields; Evade as a class ability ---
    for spec in RETIRED_SPECS:
        for r in w.rows("SELECT ClassXSpecializationID FROM ClassXSpecialization WHERE ClassID=? AND SpecKeyName=?", CLASS, spec):
            w.delete("ClassXSpecialization", r[0])
    spec_src = w.one("SELECT ClassXSpecializationID FROM ClassXSpecialization WHERE ClassID=? AND SpecKeyName='Blades'", CLASS)[0]
    w.copy("ClassXSpecialization", spec_src, dict(ClassID=CLASS, SpecKeyName="Scythe", LevelAcquired=-3))
    for ability in RETIRED_WEAPONRY:
        for r in w.rows("SELECT SpecXabilityID FROM SpecXAbility WHERE Spec=? AND ClassId=? AND AbilityKey=?", CAREER, CLASS, ability):
            w.delete("SpecXAbility", r[0])
    weaponry_src = w.one("SELECT SpecXabilityID FROM SpecXAbility WHERE Spec=? AND ClassId=? AND AbilityKey='Weaponry: Blades'", CAREER, CLASS)[0]
    w.copy("SpecXAbility", weaponry_src, dict(Spec=CAREER, SpecLevel=1, AbilityKey="Weaponry: Scythe", AbilityLevel=0, ClassId=CLASS))
    for level, rank in EVADE:
        w.copy("SpecXAbility", weaponry_src, dict(Spec=CAREER, SpecLevel=level, AbilityKey="Evade", AbilityLevel=rank, ClassId=CLASS))

    # --- realm abilities: drop the caster picks, add three Tuatha ones ---
    for key in DROPPED_RAS:
        for r in w.rows("SELECT ClassXRealmAbility_Atlas_ID FROM ClassXRealmAbility_Atlas WHERE CharClass=? AND AbilityKey=?", CLASS, key):
            w.delete("ClassXRealmAbility_Atlas", r[0])
    ability_src = w.one("SELECT AbilityID FROM Ability WHERE KeyName='Tuatha_RoyalFortitude'")[0]
    binding_src = w.one("SELECT ClassXRealmAbility_Atlas_ID FROM ClassXRealmAbility_Atlas WHERE CharClass=? LIMIT 1", CLASS)[0]
    for key, name, internal, icon, implementation, description in NEW_RAS:
        if w.one("SELECT 1 FROM Ability WHERE KeyName=? OR InternalID=?", key, internal):
            raise ValueError(f"Realm ability {key} or InternalID {internal} exists")
        w.copy("Ability", ability_src, dict(KeyName=key, Name=name, Description=description, InternalID=internal,
                                            IconID=icon, Implementation="DOL.GS.RealmAbilities." + implementation))
        w.copy("ClassXRealmAbility_Atlas", binding_src, dict(CharClass=CLASS, AbilityKey=key))
    return w.out


def ascendant_rows(w):
    return w.rows("SELECT Spell_ID FROM Spell WHERE PackageID=? AND SpellID IN (SELECT SpellID FROM LineXSpell WHERE LineName=?) "
                  "AND (Description<>? OR Value<>?)", PACKAGE, ASCENDANT_LINE, ASCENDANT, ASCENDANT_MODEL)


def main():
    db_path, out_path = Path(sys.argv[1]), Path(sys.argv[2])
    with sqlite3.connect(db_path.resolve().as_uri() + "?mode=ro", uri=True) as db:
        edits = edits_for(db)
    out_path.write_text(json.dumps(edits, indent=1))
    counts = {}
    for e in edits:
        op = "delete" if "delete" in e else "insert" if "copy" in e else "update"
        counts[(e["table"], op)] = counts.get((e["table"], op), 0) + 1
    for (table, op), n in sorted(counts.items()):
        print(f"{op:7} {n:4}  {table}")


if __name__ == "__main__":
    main()
