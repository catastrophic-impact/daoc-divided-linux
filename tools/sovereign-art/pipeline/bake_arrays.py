"""texelmap.bake for raw geometry arrays (the per-race Catacombs helm, already placed on the head)."""
import math

import numpy as np


class TM:
    pass


def bake(v, n, uv, f, size=512, pad=4):
    pos = np.zeros((size, size, 3)); nrm = np.zeros((size, size, 3))
    covered = np.zeros((size, size), bool)
    px = uv * (size - 1)
    for tri in f:
        a, b, c = px[tri]
        den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(den) < 1e-12:
            continue
        x0 = max(0, int(math.floor(min(a[0], b[0], c[0]))) - 1); x1 = min(size - 1, int(math.ceil(max(a[0], b[0], c[0]))) + 1)
        y0 = max(0, int(math.floor(min(a[1], b[1], c[1]))) - 1); y1 = min(size - 1, int(math.ceil(max(a[1], b[1], c[1]))) + 1)
        if x1 < x0 or y1 < y0:
            continue
        gy, gx = np.mgrid[y0:y1 + 1, x0:x1 + 1]
        qx, qy = gx.astype(float), gy.astype(float)
        w0 = ((b[1] - c[1]) * (qx - c[0]) + (c[0] - b[0]) * (qy - c[1])) / den
        w1 = ((c[1] - a[1]) * (qx - c[0]) + (a[0] - c[0]) * (qy - c[1])) / den
        w2 = 1 - w0 - w1
        eps = 0.6 / max(1.0, abs(den)) ** 0.5
        inside = (w0 >= -eps) & (w1 >= -eps) & (w2 >= -eps)
        if not inside.any():
            continue
        p = w0[..., None] * v[tri[0]] + w1[..., None] * v[tri[1]] + w2[..., None] * v[tri[2]]
        nn = w0[..., None] * n[tri[0]] + w1[..., None] * n[tri[1]] + w2[..., None] * n[tri[2]]
        ys, xs = gy[inside], gx[inside]
        first = ~covered[ys, xs]
        pos[ys[first], xs[first]] = p[inside][first]; nrm[ys[first], xs[first]] = nn[inside][first]
        covered[ys[first], xs[first]] = True
    nrm /= np.maximum(np.linalg.norm(nrm, axis=2, keepdims=True), 1e-9)
    for _ in range(pad):
        grow = ~covered
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            src = np.roll(np.roll(covered, dy, 0), dx, 1) & grow
            ys, xs = np.nonzero(src)
            sy, sx = (ys - dy) % size, (xs - dx) % size
            pos[ys, xs] = pos[sy, sx]; nrm[ys, xs] = nrm[sy, sx]; covered[ys, xs] = True
    t = TM(); t.pos, t.nrm, t.covered = pos, nrm, covered
    return t
