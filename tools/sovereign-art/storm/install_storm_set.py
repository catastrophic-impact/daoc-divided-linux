r"""The Storm Regalia art (Storm Sovereign, class 64): the Stormforged v20 armor and cloak skins (owner's polish passes of
2026-11-04), as the release client patch ships them.

  plan    : python -B install_storm_set.py plan     <client app folder>
  install : python -B install_storm_set.py install  <client app folder>
  rollback: python -B install_storm_set.py rollback <client app folder> <backup folder>

This is the part of the Stormforged concept kit (DAoC Offline\Stormforged-armor-concepts\v20-polished-set, install_test_copy.py
and its follow-ups) that the Storm Regalia items use, with the same IDs and rows the owner's modern test client got from it;
that client already has it (and more), so the installer refuses it. No meshes are added: the pieces draw stock meshes
with our skins. Private data under new IDs:
  items\\pskins\\pskin061.mpk      NEW  pskins textures (cata armor, cata cloak, classic DXT3 cloak, winged helm)
  figures\\Mskins\\mskin070.mpk    NEW  mskins textures (cata armor BaseTex, read by /hood)
  mskins.csv 2740-2745, pskins.csv 5790-5797 and 5800, objects.csv 4929-4933, 4937 and 4942: NEW rows (no stock row changes)
objects: 4929 hauberk, 4930 legs, 4931 sleeves, 4932 gauntlets, 4933 boots (cloned from 3370-3374, the server's hauberk uses
Extension 5), 4937 cloak (326, Cloak# 1; named for the attachment test it began as, its attachment cleared), 4942 winged
helm (2224 ToA Winged Helm (Mid), its per-race toaHelm_Winged meshes). The helm's skin is the concept kit's
(paint_wingedhelm_v24.py): our shell, brow band, gem and crest, and wings that are the stock helm skin's feather drawing
recoloured. Shipping it is the owner's decision (2026-11-08), the second exception to shipping no retail art after Marfach.
Left out: the Storm Crown 4934, wolf cloaks 4935/4936 and wolf helm 4941 (no item
uses them; custom meshes and fig3map edits). The server items are storm_regalia_* (source/tools/sovereign/storm_regalia_edits.py)."""
from __future__ import annotations

import hashlib
import json
import sys
import time
from datetime import datetime
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import regalia_client as S  # noqa: E402  (need, table, add_row, client_closed, rollback, archive, encoders, REAL, MODERN_TEST)

HERE = Path(__file__).resolve().parent
TEX = HERE / "textures"
LABEL = "Stormforged"  # pskins row names: "<LABEL> <texture>"
PSKIN_ARC, MSKIN_ARC = 61, 70
need = S.need


def img(name, size):
    return Image.open(TEX / name).convert("RGB").resize(size, Image.LANCZOS)


# mskins: id, texture, source, size (stock cata sizes: New Thane epic in mskin049)
MSKINS = [(2740, "sf_rw_mbody_m.dds", "body_m.png", (256, 512)), (2741, "sf_rw_mbody_f.dds", "body_f.png", (256, 512)),
          (2742, "sf_rw_marms.dds", "arms.png", (256, 256)), (2743, "sf_rw_mlegs.dds", "legs.png", (256, 256)),
          (2744, "sf_rw_mgloves.dds", "gloves.png", (512, 256)), (2745, "sf_rw_mboots.dds", "boots.png", (512, 256))]
# pskins: id, texture, source, size, stock row cloned, BaseTex (our mskins row), dxt3?
PSKINS = [(5790, "sf_rw_body_m.dds", "body_m.png", (256, 512), "5364", 2740, False),
          (5791, "sf_rw_body_f.dds", "body_f.png", (256, 512), "5363", 2741, False),
          (5792, "sf_rw_arms.dds", "arms.png", (256, 256), "5341", 2742, False),
          (5793, "sf_rw_legs.dds", "legs.png", (256, 256), "5346", 2743, False),
          (5794, "sf_rw_gloves.dds", "gloves.png", (512, 256), "5336", 2744, False),
          (5795, "sf_rw_boots.dds", "boots.png", (512, 256), "5365", 2745, False),
          (5796, "sf_wolfcloak.dds", "cloak.png", (512, 1024), "4972", 0, False),       # cata cloak, UV set 1
          (5797, "sf_wolfcloak256.dds", "cloak.png", (256, 256), "618", 0, True),        # classic col 11 (hooded mantle), DXT3
          (5800, "sf_wingedhelm.dds", "helm.png", (512, 256), "2683", 0, False)]         # winged helm (paint_storm_helm.py)
# objects: id, stock row cloned, name, column changes
OBJECTS = [(4929, "3370", "Stormforged Rune-Warlord Hauberk", {66: "5790", 67: "5791", 73: "6"}),
           (4930, "3371", "Stormforged Rune-Warlord Legs", {70: "5793"}),
           (4931, "3372", "Stormforged Rune-Warlord Sleeves", {68: "5792"}),
           (4932, "3373", "Stormforged Rune-Warlord Gauntlets", {69: "5794", 74: "6"}),
           (4933, "3374", "Stormforged Rune-Warlord Boots", {71: "5795", 75: "6"}),
           (4937, "326", "Stormforged Wolf Cloak (attachment test)", {11: "5797", 16: "1", 72: "5796", 78: "0"}),
           (4942, "2224", "Stormforged Winged Helm", {12: "5800", 22: "5800"})]


def build(client: Path):
    return S.build(client, sys.modules[__name__], int(time.time()))


def targets(client):
    return client / f"items/pskins/pskin{PSKIN_ARC:03d}.mpk", client / f"figures/Mskins/mskin{MSKIN_ARC:03d}.mpk"


def main():
    if len(sys.argv) >= 4 and sys.argv[1] == "rollback":
        S.rollback(Path(sys.argv[2]).resolve(), Path(sys.argv[3]).resolve()); return
    if len(sys.argv) < 3 or sys.argv[1] not in ("plan", "install"):
        print(__doc__); return
    client = Path(sys.argv[2]).resolve()
    need(all(client != r.resolve() for r in S.REAL), "this is a real client folder (the modern test client already has this art)")
    need((client / "gamedata.mpk").exists() and (client / "game.dll").exists(), "not a client folder")
    for t in targets(client):
        need(not t.exists(), f"{t.name} already exists")
    blob, pak, mpak = build(client)
    print(f"plan ok: gamedata {len(blob)} bytes, pskin{PSKIN_ARC:03d} {len(pak)}, mskin{MSKIN_ARC:03d} {len(mpak)}; "
          f"{len(MSKINS)} mskins, {len(PSKINS)} pskins, {len(OBJECTS)} objects rows")
    if sys.argv[1] != "install":
        return
    game = client / "gamedata.mpk"
    manifest = {"when": datetime.now().isoformat(timespec="seconds"), "client": str(client),
                "gamedata_before": hashlib.sha256(game.read_bytes()).hexdigest(), "gamedata_after": hashlib.sha256(blob).hexdigest()}
    backup = client.parents[1] / "backups" / ("client-storm-set-" + datetime.now().strftime("%Y%m%d-%H%M%S"))
    backup.mkdir(parents=True)
    (backup / "gamedata.mpk").write_bytes(game.read_bytes())
    manifest["new_files"] = [str(t.relative_to(client)) for t in targets(client)]
    (backup / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    for t, b in zip(targets(client), (pak, mpak)):
        t.write_bytes(b)
    game.write_bytes(blob)
    need(hashlib.sha256(game.read_bytes()).hexdigest() == manifest["gamedata_after"], "installed catalog differs")
    (client / "storm-set-install.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print("installed into", client)
    print(f'rollback: python -B "{Path(__file__).resolve()}" rollback "{client}" "{backup}"')


if __name__ == "__main__":
    main()
