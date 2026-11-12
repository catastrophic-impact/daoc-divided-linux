"""Storm Sovereign remake (2026-11-04): writes a World Workshop edits file for an installed world.

Hammer, Axe and Shields with 61 styles (storm_remake_styles.json), merged and Tempest-paid spells, Sword and Spear
retired, and a trimmed realm ability list with three new Sovereign RAs. Only class-64 rows change; Pendragon (65)
and Tuatha (67) are untouched. No player table is read for writing or written.

    python storm_remake_edits.py <world.db> edits.json            a world with the old Storm Sovereign kit
    python storm_remake_edits.py <world.db> edits.json --update   a world that already has the first remake (3b15478a9)
    python ../world_editor/cli.py --db <world.db> --client <app> plan edits.json --out remake.patch.json
"""
import json
from pathlib import Path
import sqlite3
import sys
import uuid

HERE = Path(__file__).resolve().parent
CLASS, CAREER, PACKAGE = 64, "StormforgedCareer", "OfflineDAoC.Stormforged.v1"
LEVELS = (1, 5, 10, 15, 20, 30, 40, 50)
FIRST_STYLE, FIRST_EFFECT, FIRST_SPELL = 7100, 63500, 63600   # StormRemakeStyles / StormRemakeSpells in C#
STYLE_TOOLTIP, SPELL_TOOLTIP = 27500, 27600
WEAPON = {"Hammer": 12, "Axe": 13, "Shields": 42}
POSITION = {"back": 0, "side": 1, "front": 2}
RESULT = {"chain": 7, "parry": 3, "block": 4}

# key: name, unlock, target, settings at level 50 (damage scales by rank), description. Keys match StormRemakeSpells;
# 3 (Runic Reversal) and 4 (Hunter's Mark) were merged away.
SPELLS = {
    0: ("Royal Decree", 15, "Self", dict(Radius=1500, Duration=10, RecastDelay=30, Power=5, ClientEffect=3534),
        "Your party gains 15% movement speed, 10% absorption, 10 accuracy and 10% melee damage for 10 seconds."),
    1: ("Rally to the Crown", 11, "Self", dict(Radius=1500, RecastDelay=30, Power=15, ClientEffect=3517),
        "Restore 25% of your health, 15% of your party's health and 20% endurance to everyone. 30-second reuse."),
    2: ("Runic Armament", 11, "Self", dict(Duration=65535, Power=0, ClientEffect=3537),
        "Maintained: hammers splash lightning, axes open frost wounds, every parry or block returns lightning and blocks "
        "can raise a storm shield; extra magic resistance outside the buff cap."),
    5: ("Royal Thunder", 1, "Enemy", dict(Range=1500, Damage=250, CastTime=2.5, Power=18, ClientEffect=3549),
        "Lightning bolt. With Tempest it is instant, costs no power and spends every charge for +20% damage each."),
    6: ("Stormbreak", 31, "Self", dict(Radius=350, Damage=250, CastTime=2.3, Power=25, ClientEffect=3528),
        "Lightning bursts around you, 350 radius. With Tempest it is instant, costs no power and spends every charge for +20% each."),
    7: ("Winter's Grasp", 11, "Enemy", dict(Range=1500, Damage=160, CastTime=2, Power=16, ClientEffect=14321),
        "Cold damage and a 30% snare for 8 seconds. With Tempest it is instant and spends 1 charge instead of power."),
    8: ("Sovereign's Hammerfall", 15, "Enemy", dict(Range=2100, Radius=350, Damage=350, RecastDelay=15, Power=10, ClientEffect=3541),
        "Throw a hammer and teleport beside an enemy within 2100: a strong impact, half damage around it. Spends 1 Tempest "
        "instead of power when you have one. 15-second reuse."),
}
# Old royal keys replaced by SPELLS, merged away, or covered by styles (Lightning Spear, Chain of the Crown, Challenge).
RETIRED_LINES = (0, 1, 2, 3, 4, 7, 8, 9, 10, 11, 14, 15, 16, 17, 18, 21)
RETIRED_SPECS = ("Sword", "Spear")
RETIRED_WEAPONRY = ("Weaponry: Swords", "Weaponry: Spears")
DROPPED_RAS = ("AtlasOF_Serenity", "AtlasOF_EtherealBond", "Mastery of Focus", "AtlasOF_MasteryOfConcentration", "Dual Threat",
               "AtlasOF_RagingPower", "AtlasOF_MCL", "AtlasOF_WildPower", "AtlasOF_Lifter", "AtlasOF_VeilRecovery")
NEW_RAS = (
    ("Sovereign_Thunderlord", "Thunderlord", 1904, 3549, "SovereignThunderlord",
     "Weapon style damage +5/10/15%. Three ranks, incremental costs 2/4/6 realm skill points."),
    ("Sovereign_EyeOfTempest", "Eye of Tempest", 1905, 3534, "SovereignEyeOfTempest",
     "Finishers refund 1/1/2 Tempest charges, and charges last 30/45/60 seconds. Three ranks, incremental costs 2/4/6."),
    ("Sovereign_Shieldwall", "Shieldwall of Asgard", 1906, 3536, "SovereignShieldwall",
     "Block chance +3/6/9%, and style storm shields absorb 10/20/30% more. Three ranks, incremental costs 2/4/6."),
)
# The first remake (3b15478a9) had these; --update replaces them.
FIRST_REMAKE_KEYS = (3, 4, 5, 6)


class World:
    """Read-only lookups and edit builders over one world database."""

    KEYS = {"Style": "StyleID", "StyleXSpell": "StyleXSpell_ID", "Spell": "Spell_ID", "SpellLine": "SpellLineID",
            "LineXSpell": "LineXSpell_ID", "Ability": "AbilityID", "ClassXRealmAbility_Atlas": "ClassXRealmAbility_Atlas_ID",
            "ClassXSpecialization": "ClassXSpecializationID", "SpecXAbility": "SpecXabilityID"}

    def __init__(self, db):
        db.row_factory = sqlite3.Row
        self.db = db
        self.out = []
        self.counters = {}

    def one(self, sql, *a):
        return self.db.execute(sql, a).fetchone()

    def rows(self, sql, *a):
        return self.db.execute(sql, a).fetchall()

    def new_key(self, table):
        key = self.KEYS[table]
        kind = next(r[2] for r in self.db.execute(f'PRAGMA table_info("{table}")') if r[1] == key).upper()
        if "INT" not in kind:
            return str(uuid.uuid4())
        self.counters[table] = self.counters.get(table) or (self.one(f'SELECT max("{key}") FROM "{table}"')[0] or 0)
        self.counters[table] += 1
        return self.counters[table]

    def copy(self, table, source_key, changes):
        self.out.append({"table": table, "copy": source_key, "key": self.new_key(table), "set": changes})

    def delete(self, table, key):
        self.out.append({"table": table, "delete": key})

    def spell_source(self):
        return self.one("SELECT Spell_ID FROM Spell WHERE PackageID=? AND Type='StormSovereignAbility' ORDER BY SpellID LIMIT 1", PACKAGE)[0]

    def retire_line(self, line, delete_spells=False):
        for r in self.rows("SELECT LineXSpell_ID, SpellID FROM LineXSpell WHERE LineName=?", line):
            self.delete("LineXSpell", r[0])
            if delete_spells:
                for s in self.rows("SELECT Spell_ID FROM Spell WHERE SpellID=? AND PackageID=?", r[1], PACKAGE):
                    self.delete("Spell", s[0])
        for r in self.rows("SELECT SpellLineID FROM SpellLine WHERE KeyName=?", line):
            self.delete("SpellLine", r[0])

    def add_styles(self, styles):
        style_src = self.one("SELECT StyleID FROM Style WHERE ClassId=? ORDER BY ID LIMIT 1", CLASS)[0]
        link_src = self.one("SELECT StyleXSpell_ID FROM StyleXSpell WHERE ClassID=? LIMIT 1", CLASS)[0]
        spell_src = self.spell_source()
        every = json.loads((HERE / "storm_remake_styles.json").read_text())
        ids = {s["name"]: FIRST_STYLE + s["index"] for s in every}
        icons, two_hand = {}, {}
        for spec in WEAPON:
            # Stock Midgard icons for the line, lowest level first, so the bar looks like the line's own styles.
            found = [r[0] for r in self.rows("SELECT DISTINCT Icon FROM Style WHERE SpecKeyName=? AND ClassId IN (21,22,23,24,25,26,31,32,34,43) "
                                             "ORDER BY SpecLevelRequirement", spec)]
            icons[spec] = found or [self.one("SELECT Icon FROM Style WHERE SpecKeyName=? LIMIT 1", spec)[0]]
            two_hand[spec] = self.one("SELECT TwoHandAnimation FROM Style WHERE SpecKeyName=? AND ClassId<>? LIMIT 1", spec, CLASS)[0]
        position = {spec: sum(1 for s in every if s["line"] == spec and s["index"] < min(x["index"] for x in styles)) for spec in WEAPON}
        for s in styles:
            spec, op, index = s["line"], s["open"], s["index"]
            finisher = s["growth"] >= 1.45
            self.copy("Style", style_src, dict(
                ID=ids[s["name"]], ClassId=CLASS, Name=s["name"], SpecKeyName=spec, SpecLevelRequirement=s["level"],
                Icon=icons[spec][position[spec] % len(icons[spec])], EnduranceCost=14 if finisher else 8 if op == "anytime" else 10,
                StealthRequirement=int(op == "stealth"),
                OpeningRequirementType=1 if op in ("parry", "block") else 2 if op in POSITION else 0,
                OpeningRequirementValue=ids[s["after"]] if op == "chain" else POSITION.get(op, 0),
                AttackResultRequirement=RESULT.get(op, 0), WeaponTypeRequirement=WEAPON[spec],
                GrowthOffset=0, GrowthRate=s["growth"], BonusToHit=10, BonusToDefense=5,
                TwoHandAnimation=two_hand[spec], RandomProc=0, ArmorHitLocation=0))
            position[spec] += 1
            effect = FIRST_EFFECT + index
            self.copy("Spell", spell_src, dict(
                SpellID=effect, Spell_ID=f"storm-remake-{effect}", Name=s["name"], Description=s["description"],
                Type="StormSovereignAbility", Target="Enemy", Range=0, Radius=0, Power=0, CastTime=0, Damage=0, Duration=10,
                Frequency=0, RecastDelay=0, Value=0, SubSpellID=0, SpellGroup=0, EffectGroup=0, ClientEffect=3541, Icon=3541,
                TooltipId=STYLE_TOOLTIP + index, PackageID=PACKAGE))
            self.copy("StyleXSpell", link_src, dict(SpellID=effect, ClassID=CLASS, StyleID=ids[s["name"]], Chance=100))

    def add_spells(self, keys):
        """Brings each remake line to SPELLS: rows that exist are updated, missing ones copied, stale ranks removed."""
        spell_src = self.spell_source()
        line_src = self.one("SELECT SpellLineID FROM SpellLine WHERE KeyName LIKE 'Stormforged.%' ORDER BY KeyName LIMIT 1")[0]
        lxs_src = self.one("SELECT LineXSpell_ID FROM LineXSpell WHERE LineName LIKE 'Stormforged.%' LIMIT 1")[0]
        for key in keys:
            name, unlock, target, settings, description = SPELLS[key]
            line = f"Stormforged.Remake{key:02}"
            existing_line = self.one("SELECT SpellLineID FROM SpellLine WHERE KeyName=?", line)
            if existing_line:
                self.out.append({"table": "SpellLine", "key": existing_line[0], "set": dict(Name=name)})
            else:
                self.copy("SpellLine", line_src, dict(KeyName=line, Name=name, Spec=CAREER, IsBaseLine=1, ClassIDHint=CLASS))
            wanted = set()
            for level in sorted({unlock, *[n for n in LEVELS if n >= unlock]}):
                tier = max(i for i, n in enumerate(LEVELS) if n <= level)
                spell_id = FIRST_SPELL + tier * 16 + key
                fields = dict(Range=0, Radius=0, Power=0, CastTime=0, Damage=0, Duration=0, Frequency=0, RecastDelay=0, Value=0,
                              SubSpellID=0, EffectGroup=0, Pulse=0, PulsePower=0, Concentration=0)
                fields.update(settings)
                if fields["Damage"] > 0:
                    fields["Damage"] = max(5, int(fields["Damage"] * level / 50))
                if fields["Power"] > 0:
                    fields["Power"] = max(1, int(fields["Power"] * max(.2, level / 50)))
                fields.update(SpellID=spell_id, Spell_ID=f"storm-remake-{spell_id}", Name=name, Description=description,
                              Type="StormSovereignAbility", Target=target, SpellGroup=FIRST_SPELL + key,
                              Icon=settings["ClientEffect"], TooltipId=SPELL_TOOLTIP + spell_id - FIRST_SPELL, PackageID=PACKAGE)
                wanted.add(spell_id)
                old = self.one("SELECT Spell_ID FROM Spell WHERE SpellID=?", spell_id)
                if old:
                    fields.pop("Spell_ID")
                    self.out.append({"table": "Spell", "key": old[0], "set": fields})
                else:
                    self.copy("Spell", spell_src, fields)
                link = self.one("SELECT LineXSpell_ID FROM LineXSpell WHERE LineName=? AND SpellID=?", line, spell_id)
                if link:
                    self.out.append({"table": "LineXSpell", "key": link[0], "set": dict(Level=level)})
                else:
                    self.copy("LineXSpell", lxs_src, dict(LineName=line, SpellID=spell_id, Level=level))
            for r in self.rows("SELECT LineXSpell_ID, SpellID FROM LineXSpell WHERE LineName=?", line):
                if r[1] not in wanted:
                    self.delete("LineXSpell", r[0])
                    for s in self.rows("SELECT Spell_ID FROM Spell WHERE SpellID=? AND PackageID=?", r[1], PACKAGE):
                        self.delete("Spell", s[0])


def edits_for(db):
    w = World(db)
    if w.one("SELECT 1 FROM Style WHERE ClassId=? AND ID BETWEEN ? AND ?", CLASS, FIRST_STYLE, FIRST_STYLE + 99):
        raise ValueError("The remake is already in this world: use --update")
    for lo, hi in ((FIRST_EFFECT, FIRST_EFFECT + 99), (FIRST_SPELL, FIRST_SPELL + 127)):
        if w.one("SELECT 1 FROM Spell WHERE SpellID BETWEEN ? AND ?", lo, hi):
            raise ValueError(f"Spell IDs {lo}-{hi} are taken")
    if w.one("SELECT 1 FROM Spell WHERE TooltipId BETWEEN ? AND ?", STYLE_TOOLTIP, SPELL_TOOLTIP + 127):
        raise ValueError("Tooltip IDs taken")

    # styles: replace the class's old styles; spells: add the remake lines before retiring the old ones they copy from
    old_links = [r[0] for r in w.rows("SELECT StyleXSpell_ID FROM StyleXSpell WHERE ClassID=?", CLASS)]
    old_styles = [r[0] for r in w.rows("SELECT StyleID FROM Style WHERE ClassId=?", CLASS)]
    w.add_styles(json.loads((HERE / "storm_remake_styles.json").read_text()))
    w.add_spells(SPELLS)
    for key in old_links:
        w.delete("StyleXSpell", key)
    for key in old_styles:
        w.delete("Style", key)
    for key in RETIRED_LINES:
        w.retire_line(f"Stormforged.Sovereign{key:02}")

    # weapons: Hammer, Axe and Shields only
    for spec in RETIRED_SPECS:
        for r in w.rows("SELECT ClassXSpecializationID FROM ClassXSpecialization WHERE ClassID=? AND SpecKeyName=?", CLASS, spec):
            w.delete("ClassXSpecialization", r[0])
    for ability in RETIRED_WEAPONRY:
        for r in w.rows("SELECT SpecXabilityID FROM SpecXAbility WHERE Spec=? AND ClassId=? AND AbilityKey=?", CAREER, CLASS, ability):
            w.delete("SpecXAbility", r[0])

    # realm abilities: drop the caster picks, add three Sovereign ones
    for key in DROPPED_RAS:
        for r in w.rows("SELECT ClassXRealmAbility_Atlas_ID FROM ClassXRealmAbility_Atlas WHERE CharClass=? AND AbilityKey=?", CLASS, key):
            w.delete("ClassXRealmAbility_Atlas", r[0])
    ability_src = w.one("SELECT AbilityID FROM Ability WHERE KeyName='Sovereign_RoyalFortitude'")[0]
    binding_src = w.one("SELECT ClassXRealmAbility_Atlas_ID FROM ClassXRealmAbility_Atlas WHERE CharClass=? LIMIT 1", CLASS)[0]
    for key, name, internal, icon, implementation, description in NEW_RAS:
        if w.one("SELECT 1 FROM Ability WHERE KeyName=? OR InternalID=?", key, internal):
            raise ValueError(f"Realm ability {key} or InternalID {internal} exists")
        w.copy("Ability", ability_src, dict(KeyName=key, Name=name, Description=description, InternalID=internal,
                                            IconID=icon, Implementation="DOL.GS.RealmAbilities." + implementation))
        w.copy("ClassXRealmAbility_Atlas", binding_src, dict(CharClass=CLASS, AbilityKey=key))
    return w.out


def update_edits_for(db):
    """From the first remake (3b15478a9) to now: Stormslam, the Tempest-paid spells and the 14-button spell list."""
    w = World(db)
    if not w.one("SELECT 1 FROM Style WHERE ClassId=? AND ID=?", CLASS, FIRST_STYLE):
        raise ValueError("This world does not have the first remake: run without --update")
    every = json.loads((HERE / "storm_remake_styles.json").read_text())
    have = {r[0] for r in w.rows("SELECT ID FROM Style WHERE ClassId=? AND ID BETWEEN ? AND ?", CLASS, FIRST_STYLE, FIRST_STYLE + 99)}
    w.add_styles([s for s in every if FIRST_STYLE + s["index"] not in have])
    w.add_spells(SPELLS)
    for key in FIRST_REMAKE_KEYS:
        if key not in SPELLS:
            w.retire_line(f"Stormforged.Remake{key:02}", delete_spells=True)
    for key in RETIRED_LINES:
        w.retire_line(f"Stormforged.Sovereign{key:02}")
    return w.out


def main():
    db_path, out_path = Path(sys.argv[1]), Path(sys.argv[2])
    with sqlite3.connect(db_path.resolve().as_uri() + "?mode=ro", uri=True) as db:
        edits = update_edits_for(db) if "--update" in sys.argv else edits_for(db)
    out_path.write_text(json.dumps(edits, indent=1))
    counts = {}
    for e in edits:
        op = "delete" if "delete" in e else "insert"
        counts[(e["table"], op)] = counts.get((e["table"], op), 0) + 1
    for (table, op), n in sorted(counts.items()):
        print(f"{op:7} {n:4}  {table}")


if __name__ == "__main__":
    main()
