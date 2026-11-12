"""Refit the stock Lug helmet (Lum_helm01_<sex>_<race>) to player heads, for the Regalia of Tara Crown of Lugh.

The Lug helmet was made for the Lugh NPC's head: on player heads it sits about 1.5 units too far back and too small,
so the top of the scalp shows in front of the bowl (owner 2026-11-08: "the helmet isn't on the head right"). The fit
(1.2x about the helm's own base centre, 1.5 units forward, chosen from preview renders on Celt, Elf, Firbolg and
Lurikeen heads) is written into the mesh's own NiTriShape transform (translation and scale; the rotation stays), so a
private copy of each NIF differs from the stock file in those 52 bytes only.

    python tools/sovereign-art/pipeline/fit_lugh.py        prints tara/install_tara_set.py's LUGH_MESHES table

Read only on the client.
"""
import hashlib
import struct
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import paths  # noqa: E402
import helm_fit  # noqa: E402
import nif_static  # noqa: E402
from netimmerse import read_nif  # noqa: E402

SCALE, FORWARD = 1.2, -1.5          # y is forward-negative in head space
RACES = ("cel", "elf", "lur", "fir", "syl", "sha")


def find(name):
    return next(p for p in (paths.CLIENT / "items").iterdir() if p.name.lower() == name.lower())


def transform_block(b):
    """The 13 floats NiAVObject stores for a shape: translation, rotation (row major) and scale."""
    t, r = b.get("translation"), b.get("rotation")
    rot = [r[f"m{i}{j}"] for i in (1, 2, 3) for j in (1, 2, 3)]
    return [t["x"], t["y"], t["z"]] + rot + [b.get("scale", 1.0)]


def refit(race, sex):
    stock = find(f"Lum_helm01_{sex.upper()}_{race}.nif")
    data = stock.read_bytes()
    nif = read_nif(data)
    shapes, _ = nif_static.collect(nif)
    assert len(shapes) == 1, f"{stock.name}: {len(shapes)} shapes"
    b, geo, m, _ = shapes[0]
    assert np.allclose(m, nif_static.local_matrix(b)), "the shape's parents are not identity"
    M = helm_fit.head_world(f"{race}_{sex}")
    v = np.asarray(geo["vertices"], float)[:, :3]
    w = (np.c_[v, np.ones(len(v))] @ m @ M)[:, :3]
    c = np.array([0.0, (w[:, 1].min() + w[:, 1].max()) / 2, np.percentile(w[:, 2], 10)])
    T = np.identity(4); T[:3, :3] *= SCALE; T[3, :3] = c * (1 - SCALE) + np.array([0.0, FORWARD, 0.0])
    new = m @ M @ T @ np.linalg.inv(M)
    s = np.cbrt(np.linalg.det(new[:3, :3]))
    rot = new[:3, :3] / s
    old = transform_block(b)
    assert np.allclose(rot, np.array(old[3:12]).reshape(3, 3), atol=1e-4), "the refit would turn the mesh"
    old_bytes = struct.pack("<13f", *old)
    assert data.count(old_bytes) == 1, f"{stock.name}: transform bytes not unique"
    new_bytes = struct.pack("<13f", *new[3, :3], *old[3:12], float(s * old[12]))
    # check: the patched copy places the vertices where the fit wants them
    patched = data.replace(old_bytes, new_bytes)
    (b2, geo2, m2, _), = nif_static.collect(read_nif(patched))[0]
    w2 = (np.c_[v, np.ones(len(v))] @ m2 @ M)[:, :3]
    assert np.allclose(w2, (w - c) * SCALE + c + np.array([0, FORWARD, 0]), atol=2e-3)
    return stock.name, hashlib.sha256(data).hexdigest(), old_bytes.hex(), new_bytes.hex()


def fitted(race, sex):
    """Register the refitted helm, placed on the head as helm_fit.helm places stock helms; returns its mesh name."""
    import render as R
    name, _, old, new = refit(race, sex)
    data = find(name).read_bytes().replace(bytes.fromhex(old), bytes.fromhex(new))
    (b, geo, m, _), = nif_static.collect(read_nif(data))[0]
    v = np.asarray(geo["vertices"], float)[:, :3]
    w = (np.c_[v, np.ones(len(v))] @ m @ helm_fit.head_world(f"{race}_{sex}"))[:, :3]
    mesh = f"tara_helm01_{sex}_{race}"
    R.register(mesh, w, np.asarray(geo["uv_sets"][0], float)[:, :2], nif_static.triangles_of(geo))
    return mesh


def main():
    print("LUGH_MESHES = [  # (stock NIF, its sha256, transform bytes, refit bytes): fit_lugh.py")
    for race in RACES:
        for sex in ("m", "f"):
            name, sha, old, new = refit(race, sex)
            print(f'    ("{name}", "{sha}",\n     "{old}",\n     "{new}"),')
    print("]")


if __name__ == "__main__":
    main()
