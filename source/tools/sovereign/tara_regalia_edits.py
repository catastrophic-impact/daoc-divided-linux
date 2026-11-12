"""Regalia of Tara (2026-11-06): the Tuatha Sovereign's armor and cloak as real items, as a World Workshop edits file.

Seven level 51 pieces, scale AF 102 / 27%, class 67 only, no trade or sale. Art (owner picks 2026-11-06: concept A, emerald
court scale, and the stag roundel cloak): private client objects 4950-4954, 4956 and the helm 4964 (the Crown of Lugh, pass 3
2026-11-08; 4955 was the first helm) from tools/sovereign-art/tara/install_tara_set.py
(hauberk drawn with extension 5 = Body05, as the Storm hauberk). A client without that install draws nothing for them. Two use abilities and
two armor procs (TaraRegaliaSpells in C#, 63750-63753); the set bonus is code (TaraRegalia in TuathaRemake.cs); the Brehon
of Tara gives missing pieces to a level 50 Tuatha who whispers "Regalia of Tara". Every piece has 11 lines: stats, resists,
a skill and the ToA lines a Tuatha rolls on loot, so the set reaches the ToA caps.

    python tara_regalia_edits.py <world.db> edits.json
    python tara_regalia_edits.py <world.db> edits.json --update     a world that has the Regalia: bonus lines and models only
"""
import json
from pathlib import Path
import sqlite3
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from storm_remake_edits import World
from storm_regalia_edits import P as STORM_P

P = dict(STORM_P, Scythe=90, Blades=72)
PACKAGE = "OfflineDAoC.Tuatha.v1"
SET_TEXT = ("Regalia of Tara set: 2 pieces +5% all resistances; 4 pieces +10% melee and style damage; "
            "6 pieces +10% evade and spell damage; all 7 +10% maximum health and every block, parry or evade marks the attacker twice.")

# id suffix, name, slot, model, extension, copy source, bonuses (11 lines), use (spell, reuse s), proc spell
PIECES = (
    ("helm", "Crown of the Sidhe King", 21, 4964, 0, "arcanite_improved_osnadurtha_cap3",
     dict(Str=18, Con=18, Pie=15, Hits=50, Spirit=10, Energy=10, StrCap=5, ConCap=5, SpellDamage=5, CastingSpeed=5, Fatigue=5),
     (63750, 180), 0),
    ("hauberk", "Hauberk of Tir na Nog", 25, 4950, 5, "arcanite_improved_osnadurtha_hauberk3",
     dict(Str=20, Con=20, Dex=15, Hits=70, Crush=10, Slash=10, Thrust=10, Scythe=4, MeleeDamage=5, StyleDamage=5, BuffEffect=10),
     None, 63752),
    ("sleeves", "Briarbound Sleeves", 28, 4952, 0, "arcanite_improved_osnadurtha_sleeves3",
     dict(Str=16, Qui=15, Dex=12, Hits=45, Heat=10, Cold=10, Blades=4, MeleeSpeed=5, DexCap=5, QuiCap=5, ResistPierce=5),
     None, 63753),
    ("gauntlets", "Gauntlets of Nuada", 22, 4953, 0, "arcanite_improved_osnadurtha_gloves3",
     dict(Str=15, Dex=18, Qui=15, Hits=40, Matter=10, Body=10, Shields=4, MeleeSpeed=5, ResistPierce=5, CritMelee=5, StrCap=5),
     None, 0),
    ("leggings", "Greaves of the Fianna", 27, 4951, 0, "arcanite_improved_osnadurtha_leggings3",
     dict(Con=20, Str=15, Pie=15, Hits=60, Energy=10, Body=10, Parry=4, PieCap=5, HitsCap=50, CastingSpeed=5, PowerPool=10),
     None, 0),
    ("boots", "Boots of the Moonlit Path", 23, 4954, 0, "arcanite_improved_osnadurtha_boots3",
     dict(Dex=15, Qui=18, Con=15, Hits=40, Cold=10, Matter=10, Heat=10, StyleDamage=5, QuiCap=5, Fatigue=10, DebuffEffect=10),
     None, 0),
    ("cloak", "Mantle of the Moon and Stag", 26, 4956, 0, "alltafearann14_supgee",
     dict(Str=15, Con=15, Pie=15, Hits=50, Crush=5, Slash=5, Thrust=5, MeleeDamage=5, SpellDamage=5, SpellDuration=10, Fatigue=10),
     (63751, 300), 0),
)

# id, name, target, duration, description (TaraRegaliaSpells)
SPELLS = (
    (63750, "Eyes of the Sidhe", "Self", 0, "Use: every enemy within 1,000 gets 2 Sidhe Marks. Three-minute reuse."),
    (63751, "Mantle of Mists", "Self", 20, "Use: 30% evade and 30% speed for 20 seconds. Five-minute reuse."),
    (63752, "Briarward", "Self", 0, "When struck: a 200-point ward absorbs half of each blow for 10 seconds."),
    (63753, "Bramble Snare", "Enemy", 0, "When struck: the attacker is rooted for 6 seconds and marked."),
)
SPELL_TOOLTIP = 28760


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
    if w.one("SELECT 1 FROM ItemTemplate WHERE Id_nb LIKE 'tara_regalia_%'"):
        raise ValueError("The Regalia of Tara is already in this world: use --update")
    if w.one("SELECT 1 FROM Spell WHERE SpellID BETWEEN 63750 AND 63759 OR TooltipId BETWEEN ? AND ?", SPELL_TOOLTIP, SPELL_TOOLTIP + 9):
        raise ValueError("Regalia of Tara spell or tooltip IDs are taken")
    spell_src = w.one("SELECT Spell_ID FROM Spell WHERE PackageID=? AND Type='StormSovereignAbility' ORDER BY SpellID LIMIT 1", PACKAGE)[0]
    for spell_id, name, target, duration, description in SPELLS:
        w.copy("Spell", spell_src, dict(
            SpellID=spell_id, Spell_ID=f"tara-regalia-{spell_id}", Name=name, Description=description, Type="StormSovereignAbility",
            Target=target, Range=0 if target == "Self" else 1500, Radius=0, Power=0, CastTime=0, Damage=0, Duration=duration,
            Frequency=0, RecastDelay=0, Value=0, SubSpellID=0, SpellGroup=0, EffectGroup=0, ClientEffect=5439 if target == "Enemy" else 5445,
            Icon=5439 if target == "Enemy" else 5445, TooltipId=SPELL_TOOLTIP + spell_id - 63750, PackageID=PACKAGE))
    for suffix, name, slot, model, extension, source, bonuses, use, proc in PIECES:
        if not w.one("SELECT 1 FROM ItemTemplate WHERE Id_nb=?", source):
            raise ValueError(f"Missing copy source {source}")
        fields = dict(Name=name, Level=51, Item_Type=slot, Model=model, Extension=extension, Realm=3, AllowedClasses="67",
                      Object_Type=41 if slot == 26 else 38, DPS_AF=0 if slot == 26 else 102, SPD_ABS=0 if slot == 26 else 27,
                      Quality=100, Bonus=35, BonusLevel=0, LevelRequirement=0, Color=0, Emblem=0, Effect=0,
                      IsDropable=0, IsTradable=0, CanDropAsLoot=0, IsPickable=1, Price=0,
                      SpellID=0, Charges=0, MaxCharges=0, CanUseEvery=0, SpellID1=0, Charges1=0, MaxCharges1=0,
                      ProcSpellID=proc, ProcSpellID1=0, ProcChance=10 if proc else 0, PoisonSpellID=0, PoisonCharges=0, PoisonMaxCharges=0,
                      PackageID="OfflineDAoC.Tuatha.Regalia", ClassType="", Description=SET_TEXT)
        fields.update(bonus_fields(bonuses))
        if use:
            # MaxCharges -1: no charges, only the reuse timer.
            fields.update(SpellID=use[0], Charges=-1, MaxCharges=-1, CanUseEvery=use[1])
        w.out.append({"table": "ItemTemplate", "copy": source, "key": f"tara_regalia_{suffix}", "set": fields})
    return w.out


def update_edits_for(db):
    """A world that already has the Regalia: rewrite bonus lines and models (for the art pass)."""
    w = World(db)
    for suffix, name, slot, model, extension, source, bonuses, use, proc in PIECES:
        if not w.one("SELECT 1 FROM ItemTemplate WHERE Id_nb=?", f"tara_regalia_{suffix}"):
            raise ValueError(f"tara_regalia_{suffix} is missing: run without --update")
        w.out.append({"table": "ItemTemplate", "key": f"tara_regalia_{suffix}", "set": dict(bonus_fields(bonuses), Model=model, Extension=extension)})
    return w.out


def main():
    db_path, out_path = Path(sys.argv[1]), Path(sys.argv[2])
    with sqlite3.connect(db_path.resolve().as_uri() + "?mode=ro", uri=True) as db:
        edits = update_edits_for(db) if "--update" in sys.argv else edits_for(db)
    out_path.write_text(json.dumps(edits, indent=1))
    print(f"{len(edits)} edits: {sum(e['table'] == 'ItemTemplate' for e in edits)} items, {sum(e['table'] == 'Spell' for e in edits)} spells")


if __name__ == "__main__":
    main()
