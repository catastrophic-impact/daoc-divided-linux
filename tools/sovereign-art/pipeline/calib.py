"""Bake the union surface of each Stormforged skin (Norse male reference) and report heights/extents, cached."""
import sys
from pathlib import Path

import numpy as np

import paths  # noqa: E402,F401  (helper paths in this repository)
import texelmap  # noqa: E402

HERE = Path(__file__).resolve().parent
NIF, CACHE = paths.NIF, paths.BAKES
CACHE.mkdir(exist_ok=True)

UNIONS = {"body_m": ([f"Body{i:02d}_HN_m" for i in (5, 6, 1, 2, 3, 4, 7, 8)], 1024, 0),
          "body_f": ([f"Body{i:02d}_HN_f" for i in (5, 6, 1, 2, 3, 4, 7, 8)], 1024, 0),
          "arms": (["Arms01_HN_m"], 512, 0), "legs": (["Legs01_HN_m"], 512, 0),
          "gloves": ([f"Gloves{i:02d}_HN_m" for i in (2, 1, 3, 4, 5, 6, 7, 8)], 512, 0),
          "boots": ([f"Boots{i:02d}_HN_m" for i in (2, 1, 3, 4, 5, 6, 7, 8)], 512, 0),
          "cloak": ([f"Cloak{i:02d}_nor_m" for i in (1, 2, 3)], 1024, 1)}


class TM:
    pass


def bake(key, pad=4):
    f = CACHE / f"{key}.npz"
    if f.exists():
        z = np.load(f); t = TM()
        for k in z.files:
            setattr(t, k, z[k])
        return t
    names, size, uv_set = UNIONS[key]
    tm = None
    for name in names:
        t, _ = texelmap.bake(NIF / f"{name}.nif", size=size, pad=0, uv_set=uv_set)
        if tm is None:
            tm = t; continue
        new = t.covered & ~tm.covered
        tm.pos[new] = t.pos[new]; tm.nrm[new] = t.nrm[new]; tm.shape[new] = t.shape[new]; tm.covered |= new
    # texels the main mesh draws on two far-apart surfaces (the female Body05 reuses chest texels at the skirt edge)
    conflict = tm.mirror & (np.abs(tm.pos[..., 2] - tm.pos2[..., 2]) > 6.0)
    S = tm.size
    for _ in range(pad):
        grow = ~tm.covered
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            src = np.roll(np.roll(tm.covered, dy, 0), dx, 1) & grow
            ys, xs = np.nonzero(src)
            sy, sx = (ys - dy) % S, (xs - dx) % S
            tm.pos[ys, xs] = tm.pos[sy, sx]; tm.nrm[ys, xs] = tm.nrm[sy, sx]; tm.shape[ys, xs] = tm.shape[sy, sx]
        tm.covered = tm.shape >= 0
    np.savez_compressed(f, pos=tm.pos, nrm=tm.nrm, covered=tm.covered, shape=tm.shape, conflict=conflict)
    t = TM(); t.pos, t.nrm, t.covered, t.shape, t.conflict = tm.pos, tm.nrm, tm.covered, tm.shape, conflict
    return t


def band(t, y0, y1, h=512):
    """z stats of texels in stock texture rows y0..y1 (of a texture h tall)."""
    S = t.pos.shape[0]
    r0, r1 = int(y0 / h * S), int(y1 / h * S)
    sel = t.covered[r0:r1]
    z = t.pos[r0:r1, :, 2][sel]
    return np.percentile(z, [5, 50, 95]).round(1) if z.size else None


if __name__ == "__main__":
    for k in UNIONS:
        t = bake(k)
        p = t.pos[t.covered]
        print(k, "min", p.min(0).round(1), "max", p.max(0).round(1))
    b = bake("body_m")
    for lab, y0, y1 in [("collar/top", 0, 30), ("chest", 60, 180), ("belt", 192, 222), ("chain skirt", 225, 300),
                        ("blue hem", 300, 325), ("T hem band", 329, 350), ("lower 352-512", 352, 512)]:
        print(lab, band(b, y0, y1))
