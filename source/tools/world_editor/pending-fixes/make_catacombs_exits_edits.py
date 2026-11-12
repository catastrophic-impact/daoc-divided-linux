"""Catacombs / Darkness Rising exits (2026-11-09 night, owner: "can't leave" in Catacombs areas).

The 1.130 client sends a ZonePoint id when a player walks through a zone trigger. In the Catacombs and Darkness
Rising regions (Regions.Expansion 3 and 4) about 213 trigger ids have no ZonePoint row in our world, so walking
through them does nothing. Eve of Darkness (live captures, db-public) has rows for most of them. This writes
catacombs-exits-2026-11-09.edits.json inserting those rows, per realm, as EoD has them.

Left out: rows whose target region we don't have or whose target is 0,0 (instance doors), and rows with a
ClassType our server lacks. They are listed in the report.

    py -I make_catacombs_exits_edits.py --world <opendaoc.sqlite3.db> --reference <reference.sqlite3>
"""
import argparse
import json
import sqlite3
from pathlib import Path

KNOWN_CLASSTYPES = {"", "0", None, "DOL.GS.Scripts.AdventureWingJumpPoint"}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--world", required=True)
    ap.add_argument("--reference", required=True)
    ap.add_argument("--out", default=str(Path(__file__).with_name("catacombs-exits-2026-11-09.edits.json")))
    a = ap.parse_args()
    w = sqlite3.connect(f"file:{a.world}?mode=ro", uri=True)
    r = sqlite3.connect(f"file:{a.reference}?mode=ro", uri=True)

    expansion = dict(w.execute("select RegionID, Expansion from Regions"))
    zone_region = dict(w.execute("select ZoneID, RegionID from Zones"))
    ours = {x[0] for x in w.execute("select Id from ZonePoint")}
    names = {}
    for zone, pid, name in r.execute("select zone_id, point_id, name from src_c130_zonejumps"):
        region = zone_region.get(zone)
        if pid is not None and region is not None and expansion.get(region, 0) >= 3 and pid not in ours:
            names.setdefault(pid, (zone, name))

    edits, skipped, covered, seen = [], [], set(), set()
    rows = r.execute("select Id, Realm, TargetX, TargetY, TargetZ, TargetRegion, TargetHeading, SourceX, SourceY, "
                     "SourceZ, SourceRegion, ClassType from src_eodb_ZonePoint order by Id, Realm").fetchall()
    for (pid, realm, tx, ty, tz, treg, th, sx, sy, sz, sreg, ctype) in rows:
        if pid not in names:
            continue
        why = None
        if ctype not in KNOWN_CLASSTYPES:
            why = f"class {ctype}"
        elif treg not in expansion:
            why = f"target region {treg} not in our world"
        elif tx == 0 and ty == 0:
            why = "no target"
        if why:
            skipped.append((pid, realm, names[pid][1], why))
            continue
        key = f"eod-catexit-{pid}-{realm}"
        if key in seen:
            skipped.append((pid, realm, names[pid][1], f"second EoD row for this realm (target {treg} {tx},{ty}), first kept"))
            continue
        seen.add(key)
        edits.append({"table": "ZonePoint", "key": key, "insert": {
            "Id": pid, "TargetX": tx, "TargetY": ty, "TargetZ": tz, "TargetRegion": treg, "TargetHeading": th or 0,
            "SourceX": sx or 0, "SourceY": sy or 0, "SourceZ": sz or 0, "SourceRegion": sreg or 0, "Realm": realm or 0,
            "ClassType": "" if ctype in ("0", None) else ctype.replace("DOL.GS.Scripts.AdventureWingJumpPoint", "DOL.GS.ServerRules.AdventureWingJumpPoint"), "ZonePoint_ID": key}})
        covered.add(pid)

    Path(a.out).write_text(json.dumps(edits, indent=1), encoding="utf-8")
    missing = sorted(set(names) - covered)
    report = [f"{len(edits)} ZonePoint rows for {len(covered)} of {len(names)} trigger ids", "", "Skipped EoD rows:"]
    report += [f"  {p} realm {rl} {n!r}: {why}" for p, rl, n, why in skipped]
    report += ["", "Trigger ids still with no destination:"] + [f"  {p} zone {names[p][0]} {names[p][1]!r}" for p in missing]
    Path(a.out).with_suffix("").with_suffix(".report.txt").write_text("\n".join(report) + "\n", encoding="utf-8")
    print("\n".join(report[:1]))
    print(f"{len(skipped)} skipped, {len(missing)} ids still open")


if __name__ == "__main__":
    main()
