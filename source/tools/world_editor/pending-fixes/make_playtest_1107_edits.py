"""Builds the World Workshop edits files for the 2026-11-07 playtest data fixes (read-only on the world).

    python make_playtest_1107_edits.py <world.db>

Writes, next to this script:
- playtest-1107-toa-zonepoints.edits.json: Atlantis ZonePoint Ids 119-143 carry the wrong realm copy as their
  source (Alb rows say Midgard 30, Mid rows say Albion 73) and 14 rows have no source at all. Players jump by Id,
  so only bots (which route by SourceRegion) are affected. Each row gets its own realm's copy of the region,
  and rows without a usable source take the coordinates of a sibling that has one.
- playtest-1107-haven-npcs.edits.json: aggressive realm-0 Stable Masters, Bakabti, the level 99 trogo near the
  Aerus djinn and the Hibernian marker props stop attacking players at the Havens.
- playtest-1107-mauler-wraps.edits.json: fist wraps marked left-handed (Hand 2) get the left-hand Item_Type;
  the training fist wrap is a right-hand item.
Then: python ../cli.py --game <game> plan <file>  (applies nothing).
"""
import json
import sqlite3
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REALM_OFFSET = {1: 43, 2: 0, 3: 100}  # Midgard copy 30-47, Albion +43 (73-90), Hibernia +100 (130-147)
SHARED_REGIONS = {91}                 # Celestius has no realm copies
PEACE = 16


def base_region(region):
    if 30 <= region <= 47:
        return region
    if 73 <= region <= 90:
        return region - 43
    if 130 <= region <= 147:
        return region - 100
    return None


def zonepoint_edits(db):
    rows = db.execute("select ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetX, TargetY "
                      "from ZonePoint where Id between 119 and 143 order by Id, Realm").fetchall()
    by_id = {}
    for row in rows:
        by_id.setdefault(row[1], []).append(row)
    edits = []
    for zp_id, group in by_id.items():
        def usable(r):
            return r[3] != 0 and (r[4], r[5]) != (0, 0) and (r[4], r[5]) != (r[7], r[8])

        good = [r for r in group if usable(r)]
        if not good:
            continue  # no row says where the point is (Id 136): leave it
        shared = next((r[3] for r in good if r[3] in SHARED_REGIONS), None)
        bases = {base_region(r[3]) for r in good if r[3] not in SHARED_REGIONS} - {None}
        if shared is None and len(bases) != 1:
            print(f"skip Id {zp_id}: sources disagree {sorted(bases)}")
            continue
        sample = good[0]
        for row in group:
            key, _, realm, region = row[0], row[1], row[2], row[3]
            want = shared if shared is not None else next(iter(bases)) + REALM_OFFSET[realm]
            change = {}
            if region != want:
                change["SourceRegion"] = want
            if not usable(row):
                change.update({"SourceX": sample[4], "SourceY": sample[5], "SourceZ": sample[6]})
            if change:
                edits.append({"table": "ZonePoint", "key": key, "set": change})
    return edits


def haven_edits(db):
    names = ("Bakabti", "trogo zontanos mageia", "Light of Ra", "Marker Light")
    rows = db.execute(
        "select Mob_ID, Name, Flags, Guild from Mob where Realm = 0 and AggroLevel > 0 and "
        f"(Guild = 'Stable Master' or Name in ({','.join('?' * len(names))}))", names).fetchall()
    edits = []
    for mob_id, name, flags, guild in rows:
        change = {"AggroLevel": 0, "AggroRange": 0}
        if guild == "Stable Master" or name in ("Light of Ra", "Marker Light"):
            change["Flags"] = (flags or 0) | PEACE
        edits.append({"table": "Mob", "key": mob_id, "set": change})
    return edits


def wrap_edits(db):
    rows = db.execute("select Id_nb from ItemTemplate where Object_Type = 27 and Hand = 2 and Item_Type = 10").fetchall()
    edits = []
    for (id_nb,) in rows:
        if id_nb == "local-mauler-starter-right":
            edits.append({"table": "ItemTemplate", "key": id_nb, "set": {"Hand": 0}})
        else:
            edits.append({"table": "ItemTemplate", "key": id_nb, "set": {"Item_Type": 11}})
    return edits


def main():
    world = Path(sys.argv[1])
    db = sqlite3.connect(f"file:{world.as_posix()}?mode=ro", uri=True)
    for name, edits in (("playtest-1107-toa-zonepoints", zonepoint_edits(db)),
                        ("playtest-1107-haven-npcs", haven_edits(db)),
                        ("playtest-1107-mauler-wraps", wrap_edits(db))):
        out = HERE / f"{name}.edits.json"
        out.write_text(json.dumps(edits, indent=1, ensure_ascii=False), encoding="utf-8")
        print(f"{out.name}: {len(edits)} edits")


if __name__ == "__main__":
    main()
