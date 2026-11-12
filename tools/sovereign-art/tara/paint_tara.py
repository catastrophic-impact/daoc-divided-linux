"""Regalia of Tara concept skins (Tuatha Sovereign, 2026-11-06). Preview only: nothing touches the client or server.

A Sidhe king's scale: overlapping enamelled scales (Celtic scale, like the stock osnadurtha sets), a gold lunula collar
(the Irish Bronze Age crescent), a moon-and-stag roundel on chest and back, leaf-tipped moonsilver pauldrons, a belt of
triquetra plaques with a crescent buckle, and a deep green tabard embroidered with a gold vine. Painted on the same
meshes and bakes as the Stormforged set (Body05 / Arms01 / Legs01 / Gloves06 / Boots06, UVs shared by every race), with
the v5 Canvas (painted bevels), and rendered on Hibernian figures.

    python paint_tara.py [A|B ...]   A = emerald court (green enamel scales), B = moonsilver (silver scales, green inlay)
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import distance_transform_edt

SF = Path(__file__).resolve().parents[1] / "pipeline"         # the Sovereign art pipeline (tools/sovereign-art/pipeline)
sys.path.insert(0, str(SF))
from paint_sf import fbm3, smoothstep, lerp, rivets  # noqa: E402
from paint_sf_parts import _seg  # noqa: E402
import paint_v5 as P5  # noqa: E402
from paint_v5 import Canvas, ramp, lit  # noqa: E402
import paint_v7 as P7  # noqa: E402
import calib  # noqa: E402
import render as R  # noqa: E402
import bake_arrays  # noqa: E402
import helm_fit  # noqa: E402
import fit_lugh  # noqa: E402

HERE = Path(__file__).resolve().parent
import paths  # noqa: E402
WORK = paths.WORK / "tara"; WORK.mkdir(parents=True, exist_ok=True)
R.NIF = paths.NIF
R._cache.clear()
helm_fit.PREFIX.update({"BC": "cel", "AES": "elf", "Lur": "lur", "Fir": "fir"})

EMERALD = [(1, 8, 5), (4, 27, 17), (11, 60, 38), (36, 112, 76), (138, 206, 168)]
MOON = [(9, 11, 14), (40, 44, 52), (92, 98, 110), (164, 172, 186), (234, 238, 246)]
GOLD = [(34, 24, 8), (100, 76, 32), (172, 142, 76), (224, 200, 138), (252, 242, 210)]
LEATHER = [(9, 9, 6), (28, 25, 16), (54, 47, 30), (88, 77, 52)]
CLOTH = [(2, 7, 4), (5, 19, 12), (14, 40, 26), (32, 72, 48)]
FUR = [(46, 45, 43), (102, 100, 96), (156, 154, 148), (206, 204, 198), (242, 240, 234)]
GREEN_LEATHER = [(3, 10, 6), (10, 30, 19), (24, 58, 38), (52, 96, 68)]
VARIANT = "A"

# the v7 painters' helpers read these module globals
P7.GOLD, P7.LEATHER, P7.FUR = GOLD, LEATHER, FUR
P5.BRONZE = MOON                                 # Canvas.stud: moonsilver rivets
P7.plait.__defaults__ = (GOLD, 0.16)


# ------------------------------------------------------------------ v2 polish: the new epics' look (owner 2026-11-06)
# The 1.130 new epics (Thane, Blademaster in mskin049) paint their shine and depth into the colour: a chrome sky/horizon
# reflection on metal, crisp bright inner edges, and every raised piece casting a dark contact shadow on the layer below.
POLISH = {"on": False, "v3": False}      # v3 = pass 3 (owner 2026-11-08): leaf scales, clean plate, Crown of Lugh helm
_base_layer = P5.Canvas.layer
METAL_STRENGTH = {}


def chrome(n, p, seed):
    """Painted environment reflection for a texel normal: bright sky above, a dark horizon band, a dim ground (as v17)."""
    # lifted 0.3 (v17 had none): front-facing moonsilver reflects sky, not the dark horizon band, so it stays silver
    nz = n[..., 2] + 0.30 + 0.05 * (fbm3(p, 8.0, 1, seed) - 0.5)
    sky = smoothstep(0.02, 0.45, nz)
    horizon = np.exp(-((nz + 0.02) / 0.10) ** 2)
    ground = smoothstep(-0.1, -0.7, nz) * 0.45
    side = np.clip(np.abs(n[..., 0]), 0, 1) * 0.15
    return np.clip(0.16 + 0.55 * sky + ground - 0.45 * horizon + side, 0, 1)


def _polished(self, mask, stops, tone=None, outline=3.5, rim=4.5, pillow=28.0, spec=0.6, rim_amt=0.75, grain=0.10, seed=1):
    strength = METAL_STRENGTH.get(id(stops)) if POLISH["on"] else None
    if strength is None or tone is not None or not mask.any():
        return _base_layer(self, mask, stops, tone, outline, rim, pillow, spec, rim_amt, grain, seed)
    env = chrome(self.n, self.p, seed)
    k = 1.0 if outline >= 3.0 else 1.6
    d = self.dist(mask)
    pil = smoothstep(0.0, pillow * k, d) ** 0.8
    base = 0.10 + 0.28 * pil + 0.05 * (fbm3(self.p, 0.45, 2, seed + 5) - 0.5)
    _base_layer(self, mask, stops, lerp(base, 0.05 + 0.85 * env, strength), outline, rim, pillow, spec, rim_amt, grain, seed)
    hot = (env > 0.68) & mask & (lit(self.n) > 0.92)
    self.col = np.where(hot[..., None], lerp(self.col, ramp(np.full(d.shape, 1.0), stops), 0.55), self.col)
    edge = mask & (d > outline * k + 0.3) & (d < outline * k + 1.3)
    self.col = np.where(edge[..., None], lerp(self.col, ramp(np.full(d.shape, 0.97), stops),
                                              0.45 * strength * (0.4 + 0.6 * self.up[..., None])), self.col)
    # the dark outline is what separates the layers on the new epics: keep it strong
    self.col = np.where((mask & (d < outline * k * 0.5))[..., None], self.col * 0.55, self.col)


P5.Canvas.layer = _polished


def cast_shadow(C, mask, reach=5.0, amt=0.6):
    """The contact shadow a raised piece casts on the layer around it, longer and darker below it (the epics' depth)."""
    if not POLISH["on"] or not mask.any():
        return
    d, (iy, ix) = distance_transform_edt(~mask, return_indices=True)
    d = d / C.scale
    src = C.p[iy, ix]
    near = np.linalg.norm(C.p - src, axis=-1) < 1.2                       # same surface, not a neighbouring UV island
    below = np.clip((src[..., 2] - C.p[..., 2]) / 0.5 + 0.5, 0, 1)
    s = (1 - smoothstep(0.0, reach * (0.5 + 0.9 * below), d)) * amt * (0.35 + 0.65 * below)
    s = s * (~mask & C.cov & near)
    C.col = C.col * (1 - s[..., None])


def facet(C, mask, across, amt=0.38):
    """Chiselled two-tone gold: lit on one side of the piece's ridge, dark on the other, a bright ridge line."""
    if not POLISH["on"] or not mask.any():
        return
    C.col = np.where((mask & (across > 0))[..., None], lerp(C.col, np.array([255, 240, 190.0]), amt * 0.45), C.col)
    C.col = np.where((mask & (across <= 0))[..., None], C.col * (1 - amt), C.col)
    C.col = np.where((mask & (np.abs(across) < 0.05))[..., None], lerp(C.col, np.array([255, 248, 220.0]), 0.6), C.col)


def leaf_points(C, mask, u, v, centres_u, v0, length, width, seed=90):
    """Faceted gold leaf points hanging from the line v=v0 (v grows downward) at the given u centres."""
    if not POLISH["on"]:
        return
    lu = np.full(u.shape, 9.0)
    for cu in centres_u:
        du = u - cu
        lu = np.where(np.abs(du) < np.abs(lu), du, lu)
    lv = (v - v0) / length
    halfw = width * np.sqrt(np.clip(lv, 0, 1)) * (1 - np.clip(lv, 0, 1)) * 2.2
    leaf = mask & (lv > 0) & (lv < 1) & (np.abs(lu) < halfw)
    C.layer(leaf, GOLD, outline=0.5, rim=0.7, pillow=1.2, spec=0.95, seed=seed)
    facet(C, leaf, lu)
    cast_shadow(C, leaf, reach=3.0, amt=0.55)


def bezier(pts, s):
    P0, P1, P2 = (np.array(q, float) for q in pts)
    return (1 - s) ** 2 * P0 + 2 * (1 - s) * s * P1 + s ** 2 * P2, 2 * (1 - s) * (P1 - P0) + 2 * s * (P2 - P1)


def bezier_dist(a, b, pts, n=24):
    curve = [bezier(pts, s)[0] for s in np.linspace(0, 1, n + 1)]
    best = np.full(a.shape, 99.0); along = np.zeros(a.shape)
    for i in range(n):
        dseg = _seg(a, b, curve[i][0], curve[i][1], curve[i + 1][0], curve[i + 1][1])
        take = dseg < best
        best = np.where(take, dseg, best); along = np.where(take, (i + 0.5) / n, along)
    return best, along


def hash2(a, b):
    h = np.sin(a * 12.9898 + b * 78.233) * 43758.5453
    return h - np.floor(h)


def metal(C, mask, stops, w=1.0, spec=0.85, seed=1):
    C.layer(mask, stops, outline=0.7 * w, rim=1.0 * w, pillow=1.8 * w, spec=spec, seed=seed)


def plate(C, mask, seed=1, engrave=True):
    """A large moonsilver plate, bevelled like the Storm set's steel, with its engraved polish."""
    C.layer(mask, MOON, seed=seed, grain=0.16)
    if engrave:
        p = C.p
        C.scroll(mask, p[..., 0] * 1.5 + p[..., 1] * 1.5, p[..., 2] * 1.5, inset=6.0)


def gold_line(C, region, width=1.6):
    """Moonsilver rim with a thin gold line just inside it."""
    d = C.dist(region)
    metal(C, region & (d < width), MOON)
    metal(C, region & (d >= width + 0.5) & (d < width + 1.5), GOLD, 0.8, 0.9)


METAL_STRENGTH.update({id(MOON): 0.85, id(GOLD): 0.80})


def scales(C, mask, u, v, w, h, radius=0.72, variant=None, seed=7, small=False):
    """Overlapping scales hanging downward (upper rows over lower ones), each a domed, enamelled plate with a metal rim.
    u: across (mirror-safe), v: downward. w, h: cell size in the same units."""
    if not mask.any():
        return
    variant = variant or VARIANT
    U, V = u / w, v / h
    leafy = POLISH["v3"] and not small

    def metric(du, dv):
        """Round scales; pass 3 narrows each scale's lower half to a point, so the rows read as overlapping leaves."""
        if not leafy:
            return np.hypot(du, dv)
        return np.hypot(du / (1 - 0.48 * np.clip(dv / radius, 0, 1) ** 1.2), dv)
    out_r = np.full(U.shape, np.inf); fu = np.zeros(U.shape); fv = np.zeros(U.shape); cxs = np.zeros(U.shape)
    base = np.floor(V)
    for dr in (-1, 0, 1, 2):
        rr = base + dr - 1
        off = 0.5 * (np.mod(rr, 2))
        cx = np.round(U - off) + off
        d = metric(U - cx, (V - rr) * 1.0)
        take = (d < radius) & (rr < out_r)
        out_r = np.where(take, rr, out_r); fu = np.where(take, U - cx, fu); fv = np.where(take, V - rr, fv); cxs = np.where(take, cx, cxs)
    got = np.isfinite(out_r) & mask
    rr = np.where(got, out_r, 0)
    d = metric(fu, fv)
    edge = radius - d                                           # cell units to the scale's rim
    # shadow under the row above
    up = rr - 1; off = 0.5 * np.mod(up, 2)
    cu = np.round(U - off) + off
    s = np.min([metric(U - (cu + k), V - up) for k in (-1, 0, 1)], axis=0) - radius
    shadow = 1 - smoothstep(0.0, 0.22, s)
    dome = np.sqrt(np.clip(1 - (d / radius) ** 2, 0, 1))
    hl = np.clip(0.55 - 0.55 * fv / radius - 0.25 * fu / radius, 0, 1)
    jit = hash2(cxs, rr) - 0.5
    t = 0.20 + 0.34 * dome + 0.22 * hl * dome + 0.12 * lit(C.n) - 0.34 * shadow + 0.10 * jit + 0.06 * (fbm3(C.p, 1.2, 2, seed) - 0.5)
    rimw = 0.13 if small else 0.16
    if variant == "A":
        body, rim = EMERALD, MOON
    else:
        body, rim = MOON, GOLD
    col = ramp(t, body)
    # enamel gets a glassy spot highlight
    if variant == "A":
        spot = np.exp(-(((fu + 0.16) / 0.12) ** 2 + ((fv + 0.10) / 0.10) ** 2))
        col = lerp(col, np.array([200, 245, 220.0]), (0.55 * spot)[..., None])
    rimm = edge < rimw
    tr = 0.45 + 0.4 * hl + 0.12 * lit(C.n) - 0.4 * shadow
    if POLISH["on"]:
        env = chrome(C.n, C.p, seed)
        tr = lerp(tr, 0.08 + 0.85 * env - 0.35 * shadow, 0.7)
        col = lerp(col, ramp(np.clip(0.25 + 0.6 * env * dome, 0, 1), body), 0.35)          # glassy enamel catches the sky
    col = np.where(rimm[..., None], ramp(tr, rim), col)
    col = np.where((edge < 0.035)[..., None], col * 0.25, col)            # dark outline
    if variant == "B" and not small:                                   # green enamel midrib leaf
        leaf = (np.abs(fu) < 0.075 * (1 - np.clip((fv + 0.1) / 0.55, 0, 1)) + 0.012) & (fv > -0.35) & (fv < 0.42)
        col = np.where(leaf[..., None], ramp(0.45 + 0.4 * hl, EMERALD), col)
    elif leafy:                                                        # gold midrib vein, the tip deeper green
        col = np.where((~rimm)[..., None], col * (1 - 0.35 * np.clip(fv / radius, 0, 1) ** 1.5)[..., None], col)
        rib = (np.abs(fu) < 0.028 * (1 - 0.6 * np.clip(fv / radius, 0, 1))) & (fv > -0.30) & (fv < 0.56)
        col = np.where(rib[..., None], ramp(0.45 + 0.4 * hl - 0.2 * shadow, GOLD), col)
        for sgn in (-1, 1):                                            # two side veins
            vein = (np.abs(fu - sgn * (fv + 0.25) * 0.35) < 0.018) & (fv > -0.12) & (fv < 0.30) & (sgn * fu > 0)
            col = np.where(vein[..., None], col * 0.7, col)
    elif not small:                                                    # silver midrib with a rivet
        rib = (np.abs(fu) < 0.035) & (fv > 0.05) & (fv < 0.55)
        col = np.where(rib[..., None], ramp(0.35 + 0.3 * hl - 0.2 * shadow, MOON), col)
    C.col = np.where(got[..., None], col, C.col)


# ------------------------------------------------------------------ the moon-and-stag emblem
_STAG = None


def stag_image(N=512):
    """0 empty, 1 moon disc, 2 stag (head and antlers), drawn in a -1..1 frame, y up."""
    global _STAG
    if _STAG is not None:
        return _STAG
    im = Image.new("L", (N, N), 0); dr = ImageDraw.Draw(im)
    P = lambda x, y: ((x + 1) / 2 * N, (1 - y) / 2 * N)                      # noqa: E731
    dr.ellipse([*P(-0.66, 0.78), *P(0.66, -0.54)], fill=1)
    W = int(0.075 * N / 2)
    for s in (-1, 1):
        beam = [(0.10, 0.22), (0.30, 0.46), (0.40, 0.66), (0.42, 0.86), (0.32, 0.98)]
        dr.line([P(s * x, y) for x, y in beam], fill=2, width=W, joint="curve")
        for (x0, y0), (x1, y1) in [((0.28, 0.44), (0.56, 0.52)), ((0.38, 0.62), (0.66, 0.76)), ((0.41, 0.80), (0.22, 0.95)),
                                   ((0.16, 0.30), (0.06, 0.48))]:
            dr.line([P(s * x0, y0), P(s * x1, y1)], fill=2, width=int(W * 0.8))
        dr.polygon([P(s * 0.17, 0.16), P(s * 0.50, 0.26), P(s * 0.22, 0.02)], fill=2)        # ears
    dr.polygon([P(-0.19, 0.22), P(0.19, 0.22), P(0.15, -0.08), P(0.08, -0.42), P(0, -0.50), P(-0.08, -0.42), P(-0.15, -0.08)], fill=2)
    _STAG = np.asarray(im)
    return _STAG


def emblem(C, mask, a, b, r):
    """Moonsilver roundel with a gold rim, a pale moon disc and the gold stag over it. a, b: local coords, radius r."""
    rr = np.hypot(a, b)
    disc = mask & (rr < r)
    if not disc.any():
        return
    metal(C, disc, MOON, 1.2, 0.8, seed=71)
    metal(C, disc & (rr > r * 0.84), GOLD, 1.0, 0.9)
    P7.plait(C, disc & (rr > r * 0.86), np.arctan2(a, b) * r * 1.3, rr, r * 0.86, r * 0.995, r * 0.55, stops=GOLD, width=0.2)
    img = stag_image(); N = img.shape[0]
    s = 0.80 * r
    px = np.clip(((a / s + 1) / 2 * N).astype(int), 0, N - 1); py = np.clip(((1 - b / s) / 2 * N).astype(int), 0, N - 1)
    inside = disc & (np.abs(a) < s) & (np.abs(b) < s) & (rr < r * 0.84)
    k = np.where(inside, img[py, px], 0)
    C.layer(k == 1, MOON, tone=0.62 + 0.25 * lit(C.n) - 0.18 * (rr / r), outline=0.5, rim=0.6, pillow=1.0, spec=0.4, rim_amt=0.3)
    C.col = np.where((k == 1)[..., None], lerp(C.col, np.array([226, 240, 236.0]), 0.25), C.col)
    C.layer(k == 2, GOLD, outline=0.5, rim=0.8, pillow=1.4, spec=0.9, seed=72)
    facet(C, k == 2, a, amt=0.18)
    cast_shadow(C, k == 2, reach=2.5, amt=0.5)


def triquetra_plaque(C, mask, a, b, r):
    metal(C, mask, MOON, 0.9, 0.85, seed=81)
    C.layer(mask & P7.triquetra(a, b, r * 0.72, r * 0.1), GOLD, outline=0.4, rim=0.6, pillow=1.0, spec=0.9)


def vine(C, mask, a, z, amp=0.42, period=4.4, w=0.10):
    """Gold-embroidered vine running along z at a=0, alternate leaves."""
    ph = 2 * np.pi * z / period
    sx = amp * np.sin(ph)
    stem = mask & (np.abs(a - sx) < w)
    leaves = np.zeros(a.shape, bool)
    for k in range(-30, 30):
        zc = (k + 0.25) * period / 2
        side = 1 if k % 2 == 0 else -1
        cx = amp * np.sin(2 * np.pi * zc / period) + side * 0.55
        da, dz = a - cx, z - zc - 0.15
        ang = side * 0.6
        ra = da * np.cos(ang) - dz * np.sin(ang); rb = da * np.sin(ang) + dz * np.cos(ang)
        leaves |= ((ra / 0.50) ** 2 + (rb / 0.22) ** 2) < 1
    leaves &= mask
    C.layer(leaves, GOLD, outline=0.5, rim=0.7, pillow=1.2, spec=0.7, seed=73)
    C.col = np.where((leaves & (np.abs(np.sin(ph * 3)) < 0.05))[..., None], C.col * 0.6, C.col)
    C.layer(stem, GOLD, outline=0.4, rim=0.5, pillow=0.8, spec=0.7, seed=74)


# ------------------------------------------------------------------ body (Body05 layout, as the Stormforged v26 fit)
def paint_body(tm, female=False):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    if female:
        z = z - 2.0 * smoothstep(50.5, 47.5, z) + 0.5 * smoothstep(42.0, 35.0, z)
    ax = np.abs(x); front = y < 0.5
    ang = np.arctan2(x, -y); aang = np.abs(ang); u = aang * 7.5
    C.layer(C.cov, CLOTH, tone=0.30 + 0.2 * lit(n), outline=0, rim=0, rim_amt=0, spec=0)

    # torso: the scale shirt, from the belt to the collar
    torso = (z > 45.4) & (z < 61.5)
    scales(C, torso, u, -z, 2.4, 1.7, seed=11)

    # collar: small moonsilver scales round the neck, the gold lunula over the front
    mantle_lvl = 58.9 + 1.4 * smoothstep(8.5, 11.0, ax)
    collar = (z > mantle_lvl) & ~((ax > 10.6) & (n[..., 0] * np.sign(x) > 0.45))
    scales(C, collar, u, -z, 1.15, 0.85, variant="B", small=True, seed=12)
    C.col = np.where((collar & (np.abs(z - mantle_lvl) < 0.25))[..., None], ramp(np.full(z.shape, 0.7), GOLD), C.col)
    luna = front & (np.hypot(x, z - 63.0) < 5.6) & (np.hypot(x, (z - 64.6) * 1.05) > 5.0) & (z < 61.6)
    metal(C, luna, GOLD, 1.4, 0.95, seed=13)
    for k, off in enumerate((0.55, 1.05)):                      # engraved bands near the horns, as on the Irish lunulae
        band = luna & (np.abs(np.hypot(x, z - 63.0) - (5.6 - off)) < 0.07) & (ax > 1.2)
        C.col = np.where(band[..., None], C.col * 0.5, C.col)
    hatch = luna & (ax > 3.0) & (np.abs(((x * 2.2 + z * 2.2) % 1.0) - 0.5) < 0.08)
    C.col = np.where(hatch[..., None], C.col * 0.65, C.col)
    C.col = np.where((luna & (ax < 0.3))[..., None], C.col * 0.55, C.col)
    cast_shadow(C, luna, reach=6.0, amt=0.6)

    if POLISH["on"]:
        # sweeping moonsilver bands from each shoulder down under the roundel, gold-edged, with gold thorns along their
        # outer edge: the layered chest of the new epics, in leaves and moonsilver
        bd, along = bezier_dist(ax, z, [(7.9, 60.6), (6.6, 52.6), (0.0, 48.6)])
        band = torso & (bd < 0.62) & ~collar
        edge_g = torso & (np.abs(bd - 0.70) < 0.10) & ~collar
        C.layer(band, MOON, seed=14, grain=0.16)
        C.scroll(band, along * 30.0, bd * 6.0, inset=2.5)
        C.layer(edge_g, GOLD, outline=0.3, rim=0.4, pillow=0.6, spec=0.95)
        cast_shadow(C, band | edge_g, reach=5.0, amt=0.6)
        thorn = np.zeros(z.shape, bool)
        for k in range(1, 8):
            c, tng = bezier([(7.9, 60.6), (6.6, 52.6), (0.0, 48.6)], k / 8.0)
            tng = tng / np.linalg.norm(tng)
            nrm = np.array([tng[1], -tng[0]])
            if nrm[0] < 0:
                nrm = -nrm                                       # point outward, away from the roundel
            la = (ax - c[0]) * nrm[0] + (z - c[1]) * nrm[1]      # along the thorn
            lb = (ax - c[0]) * tng[0] + (z - c[1]) * tng[1]      # across it
            lt = (la - 0.55) / 0.95
            m = torso & ~collar & (lt > 0) & (lt < 1) & (np.abs(lb) < 0.30 * (1 - lt) ** 0.8)
            C.layer(m, GOLD, outline=0.4, rim=0.6, pillow=1.0, spec=0.95, seed=15 + k)
            facet(C, m, lb)
            thorn |= m
        cast_shadow(C, thorn, reach=3.0, amt=0.55)

    # chest and back: the moon-and-stag roundel
    emblem(C, front & torso, np.where(front, x, -x), z - 52.6, 2.7)
    emblem(C, ~front & torso, -x, z - 53.6, 3.4)
    cast_shadow(C, front & torso & (np.hypot(x, z - 52.6) < 2.7) | ~front & torso & (np.hypot(x, z - 53.6) < 3.4), reach=6.0, amt=0.65)

    # pauldrons: three leaf-tipped moonsilver lames, the upper with an emerald enamel band
    should = (ax > 10.4) & (z > 50.8) & ~collar
    pu = np.arctan2(z - 55.0, -y) * 4.0
    tip = 1 - 2 * np.abs(((pu / 2.0) % 1.0) - 0.5)
    for i, z0 in enumerate((50.8, 53.2, 55.8)):
        edge = z0 - 0.8 * tip ** 1.4
        lame = should & (z >= edge) & (z < (edge + 2.6 if i < 2 else 99))
        plate(C, lame, seed=40 + i, engrave=False)
        C.scroll(lame, pu * 1.4, z * 1.4, inset=5.0)
        vein = lame & (np.abs(((pu / 2.0) % 1.0) - 0.5) < 0.025) & (z < edge + 1.6)
        C.col = np.where(vein[..., None], C.col * 0.45, C.col)
        C.layer(lame & (z < edge + 0.35), GOLD, outline=0.4, rim=0.6, pillow=0.9, spec=0.9)
        if i > 0:
            cast_shadow(C, lame, reach=5.0, amt=0.65)
    enamel = should & (z > 57.0) & (z < 58.2)
    C.layer(enamel, EMERALD, outline=0.6, rim=0.8, pillow=1.2, spec=0.9, seed=45)
    C.layer(should & (np.abs(z - 56.85) < 0.12) | should & (np.abs(z - 58.35) < 0.12), GOLD, outline=0.3, rim=0.4, pillow=0.6, spec=0.9)
    C.stud(rivets(pu, z, 2.0, 59.0, 0.2) * should)

    # belt: green leather, triquetra plaques, a crescent-moon buckle
    belt = (z > 42.4) & (z < 45.4)
    C.layer(belt, GREEN_LEATHER if POLISH["v3"] else LEATHER, outline=1.4, rim=1.8, pillow=6.0, spec=0.2, seed=51)
    C.stitch(belt, u)
    if POLISH["v3"]:                                     # gold edge lines along the belt
        C.layer(belt & ((np.abs(z - 42.75) < 0.16) | (np.abs(z - 45.05) < 0.16)), GOLD, outline=0.3, rim=0.4, pillow=0.6, spec=0.95)
    cell = ((u / 4.0) % 1.0) - 0.5
    plq = belt & (np.abs(cell) < 0.17) & (z > 42.85) & (z < 44.95) & (ax > 2.9)
    triquetra_plaque(C, plq, cell * 4.0, z - 43.9, 0.95)
    disc = front & (np.hypot(x, z - 43.9) < 2.0)
    metal(C, disc, MOON, 1.2, 0.85, seed=53)
    cres = disc & (np.hypot(x, z - 43.9) < 1.45) & (np.hypot(x - 0.45, z - 44.15) > 1.15)
    C.layer(cres, GOLD, outline=0.5, rim=0.8, pillow=1.4, spec=0.95)
    C.layer(disc & (np.hypot(x, z - 43.9) > 1.7), GOLD, outline=0.4, rim=0.6, pillow=0.9, spec=0.9)
    cast_shadow(C, plq | disc, reach=3.5, amt=0.55)

    # skirt: deep green tabards front and back (gold plait borders, an embroidered vine); scale skirt at the sides
    low = (z <= 42.4) & (z > 29.4)
    tab = low & ((aang < 0.42) | (aang > np.pi - 0.42))
    sides = low & ~tab
    fold = fbm3(p * np.array([1.8, 1.8, 0.15]), 1.0, 3, 61)
    C.layer(tab, CLOTH, tone=0.30 + 0.40 * fold + 0.12 * lit(n), outline=2.0, rim=2.4, pillow=6.0, spec=0.05, rim_amt=0.3)
    tv = np.where(front, aang, np.pi - aang)
    P7.plait(C, tab, z * 1.0, tv * 7.5, 1.85, 2.75, 1.6, stops=GOLD)
    ta = np.where(front, x, -x)
    if POLISH["v3"]:
        vine(C, tab & (z > 31.6) & (z < 37.6) & (np.abs(ta) < 1.6), ta, z, period=3.2)
        emblem(C, tab & front, ta, z - 39.6, 1.4)
        cast_shadow(C, tab & front & (np.hypot(ta, z - 39.6) < 1.4), reach=4.0, amt=0.6)
        C.layer(tab & (np.abs(np.abs(tv * 7.5) - 2.95) < 0.10) & (z > 31.3), GOLD, outline=0.3, rim=0.4, pillow=0.6, spec=0.9)
    else:
        vine(C, tab & (z > 31.6) & (z < 41.6) & (np.abs(ta) < 1.6), ta, z, period=3.2)
    hem = tab & (z < 31.2)
    scales(C, hem, np.abs(ta), -z, 0.9, 0.7, variant="B", small=True, seed=64)
    C.layer(tab & (np.abs(z - 31.3) < 0.18), GOLD, outline=0.3, rim=0.4, pillow=0.6, spec=0.9)
    scales(C, sides, u, -z, 2.4, 1.7, seed=62)
    C.layer(sides & (z < 30.2), MOON, outline=0.6, rim=0.9, pillow=1.4, spec=0.8)
    # the belt's shadow and a row of faceted gold leaf points hanging from it over the skirt
    cast_shadow(C, belt, reach=4.0, amt=0.6)
    leaf_points(C, low, u, -z, np.arange(0.7, 40.0, 1.4), -42.4, 1.6, 0.42, seed=66)

    # long-robe variants and shared texels (as the Stormforged painters)
    robe = z <= 29.4
    C.layer(robe, CLOTH, tone=0.28 + 0.4 * fold + 0.12 * lit(n), outline=0, rim=0, rim_amt=0, spec=0.05)
    P7.plait(C, robe, u * 1.0, z, 6.6, 8.8, 2.4, stops=GOLD)
    conflict = getattr(tm, "conflict", None)
    if conflict is not None and conflict.any():
        grown = distance_transform_edt(~conflict) < 3 * C.scale
        g = grown & C.cov
        C.col = np.where(g[..., None], ramp(0.18 + 0.25 * fold, CLOTH), C.col)
    return C


# ------------------------------------------------------------------ arms (Arms01)
def paint_arms(tm):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x)
    u = np.arctan2(z - 50.5, -y) * 2.6
    C.layer(C.cov, LEATHER, outline=0, rim=0, rim_amt=0, spec=0.15, seed=81)
    sleeve = (ax > 9.6) & (ax < 15.4)
    scales(C, sleeve, np.abs(u), ax, 1.35, 1.15, seed=82)
    cuff = (ax >= 15.4) & (ax < 16.9)
    plate(C, cuff, seed=83, engrave=False)
    C.layer(cuff & (np.abs(ax - 16.15) < 0.25), EMERALD, outline=0.4, rim=0.6, pillow=0.9, spec=0.9)
    C.layer(cuff & ((np.abs(ax - 15.75) < 0.08) | (np.abs(ax - 16.55) < 0.08)), GOLD, outline=0.3, rim=0.4, pillow=0.5, spec=0.9)
    cast_shadow(C, cuff, reach=4.0, amt=0.6)
    return C


# ------------------------------------------------------------------ legs (Legs01)
def paint_legs(tm):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x); ang = np.arctan2(ax - 3.7, -y); u = ang * 3.2
    fold = fbm3(p * np.array([1.4, 1.4, 0.3]), 1.0, 3, 91)
    C.layer(C.cov, CLOTH, tone=0.30 + 0.35 * fold + 0.15 * lit(n), outline=0, rim=0, rim_amt=0, spec=0.05)
    cg = ((np.abs(((z * 0.55 + u * 0.5) % 1.4) - 0.7) < 0.12) | (np.abs(((z * 0.55 - u * 0.5) % 1.4) - 0.7) < 0.12)) & (z < 27)
    C.layer(cg & C.cov, LEATHER, outline=0.8, rim=1.0, pillow=2.0, spec=0.3, seed=93)
    cuisse = (np.abs(ang) < 1.05) & (z > 27.5) & (z < 36.0)
    scales(C, cuisse & (z < 34.6), np.abs(u), -z, 1.2, 1.0, seed=94)
    top = cuisse & (z >= 34.6)
    plate(C, top, seed=95, engrave=False)
    P7.plait(C, top, z * 0.0 + u * 1.0, z, 34.75, 35.85, 1.2, stops=GOLD, width=0.2)
    C.layer(cuisse & (np.abs(np.abs(ang) - 1.0) < 0.05), GOLD, outline=0.3, rim=0.5, pillow=0.7, spec=0.9)
    cast_shadow(C, top, reach=4.0, amt=0.6)
    leaf_points(C, cuisse & (z < 34.6), np.abs(u), -z, np.arange(0.4, 4.0, 0.9), -34.6, 1.1, 0.30, seed=96)
    return C


# ------------------------------------------------------------------ gloves (Gloves06: flared cuff at the elbow end)
def paint_gloves(tm):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x)
    C.layer(C.cov, LEATHER, spec=0.15, rim_amt=0.2, seed=101)
    zc = 46.8 - (ax - 14.0) * 0.55
    vu = np.arctan2(z - zc, -(y - 1.5))
    cuff = (ax < 15.0) & (z > 43.5)
    tip = 1 - 2 * np.abs(((vu * 3.0 / 1.4) % 1.0) - 0.5)
    leafcuff = cuff & (ax > 13.6 + 0.9 * (1 - tip ** 1.3))
    metal(C, cuff & ~leafcuff, EMERALD, 1.0, 0.6, seed=102)
    plate(C, leafcuff, seed=103, engrave=False)
    vam = (ax >= 14.4) & (ax < 18.8) & (z > 40.5) & ~cuff
    plate(C, vam, seed=104, engrave=False)
    if not POLISH["v3"]:
        C.scroll(vam, vu * 4.0, ax * 1.6, inset=6.0)
    P7.plait(C, vam, vu * 3.2, ax, 15.4, 16.4, 1.55, stops=GOLD)
    C.layer(vam & (ax > 16.9) & (ax < 17.9) & (np.abs(vu) < 0.9), EMERALD, outline=0.6, rim=0.8, pillow=1.2, spec=0.95, seed=105)
    C.layer(vam & (ax > 18.35), GOLD, outline=0.8, rim=1.2, pillow=2.0)
    cast_shadow(C, leafcuff, reach=4.0, amt=0.6)
    if POLISH["on"]:                                     # three gold leaf blades down the vambrace, like the epic's talons
        blades = np.zeros(z.shape, bool)
        for k, off in enumerate((-0.55, 0.0, 0.55)):
            la = (ax - 15.3) / (2.6 if k == 1 else 2.0)
            m = vam & (la > 0) & (la < 1) & (np.abs(vu - off) < (0.16 if k == 1 else 0.12) * np.sqrt(np.clip(la, 0, 1)) * (1 - la) * 2.5)
            C.layer(m, GOLD, outline=0.4, rim=0.6, pillow=1.0, spec=0.95, seed=150 + k)
            facet(C, m, vu - off)
            blades |= m
        cast_shadow(C, blades, reach=3.0, amt=0.55)
    hand = (ax >= 18.8) | (z <= 40.5)
    back = n[..., 0] * np.sign(x) > -0.1
    scales(C, hand & back & (z > 38.6), np.abs(y) * 1.0, -z, 0.7, 0.55, variant="B", small=True, seed=106)
    fk = np.floor((38.6 - z) / 0.95)
    for i in range(5):
        metal(C, hand & back & (z <= 38.6) & (fk == i), MOON, 1.0, 0.85, seed=107 + i)
    return C


# ------------------------------------------------------------------ boots (Boots06: knee cop above 18.4, cuff band 14.2-18.4)
def paint_boots(tm):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x); ang = np.arctan2(ax - 5.4, -(y - 2.6)); aa = np.abs(ang)
    C.layer(C.cov, LEATHER, spec=0.15, rim_amt=0.2, seed=111)
    C.stitch(C.cov & (z <= 4.2), y * 1.5 + ang)
    greave = (z > 4.2) & (z < 14.2)
    plate(C, greave, seed=112, engrave=not POLISH["v3"])
    C.groove(greave)
    front = greave & (aa < 0.62)
    C.layer(front & (z > 5.2) & (z < 13.4), EMERALD, outline=0.8, rim=1.2, pillow=2.2, spec=0.9, seed=113)
    vine(C, front & (z > 5.4) & (z < 13.2), ang * 3.0, z, amp=0.25, period=3.0, w=0.08)
    C.layer(greave & (np.abs(aa - 0.68) < 0.06), GOLD, outline=0.4, rim=0.6, pillow=1.0, spec=0.9)
    C.layer(greave & (z < 4.9), GOLD, outline=1.0, rim=1.5, pillow=3.0)
    cuff = (z >= 14.2) & (z < 18.4)
    C.layer(cuff, EMERALD, outline=1.0, rim=1.4, pillow=3.0, spec=0.8, seed=114)
    P7.plait(C, cuff, ang * 3.0, z, 14.8, 17.8, 1.7, stops=GOLD)
    knee = z >= 18.4
    plate(C, knee, seed=115, engrave=not POLISH["v3"])
    kc = 21.3
    C.layer(knee & (np.hypot(ang * 3.2, z - kc) < 1.6), MOON, outline=1.0, rim=1.6, pillow=3.5, spec=0.8, seed=116)
    C.layer(knee & P7.triquetra(ang * 3.2, z - kc, 1.15, 0.15), GOLD, outline=0.5, rim=0.7, pillow=1.2, spec=0.8)
    tipz = 23.4 + 0.5 * (1 - 2 * np.abs(((ang * 3.0 / 1.2) % 1.0) - 0.5))
    C.layer(knee & (z > tipz), GOLD, outline=0.8, rim=1.2, pillow=2.0)
    cast_shadow(C, knee, reach=4.0, amt=0.6)
    cast_shadow(C, cuff, reach=3.0, amt=0.5)
    leaf_points(C, greave, ang * 3.0, -z, np.arange(-2.4, 2.5, 1.2), -14.2, 1.3, 0.34, seed=118)
    foot = (z <= 4.2) & (y < -0.5)
    sk = np.floor((-y - 0.5) / 1.1)
    for i in range(6):
        metal(C, foot & (sk == i), MOON, 1.0, 0.85, seed=117 + i)
    return C


# ------------------------------------------------------------------ cloak (cata cloak UV set 1)
def paint_cloak(tm):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    fold = fbm3(p * np.array([2.2, 2.2, 0.22]), 1.0, 3, 121)
    t = 0.26 + 0.45 * fold + 0.18 * lit(n) + 0.06 * (fbm3(p, 0.6, 2, 122) - 0.5)
    C.layer(C.cov, CLOTH, tone=t, outline=0, rim=0, rim_amt=0, spec=0.05)
    border = distance_transform_edt(C.cov) / C.scale
    band = C.cov & (border < 64) & (border > 6)
    C.layer(band, CLOTH, tone=t * 0.75, outline=0, rim=0, rim_amt=0, spec=0.05)
    along = np.arctan2(x, y) * 9.0 + z * 0.6
    P7.plait(C, band, along * 10.0, border, 14.0, 58.0, 30.0, stops=GOLD)
    C.layer(C.cov & (border <= 4.0), GOLD, outline=0.0, rim=0.0, rim_amt=0, pillow=1.0)
    P7.fur(C, P7.ragged(z, np.percentile(z[C.cov], 90), np.arctan2(x, y) * 6.0, z * 0.2, 0.9, 123) & C.cov, -z * 1.4,
           np.arctan2(x, y) * 6.0, seed=123)
    back = y > 0
    if POLISH["on"]:
        # a faint woven pattern in the green: small triquetrae on a lattice, a shade lighter than the cloth
        cu, cz = -x / 3.2, (z - 44.0) / 3.2
        off = 0.5 * (np.floor(cz) % 2)
        lu, lz = (cu + off) - np.floor(cu + off) - 0.5, cz - np.floor(cz) - 0.5
        weave = C.cov & (border > 66) & P7.triquetra(lu * 3.2, lz * 3.2, 0.95, 0.07)
        C.col = np.where(weave[..., None], lerp(C.col, ramp(np.full(z.shape, 0.75), CLOTH), 0.55), C.col)
        cast_shadow(C, C.cov & (border < 64) & (border > 6), reach=6.0, amt=0.45)
        cast_shadow(C, P7.ragged(z, np.percentile(z[C.cov], 90), np.arctan2(x, y) * 6.0, z * 0.2, 0.9, 123) & C.cov, reach=8.0, amt=0.6)
    emblem(C, back & C.cov & (z < np.percentile(z[C.cov], 86)), -x, z - 44.0, 7.5)
    cast_shadow(C, back & C.cov & (np.hypot(x, z - 44.0) < 7.5), reach=9.0, amt=0.65)
    # small gold leaves scattered down the back field, like embroidery
    lf = back & C.cov & (np.hypot(x, z - 44.0) > 8.6) & (border > 70)
    cellx, cellz = np.floor(x / 4.0), np.floor(z / 4.0)
    jx = hash2(cellx, cellz) - 0.5; jz = hash2(cellz, cellx + 3) - 0.5
    lx, lz = x - (cellx + 0.5 + 0.4 * jx) * 4.0, z - (cellz + 0.5 + 0.4 * jz) * 4.0
    a = 0.7 * np.sign(jx + 1e-3)
    ra = lx * np.cos(a) - lz * np.sin(a); rb = lx * np.sin(a) + lz * np.cos(a)
    leaf = lf & (((ra / 0.9) ** 2 + (rb / 0.38) ** 2) < 1) & (hash2(cellx + 7, cellz) > 0.35)
    C.layer(leaf, GOLD, outline=0.4, rim=0.6, pillow=1.0, spec=0.6)
    facet(C, leaf, rb)
    cast_shadow(C, leaf, reach=3.0, amt=0.5)
    return C


# ------------------------------------------------------------------ helm: stock Celtic scale helm 3 (HibHelm03), repainted
def helm_tm():
    name = helm_fit.helm("BC", "m", "hibhelm03")
    v, nn, uv, f = R.mesh(name)
    return bake_arrays.bake(v, nn, uv, f, size=512), name


def paint_helm(tm):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    cov = C.cov
    zz = z[cov]; z0, z1 = np.percentile(zz, 2), np.percentile(zz, 98)
    hz = (z - z0) / max(z1 - z0, 1e-6)
    ang = np.arctan2(x, -y)
    plate(C, cov, seed=131)
    scales(C, cov & (hz > 0.45) & (hz < 0.86), np.abs(ang) * 3.0, -hz * 6.0, 0.55, 0.42, seed=132)
    brow = cov & (hz >= 0.30) & (hz <= 0.45)
    metal(C, brow, MOON, 1.2, 0.85, seed=133)
    P7.plait(C, brow, ang * 4.0, hz * 10.0, 3.1, 4.4, 1.2, stops=GOLD, width=0.2)
    C.layer(cov & (hz > 0.86), GOLD, outline=0.6, rim=0.9, pillow=1.6, spec=0.9)
    gemm = cov & (np.hypot(ang * 3.0, (hz - 0.375) * 6.0) < 0.35) & (y < 0)
    C.layer(gemm, EMERALD, outline=0.4, rim=0.5, pillow=0.8, spec=1.0)
    return C


# ------------------------------------------------------------------ helm, pass 3: the Crown of Lugh
# The stock Lug helmet (objects 4827, items Lum_helm01_<sex>_<race>, every Hibernian race): an open-faced Sidhe helm with
# a tall crest and swept cheek blades, made for the Lugh NPC's head; the kit ships refitted copies (pipeline/fit_lugh.py). Its UVs are mirrored (both sides share texels), in six islands:
# 0/1 crest front/back, 2 cheek blades, 3 brow rim, 4 bowl, 5 neck flare.
LUGH = "lum_helm01"


def crown(race, sex):
    """The Crown of Lugh as the game draws it: the refitted copy (pipeline/fit_lugh.py) on this race's head."""
    return fit_lugh.fitted(helm_fit.PREFIX[race], sex)


def helm_tm_lugh():
    from scipy.sparse import coo_matrix
    from scipy.sparse.csgraph import connected_components
    name = helm_fit.helm("BC", "m", LUGH)       # painted in the stock mesh's space (the refit keeps the UVs)
    v, nn, uv, f = R.mesh(name)
    tm = bake_arrays.bake(v, nn, uv, f, size=512)
    key = np.round(uv * 2000).astype(np.int64)
    _, weld = np.unique(key[:, 0] * 100000 + key[:, 1], return_inverse=True)
    weld = weld.ravel()
    e = np.r_[f[:, [0, 1]], f[:, [1, 2]], f[:, [2, 0]]]
    a, b = weld[e[:, 0]], weld[e[:, 1]]
    k, lab = connected_components(coo_matrix((np.ones(len(a)), (a, b)), shape=(weld.max() + 1,) * 2), directed=False)
    # name the islands by what they are, not by their order in the file
    stats = []
    for i in range(k):
        m = lab[weld] == i
        stats.append((i, m.sum(), v[m][:, 2].min(), v[m][:, 2].max(), np.abs(v[m][:, 0]).max()))
    part = {}
    for i, cnt, z0, z1, xm in stats:
        if xm < 2.4 and z1 > 74:
            part[i] = "crest"
        elif z1 - z0 < 1.0:
            part[i] = "rim"
        elif z0 < 61:
            part[i] = "blade"
        elif z1 < 67:
            part[i] = "flare"
        else:
            part[i] = "bowl"
    im = Image.new("L", (512, 512), 0); dr = ImageDraw.Draw(im)
    codes = {"crest": 1, "blade": 2, "rim": 3, "bowl": 4, "flare": 5}
    for t in f:
        dr.polygon([tuple(uv[j] * 511) for j in t], fill=codes[part[lab[weld[t[0]]]]])
    labels = np.asarray(im)
    # texels the polygons missed (padding) take the nearest island
    miss = labels == 0
    if miss.any():
        _, (iy, ix) = distance_transform_edt(miss, return_indices=True)
        labels = labels[iy, ix]
    tm.part = {k_: (labels == c) & tm.covered for k_, c in codes.items()}
    return tm, name


def paint_helm_lugh(tm):
    """Moonsilver bowl with emerald leaf panels framed in gold, a faceted gold crest, gold cheek blades with an emerald
    enamel leaf down each, leaf scales on the neck flare, a moonstone on the brow."""
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x)
    P = tm.part
    th = np.abs(np.arctan2(x, -y))                         # 0 at the front, pi at the back (both sides share texels)
    C.layer(C.cov, MOON, outline=0, rim=0, rim_amt=0)
    # bowl: polished moonsilver, gold rim and centre ridge, three emerald leaves a side and one at the back
    bowl = P["bowl"]
    plate(C, bowl, seed=131, engrave=False)
    db = C.dist(bowl)
    if bowl.any():
        # depth: darker toward the lower edge and the back, soft flutes running up to the crest, bright crest-line ridges
        zb0, zb1 = np.percentile(z[bowl], 2), np.percentile(z[bowl], 98)
        hz = np.clip((z - zb0) / max(zb1 - zb0, 1e-6), 0, 1)
        shade = 0.62 + 0.38 * smoothstep(0.0, 0.75, hz) - 0.10 * (th / np.pi)
        fl = np.sin(2 * np.pi * th / 0.30)
        shade = shade * (1 - 0.16 * (1 - fl))
        C.col = np.where(bowl[..., None], C.col * shade[..., None], C.col)
        C.col = np.where((bowl & (fl > 0.94) & (hz > 0.15))[..., None], lerp(C.col, ramp(np.full(z.shape, 0.97), MOON), 0.45), C.col)
        # the lower tier: an emerald band with a gold plait
        tier = bowl & (hz < 0.17) & (db > 3.0)
        C.layer(tier, EMERALD, outline=0.5, rim=0.7, pillow=1.2, spec=0.9, seed=143)
        P7.plait(C, tier, th * 6.0, hz * 12.0, 0.35, 1.75, 1.0, stops=GOLD, width=0.2)
        cast_shadow(C, tier, reach=3.0, amt=0.5)
    for c_th, z0, z1, wmax in ((0.95, 64.4, 70.2, 0.30), (1.85, 63.6, 70.0, 0.30), (2.80, 63.4, 69.6, 0.26)):
        t = np.clip((z - z0) / (z1 - z0), 0, 1)
        half = wmax * np.sin(np.pi * t) ** 0.8 * (1 - 0.35 * t)
        leaf = bowl & (z > z0) & (z < z1) & (np.abs(th - c_th) < half)
        frame = bowl & (z > z0 - 0.35) & (z < z1 + 0.35) & (np.abs(th - c_th) < half + 0.07) & ~leaf
        C.layer(frame, GOLD, outline=0.3, rim=0.5, pillow=0.8, spec=0.95, seed=132)
        C.layer(leaf, EMERALD, outline=0.6, rim=0.8, pillow=1.4, spec=0.95, seed=133)
        vein = leaf & (np.abs(th - c_th) < 0.018 + 0.01 * (1 - t))
        C.col = np.where(vein[..., None], ramp(np.full(z.shape, 0.75), GOLD), C.col)
        cast_shadow(C, frame | leaf, reach=3.0, amt=0.5)
    C.layer(bowl & (db < 2.6), GOLD, outline=0.4, rim=0.6, pillow=1.0, spec=0.95, seed=134)
    C.layer(bowl & (db >= 3.2) & (db < 4.0), GOLD, outline=0.2, rim=0.3, pillow=0.4, spec=0.9, seed=135)
    cast_shadow(C, bowl & (db < 2.6), reach=3.0, amt=0.5)
    # crest: faceted gold with a moonsilver edge
    crest = P["crest"]
    dc = C.dist(crest)
    C.layer(crest, GOLD, outline=0.5, rim=0.8, pillow=1.6, spec=0.95, seed=136)
    C.layer(crest & (dc < 1.6) & (z > 68.0), MOON, outline=0.3, rim=0.4, pillow=0.6, spec=0.9, seed=137)
    # cheek blades: gold, an emerald leaf inlaid down the middle with a gold vein
    blade = P["blade"]
    dbl = C.dist(blade)
    C.layer(blade, GOLD, outline=0.5, rim=0.8, pillow=1.6, spec=0.95, seed=138)
    inlay = blade & (dbl > 0.55 * (dbl[blade].max() if blade.any() else 1.0))
    C.layer(inlay, EMERALD, outline=0.6, rim=0.8, pillow=1.4, spec=0.95, seed=139)
    di = C.dist(inlay)
    ridge = inlay & (di > np.percentile(di[inlay], 82) if inlay.any() else inlay)
    C.col = np.where(ridge[..., None], lerp(C.col, ramp(np.full(z.shape, 0.8), GOLD), 0.85), C.col)
    cast_shadow(C, blade & ~inlay, reach=2.0, amt=0.4)
    # brow rim: gold; neck flare: small leaf scales with a gold edge
    C.layer(P["rim"], GOLD, outline=0.4, rim=0.6, pillow=1.0, spec=0.95, seed=140)
    flare = P["flare"]
    scales(C, flare, th * 3.2, -z, 0.62, 0.50, variant="B", small=True, seed=141)
    df = C.dist(flare)
    C.layer(flare & (df < 2.4), GOLD, outline=0.3, rim=0.5, pillow=0.8, spec=0.95, seed=142)
    # moonstone on the brow, under the crest
    if bowl.any():
        fz = np.percentile(z[bowl & (th < 0.25)], 8) if (bowl & (th < 0.25)).any() else 66.5
        stone = bowl & (np.hypot(x * 1.0, z - (fz + 1.2)) < 0.75) & (th < 0.6)
        C.layer(stone, MOON, tone=0.55 + 0.4 * lit(n), outline=0.4, rim=0.5, pillow=0.8, spec=1.0, rim_amt=0.4)
        C.col = np.where(stone[..., None], lerp(C.col, np.array([214, 240, 236.0]), 0.35), C.col)
        C.layer(bowl & (np.abs(np.hypot(x, z - (fz + 1.2)) - 0.88) < 0.14) & (th < 0.6), GOLD, outline=0.2, rim=0.3, pillow=0.4, spec=0.95)
    return C


def antlers(name, head_top, scale):
    """Preview-only antler geometry (tubes) on the helm, to show the antlered silhouette. Returns a mesh name."""
    vs, fs, uvs, off = [], [], [], 0
    cx, cy, cz = head_top
    for s in (-1, 1):
        chains = [[(0.30, 0.0, 0.0), (0.75, 0.15, 0.55), (1.15, 0.25, 1.30), (1.25, 0.20, 2.05), (1.0, 0.1, 2.55)],
                  [(0.68, 0.14, 0.45), (1.20, -0.25, 0.75)], [(1.05, 0.22, 1.05), (1.65, 0.0, 1.55)],
                  [(1.2, 0.22, 1.75), (1.75, 0.15, 2.25)], [(0.4, 0.05, 0.12), (0.45, -0.45, 0.55)]]
        for ch in chains:
            pts = [np.array([cx + s * a * scale, cy + b * scale, cz + c * scale]) for a, b, c in ch]
            for i in range(len(pts) - 1):
                a, b = pts[i], pts[i + 1]
                r0 = 0.22 * scale * (1 - 0.5 * i / len(pts)); r1 = r0 * 0.8 if i < len(pts) - 2 else 0.03 * scale
                d = b - a; d /= np.linalg.norm(d)
                e1 = np.cross(d, [0, 0, 1.0]); e1 = e1 / max(np.linalg.norm(e1), 1e-6) if np.linalg.norm(e1) > 1e-3 else np.array([1.0, 0, 0])
                e2 = np.cross(d, e1)
                ring = []
                for c_, r in ((a, r0), (b, r1)):
                    for k in range(8):
                        th = 2 * np.pi * k / 8
                        ring.append(c_ + r * (np.cos(th) * e1 + np.sin(th) * e2))
                vs.append(np.array(ring)); uvs.append(np.tile([[0.5, 0.5]], (16, 1)))
                for k in range(8):
                    k2 = (k + 1) % 8
                    fs.append([off + k, off + 8 + k, off + 8 + k2]); fs.append([off + k, off + 8 + k2, off + k2])
                off += 16
    R.register(name, np.concatenate(vs), np.concatenate(uvs), np.array(fs))
    return name


# ------------------------------------------------------------------ preview
HEADS = {("BC", "m"): "Bri_m_Head01", ("BC", "f"): "Bri_f_Head01", ("AES", "m"): "Elf_m_Head01", ("AES", "f"): "elf_f_head01",
         ("Lur", "m"): "lur_m_head01", ("Lur", "f"): "lur_f_Head01", ("Fir", "m"): "fir_m_head01", ("Fir", "f"): "Fir_f_Head01"}
CLOAK = {"BC": "Cel", "AES": "Elf", "Lur": "Lur", "Fir": "Fir"}
SKIN = (196, 158, 132)


def outfit(race, sex, tex, cloak=None, helm=False):
    parts = [(f"Body05_{race}_{sex}", tex["body_" + sex]), (f"Legs01_{race}_{sex}", tex["legs"]),
             (f"Arms01_{race}_{sex}", tex["arms"]), (f"Gloves06_{race}_{sex}", tex["gloves"]), (f"Boots06_{race}_{sex}", tex["boots"])]
    if not helm:
        parts.append((HEADS[(race, sex)], SKIN))
    if cloak:
        parts.append((f"Cloak{cloak:02d}_{CLOAK[race]}_{sex}@1", tex["cloak"]))
    if helm and POLISH["v3"]:
        parts += [(HEADS[(race, sex)], SKIN), (crown(race, sex), tex["helm"])]
    elif helm:
        name = helm_fit.helm(race, sex, "hibhelm03")
        v = R.mesh(name)[0]
        top = (0.0, float(np.median(v[:, 1])), float(np.percentile(v[:, 2], 92)))
        parts += [(HEADS[(race, sex)], SKIN), (name, tex["helm"]),
                  (antlers(f"antlers_{race}_{sex}", top, (v[:, 2].max() - v[:, 2].min()) * 0.27), (196, 202, 214))]
    return parts


JOBS = {"body_m": ("body_m", paint_body, (512, 1024)), "body_f": ("body_f", lambda tm: paint_body(tm, female=True), (512, 1024)),
        "arms": ("arms", paint_arms, (512, 512)), "legs": ("legs", paint_legs, (512, 512)),
        "gloves": ("g6", paint_gloves, (512, 256)), "boots": ("bt6", paint_boots, (512, 256)), "cloak": ("cloak", paint_cloak, (512, 1024))}


def main(variant, only=()):
    """only: paint just these JOBS keys (and the helm), reuse the rest from out_<variant>/; ("render",) paints nothing."""
    only_render = "render" in only
    global VARIANT
    POLISH["on"] = variant[-1] in "23"
    POLISH["v3"] = variant.endswith("3")
    VARIANT = variant[0]
    out = WORK / f"out_{variant}"; out.mkdir(exist_ok=True)
    tex = {}
    for key, (bake_key, fn, size) in JOBS.items():
        if only_render or (only and key not in only):
            if (out / f"{key}.png").exists():
                tex[key] = Image.open(out / f"{key}.png"); continue
        tex[key] = fn(calib.bake(bake_key)).finish(size); tex[key].save(out / f"{key}.png"); print(variant, "painted", key)
    if only_render:
        tex = {k: Image.open(out / f"{k}.png") for k in JOBS}
        tex["helm"] = Image.open(out / ("helm_lugh.png" if POLISH["v3"] else "helm.png"))
    elif POLISH["v3"]:
        htm, _ = helm_tm_lugh()
        tex["helm"] = paint_helm_lugh(htm).finish((256, 256)); tex["helm"].save(out / "helm_lugh.png")   # pskins 5828
    else:
        htm, _ = helm_tm()
        tex["helm"] = paint_helm(htm).finish((256, 256)); tex["helm"].save(out / "helm.png")
    V = R.VIEWS
    name = {"A": "A: emerald court (green enamel scales, moonsilver rims)", "B": "B: moonsilver (silver scales, green enamel leaf ribs)",
            "A2": "A polished like the new epics (layered bands, cast shadows, chrome, faceted gold)",
            "A3": "A pass 3 (leaf scales, clean plate, tabard roundel)"}[variant]
    helm_note = "Crown of Lugh (stock Lug helmet mesh)" if POLISH["v3"] else "antlered helm (antlers are a preview mock-up)"
    r1 = R.render(outfit("BC", "m", tex, helm=True), views=[V[0], V[1], V[3]], width=380, height=600,
                  label=f"Regalia of Tara, concept {name}  |  Celt male, {helm_note}")
    r2 = P7.row([R.render(outfit("BC", "m", tex, cloak=1), views=[V[1], V[3]], label="with the Mantle of the Moon and Stag"),
                 R.render(outfit("AES", "f", tex), views=[V[0], V[1]], label="Elf female")])
    r3 = P7.row([R.render(outfit("Fir", "m", tex), views=[V[1]], label="Firbolg male"),
                 R.render(outfit("Lur", "m", tex), views=[V[0]], label="Lurikeen male"),
                 R.render(outfit("BC", "f", tex, cloak=1), views=[V[3]], label="Celt female + cloak"),
                 R.render(outfit("Fir", "f", tex), views=[V[0]], label="Firbolg female")])
    R.stack([r1, r2, r3]).save(WORK / f"tara_{variant}.png")
    close = [R.render([("Body05_BC_m", tex["body_m"])], views=[V[0], V[1], V[3]], width=360, height=420, label="chest, pauldrons, back"),
             R.render([("Gloves06_BC_m", tex["gloves"]), ("Boots06_BC_m", tex["boots"]), ("Arms01_BC_m", tex["arms"]),
                       ("Legs01_BC_m", tex["legs"])], views=[V[0], V[1]], width=360, height=520, label="arms, legs, gloves, boots")]
    R.stack(close).save(WORK / f"tara_{variant}_close.png")
    if POLISH["v3"]:
        heads = [R.render([(HEADS[(r, sx)], SKIN), (crown(r, sx), tex["helm"])], views=[V[0], V[1], V[2], V[3]],
                          width=230, height=250, label=f"Crown of Lugh, {lab}")
                 for r, sx, lab in (("BC", "m", "Celt male"), ("AES", "f", "Elf female"))]
        heads += [P7.row([R.render([(HEADS[(r, sx)], SKIN), (crown(r, sx), tex["helm"])], views=[V[1]], width=230,
                                   height=250, label=lab) for r, sx, lab in (("Fir", "m", "Firbolg m"), ("Lur", "m", "Lurikeen m"),
                                                                              ("Fir", "f", "Firbolg f"), ("Lur", "f", "Lurikeen f"))])]
        R.stack(heads).save(WORK / f"tara_{variant}_helm.png")
    print("done", variant)


if __name__ == "__main__":
    args = sys.argv[1:] or ["A"]
    only = tuple(a.split("=", 1)[1] for a in args if a.startswith("only="))
    only = tuple(k for o in only for k in o.split(","))
    for v in (a for a in args if not a.startswith("only=")):
        main(v, only)
