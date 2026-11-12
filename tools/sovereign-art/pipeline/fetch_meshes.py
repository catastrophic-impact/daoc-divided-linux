"""Copy what the Sovereign art painters read from the game client into the work folder (read only on the client).

  nif/  every figure mesh the bakes and previews use: Body01-08, Arms01, Legs01, Gloves01-08, Boots01-08, Cloak01-03
        and the Head01 meshes, all races and both sexes (from figures/fig3/*.mpk)
  tex/  the stock Albion full plate helm skin (pskins row 353, b_plt1_helm2), whose eye slot the Camelot helm keeps

    python tools/sovereign-art/pipeline/fetch_meshes.py        (OFFLINE_DAOC_CLIENT picks another client folder)
"""
import re
import sys
from io import BytesIO
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import paths  # noqa: E402
import daoc_catalog as d  # noqa: E402
from daoc_catalog import archive  # noqa: E402
from PIL import Image  # noqa: E402

PART = re.compile(r"^(body0[1-8]|arms01|legs01|gloves0[1-8]|boots0[1-8]|cloak0[1-3])_[a-z]+_[mf]\.nif$|_head01\.nif$")
LATER = re.compile(r"^cloak0[1-3]_(nor|dwa|tro)_[mf]\.nif$")
SKINS = ("353",)


def main():
    # Some meshes are in more than one archive. The first archive's copy is the one the bakes and previews were made
    # from, except the Norse, Dwarf and Troll cloaks: the mesh bakes' cloak union (Cloak01-03_nor_m) used the later
    # copies in fig010 (lower-case names there).
    found = {}
    for mpk in sorted((paths.CLIENT / "figures" / "fig3").glob("*.mpk"), key=lambda f: f.name.lower()):
        try:
            entries = archive.read(mpk.read_bytes())[1]
        except Exception:
            continue
        for e in entries:
            k = e.name.lower()
            if PART.search(k) and (k not in found or LATER.match(k)):
                found[k] = e
    for f in paths.NIF.iterdir():
        if f.name.lower() in found:
            f.unlink()
    for e in found.values():
        (paths.NIF / e.name).write_bytes(e.data)
    print(f"meshes: {len(found)} in {paths.NIF}")
    # each skin from the archive its pskins row names (column 4): the same texture name is in several archives at
    # different sizes (b_plt1_helm2 is 128x128 in pskin001, 256x256 in pskin006 and pskin007; row 353 says 7)
    files = d.gamedata()
    psk = {r[0]: r for r in d.csv_rows(files, "pskins.csv") if r}
    want = {}
    for s_id in SKINS:
        row = psk[s_id]
        stem = row[2].lower().rsplit(".", 1)[0]
        mpk = paths.CLIENT / "items" / "pskins" / f"pskin{int(row[4]):03d}.mpk"
        e = next((e for e in archive.read(mpk.read_bytes())[1] if e.name.lower().rsplit(".", 1)[0] == stem), None)
        if e is None:
            want[stem] = s_id; continue
        Image.open(BytesIO(e.data)).convert("RGBA").save(paths.TEX / f"{stem}.png")
        print("skin", s_id, mpk.name, e.name, "->", paths.TEX / f"{stem}.png")
    if want:
        raise SystemExit(f"skins not found in the client: {sorted(want)}")


if __name__ == "__main__":
    main()
