"""Pendragon Sovereign remake (2026-11-06): writes a World Workshop edits file for an installed world.

Sword (Slash), Polearm and Shields with 61 styles (pendragon_remake_styles.json), Valor (C#: PendragonRemake*.cs), 9 new
spell buttons beside 7 kept ones (16), the seven Valor buff icons, Crush/Thrust/Two Handed retired, a trimmed realm
ability list and three Pendragon realm abilities, the Lion of Albion's text, and the Royal Lion pet as the Black Lion.
Only class-65 rows (and the Royal Lion's NpcTemplate) change; Storm (64) and Tuatha (67) are untouched. No player table
is written. See docs/PENDRAGON-SOVEREIGN-REMAKE.md.

    python pendragon_remake_edits.py <world.db> edits.json
    python ../world_editor/cli.py --db <world.db> --client <app> plan edits.json --out pendragon.patch.json
"""
import json
from pathlib import Path
import sqlite3
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from storm_remake_edits import World, DROPPED_RAS, LEVELS

HERE = Path(__file__).resolve().parent
CLASS, CAREER, PACKAGE = 65, "PendragonCareer", "OfflineDAoC.Pendragon.v1"
FIRST_STYLE, FIRST_EFFECT, FIRST_SPELL = 7300, 63900, 65800   # PendragonRemakeStyles / PendragonRemakeSpells in C#
STYLE_TOOLTIP, SPELL_TOOLTIP = 29610, 29700                    # free 29605-29999 on 2026-11-06
WEAPON = {"Slash": 3, "Polearm": 7, "Shields": 42}
POSITION = {"back": 0, "side": 1, "front": 2}
RESULT = {"chain": 7, "parry": 3, "block": 4, "evade": 5}

# key: name, unlock, target, settings at level 50 (damage scales by rank), description. Keys match PendragonRemakeSpells.
SPELLS = {
    0: ("Crownfire Lance", 1, "Enemy", dict(Range=1500, Damage=250, CastTime=2.5, Power=18, ClientEffect=1678),
        "Holy fire bolt. Instant with 3 or more Valor."),
    1: ("Guard of the Crown", 5, "Realm", dict(Range=1500, Duration=15, RecastDelay=15, Power=10, ClientEffect=1506),
        "An ally (or you) gains 20% absorption and a holy ward of 10% of its health for 15 seconds; you gain 1 Valor. 15-second reuse."),
    2: ("Sovereign's Challenge", 10, "Self", dict(Radius=500, RecastDelay=30, Power=10, ClientEffect=1465),
        "Taunts every enemy within 500; +1 Valor per enemy, up to 3. 30-second reuse."),
    3: ("Rally to the Crown", 11, "Self", dict(Radius=1500, RecastDelay=30, Power=15, ClientEffect=1453),
        "Your party heals 10% at once and more over 12 seconds. 30-second reuse."),
    4: ("Consecrated Armament", 11, "Self", dict(Duration=65535, Power=0, ClientEffect=1457),
        "Maintained: sword hits burn with holy fire, polearm hits splash holy fire on an enemy nearby, every block or parry "
        "returns holy fire; extra magic resistance outside the buff cap."),
    5: ("Decree: Stand Fast", 20, "Self", dict(Radius=1500, Duration=15, RecastDelay=30, Power=15, ClientEffect=1461),
        "Your party gains a holy ward of 10% of its health and +10% block and parry for 15 seconds. 30-second reuse."),
    6: ("Binding Radiance", 20, "Enemy", dict(Range=1500, Radius=300, Damage=100, RecastDelay=20, Power=15, ClientEffect=1612),
        "Stuns the target for 4 seconds and snares enemies within 300 of it by 40%. 20-second reuse."),
    7: ("Judgment", 31, "Self", dict(Radius=350, Damage=250, RecastDelay=10, Power=0, ClientEffect=1610),
        "Spends all your Valor: holy fire on every enemy within 350, +20% per Valor, a 2-second stun at 5, and your party "
        "heals 3% per Valor. 10-second reuse."),
    8: ("Field of Judgement", 40, "Self", dict(Radius=350, Damage=100, Duration=30, RecastDelay=30, Power=20, ClientEffect=1608),
        "Holy ground at your feet for 30 seconds: burns enemies inside and heals allies 3% every 3 seconds."),
}
# The Valor buff icons (tier 0 keys 9-15, not in the spellbook; PendragonValor shows them).
VALOR_ICON_FIRST_KEY, VALOR_ICONS, VALOR_ICON = 9, 7, 1506
# Kept old keys: 6 chant, 12 Will, 13 Armor, 19 Royal Lion, 20 Lion of Albion, 21 King's Advance, 22 Veil of Avalon.
RETIRED_LINES = (0, 1, 2, 3, 4, 5, 7, 8, 9, 10, 11, 14, 15, 16, 17, 18)
RETIRED_SPECS = ("Crush", "Thrust", "Two Handed")
RETIRED_WEAPONRY = ("Weaponry: Crushing", "Weaponry: Thrusting", "Weaponry: Two Handed")
LION_LINE = "Pendragon.Sovereign20"
LION = ("The Lion of Albion's form for one minute: +50% melee damage, double royal spell damage, royal spells cast in at most "
        "0.25 seconds, +20% absorption, +20% melee haste, 10% health/power/endurance every 4 seconds, 1.5x life/power/endurance "
        "steal and holy fire pulses. It fills your Valor, and your Judgments spend none while it lasts. Three-minute reuse.")
PET_TEMPLATE, PET_MODEL = 60172001, "104"        # Royal Lion: the Black Lion (owner 2026-11-06; was 100, a plain Tan Lion)
NEW_RAS = (
    ("Pendragon_CodeOfChivalry", "Code of Chivalry", 1947, 1506, "PendragonCodeOfChivalry",
     "Your Valor cap rises to 6/6/7, and out of combat Valor fades every 8/12/16 seconds. Three ranks, incremental costs 2/4/6 realm skill points."),
    ("Pendragon_KnightsGuard", "Knight's Guard", 1948, 1465, "PendragonKnightsGuard",
     "Block +3/6/9%, and Guard of the Crown's ward is 25/50/75% stronger. Three ranks, incremental costs 2/4/6."),
    ("Pendragon_GrailBlessing", "Grail Blessing", 1949, 1453, "PendragonGrailBlessing",
     "Your Judgments' party heals and wards are 25/50/75% stronger. Three ranks, incremental costs 2/4/6."),
)


def styles():
    return json.loads((HERE / "pendragon_remake_styles.json").read_text())


def edits_for(db):
    w = World(db)
    if w.one("SELECT 1 FROM Style WHERE ClassId=? AND ID BETWEEN ? AND ?", CLASS, FIRST_STYLE, FIRST_STYLE + 99):
        raise ValueError("The Pendragon remake is already in this world")
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
        icons[spec] = [r[0] for r in w.rows("SELECT DISTINCT Icon FROM Style WHERE SpecKeyName=? AND ClassId<>? ORDER BY SpecLevelRequirement", spec, CLASS)]
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
            SpellID=effect, Spell_ID=f"pendragon-remake-{effect}", Name=s["name"], Description=s["description"],
            Type="StormSovereignAbility", Target="Enemy", Range=0, Radius=0, Power=0, CastTime=0, Damage=0, Duration=10,
            Frequency=0, RecastDelay=0, Value=0, SubSpellID=0, SpellGroup=0, EffectGroup=0, ClientEffect=1603, Icon=1603,
            TooltipId=STYLE_TOOLTIP + index, PackageID=PACKAGE))
        w.copy("StyleXSpell", link_src, dict(SpellID=effect, ClassID=CLASS, StyleID=ids[s["name"]], Chance=100))
    for key in old_links:
        w.delete("StyleXSpell", key)
    for key in old_styles:
        w.delete("Style", key)

    # --- spells: the remake lines and the Valor icons, then retire the old lines ---
    line_src = w.one("SELECT SpellLineID FROM SpellLine WHERE KeyName='Pendragon.Sovereign00'")[0]
    lxs_src = w.one("SELECT LineXSpell_ID FROM LineXSpell WHERE LineName='Pendragon.Sovereign00' LIMIT 1")[0]
    base = dict(Range=0, Radius=0, Power=0, CastTime=0, Damage=0, Duration=0, Frequency=0, RecastDelay=0, Value=0,
                SubSpellID=0, EffectGroup=0, Pulse=0, PulsePower=0, Concentration=0)
    for key, (name, unlock, target, settings, description) in SPELLS.items():
        line = f"Pendragon.Remake{key:02}"
        w.copy("SpellLine", line_src, dict(KeyName=line, Name=name, Spec=CAREER, IsBaseLine=1, ClassIDHint=CLASS))
        for level in sorted({unlock, *[n for n in LEVELS if n >= unlock]}):
            tier = max(i for i, n in enumerate(LEVELS) if n <= level)
            spell_id = FIRST_SPELL + tier * 16 + key
            fields = dict(base, **settings)
            if fields["Damage"] > 0:
                fields["Damage"] = max(5, int(fields["Damage"] * level / 50))
            if fields["Power"] > 0:
                fields["Power"] = max(1, int(fields["Power"] * max(.2, level / 50)))
            fields.update(SpellID=spell_id, Spell_ID=f"pendragon-remake-{spell_id}", Name=name, Description=description,
                          Type="StormSovereignAbility", Target=target, SpellGroup=FIRST_SPELL + key,
                          Icon=settings["ClientEffect"], TooltipId=SPELL_TOOLTIP + spell_id - FIRST_SPELL, PackageID=PACKAGE)
            w.copy("Spell", spell_src, fields)
            w.copy("LineXSpell", lxs_src, dict(LineName=line, SpellID=spell_id, Level=level))
    for i in range(VALOR_ICONS):
        n, spell_id = i + 1, FIRST_SPELL + VALOR_ICON_FIRST_KEY + i
        w.copy("Spell", spell_src, dict(base, SpellID=spell_id, Spell_ID=f"pendragon-remake-{spell_id}", Name=f"Valor {n}",
                                        Description=f"Valor {n}: your next Judgment deals {20 * n}% more damage and heals or wards your party {3 * n}%."
                                                    + (" At 5 or more it also stuns for 2 seconds." if n >= 5 else ""),
                                        Type="StormSovereignAbility", Target="Self", SpellGroup=0, ClientEffect=0, Icon=VALOR_ICON,
                                        TooltipId=SPELL_TOOLTIP + VALOR_ICON_FIRST_KEY + i, PackageID=PACKAGE))
    for key in RETIRED_LINES:
        w.retire_line(f"Pendragon.Sovereign{key:02}")
    for r in lion_rows(w):
        w.out.append({"table": "Spell", "key": r[0], "set": dict(Description=LION)})
    w.out.append({"table": "NpcTemplate", "where": {"TemplateId": PET_TEMPLATE}, "set": {"Model": PET_MODEL}})

    # --- weapons: Sword, Polearm and Shields ---
    for spec in RETIRED_SPECS:
        for r in w.rows("SELECT ClassXSpecializationID FROM ClassXSpecialization WHERE ClassID=? AND SpecKeyName=?", CLASS, spec):
            w.delete("ClassXSpecialization", r[0])
    for ability in RETIRED_WEAPONRY:
        for r in w.rows("SELECT SpecXabilityID FROM SpecXAbility WHERE Spec=? AND ClassId=? AND AbilityKey=?", CAREER, CLASS, ability):
            w.delete("SpecXAbility", r[0])

    # --- realm abilities: drop the caster picks, add three Pendragon ones ---
    for key in DROPPED_RAS:
        for r in w.rows("SELECT ClassXRealmAbility_Atlas_ID FROM ClassXRealmAbility_Atlas WHERE CharClass=? AND AbilityKey=?", CLASS, key):
            w.delete("ClassXRealmAbility_Atlas", r[0])
    ability_src = w.one("SELECT AbilityID FROM Ability WHERE KeyName='Pendragon_RoyalFortitude'")[0]
    binding_src = w.one("SELECT ClassXRealmAbility_Atlas_ID FROM ClassXRealmAbility_Atlas WHERE CharClass=? LIMIT 1", CLASS)[0]
    for key, name, internal, icon, implementation, description in NEW_RAS:
        if w.one("SELECT 1 FROM Ability WHERE KeyName=? OR InternalID=?", key, internal):
            raise ValueError(f"Realm ability {key} or InternalID {internal} exists")
        w.copy("Ability", ability_src, dict(KeyName=key, Name=name, Description=description, InternalID=internal,
                                            IconID=icon, Implementation="DOL.GS.RealmAbilities." + implementation))
        w.copy("ClassXRealmAbility_Atlas", binding_src, dict(CharClass=CLASS, AbilityKey=key))
    return w.out


def lion_rows(w):
    return w.rows("SELECT Spell_ID FROM Spell WHERE PackageID=? AND SpellID IN (SELECT SpellID FROM LineXSpell WHERE LineName=?) "
                  "AND Description<>?", PACKAGE, LION_LINE, LION)


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
