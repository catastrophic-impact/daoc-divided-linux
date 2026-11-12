"""Regalia of Camelot concept skins (Pendragon Sovereign, 2026-11-07). Preview only: nothing touches the client or server.

A Pendragon king's plate, in the new epics' layered look (as the Tara A2 polish): polished steel with chrome reflections,
crimson enamel bands edged in faceted gold, every raised piece casting a contact shadow, a gold chain of office over the
gorget, the red dragon of the Pendragons on chest, back and cloak, a crimson tabard with a gold lozenge border, and a
crimson mantle with an ermine collar. Same meshes and bakes as Tara and Stormforged (Body05 / Arms01 / Legs01 / Gloves06 /
Boots06, UVs shared by every race); the helm is the stock Albion full plate helm (albHelm03) repainted, keeping its eye
slot and breathing holes. Rendered on Albion figures.

    python paint_camelot.py [A|B ...]   A = royal steel (bright steel, crimson enamel, gold), B = gilded (gold-washed plate)
"""
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "tara"))
import paint_tara as T  # noqa: E402  (installs the polish patch on Canvas.layer)
from paint_tara import (np, Image, ImageDraw, distance_transform_edt, fbm3, smoothstep, lerp, rivets, P5, P7,  # noqa: E402
                        Canvas, ramp, lit, calib, R, bake_arrays, helm_fit, cast_shadow, facet, bezier, bezier_dist, hash2)

import paths  # noqa: E402  (pipeline paths; paint_tara put the pipeline on sys.path)
WORK = paths.WORK / "camelot"; WORK.mkdir(parents=True, exist_ok=True)
R.NIF = paths.NIF
R._cache.clear()
helm_fit.PREFIX.update({"BC": "bri", "AES": "ava", "SFS": "sar", "Hig": "hig", "Inc": "inc", "Hal": "hal"})

STEEL = [(9, 10, 12), (44, 47, 53), (102, 106, 114), (174, 178, 186), (240, 242, 246)]
GILT = [(20, 11, 3), (74, 45, 13), (146, 100, 34), (212, 164, 74), (255, 236, 178)]           # variant B: burnished gilt plate
BRASS_MAIL = [(7, 5, 3), (34, 25, 12), (82, 62, 30), (150, 120, 64), (222, 196, 136)]
P5_GUNMETAL = P5.GUNMETAL
NIELLO = [(3, 2, 2), (12, 9, 7), (26, 20, 15), (52, 42, 30)]                                    # black inlay inside the gold edges
GOLD = T.GOLD
CRIMSON = [(16, 2, 3), (58, 6, 10), (118, 14, 22), (178, 34, 40), (240, 128, 116)]
CRIMSON_LEATHER = [(14, 2, 3), (46, 7, 10), (92, 16, 20), (142, 34, 36)]
CLOTH = [(8, 1, 2), (34, 4, 7), (76, 10, 16), (126, 24, 30)]
LEATHER = [(10, 7, 6), (30, 20, 16), (58, 40, 30), (92, 66, 50)]
ERMINE = [(150, 146, 140), (196, 192, 186), (228, 226, 220), (244, 242, 238), (255, 255, 252)]
VARIANT = "A"

P5.BRONZE = GOLD                                     # Canvas.stud: gold rivets
T.EMERALD = CRIMSON
T.METAL_STRENGTH.update({id(STEEL): 0.85, id(GILT): 0.92, id(CRIMSON): 0.35})


def plate_stops():
    return STEEL if VARIANT == "A" else GILT


def use_palette():
    """paint_tara's helpers (plate, metal, gold_line, leaf_points) read these module globals."""
    T.MOON = plate_stops()
    T.GOLD = GOLD


def plate(C, mask, seed=1, engrave=False):
    C.layer(mask, plate_stops(), seed=seed, grain=0.14)
    if engrave:
        p = C.p
        C.scroll(mask, p[..., 0] * 1.5 + p[..., 1] * 1.5, p[..., 2] * 1.5, inset=6.0)


def gold(C, mask, w=1.0, seed=3):
    C.layer(mask, GOLD, outline=0.4 * w, rim=0.6 * w, pillow=1.0 * w, spec=0.95, seed=seed)


def enamel(C, mask, w=1.0, seed=5):
    C.layer(mask, CRIMSON, outline=0.6 * w, rim=0.8 * w, pillow=1.4 * w, spec=0.9, seed=seed)


def edge_line(C, region, v, v_at, half=0.12):
    gold(C, region & (np.abs(v - v_at) < half), 0.7)


# ------------------------------------------------------------------ the Pendragon dragon
_DRAGON = None


def dragon_image(N=512):
    """1 = the red dragon passant (facing left, wings raised, tail curled), drawn in a -1..1 frame, y up."""
    global _DRAGON
    if _DRAGON is not None:
        return _DRAGON
    im = Image.new("L", (N, N), 0); dr = ImageDraw.Draw(im)
    P = lambda x, y: ((x + 1) / 2 * N, (1 - y) / 2 * N)                      # noqa: E731
    W = lambda w: int(w * N / 2)                                            # noqa: E731
    # body and chest
    dr.ellipse([*P(-0.44, 0.16), *P(0.46, -0.34)], fill=1)
    dr.ellipse([*P(-0.52, 0.18), *P(-0.06, -0.30)], fill=1)
    # neck up to the head, head with open jaw and a forked tongue
    dr.line([P(-0.34, -0.02), P(-0.52, 0.22), P(-0.58, 0.44)], fill=1, width=W(0.26), joint="curve")
    dr.polygon([P(-0.48, 0.58), P(-0.60, 0.62), P(-0.78, 0.54), P(-0.92, 0.50), P(-0.80, 0.44), P(-0.66, 0.42),
                P(-0.86, 0.36), P(-0.74, 0.30), P(-0.56, 0.32), P(-0.46, 0.40)], fill=1)
    dr.line([P(-0.92, 0.43), P(-1.0, 0.47)], fill=1, width=W(0.03)); dr.line([P(-0.92, 0.43), P(-0.99, 0.38)], fill=1, width=W(0.03))
    dr.polygon([P(-0.52, 0.58), P(-0.44, 0.74), P(-0.46, 0.58)], fill=1)       # horn / ear
    dr.ellipse([*P(-0.66, 0.53), *P(-0.61, 0.49)], fill=0)                     # eye
    # wing: raised, three ribs ending in points, a scalloped trailing edge
    wing = [P(-0.16, 0.06), P(-0.24, 0.40), P(-0.14, 0.78), P(0.00, 0.98), P(0.08, 0.80), P(0.22, 0.94), P(0.30, 0.74),
            P(0.48, 0.84), P(0.50, 0.62), P(0.66, 0.66), P(0.56, 0.44), P(0.44, 0.40), P(0.36, 0.26), P(0.22, 0.24), P(0.12, 0.08)]
    dr.polygon(wing, fill=1)
    for a, b in (((-0.10, 0.16), (0.00, 0.92)), ((-0.04, 0.16), (0.24, 0.88)), ((0.04, 0.14), (0.46, 0.78))):
        dr.line([P(*a), P(*b)], fill=0, width=W(0.012))
    # legs: the near fore-leg raised, the others planted, three-clawed feet
    for pts in ([(-0.32, -0.12), (-0.50, -0.20), (-0.62, -0.06)], [(-0.22, -0.20), (-0.28, -0.52), (-0.42, -0.60)],
                [(0.20, -0.20), (0.30, -0.50), (0.18, -0.62)], [(0.32, -0.16), (0.48, -0.44), (0.40, -0.62)]):
        dr.line([P(*q) for q in pts], fill=1, width=W(0.16), joint="curve")
        fx, fy = pts[-1]
        for dx, dy in ((-0.10, 0.02), (-0.09, -0.05), (-0.04, -0.08)):
            dr.line([P(fx, fy), P(fx + dx * 1.2, fy + dy * 1.2)], fill=1, width=W(0.05))
    # tail: sweeps back and curls up over the hind quarters, ending in an arrowhead
    tail = [(0.36, -0.12), (0.62, -0.30), (0.82, -0.22), (0.90, 0.02), (0.80, 0.18), (0.66, 0.12), (0.70, 0.00)]
    dr.line([P(*q) for q in tail], fill=1, width=W(0.12), joint="curve")
    dr.polygon([P(0.70, 0.10), P(0.54, -0.10), P(0.82, -0.12)], fill=1)
    # dorsal spines along the neck
    for (x, y) in ((-0.40, 0.14), (-0.46, 0.26), (-0.50, 0.38)):
        dr.polygon([P(x, y), P(x + 0.10, y + 0.04), P(x + 0.02, y - 0.06)], fill=1)
    _DRAGON = np.asarray(im)
    return _DRAGON


def roundel(C, mask, a, b, r, field=None, beast=None):
    """Gold rim with a lozenge band, an enamel field and the dragon. a, b: local coords (a across, b up), radius r."""
    field = field or CRIMSON
    beast = beast or GOLD
    rr = np.hypot(a, b)
    disc = mask & (rr < r)
    if not disc.any():
        return
    C.layer(disc, field, outline=0.8, rim=1.0, pillow=1.6, spec=0.9, seed=71)
    rim = disc & (rr > r * 0.84)
    gold(C, rim, 1.0, seed=72)
    ang = np.arctan2(a, b)
    loz = rim & (np.abs(((ang * 6 / np.pi) % 1.0) - 0.5) * 2 + np.abs((rr - r * 0.92) / (r * 0.07)) < 0.85)
    C.layer(loz, CRIMSON, outline=0.3, rim=0.4, pillow=0.5, spec=0.95, seed=73)
    img = dragon_image(); N = img.shape[0]
    s = 0.78 * r
    px = np.clip(((a / s + 1) / 2 * N).astype(int), 0, N - 1); py = np.clip(((1 - b / s) / 2 * N).astype(int), 0, N - 1)
    inside = disc & (np.abs(a) < s) & (np.abs(b) < s) & (rr < r * 0.84)
    k = inside & (img[py, px] == 1)
    C.layer(k, beast, outline=0.5, rim=0.7, pillow=1.2, spec=0.9, seed=74)
    cast_shadow(C, k, reach=2.5, amt=0.55)
    cast_shadow(C, rim, reach=2.0, amt=0.4)


def crown(a, b, w):
    """A small crown: a band and five points tipped with balls. a across, b up, w = width."""
    h = w * 0.62
    band = (np.abs(a) < w / 2) & (b > 0) & (b < h * 0.35)
    k = np.round(a / (w / 4))
    du = a - k * (w / 4)
    pts = (np.abs(k) <= 2) & (b >= h * 0.35) & (b < h * 0.95) & (np.abs(du) < (w / 9) * (1 - (b - h * 0.35) / (h * 0.62)))
    balls = (np.abs(k) <= 2) & (np.hypot(du, b - h) < w * 0.06)
    return band | pts | balls


def lozenge_band(C, mask, u, v, v0, v1, period, field=GOLD, gem=None):
    """A gold band with a chain of lozenges, each set with a crimson enamel centre (Albion's answer to the plait)."""
    gem = gem or CRIMSON
    band = mask & (v >= v0) & (v <= v1)
    if not band.any():
        return
    vc, hh = (v0 + v1) / 2, (v1 - v0) / 2
    fu = (u / period) - np.floor(u / period) - 0.5
    dl = np.abs(fu) * 2 + np.abs((v - vc) / hh)
    C.layer(band, field, outline=0.4, rim=0.6, pillow=0.9, spec=0.95, seed=81)
    C.col = np.where((band & (np.abs(dl - 0.92) < 0.06))[..., None], C.col * 0.5, C.col)
    C.layer(band & (dl < 0.45), gem, outline=0.3, rim=0.4, pillow=0.6, spec=1.0, seed=82)
    facet(C, band & (dl >= 0.45) & (dl < 0.86), fu, amt=0.22)


def flute(C, mask, coord, period, amt=0.30):
    """Fluted plate (the Maximilian ridges): alternating lit ridges and shadowed hollows along 'coord', a bright crest line."""
    if not mask.any():
        return
    s = np.sin(2 * np.pi * coord / period)
    C.col = np.where(mask[..., None], C.col * (1 - amt * 0.5 * (1 - s))[..., None], C.col)
    crest = mask & (s > 0.93)
    C.col = np.where(crest[..., None], lerp(C.col, ramp(np.full(s.shape, 0.97), plate_stops()), 0.45), C.col)


def niello(C, mask):
    """Black inlay: the dark channel that sets every gold edge off the plate (gilded parade armour)."""
    C.layer(mask, NIELLO, outline=0.2, rim=0.3, pillow=0.4, spec=0.5, rim_amt=0.4, seed=9)


def trimmed_edge(C, region, v, v_edge, inward, w_gold, w_niello):
    """Gold edge at v_edge, then a niello channel just inside it (inward = +1 if the lame lies at larger v)."""
    dv = (v - v_edge) * inward
    gold(C, region & (dv >= -0.02) & (dv < w_gold), 0.6)
    niello(C, region & (dv >= w_gold) & (dv < w_gold + w_niello))


def lames(C, mask, v, v_top, step, count, seed=20, edge=True, down=True, flutes=None):
    """Overlapping plate lames: each band of 'step' along v, the upper (or outer) one casting its shadow on the next.
    Each lame is fluted along flutes=(coord, period) and trimmed with a gold edge and a niello channel."""
    bands = []
    for i in range(count):
        v0 = v_top - (i + 1) * step if down else v_top + i * step
        lam = mask & (v >= v0) & (v < v0 + step)
        plate(C, lam, seed=seed + i, engrave=False)
        if flutes is not None:
            flute(C, lam, *flutes)
        bands.append((lam, v0))
    for lam, v0 in bands:
        if edge:
            trimmed_edge(C, lam, v, v0 if down else v0 + step, 1 if down else -1, 0.13 * step, 0.11 * step)
        cast_shadow(C, lam, reach=3.5, amt=0.6)
    return bands


# ------------------------------------------------------------------ body (Body05 layout)
def paint_body(tm, female=False):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    if female:
        z = z - 2.0 * smoothstep(50.5, 47.5, z) + 0.5 * smoothstep(42.0, 35.0, z)
    ax = np.abs(x); front = y < 0.5
    ang = np.arctan2(x, -y); aang = np.abs(ang); u = aang * 7.5
    C.layer(C.cov, STEEL, tone=0.12 + 0.05 * lit(n), outline=0, rim=0, rim_amt=0, spec=0)
    C.chain(C.cov, u, -z, period=0.85)                                   # mail under everything

    torso = (z > 45.4) & (z < 61.5)
    mantle_lvl = 58.9 + 1.4 * smoothstep(8.5, 11.0, ax)
    collar = (z > mantle_lvl) & ~((ax > 10.6) & (n[..., 0] * np.sign(x) > 0.45))

    # the breastplate, with a raised medial ridge, and two fauld lames below it
    breast = torso & ~collar & (z >= 49.6)
    plate(C, breast, seed=11)
    flute(C, breast & (np.abs(x) > 1.6), u, 1.6, amt=0.18)
    facet(C, breast & front, x, amt=0.20)
    trimmed_edge(C, breast, z, 49.6, 1, 0.28, 0.24)
    lames(C, torso & ~collar & (z < 49.6), z, 49.6, 2.1, 2, seed=12, flutes=(u, 1.6, 0.18))
    cast_shadow(C, breast, reach=5.0, amt=0.6)

    # crimson enamel bands sweeping from each shoulder down under the roundel, gold-edged, gold fleurons outward
    bd, along = bezier_dist(ax, z, [(7.9, 60.6), (6.6, 52.6), (0.0, 48.6)])
    band = torso & (bd < 0.62) & ~collar
    edge_g = torso & (np.abs(bd - 0.70) < 0.10) & ~collar
    enamel(C, band, seed=14)
    C.col = np.where((band & (np.abs(((along * 18.0) % 1.0) - 0.5) < 0.06))[..., None], C.col * 0.6, C.col)
    gold(C, edge_g, 0.6)
    gold(C, band & (np.hypot(((along * 18.0) % 1.0 - 0.5) * 1.6, bd) < 0.16), 0.5)   # gold studs down the band
    cast_shadow(C, band | edge_g, reach=5.0, amt=0.6)
    fl = np.zeros(z.shape, bool)
    for k in range(1, 8):
        c, tng = bezier([(7.9, 60.6), (6.6, 52.6), (0.0, 48.6)], k / 8.0)
        tng = tng / np.linalg.norm(tng)
        nrm = np.array([tng[1], -tng[0]])
        if nrm[0] < 0:
            nrm = -nrm
        la = (ax - c[0]) * nrm[0] + (z - c[1]) * nrm[1]
        lb = (ax - c[0]) * tng[0] + (z - c[1]) * tng[1]
        lt = (la - 0.55) / 0.85
        m = torso & ~collar & (lt > 0) & (lt < 1) & ((np.abs(lb) < 0.26 * (1 - lt) ** 0.8) | (np.hypot(lb, la - 1.25) < 0.16))
        gold(C, m, 0.8, seed=15 + k)
        facet(C, m, lb)
        fl |= m
    cast_shadow(C, fl, reach=3.0, amt=0.55)

    # gorget: two steel rings, and the gold chain of office across the front with a dragon pendant
    gor = collar
    plate(C, gor, seed=30, engrave=False)
    edge_line(C, gor, z, mantle_lvl + 0.15)
    edge_line(C, gor, z, mantle_lvl + 1.6)
    cast_shadow(C, gor, reach=5.0, amt=0.6)
    chain_r = np.hypot(x, z - 63.0)
    chain = front & (np.abs(chain_r - 5.25) < 0.32) & (z < 61.8) & (z > 56.4)
    link = (np.arctan2(x, 63.0 - z) * 5.25 / 0.62) % 1.0
    gold(C, chain & (np.abs(link - 0.5) < 0.38), 0.7, seed=31)
    C.col = np.where((chain & (np.abs(link - 0.5) < 0.12) & (np.abs(chain_r - 5.25) < 0.12))[..., None], C.col * 0.35, C.col)
    cast_shadow(C, chain, reach=3.0, amt=0.6)
    pend = front & (np.hypot(x, z - 57.0) < 1.05)
    gold(C, pend, 1.0, seed=32)
    enamel(C, front & (np.hypot(x, z - 57.0) < 0.62), 0.6, seed=33)
    cast_shadow(C, pend, reach=3.0, amt=0.6)

    # chest and back: the dragon roundel
    roundel(C, front & torso, np.where(front, x, -x), z - 52.9, 3.1)
    roundel(C, ~front & torso, -x, z - 53.9, 3.9)
    cast_shadow(C, front & torso & (np.hypot(x, z - 52.9) < 3.1) | ~front & torso & (np.hypot(x, z - 53.9) < 3.9), reach=6.0, amt=0.7)

    # pauldrons: three scalloped lames, the upper with a crimson enamel band, gold rivets
    should = (ax > 10.4) & (z > 50.8) & ~collar
    pu = np.arctan2(z - 55.0, -y) * 4.0
    lobe = np.abs(np.sin(pu * np.pi / 2.0))
    for i, z0 in enumerate((50.8, 53.2, 55.8)):
        edge = z0 - 0.7 * lobe ** 0.5
        lam = should & (z >= edge) & (z < (edge + 2.6 if i < 2 else 99))
        plate(C, lam, seed=40 + i)
        flute(C, lam, pu, 1.3, amt=0.20)
        gold(C, lam & (z < edge + 0.32), 0.7)
        niello(C, lam & (z >= edge + 0.32) & (z < edge + 0.56))
        if i > 0:
            cast_shadow(C, lam, reach=5.0, amt=0.65)
    enamel(C, should & (z > 57.0) & (z < 58.2), seed=45)
    gold(C, should & (np.abs(z - 56.85) < 0.12) | should & (np.abs(z - 58.35) < 0.12), 0.5)
    C.stud(rivets(pu, z, 2.0, 59.0, 0.2) * should)
    cap = should & (n[..., 0] * np.sign(x) > 0.45)
    boss_r = np.hypot(y - 1.3, z - 56.2)
    boss = cap & (boss_r < 1.15)
    gold(C, boss, 1.0, seed=46)
    pet = np.arctan2(y - 1.3, z - 56.2)
    C.layer(boss & (boss_r < 0.72 + 0.12 * np.cos(pet * 6)), CRIMSON, outline=0.3, rim=0.5, pillow=0.8, spec=0.95, seed=47)
    gold(C, boss & (boss_r < 0.22), 0.5)
    cast_shadow(C, boss, reach=3.5, amt=0.65)

    # belt: dark leather, gold plaques set with crimson, a gold buckle with the crown
    belt = (z > 42.4) & (z < 45.4)
    C.layer(belt, CRIMSON_LEATHER, outline=1.4, rim=1.8, pillow=6.0, spec=0.3, seed=51)
    C.stitch(belt, u)
    cell = ((u / 3.2) % 1.0) - 0.5
    plq = belt & (np.abs(cell) < 0.16) & (z > 42.9) & (z < 44.9) & (ax > 2.9)
    gold(C, plq, 0.9, seed=52)
    enamel(C, plq & (np.hypot(cell * 3.2, z - 43.9) < 0.32), 0.5)
    buckle = front & (np.abs(x) < 2.0) & (z > 42.5) & (z < 45.3)
    gold(C, buckle, 1.2, seed=53)
    enamel(C, buckle & (np.abs(x) < 1.45) & (z > 43.0) & (z < 44.8), 0.8)
    gold(C, buckle & crown(x, z - 43.25, 2.1), 0.5, seed=54)
    cast_shadow(C, plq | buckle, reach=3.5, amt=0.55)

    # skirt: crimson tabards front and back (gold lozenge border, a gold crown), steel tassets at the sides
    low = (z <= 42.4) & (z > 29.4)
    tab = low & ((aang < 0.42) | (aang > np.pi - 0.42))
    sides = low & ~tab
    fold = fbm3(p * np.array([1.8, 1.8, 0.15]), 1.0, 3, 61)
    C.layer(tab, CLOTH, tone=0.30 + 0.40 * fold + 0.12 * lit(n), outline=2.0, rim=2.4, pillow=6.0, spec=0.05, rim_amt=0.3)
    tv = np.where(front, aang, np.pi - aang)
    lozenge_band(C, tab, z * 1.0, tv * 7.5, 2.05, 2.95, 1.2)
    ta = np.where(front, x, -x)
    cr = tab & crown(ta, z - 37.2, 2.4)
    gold(C, cr, 0.8, seed=62)
    facet(C, cr, ta, amt=0.2)
    cast_shadow(C, cr, reach=3.0, amt=0.55)
    lozenge_band(C, tab, ta * 1.0, z, 29.6, 30.8, 1.2)
    lames(C, sides, z, 42.4, 4.3, 3, seed=63, flutes=(u, 1.6, 0.18))
    cast_shadow(C, belt, reach=4.0, amt=0.6)
    T.leaf_points(C, low, u, -z, np.arange(0.7, 40.0, 1.4), -42.4, 1.1, 0.36, seed=66)

    robe = z <= 29.4
    C.layer(robe, CLOTH, tone=0.28 + 0.4 * fold + 0.12 * lit(n), outline=0, rim=0, rim_amt=0, spec=0.05)
    lozenge_band(C, robe, u * 1.0, z, 6.6, 8.8, 2.4)
    conflict = getattr(tm, "conflict", None)
    if conflict is not None and conflict.any():
        g = (distance_transform_edt(~conflict) < 3 * C.scale) & C.cov
        C.col = np.where(g[..., None], ramp(0.18 + 0.25 * fold, CLOTH), C.col)
    return C


# ------------------------------------------------------------------ arms (Arms01)
def paint_arms(tm):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x)
    C.layer(C.cov, STEEL, tone=0.12 + 0.05 * lit(n), outline=0, rim=0, rim_amt=0, spec=0)
    C.chain(C.cov, np.arctan2(z - 50.5, -y) * 2.6, ax, period=0.8)
    sleeve = (ax > 9.6) & (ax < 15.4)
    lames(C, sleeve, ax, 9.6, 1.15, 5, seed=82, down=False, flutes=(np.arctan2(z - 50.5, -y) * 2.6, 0.8, 0.20))
    cuff = (ax >= 15.4) & (ax < 16.9)
    plate(C, cuff, seed=83, engrave=False)
    enamel(C, cuff & (np.abs(ax - 16.15) < 0.25), 0.6)
    gold(C, cuff & ((np.abs(ax - 15.75) < 0.08) | (np.abs(ax - 16.55) < 0.08)), 0.5)
    cast_shadow(C, cuff, reach=4.0, amt=0.6)
    return C


# ------------------------------------------------------------------ legs (Legs01)
def paint_legs(tm):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x); ang = np.arctan2(ax - 3.7, -y); u = ang * 3.2
    C.layer(C.cov, STEEL, tone=0.12 + 0.05 * lit(n), outline=0, rim=0, rim_amt=0, spec=0)
    C.chain(C.cov, u, -z, period=0.8)
    cuisse = (np.abs(ang) < 1.15) & (z > 26.0) & (z < 36.0)
    lames(C, cuisse & (z < 34.6), z, 34.6, 1.7, 5, seed=94, flutes=(u, 1.0, 0.18))
    top = cuisse & (z >= 34.6)
    plate(C, top, seed=95, engrave=False)
    lozenge_band(C, top, u * 1.0, z, 34.75, 35.85, 1.0)
    gold(C, cuisse & (np.abs(np.abs(ang) - 1.1) < 0.05), 0.5)
    cast_shadow(C, top, reach=4.0, amt=0.6)
    return C


# ------------------------------------------------------------------ gloves (Gloves06)
def paint_gloves(tm):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x)
    plate(C, C.cov, seed=101)
    zc = 46.8 - (ax - 14.0) * 0.55
    vu = np.arctan2(z - zc, -(y - 1.5))
    cuff = (ax < 15.0) & (z > 43.5)
    lobe = np.abs(np.sin(vu * 3.0 / 1.4 * np.pi))
    flare = cuff & (ax > 13.6 + 0.7 * lobe ** 0.6)
    enamel(C, cuff & ~flare, seed=102)
    plate(C, flare, seed=103, engrave=False)
    gold(C, flare & (ax > 14.75), 0.5)
    vam = (ax >= 14.4) & (ax < 18.8) & (z > 40.5) & ~cuff
    plate(C, vam, seed=104, engrave=False)
    pass
    lozenge_band(C, vam, vu * 3.2, ax, 15.4, 16.4, 1.1)
    gold(C, vam & (ax > 18.35), 1.2)
    cast_shadow(C, flare, reach=4.0, amt=0.6)
    ridge = vam & (ax > 16.6) & (ax < 18.3) & (np.abs(vu) < 0.10 * (1 - (ax - 16.6) / 1.7) + 0.03)
    gold(C, ridge, 0.8, seed=150)
    facet(C, ridge, vu)
    cast_shadow(C, ridge, reach=3.0, amt=0.55)
    hand = (ax >= 18.8) | (z <= 40.5)
    back = n[..., 0] * np.sign(x) > -0.1
    fk = np.floor((40.5 - z) / 0.9)
    for i in range(7):
        m = hand & back & (fk == i)
        plate(C, m, seed=107 + i, engrave=False)
        cast_shadow(C, m, reach=2.0, amt=0.45)
    gold(C, hand & back & (np.abs(z - 40.3) < 0.18), 0.5)
    C.layer(hand & ~back, LEATHER, spec=0.15, rim_amt=0.2, seed=109)
    flute(C, vam & (ax < 16.6), vu, 0.5, amt=0.18)
    return C


# ------------------------------------------------------------------ boots (Boots06)
def paint_boots(tm):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x); ang = np.arctan2(ax - 5.4, -(y - 2.6)); aa = np.abs(ang)
    plate(C, C.cov, seed=111)
    greave = (z > 4.2) & (z < 14.2)
    plate(C, greave, seed=112)
    flute(C, greave & (aa > 0.34), ang * 3.0, 0.7, amt=0.20)
    facet(C, greave & (aa < 0.7), ang, amt=0.16)
    strip = greave & (aa < 0.30) & (z > 5.2) & (z < 13.4)
    enamel(C, strip, seed=113)
    lozenge_band(C, strip, z * 1.0, ang * 3.0, -0.75, 0.75, 1.1)
    cast_shadow(C, strip, reach=3.0, amt=0.5)
    gold(C, greave & (z < 4.9), 1.0)
    niello(C, greave & (z >= 4.9) & (z < 5.25))
    cuff = (z >= 14.2) & (z < 18.4)
    plate(C, cuff, seed=114, engrave=False)
    lozenge_band(C, cuff, ang * 3.0, z, 14.9, 17.7, 1.6)
    knee = z >= 18.4
    plate(C, knee, seed=115)
    kc = 21.3
    ros = knee & (np.hypot(ang * 3.2, z - kc) < 1.6)
    gold(C, ros, 1.2, seed=116)
    pet = np.arctan2(ang * 3.2, z - kc)
    C.layer(ros & (np.hypot(ang * 3.2, z - kc) < 1.1 + 0.25 * np.cos(pet * 5)), CRIMSON, outline=0.4, rim=0.6, pillow=1.0, spec=0.95)
    gold(C, knee & (np.hypot(ang * 3.2, z - kc) < 0.35), 0.5)
    cast_shadow(C, ros, reach=4.0, amt=0.6)
    cast_shadow(C, knee, reach=4.0, amt=0.6)
    cast_shadow(C, cuff, reach=3.0, amt=0.5)
    C.layer(C.cov & (z < 0.7), LEATHER, spec=0.15, rim_amt=0.2, seed=119)
    foot = (z <= 4.2) & (y < -0.5)
    sk = np.floor((-y - 0.5) / 1.1)
    for i in range(6):
        m = foot & (sk == i)
        plate(C, m, seed=117 + i, engrave=False)
        cast_shadow(C, m, reach=2.0, amt=0.45)
    return C


# ------------------------------------------------------------------ cloak (cata cloak UV set 1)
def paint_cloak(tm):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    fold = fbm3(p * np.array([2.2, 2.2, 0.22]), 1.0, 3, 121)
    t = 0.26 + 0.45 * fold + 0.18 * lit(n) + 0.06 * (fbm3(p, 0.6, 2, 122) - 0.5)
    C.layer(C.cov, CLOTH, tone=t, outline=0, rim=0, rim_amt=0, spec=0.05)
    border = distance_transform_edt(C.cov) / C.scale
    along = np.arctan2(x, y) * 9.0 + z * 0.6
    lozenge_band(C, C.cov & (border < 40), along * 10.0, border, 8.0, 36.0, 34.0)
    gold(C, C.cov & (border <= 4.0), 1.0)
    cast_shadow(C, C.cov & (border < 40) & (border > 6), reach=6.0, amt=0.45)
    # ermine collar: white fur, black tails
    top = np.percentile(z[C.cov], 90)
    erm = P7.ragged(z, top, np.arctan2(x, y) * 6.0, z * 0.2, 0.9, 123) & C.cov
    P7.FUR = ERMINE
    P7.fur(C, erm, -z * 1.4, np.arctan2(x, y) * 6.0, seed=123)
    ex, ez = np.arctan2(x, y) * 6.0, z * 1.0
    cx, cz = np.floor(ex / 1.6), np.floor(ez / 1.6)
    off = 0.5 * (cz % 2)
    fx, fz = ex / 1.6 - cx - off, ez / 1.6 - cz
    fx = fx - np.round(fx)
    tail = erm & (np.abs(fx) < 0.09 * (1 - np.clip(fz, 0, 1))) & (fz > 0.2) & (fz < 0.75)
    C.col = np.where(tail[..., None], lerp(C.col, np.array([12, 10, 10.0]), 0.85), C.col)
    cast_shadow(C, erm, reach=8.0, amt=0.6)
    back = y > 0
    # a faint damask of crowns in the crimson field
    cu, cz2 = -x / 3.4, (z - 44.0) / 3.4
    o2 = 0.5 * (np.floor(cz2) % 2)
    lu, lz = (cu + o2) - np.floor(cu + o2) - 0.5, cz2 - np.floor(cz2) - 0.5
    dam = C.cov & (border > 44) & ~erm & crown(lu * 3.4, lz * 3.4 + 0.6, 1.5)
    C.col = np.where(dam[..., None], lerp(C.col, ramp(np.full(z.shape, 0.75), CLOTH), 0.55), C.col)
    # the great roundel on the back: the red dragon on gold
    roundel(C, back & C.cov & ~erm & (z < np.percentile(z[C.cov], 86)), -x, z - 44.0, 7.5, field=GOLD, beast=CRIMSON)
    cast_shadow(C, back & C.cov & (np.hypot(x, z - 44.0) < 7.5), reach=9.0, amt=0.65)
    return C


# ------------------------------------------------------------------ helm: stock Albion full plate helm (albHelm03), repainted
STOCK_HELM = paths.TEX / "b_plt1_helm2.png"                 # fetched by pipeline/fetch_meshes.py


def helm_tm():
    name = helm_fit.helm("BC", "m", "albhelm03")
    v, nn, uv, f = R.mesh(name)
    return bake_arrays.bake(v, nn, uv, f, size=512), name


def openings(shape):
    """The stock helm's eye slot and breathing holes as a mask at the canvas size, without its thin dark seam lines:
    dark connected regions in the face-plate rows, kept if blob-shaped (holes) or large (the eye slot)."""
    from scipy.ndimage import label, find_objects
    h, w = shape
    stock = np.asarray(Image.open(STOCK_HELM).convert("RGB").resize((w, h), Image.LANCZOS), float)
    dark = (stock.mean(-1) < 42) & (np.arange(h)[:, None] < h * 0.62)
    lab, n = label(dark)
    keep = np.zeros(shape, bool); slot = np.zeros(shape, bool)
    k = (w / 256.0) ** 2
    for i, sl in enumerate(find_objects(lab), 1):
        hh, ww = sl[0].stop - sl[0].start, sl[1].stop - sl[1].start
        area = int((lab[sl] == i).sum())
        fill = area / float(hh * ww)
        if area > 120 * k and fill > 0.5:                                  # the eye slot
            slot |= lab == i
        elif 3 * k <= area <= 60 * k and max(hh, ww) <= 2.6 * min(hh, ww) and fill > 0.4:
            keep |= lab == i                                                # a breathing hole
    return slot, keep


def paint_helm(tm):
    """The crowned great helm: fluted gilt skull, a gold cross on the face, a gold-framed eye slot, rivets, a jewelled
    crown (lozenge band, crimson gems, ball-tipped points) over a crimson enamel band, black niello under every edge."""
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    cov = C.cov
    zz = z[cov]; z0, z1 = np.percentile(zz, 2), np.percentile(zz, 98)
    hz = (z - z0) / max(z1 - z0, 1e-6)
    ang = np.arctan2(x, -y)
    front = cov & (y < 0)
    slot, holes = openings(C.col.shape[:2])
    slot &= front; holes &= front & (hz < 0.6)
    plate(C, cov, seed=131)
    flute(C, cov & ~front & (hz < 0.62) | cov & (np.abs(ang) > 1.25) & (hz < 0.62), ang * 3.0, 0.42, amt=0.24)
    facet(C, front, x, amt=0.18)
    top = np.percentile(hz[slot], 97) if slot.any() else 0.55
    bot = np.percentile(hz[slot], 3) if slot.any() else 0.48
    sx = np.percentile(np.abs(x[slot]), 97) if slot.any() else 3.0
    # the gold cross on the face: an upright from the brow to the rim, a bar along the top of the eye slot
    upright = front & (np.abs(x) < 0.055 * (z1 - z0)) & (hz < 0.64) & ~slot
    bar = front & (hz > top + 0.012) & (hz < top + 0.045) & (np.abs(x) < sx * 1.12)
    cross = upright | bar
    niello(C, front & ~cross & ((np.abs(x) < 0.08 * (z1 - z0)) & (hz < 0.64) | (hz > top + 0.004) & (hz < top + 0.058) & (np.abs(x) < sx * 1.2)) & ~slot)
    gold(C, cross, 1.0, seed=134)
    facet(C, upright, x, amt=0.25)
    facet(C, bar, hz - (top + 0.0285), amt=0.2)
    C.stud(rivets(x * 6.0, hz * 60.0, 2.4, (top + 0.0285) * 60.0, 0.30) * bar + rivets(hz * 60.0, x * 6.0, 2.6, 0.0, 0.32) * upright)
    cast_shadow(C, cross, reach=4.0, amt=0.65)
    # a gold frame round the eye slot and each breathing hole
    sight = front & (hz >= bot - 0.004) & (hz <= top + 0.004) & (np.abs(x) < sx * 1.06) & ~upright
    if slot.any():
        C.layer(sight, NIELLO, tone=0.03 + 0.03 * lit(n), outline=0, rim=0, rim_amt=0, spec=0)
        cast_shadow(C, bar, reach=2.0, amt=0.5)
        ring = (distance_transform_edt(~slot) < 1.1 * C.scale) & ~slot & cov & ~(hz > top)
        gold(C, ring & ~cross, 0.6, seed=135)
        cast_shadow(C, ring, reach=2.5, amt=0.5)
        C.layer(slot, NIELLO, tone=0.02 + 0.03 * lit(n), outline=0, rim=0, rim_amt=0, spec=0)
    if holes.any():
        hr = (distance_transform_edt(~holes) < 0.9 * C.scale) & ~holes & cov
        C.col = np.where(hr[..., None], lerp(C.col, ramp(np.full(z.shape, 0.92), GOLD), 0.7), C.col)
        C.col = np.where(holes[..., None], np.array([6, 4, 3.0]), C.col)
    # the crimson enamel band and the jewelled crown above it
    trimmed_edge(C, cov, hz, 0.64, 1, 0.012, 0.012)
    enamel(C, cov & (hz > 0.664) & (hz <= 0.70), 0.6)
    circ = cov & (hz > 0.70) & (hz < 0.80)
    lozenge_band(C, circ, ang * 4.0, hz * 10.0, 7.0, 8.0, 0.55)
    k = np.round(ang / (np.pi / 6)); du = ang - k * (np.pi / 6)
    tall = (k % 2) == 0
    peak = np.where(tall, 0.97, 0.89)
    th = (hz - 0.80) / (peak - 0.80)
    pts = cov & (hz >= 0.80) & (th < 1) & (np.abs(du) < 0.17 * (1 - th) ** 0.9)
    ball = cov & (np.hypot(du * 2.2, hz - peak - 0.012) < 0.022)
    C.layer(pts | ball, plate_stops(), outline=0.4, rim=0.6, pillow=1.0, spec=0.9, seed=133)
    facet(C, pts, du, amt=0.22)
    C.col = np.where((pts | ball)[..., None], lerp(C.col, ramp(np.full(z.shape, 0.62), plate_stops()), 0.35), C.col)
    gem = circ & tall & (np.hypot(du * 3.0, (hz - 0.75) * 6.0) < 0.26)
    C.layer(gem, CRIMSON, outline=0.3, rim=0.4, pillow=0.6, spec=1.0)
    gold(C, circ & tall & (np.abs(np.hypot(du * 3.0, (hz - 0.75) * 6.0) - 0.30) < 0.04), 0.4)
    cast_shadow(C, circ | pts | ball, reach=4.0, amt=0.65)
    trimmed_edge(C, cov, hz, 0.0, 1, 0.035, 0.02)
    return C


def keep_openings(img):
    """Carry the stock texture's eye slot and breathing holes (its near-black texels) over onto the new skin."""
    stock = np.asarray(Image.open(STOCK_HELM).convert("RGB").resize(img.size, Image.LANCZOS), float)
    lum = stock.mean(-1)
    h = img.size[1]
    rows = np.arange(h)[:, None] < h * 0.62                                 # the face plate, not the leather strip below
    dark = (lum < 42) & rows
    out = np.asarray(img, float).copy()
    out[dark] = stock[dark] * 0.6
    return Image.fromarray(out.astype(np.uint8))


# ------------------------------------------------------------------ preview
HEADS = {("BC", "m"): "Bri_m_Head01", ("BC", "f"): "Bri_f_Head01", ("AES", "m"): "Ava_m_Head01", ("AES", "f"): "Ava_f_Head01",
         ("SFS", "m"): "sar_m_Head01", ("SFS", "f"): "sar_f_head01", ("Hig", "m"): "hig_m_head01", ("Hig", "f"): "hig_f_Head01",
         ("Inc", "m"): "Inc_m_Head01", ("Inc", "f"): "inc_f_Head01", ("Hal", "m"): "hal_m_head01", ("Hal", "f"): "Hal_f_Head01"}
CLOAK = {("BC", "m"): "Bri_m", ("BC", "f"): "Bri_f", ("AES", "m"): "ava_m", ("AES", "f"): "Ava_f", ("SFS", "m"): "Sar_m",
         ("SFS", "f"): "sar_f", ("Hig", "m"): "Hig_m", ("Hig", "f"): "hig_f", ("Inc", "m"): "Inc_m", ("Inc", "f"): "Inc_f",
         ("Hal", "f"): "Hal_f"}
SKIN = (200, 160, 134)


LIMBS = {"Hig": "HN"}                                # Highlanders wear the Highlander/Norse limb meshes


def outfit(race, sex, tex, cloak=None, helm=False):
    lr = LIMBS.get(race, race)
    parts = [(f"Body05_{race}_{sex}", tex["body_" + sex]), (f"Legs01_{lr}_{sex}", tex["legs"]),
             (f"Arms01_{lr}_{sex}", tex["arms"]), (f"Gloves06_{lr}_{sex}", tex["gloves"]), (f"Boots06_{lr}_{sex}", tex["boots"])]
    if cloak:
        parts.append((f"Cloak{cloak:02d}_{CLOAK[(race, sex)]}@1", tex["cloak"]))
    if helm:
        parts.append((helm_fit.helm(race, sex, "albhelm03"), tex["helm"]))
    else:
        parts.append((HEADS[(race, sex)], SKIN))
    return parts


JOBS = {"body_m": ("body_m", paint_body, (512, 1024)), "body_f": ("body_f", lambda tm: paint_body(tm, female=True), (512, 1024)),
        "arms": ("arms", paint_arms, (512, 512)), "legs": ("legs", paint_legs, (512, 512)),
        "gloves": ("g6", paint_gloves, (512, 256)), "boots": ("bt6", paint_boots, (512, 256)), "cloak": ("cloak", paint_cloak, (512, 1024))}


def main(variant, only=None):
    global VARIANT
    VARIANT = variant
    T.POLISH["on"] = True
    use_palette()
    P5.GUNMETAL = BRASS_MAIL if variant == "B" else P5_GUNMETAL
    out = WORK / f"out_{variant}"; out.mkdir(exist_ok=True)
    tex = {}
    for key, (bake_key, fn, size) in JOBS.items():
        if only == ["render"]:
            tex[key] = Image.open(out / f"{key}.png").convert("RGB"); continue
        if only and key not in only:
            continue
        tex[key] = fn(calib.bake(bake_key)).finish(size); tex[key].save(out / f"{key}.png"); print(variant, "painted", key)
    if only and only != ["render"]:
        return
    if only == ["render"]:
        tex["helm"] = Image.open(out / "helm.png").convert("RGB")
    else:
        htm, _ = helm_tm()
        tex["helm"] = paint_helm(htm).finish((256, 256)); tex["helm"].save(out / "helm.png")
    V = R.VIEWS
    name = {"A": "A: royal steel (polished steel, crimson enamel, faceted gold)", "B": "B: gilded (gold-washed plate, crimson enamel)"}[variant]
    r1 = R.render(outfit("BC", "m", tex, helm=True), views=[V[0], V[1], V[3]], width=380, height=600,
                  label=f"Regalia of Camelot, concept {name}  |  Briton male, crowned great helm")
    r2 = P7.row([R.render(outfit("BC", "m", tex, cloak=1), views=[V[1], V[3]], label="with the Mantle of the Red Dragon"),
                 R.render(outfit("AES", "f", tex), views=[V[0], V[1]], label="Avalonian female")])
    r3 = P7.row([R.render(outfit("Hig", "m", tex), views=[V[1]], label="Highlander male"),
                 R.render(outfit("SFS", "m", tex), views=[V[0]], label="Saracen male"),
                 R.render(outfit("BC", "f", tex, cloak=1), views=[V[3]], label="Briton female + cloak"),
                 R.render(outfit("Inc", "f", tex), views=[V[0]], label="Inconnu female")])
    R.stack([r1, r2, r3]).save(WORK / f"camelot_{variant}.png")
    close = [R.render([("Body05_BC_m", tex["body_m"])], views=[V[0], V[1], V[3]], width=360, height=420, label="chest, pauldrons, back"),
             R.render([("Gloves06_BC_m", tex["gloves"]), ("Boots06_BC_m", tex["boots"]), ("Arms01_BC_m", tex["arms"]),
                       ("Legs01_BC_m", tex["legs"])], views=[V[0], V[1]], width=360, height=520, label="arms, legs, gloves, boots")]
    R.stack(close).save(WORK / f"camelot_{variant}_close.png")
    print("done", variant)


if __name__ == "__main__":
    args = sys.argv[1:] or ["A"]
    only = [a[5:] for a in args if a.startswith("only=")]
    for v in [a for a in args if not a.startswith("only=")]:
        main(v, only[0].split(",") if only else None)
