"""Pendragon (65) and Tuatha (67) spell fixes, 2026-11-09: writes a World Workshop edits file for an installed world.

Owner report: "some of their spells don't work right and don't look right", "the Alb one has spells that don't use
Valor", "it's using lightning effects for some of its abilities". The server side is fix/sovereign-spells; this is the
world side:

- descriptions for the Pendragon spells that now use held Valor (+10% a point) and for Fairy Ring outside a court;
- one icon per button where several buttons shared one (Icon is a spells.csv row id; that row's Icon is drawn);
- Faerie Step plays Phase Shift (it played the Growing Vines root), Tuatha style procs play one effect per weapon line
  (all 61 played the Champion's Blast of Valiance);
- the caster realm abilities (Mastery of Magery, Wild Minion, Static Tempest's lightning storm, Augmented Acuity)
  leave both classes, and their "Storm Mastery" is renamed for their realm.

Only rows of classes 65 and 67 change; the Storm Sovereign (64) keeps its own. No player table is written.

    python sovereign_spell_fix_edits.py <world.db> edits.json
    python ../world_editor/cli.py --profile modern plan edits.json --out sovereign-spells.patch.json
"""
import json
from pathlib import Path
import sqlite3
import sys

HERE = Path(__file__).resolve().parent
PEND, TUA = 65, 67
TUATHA_STYLE_EFFECT = 63800

VALOR = " Valor held: +10% {what} per point, none spent."
DESCRIPTIONS = {
    "Pendragon.Remake00": "Holy fire bolt. Instant with 3 or more Valor." + VALOR.format(what="damage"),
    "Pendragon.Remake01": "An ally (or you) gains 20% absorption and a holy ward of 10% of its health for 15 seconds; you gain 1 Valor. "
                          "15-second reuse." + VALOR.format(what="ward"),
    "Pendragon.Remake03": "Your party heals 10% at once, +2% per Valor held (none spent), and more over 12 seconds. 30-second reuse.",
    "Pendragon.Remake04": "Maintained: sword hits burn with holy fire, polearm hits splash holy fire on an enemy nearby, every block or parry "
                          "returns holy fire; extra magic resistance outside the buff cap." + VALOR.format(what="holy fire"),
    "Pendragon.Remake05": "Your party gains a holy ward of 10% of its health and +10% block and parry for 15 seconds. 30-second reuse."
                          + VALOR.format(what="ward"),
    "Pendragon.Remake06": "Stuns the target for 4 seconds (6 with 3 or more Valor) and snares enemies within 300 of it by 40%. "
                          "20-second reuse." + VALOR.format(what="damage"),
    "Pendragon.Remake08": "Holy ground at your feet for 30 seconds: burns enemies inside and heals allies 3% every 3 seconds. "
                          "Each pulse: +10% damage and healing per Valor held.",
    "Pendragon.Sovereign21": "Throw a royal blade and teleport beside an enemy within 2100. Strong primary impact, half damage around it; "
                             "+10 critical chance on the primary hit from stealth. Earns 1 Valor; +10% damage per Valor held. "
                             "Instant; 15-second reuse.",
    "Tuatha.Remake08": "A ring at your feet for 30 seconds: burns enemies inside and heals allies 3% every 3 seconds. In the Unseelie "
                       "Court it also drains half its damage to you; in the Seelie Court it heals 6%.",
}

# Icon = a spells.csv row id (the client draws that row's icon). One icon per button.
ICONS = {
    "Pendragon.Sovereign06": 5822,    # Royal Procession: Excalibur's Bastion (was Crownfire Lance's)
    "Pendragon.Sovereign20": 6804,    # Lion of Albion: Black Lion Shapechange (was Crownfire Lance's)
    "Pendragon.Remake04": 1691,       # Consecrated Armament: Heavenly Strike (was Royal Lion's Holy Might)
    "Pendragon.Sovereign13": 1504,    # Armor of the Crown: Holy Shield (was Holy Might)
    "Pendragon.Remake01": 14396,      # Guard of the Crown: Prophet's Faith shield (was Holy Barrier, shared by three)
    "Pendragon.Sovereign12": 7036,    # Will of the Pendragon: Divine Intervention (was Holy Barrier)
    "Pendragon.Sovereign22": 11206,   # Veil of Avalon: Veil Path (was Holy Barrier)
    "Tuatha.Sovereign06": 5483,       # Court of the Sidhe: Fortification of Will, a green tree (was Power of the Champion)
    "Tuatha.Sovereign20": 13080,      # Otherworld Ascendant, the Morrigan: Call of the Raven (was Power of the Champion)
}
SPELL_ICONS = {65609: 10801, 65610: 780}   # Seelie Court: 4 Leaf Clover; Unseelie Court: Dreams of Winter (were shared)
FAERIE_STEP = ("Tuatha.Remake05", 7298)     # Phase Shift effect and icon (was Growing Vines, a root)
# Tuatha style procs, one effect per weapon line (all 61 were the Champion's Blast of Valiance 5433).
STYLE_EFFECTS = {"Scythe": 4106, "Blades": 5274, "Shields": 5433}   # Moon Blast, Thornsprout, Blast of Valiance

CASTER_RAS = ("AtlasOF_MasteryOfMagery", "AtlasOF_WildMinion", "AtlasOF_StaticTempest", "AtlasOF_AugAcuity")
MASTERY_NAMES = {"Pendragon_StormMastery": "Royal Mastery", "Tuatha_StormMastery": "Sidhe Mastery"}


def rows(db, sql, *args):
    return db.execute(sql, args).fetchall()


def line_spells(db, line):
    return rows(db, "SELECT s.Spell_ID, s.Description, s.Icon, s.ClientEffect FROM Spell s JOIN LineXSpell l ON l.SpellID=s.SpellID "
                    "WHERE l.LineName=?", line)


def edits_for(db):
    out = []

    def spell(key, **values):
        out.append({"table": "Spell", "key": key, "set": values})

    for line, text in DESCRIPTIONS.items():
        found = line_spells(db, line)
        if not found:
            raise ValueError(f"{line} has no spells in this world")
        for key, description, _, _ in found:
            if description != text:
                spell(key, Description=text)
    for line, icon in ICONS.items():
        found = line_spells(db, line)
        if not found:
            raise ValueError(f"{line} has no spells in this world")
        for key, _, current, _ in found:
            if current != icon:
                spell(key, Icon=icon)
    for spell_id, icon in SPELL_ICONS.items():
        for key, current in rows(db, "SELECT Spell_ID, Icon FROM Spell WHERE SpellID=?", spell_id):
            if current != icon:
                spell(key, Icon=icon)
    line, effect = FAERIE_STEP
    for key, _, icon, current in line_spells(db, line):
        if (icon, current) != (effect, effect):
            spell(key, ClientEffect=effect, Icon=effect)
    for style in json.loads((HERE / "tuatha_remake_styles.json").read_text()):
        effect = STYLE_EFFECTS[style["line"]]
        for key, icon, current in rows(db, "SELECT Spell_ID, Icon, ClientEffect FROM Spell WHERE SpellID=?", TUATHA_STYLE_EFFECT + style["index"]):
            if (icon, current) != (effect, effect):
                spell(key, ClientEffect=effect, Icon=effect)

    for cls in (PEND, TUA):
        for ra in CASTER_RAS:
            for (key,) in rows(db, "SELECT ClassXRealmAbility_Atlas_ID FROM ClassXRealmAbility_Atlas WHERE CharClass=? AND AbilityKey=?", cls, ra):
                out.append({"table": "ClassXRealmAbility_Atlas", "delete": key})
    for ability, name in MASTERY_NAMES.items():
        for (key, current) in rows(db, "SELECT AbilityID, Name FROM Ability WHERE KeyName=?", ability):
            if current != name:
                out.append({"table": "Ability", "key": str(key), "set": {"Name": name}})
    return out


def main():
    db_path, out_path = Path(sys.argv[1]), Path(sys.argv[2])
    with sqlite3.connect(db_path.resolve().as_uri() + "?mode=ro", uri=True) as db:
        edits = edits_for(db)
    out_path.write_text(json.dumps(edits, indent=1))
    counts = {}
    for e in edits:
        op = "delete" if "delete" in e else "update"
        counts[(e["table"], op)] = counts.get((e["table"], op), 0) + 1
    for (table, op), n in sorted(counts.items()):
        print(f"{op:7} {n:4}  {table}")


if __name__ == "__main__":
    main()
