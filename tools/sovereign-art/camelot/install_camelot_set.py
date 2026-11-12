r"""Install the Regalia of Camelot art (Pendragon Sovereign; owner pick B 2026-11-07: gilded plate, crimson enamel, red dragon) into the
modern 1.130 client.

  plan    : python -B install_camelot_set.py plan     <client app folder> [--modern-test]
  install : python -B install_camelot_set.py install  <client app folder> [--modern-test]
  rollback: python -B install_camelot_set.py rollback <client app folder> <backup folder>

Refuses the real client folders; the owner's modern test client only with --modern-test. No meshes are added: the
pieces draw stock meshes with private skins. Private data under new IDs (free on 2026-11-07):
  items\pskins\pskin063.mpk      NEW  pskins textures (cata armor, cata cloak, classic DXT3 cloak, helm)
  figures\Mskins\mskin072.mpk    NEW  mskins textures (cata armor BaseTex, read by /hood)
  mskins.csv 2756-2761, pskins.csv 5819-5827, objects.csv 4957-4963: NEW rows (no stock row changes)
objects: 4957 breastplate, 4958 legs, 4959 sleeves, 4960 gauntlets, 4961 boots (cloned from 428-432 as the Tara set, so
the pieces draw the same Body05 (server Extension 5) / Legs01 / Arms01 / Gloves06 / Boots06 meshes the skins are painted
on), 4962 helm (93 Albion Plate Helmet 2 (Full), albHelm03 meshes), 4963 cloak (57, Cloak# 1).
Then point the server items at them: source/tools/sovereign/camelot_regalia_edits.py --update (World Workshop)."""
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
LABEL = "Camelot"  # pskins row names: "<LABEL> <texture>"
PSKIN_ARC, MSKIN_ARC = 63, 72
need = S.need


def img(name, size):
    return Image.open(TEX / name).convert("RGB").resize(size, Image.LANCZOS)


# mskins: id, texture, source, size (stock cata sizes)
MSKINS = [(2756, "camelot_mbody_m.dds", "body_m.png", (256, 512)), (2757, "camelot_mbody_f.dds", "body_f.png", (256, 512)),
          (2758, "camelot_marms.dds", "arms.png", (256, 256)), (2759, "camelot_mlegs.dds", "legs.png", (256, 256)),
          (2760, "camelot_mgloves.dds", "gloves.png", (512, 256)), (2761, "camelot_mboots.dds", "boots.png", (512, 256))]
# pskins: id, texture, source, size, stock row cloned, BaseTex (our mskins row), dxt3?
PSKINS = [(5819, "camelot_body_m.dds", "body_m.png", (256, 512), "4330", 2756, False),
          (5820, "camelot_body_f.dds", "body_f.png", (256, 512), "4331", 2757, False),
          (5821, "camelot_arms.dds", "arms.png", (256, 256), "4332", 2758, False),
          (5822, "camelot_legs.dds", "legs.png", (256, 256), "4334", 2759, False),
          (5823, "camelot_gloves.dds", "gloves.png", (512, 256), "4333", 2760, False),
          (5824, "camelot_boots.dds", "boots.png", (512, 256), "4335", 2761, False),
          (5825, "camelot_cloak.dds", "cloak.png", (512, 1024), "4972", 0, False),      # cata cloak, UV set 1
          (5826, "camelot_cloak256.dds", "cloak.png", (256, 256), "362", 0, True),     # classic col 11 (cloak 57's), DXT3
          (5827, "camelot_helm.dds", "helm.png", (256, 256), "353", 0, False)]         # helm (classic cols 12 and 22)
# objects: id, stock row cloned, name, column changes (every inherited skin column is decided)
OBJECTS = [(4957, "428", "Regalia of Camelot Breastplate", {66: "5819", 67: "5820"}),
           (4958, "429", "Regalia of Camelot Greaves", {70: "5822"}),
           (4959, "430", "Regalia of Camelot Sleeves", {68: "5821"}),
           (4960, "431", "Regalia of Camelot Gauntlets", {69: "5823"}),
           (4961, "432", "Regalia of Camelot Boots", {71: "5824"}),
           (4962, "93", "Regalia of Camelot Crown Helm", {12: "5827", 22: "5827"}),
           (4963, "57", "Regalia of Camelot Mantle", {11: "5826", 16: "1", 72: "5825"})]


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
    real_ok = "--modern-test" in sys.argv and client == S.MODERN_TEST.resolve()
    need(real_ok or all(client != r.resolve() for r in S.REAL), "this is a real client folder; only the modern test client with --modern-test")
    need((client / "gamedata.mpk").exists() and (client / "game.dll").exists(), "not a client folder")
    if real_ok:
        S.client_closed()
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
    backup = client.parents[1] / "backups" / ("client-camelot-set-" + datetime.now().strftime("%Y%m%d-%H%M%S"))
    backup.mkdir(parents=True)
    (backup / "gamedata.mpk").write_bytes(game.read_bytes())
    manifest["new_files"] = [str(t.relative_to(client)) for t in targets(client)]
    (backup / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    for t, b in zip(targets(client), (pak, mpak)):
        t.write_bytes(b)
    game.write_bytes(blob)
    need(hashlib.sha256(game.read_bytes()).hexdigest() == manifest["gamedata_after"], "installed catalog differs")
    (client / "camelot-set-install.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print("installed into", client)
    print(f'rollback: python -B "{Path(__file__).resolve()}" rollback "{client}" "{backup}"')


if __name__ == "__main__":
    main()
