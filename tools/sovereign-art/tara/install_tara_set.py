r"""Install the Regalia of Tara art (concept A, emerald court, with the stag roundel cloak) into the modern 1.130 client.
Owner picks 2026-11-06: armor A, cloak v1.

  plan    : python -B install_tara_set.py plan     <client app folder> [--modern-test]
  install : python -B install_tara_set.py install  <client app folder> [--modern-test]
  update  : python -B install_tara_set.py update   <client app folder> [--modern-test]   (over an earlier install:
            adds the new rows, rebuilds the two skin archives, adds the meshes; its own backup and rollback)
  rollback: python -B install_tara_set.py rollback <client app folder> <backup folder>

Refuses the real client folders; the owner's modern test client only with --modern-test. The armor draws the stock
Celtic scale meshes with private skins; the helm draws private copies of the Lug helmet, refitted to player heads. Private data under new IDs (free on 2026-11-06):
  items\\pskins\\pskin062.mpk      NEW  pskins textures (cata armor, cata cloak, classic DXT3 cloak, helm)
  figures\\Mskins\\mskin071.mpk    NEW  mskins textures (cata armor BaseTex, read by /hood)
  items\Tara_helm01_<sex>_<race>.nif  NEW  12 refitted copies of the stock Lug helmet (pipeline/fit_lugh.py)
  mskins.csv 2750-2755, pskins.csv 5810-5818 and 5828, objects.csv 4950-4956 and 4964, items.csv 44-55: NEW rows
  (no stock row changes)
objects: 4950 hauberk (cloned from 428 Celtic Scaled 3 Vest; the server item uses Extension 5 = Body05, as the Storm
hauberk), 4951 legs (429), 4952 sleeves (430), 4953 gloves (431), 4954 boots (432), 4955 helm (840 Celtic scale helm 3, the
2026-11-06 look, kept as DR-dev-0.1e shipped it), 4964 helm (pass 3 2026-11-08, the Crown of Lugh: 4827 LugHelmet with its
per-race columns 79-90 pointed at items 44-55, the refitted copies; skin pskins 5828),
4956 cloak (326, Cloak# 1).
Then point the server items at them: source/tools/sovereign/tara_regalia_edits.py --update (World Workshop)."""
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
LABEL = "Tara"  # pskins row names: "<LABEL> <texture>"
PSKIN_ARC, MSKIN_ARC = 62, 71
need = S.need


def img(name, size):
    return Image.open(TEX / name).convert("RGB").resize(size, Image.LANCZOS)


# mskins: id, texture, source, size (stock cata sizes)
MSKINS = [(2750, "tara_mbody_m.dds", "body_m.png", (256, 512)), (2751, "tara_mbody_f.dds", "body_f.png", (256, 512)),
          (2752, "tara_marms.dds", "arms.png", (256, 256)), (2753, "tara_mlegs.dds", "legs.png", (256, 256)),
          (2754, "tara_mgloves.dds", "gloves.png", (512, 256)), (2755, "tara_mboots.dds", "boots.png", (512, 256))]
# pskins: id, texture, source, size, stock row cloned, BaseTex (our mskins row), dxt3?
PSKINS = [(5810, "tara_body_m.dds", "body_m.png", (256, 512), "4330", 2750, False),
          (5811, "tara_body_f.dds", "body_f.png", (256, 512), "4331", 2751, False),
          (5812, "tara_arms.dds", "arms.png", (256, 256), "4332", 2752, False),
          (5813, "tara_legs.dds", "legs.png", (256, 256), "4334", 2753, False),
          (5814, "tara_gloves.dds", "gloves.png", (512, 256), "4333", 2754, False),
          (5815, "tara_boots.dds", "boots.png", (512, 256), "4335", 2755, False),
          (5816, "tara_cloak.dds", "cloak.png", (512, 1024), "4972", 0, False),      # cata cloak, UV set 1
          (5817, "tara_cloak256.dds", "cloak.png", (256, 256), "618", 0, True),      # classic col 11 (hooded mantle), DXT3
          (5818, "tara_helm.dds", "helm.png", (256, 256), "615", 0, False),          # 4955 helm (classic cols 12 and 22)
          (5828, "tara_helm_lugh.dds", "helm_lugh.png", (256, 256), "615", 0, False)]  # 4964 Crown of Lugh
LUGH_MESHES = [  # (stock NIF, its sha256, transform bytes, refit bytes): fit_lugh.py
    ("Lum_helm01_M_cel.nif", "d69d23282803e06b3cd0875765312dac78c4040b7ff15e5ac93a75b2f7821e95",
     "9e112b3cd294cf3e7042abc00000803f0000000000000000000000000000803f0000000000000000000000000000803f0000803f",
     "00534d3c87fcacbf912c9ac00000803f0000000000000000000000000000803f0000000000000000000000000000803f9a99993f"),
    ("Lum_helm01_F_cel.nif", "b588f67206360faf6642f9d6a45e59e8aa6a679f60cb7ff4cbd43acaf843c298",
     "3f242b3c79297b3de05f99c00000803f0000000000000000000000000000803f0000000000000000000000000000803f0000803f",
     "51684d3c40aed4bf724689c00000803f0000000000000000000000000000803f0000000000000000000000000000803f9a99993f"),
    ("Lum_helm01_M_elf.nif", "45a77e98657626fe5b8554d80b00379cb3a221b6be397732febb016e98024437",
     "d9212b3c59ceaa3e90cea4c00000803f0000000000000000000000000000803f0000000000000000000000000000803f0000803f",
     "d7654d3cd66eabbfb07591c00000803f0000000000000000000000000000803f0000000000000000000000000000803f9a99993f"),
    ("Lum_helm01_F_elf.nif", "655c5cf2e911272ae735bd910a3adac753768bbf646b0591084be8623b7321f1",
     "db212b3cf6c2a23ea0672dc00000803f0000000000000000000000000000803f0000000000000000000000000000803f0000803f",
     "81654d3cba02aabfedb6dbbf0000803f0000000000000000000000000000803f0000000000000000000000000000803f9a99993f"),
    ("Lum_helm01_M_lur.nif", "e4119500658d35a8ae51643334286505c78f89f16f12ea5b3e3b3ef486573b11",
     "f6212b3cf2febfbe1f07aec00000803f0000000000000000000000000000803f0000000000000000000000000000803f0000803f",
     "be664d3c153a12c060b398c00000803f0000000000000000000000000000803f0000000000000000000000000000803f9a99993f"),
    ("Lum_helm01_F_lur.nif", "c820aac125152f3ca4274ab5ffdccd1abfd05004bed0e28e27df1f1d58683aaf",
     "db112b3c34f9553e7f0381c00000803f0000000000000000000000000000803f0000000000000000000000000000803f0000803f",
     "a6534d3c44b5d9bfb3c145c00000803f0000000000000000000000000000803f0000000000000000000000000000803f9a99993f"),
    ("Lum_helm01_M_fir.nif", "46d0a7b3a45a979254066b389c6093e44e0fe297a01bc9cdad62d269475712bc",
     "39252b3c67863ebe5fc5d5c00000803f0000000000000000000000000000803f0000000000000000000000000000803f0000803f",
     "70694d3c33b0f4bfc461cfc00000803f0000000000000000000000000000803f0000000000000000000000000000803f9a99993f"),
    ("Lum_helm01_F_fir.nif", "181e62744ed483fad678df5f2bbd689dc8a1ea5c67e4017c1758521599e76f93",
     "ec242b3c410e233ef053a7c00000803f0000000000000000000000000000803f0000000000000000000000000000803f0000803f",
     "bc694d3c00abcabffc6d9ec00000803f0000000000000000000000000000803f0000000000000000000000000000803f9a99993f"),
    ("Lum_helm01_M_syl.nif", "7e231a47b2ec8744191291859131d90428ededb13f9363b40963639188ea4ba4",
     "6b012b3cf0d89c3e2f289ac00000803f0000000000000000000000000000803f0000000000000000000000000000803f0000803f",
     "233f4d3c4d78b6bfdf1088c00000803f0000000000000000000000000000803f0000000000000000000000000000803f9a99993f"),
    ("Lum_helm01_F_syl.nif", "3320d6cabc2a289080fe58cb53bcc1ef5e04d0ccc64a0eaee79f01802b28db09",
     "dd212b3c8501ae3ee0ab33c00000803f0000000000000000000000000000803f0000000000000000000000000000803f0000803f",
     "c2654d3c2378acbf9c2decbf0000803f0000000000000000000000000000803f0000000000000000000000000000803f9a99993f"),
    ("Lum_helm01_M_sha.nif", "35eab2c377e1b4df0ac0570e64d6c09fc2c4c256703a98ee0697e55463be30de",
     "8e212b3cb4c0d63ef0f5a9c00000803f0000000000000000000000000000803f0000000000000000000000000000803f0000803f",
     "22664d3c8f31abbf11e098c00000803f0000000000000000000000000000803f0000000000000000000000000000803f9a99993f"),
    ("Lum_helm01_F_sha.nif", "f4afdda3d8a3a8f4d89889b4371ba13bd1d955b49d55a122a1606d2b79e7ff05",
     "55202b3c99faee3d501ba1c00000803f0000000000000000000000000000803f0000000000000000000000000000803f0000803f",
     "59634d3ccfc3c3bf6ec58ec00000803f0000000000000000000000000000803f0000000000000000000000000000803f9a99993f"),
]
# the Crown of Lugh meshes: items.csv rows 44-55 (a gap free in the stock 1.130 table, below its highest ID), in the
# order of LugHelmet's per-race columns 79-90 (Celt, Elf, Lurikeen, Firbolg, Sylvan, Shar; male then female)
LUGH_ITEMS = ["2911", "2904", "2912", "2905", "2914", "2907", "2913", "2906", "2917", "2910", "2916", "2909"]
MESHES = [("items/" + stock.replace("Lum_", "Tara_"), "items/" + stock, sha, old, new) for stock, sha, old, new in LUGH_MESHES]
ITEMS = [(44 + i, stock, MESHES[i][0][len("items/"):-len(".nif")]) for i, stock in enumerate(LUGH_ITEMS)]
# objects: id, stock row cloned, name, column changes (every inherited skin column is decided)
OBJECTS = [(4950, "428", "Regalia of Tara Hauberk", {66: "5810", 67: "5811"}),
           (4951, "429", "Regalia of Tara Greaves", {70: "5813"}),
           (4952, "430", "Regalia of Tara Sleeves", {68: "5812"}),
           (4953, "431", "Regalia of Tara Gauntlets", {69: "5814"}),
           (4954, "432", "Regalia of Tara Boots", {71: "5815"}),
           (4955, "840", "Regalia of Tara Crown", {12: "5818", 22: "5818"}),
           # a new ID, not a changed 4955: the launcher keeps a client's existing rows, and the server items move to 4964
           (4964, "4827", "Regalia of Tara Crown of Lugh", {12: "5828", 22: "5828", **{79 + i: str(44 + i) for i in range(12)}}),
           (4956, "326", "Regalia of Tara Mantle", {11: "5817", 16: "1", 72: "5816"})]


def build(client: Path):
    return S.build(client, sys.modules[__name__], int(time.time()))


def targets(client):
    return (client / f"items/pskins/pskin{PSKIN_ARC:03d}.mpk", client / f"figures/Mskins/mskin{MSKIN_ARC:03d}.mpk",
            *(client / target for target, *_ in MESHES))


def update(client):
    blob, replaced, new = S.update(client, sys.modules[__name__], int(time.time()))
    game = client / "gamedata.mpk"
    manifest = {"when": datetime.now().isoformat(timespec="seconds"), "client": str(client), "kind": "update",
                "gamedata_before": hashlib.sha256(game.read_bytes()).hexdigest(), "gamedata_after": hashlib.sha256(blob).hexdigest(),
                "new_files": sorted(new), "replaced_files": sorted(replaced)}
    backup = client.parents[1] / "backups" / ("client-tara-update-" + datetime.now().strftime("%Y%m%d-%H%M%S"))
    backup.mkdir(parents=True)
    (backup / "gamedata.mpk").write_bytes(game.read_bytes())
    for rel in replaced:
        (backup / "replaced" / rel).parent.mkdir(parents=True, exist_ok=True)
        (backup / "replaced" / rel).write_bytes((client / rel).read_bytes())
    (backup / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    for rel, data in {**replaced, **new}.items():
        (client / rel).write_bytes(data)
    game.write_bytes(blob)
    need(hashlib.sha256(game.read_bytes()).hexdigest() == manifest["gamedata_after"], "updated catalog differs")
    (client / "tara-set-update.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"updated {client}: {len(new)} new meshes, {len(replaced)} archives rebuilt")
    print(f'rollback: python -B "{Path(__file__).resolve()}" rollback "{client}" "{backup}"')


def main():
    if len(sys.argv) >= 4 and sys.argv[1] == "rollback":
        S.rollback(Path(sys.argv[2]).resolve(), Path(sys.argv[3]).resolve()); return
    if len(sys.argv) < 3 or sys.argv[1] not in ("plan", "install", "update"):
        print(__doc__); return
    client = Path(sys.argv[2]).resolve()
    real_ok = "--modern-test" in sys.argv and client == S.MODERN_TEST.resolve()
    need(real_ok or all(client != r.resolve() for r in S.REAL), "this is a real client folder; only the modern test client with --modern-test")
    need((client / "gamedata.mpk").exists() and (client / "game.dll").exists(), "not a client folder")
    if real_ok:
        S.client_closed()
    if sys.argv[1] == "update":
        update(client); return
    for t in targets(client):
        need(not t.exists(), f"{t.name} already exists")
    blob, pak, mpak = build(client)
    meshes = [ours for *_, ours in S.mesh_files(client, sys.modules[__name__])]
    print(f"plan ok: gamedata {len(blob)} bytes, pskin{PSKIN_ARC:03d} {len(pak)}, mskin{MSKIN_ARC:03d} {len(mpak)}; "
          f"{len(MSKINS)} mskins, {len(PSKINS)} pskins, {len(OBJECTS)} objects rows")
    if sys.argv[1] != "install":
        return
    game = client / "gamedata.mpk"
    manifest = {"when": datetime.now().isoformat(timespec="seconds"), "client": str(client),
                "gamedata_before": hashlib.sha256(game.read_bytes()).hexdigest(), "gamedata_after": hashlib.sha256(blob).hexdigest()}
    backup = client.parents[1] / "backups" / ("client-tara-set-" + datetime.now().strftime("%Y%m%d-%H%M%S"))
    backup.mkdir(parents=True)
    (backup / "gamedata.mpk").write_bytes(game.read_bytes())
    manifest["new_files"] = [str(t.relative_to(client)) for t in targets(client)]
    (backup / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    for t, b in zip(targets(client), (pak, mpak, *meshes)):
        t.write_bytes(b)
    game.write_bytes(blob)
    need(hashlib.sha256(game.read_bytes()).hexdigest() == manifest["gamedata_after"], "installed catalog differs")
    (client / "tara-set-install.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print("installed into", client)
    print(f'rollback: python -B "{Path(__file__).resolve()}" rollback "{client}" "{backup}"')


if __name__ == "__main__":
    main()
