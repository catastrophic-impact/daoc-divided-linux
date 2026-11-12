"""Regalia of Camelot (2026-11-06): the Pendragon Sovereign's armor and cloak as real items, as a World Workshop edits file.

Seven level 51 pieces, plate AF 102 / 34% (Albion plate's own absorption), class 65 only, no trade or sale. The art is
the owner's pick B (gilded plate, crimson enamel, red dragon; 2026-11-07): client objects 4957-4963 from
tools/sovereign-art/camelot/install_camelot_set.py (breastplate Extension 5 = Body05, as the Tara and Storm sets). Two use
abilities and two armor procs (CamelotRegaliaSpells in C#, 63760-63763); the set bonus is code (CamelotRegalia in
PendragonRemake.cs); the Pendragon trainer gives missing pieces to a level 50 Pendragon who whispers "Regalia of Camelot".
Every piece has 11 lines: stats, resists, a skill and the ToA lines a Pendragon rolls on loot.

    python camelot_regalia_edits.py <world.db> edits.json
    python camelot_regalia_edits.py <world.db> edits.json --update     a world that has the Regalia: bonus lines and models only
"""
import json
from pathlib import Path
import sqlite3
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from storm_remake_edits import World
from storm_regalia_edits import P as STORM_P

P = dict(STORM_P, Sword=44, Polearm=41)
PACKAGE = "OfflineDAoC.Pendragon.v1"
SET_TEXT = ("Regalia of Camelot set: 2 pieces +5% all resistances; 4 pieces +10% melee and style damage; "
            "6 pieces +10% block and parry; all 7 +10% maximum health and every block or parry earns 2 Valor.")

# id suffix, name, slot, model, extension, copy source, bonuses (11 lines), use (spell, reuse s), proc spell
PIECES = (
    ("helm", "Crown of the Pendragon", 21, 4962, 0, "arcanium_full_plate_helm",
     dict(Str=18, Con=18, Pie=15, Hits=50, Spirit=10, Energy=10, StrCap=5, ConCap=5, SpellDamage=5, CastingSpeed=5, Fatigue=5),
     (63760, 180), 0),
    ("hauberk", "Breastplate of Camelot", 25, 4957, 5, "Ageless_Fluted_Protector",
     dict(Str=20, Con=20, Dex=15, Hits=70, Crush=10, Slash=10, Thrust=10, Sword=4, MeleeDamage=5, StyleDamage=5, BuffEffect=10),
     None, 63762),
    ("sleeves", "Vambraces of the Round Table", 28, 4959, 0, "arcanium_full_plate_vambraces",
     dict(Str=16, Qui=15, Dex=12, Hits=45, Heat=10, Cold=10, Polearm=4, MeleeSpeed=5, DexCap=5, QuiCap=5, ResistPierce=5),
     None, 63763),
    ("gauntlets", "Gauntlets of Excalibur", 22, 4960, 0, "arcanium_full_plate_gauntlets",
     dict(Str=15, Dex=18, Qui=15, Hits=40, Matter=10, Body=10, Shields=4, MeleeSpeed=5, ResistPierce=5, CritMelee=5, StrCap=5),
     None, 0),
    ("leggings", "Greaves of Badon Hill", 27, 4958, 0, "arcanium_full_plate_greaves",
     dict(Con=20, Str=15, Pie=15, Hits=60, Energy=10, Body=10, Parry=4, PieCap=5, HitsCap=50, CastingSpeed=5, PowerPool=10),
     None, 0),
    ("boots", "Sabatons of the Grail Knight", 23, 4961, 0, "arcanium_full_plate_boots",
     dict(Dex=15, Qui=18, Con=15, Hits=40, Cold=10, Matter=10, Heat=10, StyleDamage=5, QuiCap=5, Fatigue=10, DebuffEffect=10),
     None, 0),
    ("cloak", "Mantle of the Red Dragon", 26, 4963, 0, "Afanc_Hide_Cloak",
     dict(Str=15, Con=15, Pie=15, Hits=50, Crush=5, Slash=5, Thrust=5, MeleeDamage=5, SpellDamage=5, SpellDuration=10, Fatigue=10),
     (63761, 300), 0),
)

# id, name, target, duration, description (CamelotRegaliaSpells)
SPELLS = (
    (63760, "Crown of Camelot", "Self", 0, "Use: fills your Valor. Three-minute reuse."),
    (63761, "Mercy of Avalon", "Self", 0, "Use: heals your party 20%. Five-minute reuse."),
    (63762, "Holy Bulwark", "Self", 0, "When struck: a 200-point ward absorbs half of each blow for 10 seconds, and you gain 1 Valor."),
    (63763, "Searing Rebuke", "Enemy", 0, "When struck: the attacker burns with holy fire and is taunted."),
)
SPELL_TOOLTIP = 29840


def bonus_fields(bonuses):
    if len(bonuses) > 11:
        raise ValueError("An item holds at most 11 bonus lines")
    fields = {f"Bonus{i}": 0 for i in range(1, 11)} | {f"Bonus{i}Type": 0 for i in range(1, 11)} | dict(ExtraBonus=0, ExtraBonusType=0)
    for i, (prop, value) in enumerate(bonuses.items(), 1):
        prefix = "ExtraBonus" if i == 11 else f"Bonus{i}"
        fields[prefix], fields[prefix + "Type"] = value, P[prop]
    return fields


def edits_for(db):
    w = World(db)
    if w.one("SELECT 1 FROM ItemTemplate WHERE Id_nb LIKE 'camelot_regalia_%'"):
        raise ValueError("The Regalia of Camelot is already in this world: use --update")
    if w.one("SELECT 1 FROM Spell WHERE SpellID BETWEEN 63760 AND 63769 OR TooltipId BETWEEN ? AND ?", SPELL_TOOLTIP, SPELL_TOOLTIP + 9):
        raise ValueError("Regalia of Camelot spell or tooltip IDs are taken")
    spell_src = w.one("SELECT Spell_ID FROM Spell WHERE PackageID=? AND Type='StormSovereignAbility' ORDER BY SpellID LIMIT 1", PACKAGE)[0]
    for spell_id, name, target, duration, description in SPELLS:
        w.copy("Spell", spell_src, dict(
            SpellID=spell_id, Spell_ID=f"camelot-regalia-{spell_id}", Name=name, Description=description, Type="StormSovereignAbility",
            Target=target, Range=0 if target == "Self" else 1500, Radius=0, Power=0, CastTime=0, Damage=0, Duration=duration,
            Frequency=0, RecastDelay=0, Value=0, SubSpellID=0, SpellGroup=0, EffectGroup=0, ClientEffect=1678 if target == "Enemy" else 1506,
            Icon=1678 if target == "Enemy" else 1506, TooltipId=SPELL_TOOLTIP + spell_id - 63760, PackageID=PACKAGE))
    for suffix, name, slot, model, extension, source, bonuses, use, proc in PIECES:
        if not w.one("SELECT 1 FROM ItemTemplate WHERE Id_nb=?", source):
            raise ValueError(f"Missing copy source {source}")
        fields = dict(Name=name, Level=51, Item_Type=slot, Model=model, Extension=extension, Realm=1, AllowedClasses="65",
                      Object_Type=41 if slot == 26 else 36, DPS_AF=0 if slot == 26 else 102, SPD_ABS=0 if slot == 26 else 34,
                      Quality=100, Bonus=35, BonusLevel=0, LevelRequirement=0, Color=0, Emblem=0, Effect=0,
                      IsDropable=0, IsTradable=0, CanDropAsLoot=0, IsPickable=1, Price=0,
                      SpellID=0, Charges=0, MaxCharges=0, CanUseEvery=0, SpellID1=0, Charges1=0, MaxCharges1=0,
                      ProcSpellID=proc, ProcSpellID1=0, ProcChance=10 if proc else 0, PoisonSpellID=0, PoisonCharges=0, PoisonMaxCharges=0,
                      PackageID="OfflineDAoC.Pendragon.Regalia", ClassType="", Description=SET_TEXT)
        fields.update(bonus_fields(bonuses))
        if use:
            # MaxCharges -1: no charges, only the reuse timer.
            fields.update(SpellID=use[0], Charges=-1, MaxCharges=-1, CanUseEvery=use[1])
        w.out.append({"table": "ItemTemplate", "copy": source, "key": f"camelot_regalia_{suffix}", "set": fields})
    return w.out


def update_edits_for(db):
    """A world that already has the Regalia: rewrite bonus lines and models (for the art pass)."""
    w = World(db)
    for suffix, name, slot, model, extension, source, bonuses, use, proc in PIECES:
        if not w.one("SELECT 1 FROM ItemTemplate WHERE Id_nb=?", f"camelot_regalia_{suffix}"):
            raise ValueError(f"camelot_regalia_{suffix} is missing: run without --update")
        w.out.append({"table": "ItemTemplate", "key": f"camelot_regalia_{suffix}", "set": dict(bonus_fields(bonuses), Model=model, Extension=extension)})
    return w.out


def main():
    db_path, out_path = Path(sys.argv[1]), Path(sys.argv[2])
    with sqlite3.connect(db_path.resolve().as_uri() + "?mode=ro", uri=True) as db:
        edits = update_edits_for(db) if "--update" in sys.argv else edits_for(db)
    out_path.write_text(json.dumps(edits, indent=1))
    print(f"{len(edits)} edits: {sum(e['table'] == 'ItemTemplate' for e in edits)} items, {sum(e['table'] == 'Spell' for e in edits)} spells")


if __name__ == "__main__":
    main()
