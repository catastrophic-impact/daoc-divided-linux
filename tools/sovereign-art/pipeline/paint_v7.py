"""Stormforged 'Rune-Warlord' v7 - after the owner's concept sheet (Midgard berserker king): worn silver-iron plate
with knotwork engraving, pale wolf fur at the collar, cuffs and boot tops, wolf-head pauldrons, a navy tabard with
pale-gold plait borders and ice-blue runes, brown leather harness, a raven on the cloak, and a warlord helm or a rune crown.

Meshes: Body06 (big pauldrons), Gloves06 (flared cuff), Boots06 (knee cop); helms midHelm03 (warlord) or the
Crown of Zahur mesh; the cloak hood (Cloak02, hood up) is a wolf head, and a wolf face sits on the back (hood down). Painted with the v5 Canvas (painted bevels).
python paint_v7.py [piece ...] -> out_v7/*.png, sheets_v7/*.png. Preview only: nothing touches the client or server."""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy.ndimage import distance_transform_edt, gaussian_filter

from paint_sf import fbm3, smoothstep, lerp, rivets
from paint_sf_parts import rune_band, _seg
from paint_v5 import Canvas, ramp, lit
import bake_arrays
import calib
import helm_fit
import render as R

HERE = Path(__file__).resolve().parent
import paths  # noqa: E402
OUT, SHEETS = paths.WORK / "out_v7", paths.WORK / "sheets_v7"
OUT.mkdir(exist_ok=True); SHEETS.mkdir(exist_ok=True)

# the v7 meshes go first in each union, so texels they share with other variants are placed on their surface
calib.UNIONS.update({
    "b6_m": ([f"Body{i:02d}_HN_m" for i in (6, 5, 1, 2, 3, 4, 7, 8)], 1024, 0),
    "b6_f": ([f"Body{i:02d}_HN_f" for i in (6, 5, 1, 2, 3, 4, 7, 8)], 1024, 0),
    "g6": ([f"Gloves{i:02d}_HN_m" for i in (6, 5, 4, 1, 2, 3, 7, 8)], 512, 0),
    "bt6": ([f"Boots{i:02d}_HN_m" for i in (6, 5, 4, 1, 2, 3, 7, 8)], 512, 0)})

IRON = [(10, 10, 13), (36, 37, 42), (78, 79, 86), (146, 146, 150), (230, 228, 220)]
GOLD = [(36, 26, 10), (104, 80, 40), (176, 146, 88), (224, 204, 150), (250, 242, 214)]
LEATHER = [(18, 11, 7), (56, 33, 19), (102, 64, 38), (146, 102, 66)]
NAVY = [(5, 8, 22), (14, 24, 60), (30, 50, 108), (66, 96, 162)]
FUR = [(26, 22, 19), (72, 64, 55), (132, 121, 106), (192, 181, 162), (238, 231, 216)]
ICE = [(16, 46, 104), (54, 126, 216), (150, 210, 255), (236, 250, 255)]
CLOTH_DARK = [(14, 12, 14), (34, 30, 32), (60, 54, 54), (92, 84, 80)]


def P2(a, b):
    """2D coordinates as a point array for fbm3."""
    return np.stack([a, b, np.zeros_like(a)], -1)


# ------------------------------------------------------------------ shared painting helpers
def fur(C, mask, down, across, seed=1, tone=0.0):
    """Shaggy fur: long strands hanging along 'down', gathered in wavy tufts that darken at the root."""
    if not mask.any():
        return
    clump = fbm3(P2(across * 1.3, down * 0.22), 1.0, 3, seed)
    strand = fbm3(P2(across * 16.0, down * 0.45), 1.0, 2, seed + 3)
    wav = fbm3(P2(across * 0.9, down * 0.05), 1.0, 2, seed + 7)
    tipf = (down * 0.42 + 3.0 * wav) % 1.0                        # tuft layers, staggered so they never line up
    tuft = smoothstep(0.0, 0.85, tipf) * (1 - smoothstep(0.88, 1.0, tipf))
    t = 0.04 + 0.30 * clump + 0.30 * strand ** 1.4 + 0.30 * tuft + 0.10 * lit(C.n) + tone
    C.col = np.where(mask[..., None], ramp(t, FUR), C.col)


def ragged(edge_coord, level, noise_coord_a, noise_coord_b, amp, seed):
    """A shaggy border: edge_coord > level, with the level pushed around by tuft-sized noise."""
    w = fbm3(P2(noise_coord_a * 2.4, noise_coord_b), 1.0, 3, seed) - 0.5
    return edge_coord > level + amp * w * 2.2


def plait(C, mask, u, v, v0, v1, period, stops=GOLD, width=0.16):
    """Three-strand knotwork plait in the band v0..v1 along u: proper over/under at every crossing."""
    vc, hh = (v0 + v1) / 2, (v1 - v0) / 2
    ph = 2 * np.pi * u / period
    best_depth = np.full(u.shape, -9.0); best_d = np.full(u.shape, 9.0); near = np.full(u.shape, 9.0)
    for i in range(3):
        a = ph + 2 * np.pi * i / 3
        c = vc + hh * 0.72 * np.sin(a)
        slope = hh * 0.72 * 2 * np.pi / period * np.cos(a)
        d = np.abs(v - c) / np.sqrt(1 + slope ** 2) / hh
        depth = np.cos(a)
        on = d < width
        take = on & (depth > best_depth)
        best_depth = np.where(take, depth, best_depth); best_d = np.where(take, d, best_d)
        near = np.minimum(near, d)
    inside = mask & (v > v0) & (v < v1)
    strand = inside & (best_d < width)
    t = 0.30 + 0.55 * (1 - (best_d / width) ** 2) * (0.75 + 0.25 * lit(C.n)) + 0.1 * best_depth
    C.col = np.where(strand[..., None], ramp(t, stops), C.col)
    # dark outline round the top strand: this is what makes the lower strand read as passing underneath
    edge = inside & (best_d >= width * 0.78) & (best_d < width) | inside & ~strand & (near < width * 1.35)
    C.col = np.where(edge[..., None], C.col * 0.35, C.col)
    return strand


def triquetra(a, b, r, w):
    """Trinity knot (three interlaced arcs) inside a ring, centred on (0,0) of a local frame; returns a 0/1 mask."""
    rr = np.hypot(a, b)
    arcs = np.zeros(a.shape, bool)
    c, rho = 0.40 * r, 0.64 * r                      # circles overlap past the centre: three lobes round a curved triangle
    cs = [(c * np.sin(k * 2 * np.pi / 3 + np.pi), c * np.cos(k * 2 * np.pi / 3 + np.pi)) for k in range(3)]
    for i, (ca, cb) in enumerate(cs):
        ring = np.abs(np.hypot(a - ca, b - cb) - rho) < w
        others = [np.hypot(a - cs[j][0], b - cs[j][1]) < rho + w for j in range(3) if j != i]
        arcs |= ring & (others[0] | others[1])
    return arcs | (np.abs(rr - r * 1.0) < w * 0.9)


def glyph_glow(C, strokes, amt=0.55, blur=3.0):
    """Ice-blue rune inlay with a soft halo (kept small: the stock sets use painted light, not bloom)."""
    m = strokes > 0.5
    if not m.any():
        return
    C.layer(m, ICE, outline=0.5, rim=0.6, pillow=1.2, spec=0.9, rim_amt=0.5)
    halo = gaussian_filter(strokes.astype(float), blur * C.scale * 2) * C.cov
    halo = np.clip(halo / max(halo.max(), 1e-6) * 1.6, 0, 1) * ~m
    C.col = lerp(C.col, np.array([110, 180, 255.0]), (amt * halo)[..., None])


def decal(poly_list, a, b, size, cx=0.0, cy=0.0):
    """Rasterise polygons drawn in a -1..1 frame (x right, y up) and sample them at local coords (a, b)."""
    N = 512
    im = Image.new("L", (N, N), 0); d = ImageDraw.Draw(im)
    for pts, val in poly_list:
        d.polygon([((x + 1) / 2 * N, (1 - (y + 1) / 2) * N) for x, y in pts], fill=val)
    arr = np.asarray(im)
    ia = np.clip(((a - cx) / size + 1) / 2 * N, 0, N - 1).astype(int)
    ib = np.clip((1 - ((b - cy) / size + 1) / 2) * N, 0, N - 1).astype(int)
    inside = (np.abs((a - cx) / size) < 1) & (np.abs((b - cy) / size) < 1)
    return np.where(inside, arr[ib, ia], 0)


WOLF = [(0.70, -0.85), (0.78, -0.15), (0.60, 0.30), (0.50, 0.70), (0.38, 1.0), (0.22, 0.62), (0.05, 0.55),
        (-0.22, 0.42), (-0.55, 0.28), (-0.88, 0.16), (-1.0, 0.06), (-0.96, -0.05), (-0.62, -0.10), (-0.40, -0.14),
        (-0.72, -0.24), (-0.58, -0.40), (-0.12, -0.44), (0.10, -0.70), (0.32, -0.92)]
WOLF_EAR = [(0.48, 0.66), (0.38, 0.93), (0.27, 0.62)]
WOLF_MOUTH = [(-0.95, -0.06), (-0.40, -0.12), (-0.62, -0.17)]
RAVEN = [(-1.0, 0.50), (-0.72, 0.62), (-0.58, 0.74), (-0.40, 0.80), (-0.22, 0.72), (0.10, 0.52), (0.40, 0.48),
         (0.95, -0.05), (0.98, -0.22), (1.0, -0.42), (0.62, -0.34), (0.30, -0.38), (-0.05, -0.40), (-0.38, -0.15),
         (-0.52, 0.18), (-0.58, 0.40), (-0.72, 0.47)]
RAVEN_WING = [(-0.15, 0.45), (0.40, 0.40), (0.92, -0.06), (0.50, -0.08), (0.05, 0.05), (-0.20, 0.22)]


WOLF_FACE = [(-0.80, 1.0), (-0.40, 0.50), (0.40, 0.50), (0.80, 1.0), (0.74, 0.22), (0.88, -0.08), (0.46, -0.36),
             (0.22, -0.86), (0.0, -0.96), (-0.22, -0.86), (-0.46, -0.36), (-0.88, -0.08), (-0.74, 0.22)]
WOLF_EARS = [[(-0.72, 0.88), (-0.46, 0.50), (-0.66, 0.36)], [(0.72, 0.88), (0.46, 0.50), (0.66, 0.36)]]


def wolf_head(C, mask, a, b, size, cx, cy):
    """Forged wolf face in relief (front view, symmetric) on a dark sunk roundel with a gold ring."""
    la, lb = (a - cx) / size, (b - cy) / size
    rr = np.hypot(la, lb)
    rnd = mask & (rr < 1.18)
    C.layer(rnd, IRON, tone=np.full(a.shape, 0.10) + 0.08 * lit(C.n), outline=1.2, rim=1.8, pillow=3.0, spec=0.2)
    C.layer(rnd & (np.abs(rr - 1.10) < 0.08), GOLD, outline=0.6, rim=0.9, pillow=1.5, spec=0.7)
    head = (decal([(WOLF_FACE, 255)], a, b, size, cx, cy) > 128) & rnd
    t = 0.30 + 0.30 * smoothstep(0.0, 5.0, C.dist(head)) + 0.15 * lit(C.n) + 0.12 * smoothstep(-0.2, -0.8, lb) * (np.abs(la) < 0.3)
    C.layer(head, IRON, tone=t, outline=1.2, rim=2.2, pillow=5.0, spec=0.9, rim_amt=0.9, seed=301)
    ears = (decal([(e, 255) for e in WOLF_EARS], a, b, size, cx, cy) > 128) & head
    C.col = np.where(ears[..., None], C.col * 0.35, C.col)
    brow = head & (np.abs(lb - 0.30 - 0.25 * np.abs(la)) < 0.05) & (np.abs(la) < 0.55)
    C.col = np.where(brow[..., None], C.col * 0.45, C.col)
    nose = head & (np.hypot(la, (lb + 0.80) * 1.6) < 0.13)
    C.col = np.where(nose[..., None], C.col * 0.2, C.col)
    muzzle = head & (np.abs(la) < 0.04) & (lb < -0.35) & (lb > -0.7)
    C.col = np.where(muzzle[..., None], C.col * 0.5, C.col)
    eye = head & (np.hypot((np.abs(la) - 0.30) * 1.0, (lb - 0.10 + 0.25 * (np.abs(la) - 0.30)) * 2.2) < 0.10)
    glyph_glow(C, eye.astype(float), amt=0.5, blur=1.0)
    return head


def wolf_face(C, face, pelt, la, lb):
    """Wolf pelt face, front view (la across, lb up, in face units). Grey-wolf markings so it reads against the pelt:
    a dark mask over the brow and down the nose, pale muzzle, cheeks and brow spots, black ears and nose, ice eyes."""
    fur(C, pelt, -lb * 9.0, la * 5.0, seed=127)
    fur(C, face, -lb * 9.0 + np.abs(la) * 3.0, np.abs(la) * 5.0, seed=129, tone=-0.22)
    al = np.abs(la)
    pale = face & ((al > 0.30 - 0.1 * lb) & (lb < 0.0) | (al < 0.24) & (lb < -0.3)
                   | (np.hypot(al - 0.30, lb - 0.30) < 0.10))
    C.col = np.where(pale[..., None], lerp(C.col, ramp(np.full(la.shape, 0.92), FUR), 0.65), C.col)
    stripe = face & (al < 0.08) & (lb > -0.55) & (lb < 0.5)
    C.col = np.where(stripe[..., None], C.col * 0.55, C.col)
    ears = face & (decal([(e, 255) for e in WOLF_EARS], la, lb, 1.0) > 128)
    C.col = np.where(ears[..., None], C.col * 0.25, C.col)
    sock = np.hypot((al - 0.30), (lb - 0.10 + 0.25 * (al - 0.30)) * 2.2)
    C.col = np.where((face & (sock < 0.2))[..., None], C.col * 0.2, C.col)
    glyph_glow(C, (face & (sock < 0.11)).astype(float), amt=0.6, blur=1.6)
    nose = face & (np.hypot(la, (lb + 0.80) * 1.4) < 0.16)
    C.layer(nose, IRON, tone=np.full(la.shape, 0.05) + 0.1 * lit(C.n), outline=0.6, rim=0.8, pillow=1.5, spec=0.9)


def straps(C, mask, d, half, stitch_along=None):
    C.layer(mask & (d < half), LEATHER, outline=1.2, rim=1.6, pillow=4.0, spec=0.25, seed=211)
    if stitch_along is not None:
        C.stitch(mask & (d < half), stitch_along, inset=2.6, dash=0.4)


# ------------------------------------------------------------------ body
def paint_body(tm, female=False):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    if female:
        z = z - 2.0 * smoothstep(50.5, 47.5, z) + 0.5 * smoothstep(42.0, 35.0, z)
    ax = np.abs(x); front = y < 0.5
    ang = np.arctan2(x, -y); aang = np.abs(ang); u = ang * 7.5
    # under layer: dark quilted gambeson (shows only in gaps)
    q = np.abs(((u * 0.8) % 1.0) - 0.5) + np.abs(((z * 0.8) % 1.0) - 0.5)
    C.layer(C.cov, CLOTH_DARK, tone=0.35 + 0.35 * smoothstep(0.55, 0.2, q) + 0.15 * lit(n), outline=0, rim=0, rim_amt=0, spec=0)

    # chest: one upper plate with a trinity knot boss, then lames down to the belt (lamellar, as on the sheet)
    upper = (z > 53.6) & (z < 59.2) & (ax < 10.4)
    C.layer(upper, IRON, seed=21)
    C.groove(upper)
    plait(C, upper & front, x * 1.0, z, 53.8, 54.9, 2.2, stops=IRON, width=0.2)
    boss = front & (np.hypot(x, z - 56.4) < 2.7)
    C.layer(boss, IRON, outline=1.4, rim=2.4, pillow=6.0, spec=0.8, seed=22)
    C.layer(boss & triquetra(x, z - 56.4, 2.0, 0.24), GOLD, outline=0.7, rim=0.9, pillow=1.6, spec=0.8)
    k = np.floor((53.6 - z) / 2.0)
    for i in range(4):
        lame = (k == i) & (z > 45.4) & (ax < 10.0 - 0.4 * i)
        C.layer(lame, IRON, seed=23 + i)
        if i == 0:
            C.layer(lame & (((53.6 - z) / 2.0) % 1.0 > 0.84), GOLD, outline=0.7, rim=1.0, pillow=1.8, spec=0.6)
    C.stud(rivets(u, z, 2.4, 53.6 - 2.0 * k - 0.45, 0.2) * (z > 45.4) * (z < 53.6) * (ax > 1.2) * (ax < 9.0))

    # harness: two leather straps from the shoulders to the belt (a V front and back, mirror-safe), ring at the boss
    sd = _seg(ax, z, 6.2, 59.6, 2.6, 45.6)
    straps(C, (z > 45.4) & (z < 60.0) & ~boss, sd, 0.85, stitch_along=z * 2.0)
    ring = (np.abs(np.hypot(ax - 3.9, z - 52.0) - 0.75) < 0.26)
    C.layer(ring & (z > 45.4), IRON, outline=0.6, rim=0.8, pillow=1.4, spec=0.9)

    # fur mantle round the neck and over the tops of the pauldrons, shaggy lower edge
    top_n = n[..., 2]
    mantle_lvl = 58.8 + 1.4 * smoothstep(8.5, 11.0, ax)
    mantle = (ragged(z, mantle_lvl, u * 0.9, z * 0.2, 0.6, 31) | (z > 60.6)) & ~((ax > 10.6) & (n[..., 0] * np.sign(x) > 0.45))
    fur(C, mantle & C.cov, -z * 1.6, u * 1.1, seed=31, tone=0.05 * (top_n > 0.5))

    # pauldrons: plain iron shells under the fur, plait border, studs
    should = (ax > 10.4) & (z > 50.8) & ~mantle
    C.layer(should, IRON, seed=41)
    C.groove(should)
    pu = np.arctan2(z - 55.0, -y) * 4.0
    plait(C, should, pu * 1.0, z, 50.9, 52.3, 2.6)
    C.stud(rivets(pu, z, 1.6, 52.9, 0.2) * should)

    # belt: wide double leather belt, gold rune disc buckle with a glowing rune
    belt = (z > 42.4) & (z < 45.5)
    C.layer(belt, LEATHER, outline=1.4, rim=1.8, pillow=6.0, spec=0.2, seed=51)
    C.layer(belt & (np.abs(z - 43.95) < 0.18), LEATHER, tone=np.full(z.shape, 0.08), outline=0, rim=0, rim_amt=0)
    C.stitch(belt, u)
    C.stud(rivets(u, z, 1.8, 44.75, 0.2) * belt * (ax > 3.6))
    C.stud(rivets(u, z, 1.8, 43.15, 0.2) * belt * (ax > 3.6))
    disc = front & (np.hypot(x, z - 43.9) < 2.3)
    C.layer(disc, GOLD, outline=1.2, rim=2.0, pillow=5.0, spec=0.8, seed=52)
    C.layer(disc & (np.hypot(x, z - 43.9) < 1.6), IRON, outline=1.0, rim=1.4, pillow=3.0, seed=53)
    # bind-rune: stave, two branches and a cross bar
    rune = (_seg(x, z, 0, 42.75, 0, 45.05) < 0.17) | (_seg(ax, z, 0, 44.3, 0.8, 45.0) < 0.16) | \
           (_seg(ax, z, 0, 43.5, 0.8, 42.8) < 0.16)
    glyph_glow(C, (disc & rune).astype(float), amt=0.6, blur=2.0)

    # skirt: navy tabard down the front and back, iron lamellar tassets at the sides, fur trim at the hem
    low = (z <= 42.4) & (z > 29.4)
    tab = low & ((aang < 0.42) | (aang > np.pi - 0.42))
    side = low & ~tab
    # lamellar scales with rounded lower ends; upper rows overlap lower ones, so paint bottom-up
    H, W = 1.9, 1.5
    for i in range(7, -1, -1):
        zt = 42.4 - H * i
        fv = (zt - z) / (H * 1.35)
        cu = u / W + 0.5 * (i % 2); fu = cu - np.floor(cu) - 0.5
        shape = (fv > 0) & (fv < 0.72 + 0.28 * np.sqrt(np.clip(1 - (2.2 * fu) ** 2, 0, 1))) & (np.abs(fu) < 0.46)
        C.layer(side & shape, IRON, outline=1.0, rim=1.8, pillow=5.0, seed=61 + i)
        C.stud(rivets(u + 0.5 * W * (i % 2), z, W, zt - 0.35, 0.13) * side * shape)
    fold = fbm3(p * np.array([1.8, 1.8, 0.15]), 1.0, 3, 81)
    C.layer(tab, NAVY, tone=0.30 + 0.40 * fold + 0.12 * lit(n), outline=2.0, rim=2.4, pillow=6.0, spec=0.05, rim_amt=0.3)
    ta = np.where(front, x, -x)
    bord = tab & (np.abs(aang - 0.0) >= 0) & ((np.abs(np.where(front, aang, np.pi - aang) - 0.36) < 0.06))
    C.layer(bord, GOLD, outline=0.6, rim=0.8, pillow=1.4, spec=0.6)
    tv = np.where(front, aang, np.pi - aang)                     # 0 at the tabard centre line
    plait(C, tab, z * 1.0, tv * 7.5, 1.65, 2.55, 1.6)             # plait running down each side of the tabard
    glyph_glow(C, (tab & (rune_band(-z * 1.0, tv * 7.5, -1.0, 1.0, 1.25, width=0.11) > 0.5) & (z > 31.0) & (z < 41.0)
                   ).astype(float), amt=0.45, blur=1.8)
    hem = ragged(-z, -31.2, u * 0.9, z * 0.2, 0.5, 91) & (z > 29.4) & (z <= 42.4)
    fur(C, hem & side, -z * 1.6, u * 1.1, seed=91)
    C.layer(tab & (z < 30.4), GOLD, outline=0.8, rim=1.2, pillow=2.0, spec=0.6)

    # long-robe variants: tabard cloth with a plait hem (same reasoning as v6: no stray colours on other variants)
    robe = z <= 29.4
    C.layer(robe, NAVY, tone=0.28 + 0.4 * fold + 0.12 * lit(n), outline=0, rim=0, rim_amt=0, spec=0.05)
    plait(C, robe, u * 1.0, z, 6.6, 8.8, 2.4)
    conflict = getattr(tm, "conflict", None)
    if conflict is not None and conflict.any():
        grown = distance_transform_edt(~conflict) < 3 * C.scale
        g = grown & C.cov
        C.col = np.where(g[..., None], ramp(0.22 + 0.25 * fold, IRON), C.col)
    return C


# ------------------------------------------------------------------ arms
def paint_arms(tm):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x)
    u = np.arctan2(z - 50.5, -y) * 2.6
    C.layer(C.cov, LEATHER, outline=0, rim=0, rim_amt=0, spec=0.15, seed=81)
    # leather wraps crossing the sleeve
    wrap = (np.abs(((ax * 0.9 + u * 0.6) % 1.6) - 0.8) < 0.16) | (np.abs(((ax * 0.9 - u * 0.6) % 1.6) - 0.8) < 0.16)
    C.layer(wrap & C.cov, LEATHER, outline=0.8, rim=1.0, pillow=2.0, spec=0.3, seed=82)
    outer = (ax < 15.6) & (ax > 10.0) & (np.abs(u) > 0.0)
    k = np.floor((ax - 10.0) / 1.85); lf = (ax - 10.0) / 1.85 - k
    for i in range(3):
        C.layer(outer & (k == i) & (lf >= 0.14), IRON, seed=83 + i)
        C.layer(outer & (k == i) & (lf < 0.14), GOLD, outline=0.9, rim=1.3, pillow=2.5)
    C.stud(rivets(u * 2.0, ax, 1.4, 10.0 + 1.85 * k + 0.6, 0.22) * outer)
    fur(C, (ax >= 15.6) & (ax < 16.9) & C.cov, ax * 2.0, u * 2.0, seed=87)
    return C


# ------------------------------------------------------------------ legs
def paint_legs(tm):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x); ang = np.arctan2(ax - 3.7, -y); u = ang * 3.2
    fold = fbm3(p * np.array([1.4, 1.4, 0.3]), 1.0, 3, 91)
    C.layer(C.cov, CLOTH_DARK, tone=0.30 + 0.35 * fold + 0.15 * lit(n), outline=0, rim=0, rim_amt=0, spec=0.05)
    # cross-gartering from the boot tops up the shin and calf
    cg = ((np.abs(((z * 0.55 + u * 0.5) % 1.4) - 0.7) < 0.12) | (np.abs(((z * 0.55 - u * 0.5) % 1.4) - 0.7) < 0.12)) & (z < 27)
    C.layer(cg & C.cov, LEATHER, outline=0.8, rim=1.0, pillow=2.0, spec=0.3, seed=93)
    cuisse = (np.abs(ang) < 0.95) & (z > 27.5) & (z < 36.0)
    C.layer(cuisse, IRON, seed=94)
    C.groove(cuisse)
    plait(C, cuisse, z * 1.0, np.abs(ang) * 3.2, 1.9, 2.9, 1.7)
    glyph_glow(C, (cuisse & (rune_band(-z, np.abs(ang) * 3.2, -0.55, 0.55, 1.15, width=0.11) > 0.5) & (z > 28.6) & (z < 35.0)
                   ).astype(float), amt=0.4, blur=1.6)
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
    fur(C, ragged(-ax, -15.0, vu * 3.0, ax, 0.3, 102) & cuff & C.cov, ax * 3.0, vu * 6.0, seed=102)
    vam = (ax >= 14.4) & (ax < 18.8) & (z > 40.5) & ~(cuff & ragged(-ax, -15.0, vu * 3.0, ax, 0.3, 102))
    C.layer(vam, IRON, seed=103)
    plait(C, vam, vu * 3.2, ax, 15.5, 16.5, 1.55)
    glyph_glow(C, (vam & (rune_band(vu * 3.2, ax, 16.5, 18.1, 1.0, width=0.12) > 0.5)).astype(float), amt=0.4, blur=1.4)
    C.layer(vam & (ax > 18.35), GOLD, outline=0.8, rim=1.2, pillow=2.0)
    hand = (ax >= 18.8) | (z <= 40.5)
    back = n[..., 0] * np.sign(x) > -0.1
    C.layer(hand & back & (z > 38.6), IRON, seed=104)
    fk = np.floor((38.6 - z) / 0.95)
    for i in range(5):
        C.layer(hand & back & (z <= 38.6) & (fk == i), IRON, outline=1.0, rim=1.4, pillow=3.0, seed=105 + i)
    C.stud(rivets(y, z, 1.2, 40.0, 0.22) * hand * back * (z > 38.6))
    return C


# ------------------------------------------------------------------ boots (Boots06: knee cop above 18.5, cuff band 14-17)
def paint_boots(tm):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x); ang = np.arctan2(ax - 5.4, -(y - 2.6)); aa = np.abs(ang)
    C.layer(C.cov, LEATHER, spec=0.15, rim_amt=0.2, seed=111)
    C.stitch(C.cov & (z <= 4.2), y * 1.5 + ang)
    greave = (z > 4.2) & (z < 14.2)
    C.layer(greave, IRON, seed=112)
    C.groove(greave)
    # crossed leather straps over the greave sides, a rune column down the front
    xs = (np.abs(((z * 0.6 + aa * 1.6) % 1.5) - 0.75) < 0.13) | (np.abs(((z * 0.6 - aa * 1.6) % 1.5) - 0.75) < 0.13)
    C.layer(greave & xs & (aa > 0.75), LEATHER, outline=0.8, rim=1.0, pillow=2.0, spec=0.3, seed=113)
    glyph_glow(C, (greave & (rune_band(-z, ang * 3.0, -0.6, 0.6, 1.2, width=0.12) > 0.5) & (z > 5.4) & (z < 13.2)
                   ).astype(float), amt=0.45, blur=1.6)
    C.layer(greave & (aa < 0.75) & (np.abs(aa - 0.62) < 0.07), GOLD, outline=0.6, rim=0.8, pillow=1.4)
    C.layer(greave & (z < 4.9), GOLD, outline=1.0, rim=1.5, pillow=3.0)
    cuff = (z >= 14.2) & (z < 18.4)
    fur(C, ragged(-z, -18.4, ang * 3.0, z * 0.2, 0.5, 114) & cuff | (cuff & (z < 17.0)), -z * 2.0, ang * 3.0, seed=114)
    knee = z >= 18.4
    C.layer(knee, IRON, seed=115)
    kc = 21.3
    C.layer(knee & (np.hypot(ang * 3.2, z - kc) < 1.6), IRON, outline=1.0, rim=1.6, pillow=3.5, spec=0.8, seed=116)
    C.layer(knee & triquetra(ang * 3.2, z - kc, 1.15, 0.15), GOLD, outline=0.5, rim=0.7, pillow=1.2, spec=0.8)
    C.layer(knee & (z > 23.4), GOLD, outline=0.8, rim=1.2, pillow=2.0)
    foot = (z <= 4.2) & (y < -0.5)
    sk = np.floor((-y - 0.5) / 1.1)
    for i in range(6):
        C.layer(foot & (sk == i), IRON, outline=1.0, rim=1.4, pillow=3.0, seed=117 + i)
    C.stud(rivets(ang * 3.0, z, 1.6, 5.6, 0.22) * greave * (z < 6.5))
    return C


# ------------------------------------------------------------------ cloak
def paint_cloak(tm):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x)
    fold = fbm3(p * np.array([2.2, 2.2, 0.22]), 1.0, 3, 121)
    t = 0.25 + 0.45 * fold + 0.18 * lit(n) + 0.06 * (fbm3(p, 0.6, 2, 122) - 0.5)
    C.layer(C.cov, NAVY, tone=t, outline=0, rim=0, rim_amt=0, spec=0.05)
    border = distance_transform_edt(C.cov) / C.scale
    band = C.cov & (border < 64) & (border > 6)
    C.layer(band, NAVY, tone=t * 0.8, outline=0, rim=0, rim_amt=0, spec=0.05)
    along = np.arctan2(x, y) * 9.0 + z * 0.6
    plait(C, band, along * 10.0, border, 14.0, 58.0, 30.0)
    C.layer(C.cov & (border <= 4.0), GOLD, outline=0.0, rim=0.0, rim_amt=0, pillow=1.0)
    # fur collar along the top
    top = z > np.percentile(z[C.cov], 92)
    fur(C, ragged(z, np.percentile(z[C.cov], 90), np.arctan2(x, y) * 6.0, z * 0.2, 0.9, 123) & C.cov, -z * 1.4,
        np.arctan2(x, y) * 6.0, seed=123)
    # hood down (Cloak01/03): a wolf pelt face across the upper back, ears up toward the neck
    back = y > 0
    WS, WZ = 5.6, 53.8
    la, lb = -x / WS, (z - WZ) / WS
    face = back & (decal([(WOLF_FACE, 255)], -x, z, WS, 0.0, WZ) > 128)
    pelt = back & ragged(-np.hypot(la * 0.9, (lb - 0.1) * 0.8), -1.15, np.arctan2(la, lb) * 3.0, lb, 0.12, 128)
    wolf_face(C, face, pelt, la, lb)
    # hood up (Cloak02 only, texels above the shoulders): the wolf's head worn over the wearer's head
    hood = C.cov & (z > 63.2)
    if hood.any():
        fur(C, hood, -z * 2.0 + y * 0.8, np.arctan2(x, y) * 5.0, seed=131, tone=0.04)
        # the wolf's face over the hood opening, seen from the front: nose just above the wearer's brow, ears at the top
        # projected from above-front, so the face lies over the crown of the hood with its nose at the opening
        d = np.array([0.0, -0.55, 0.83]); upv = np.array([0.0, 0.83, 0.55])
        c0 = np.array([0.0, -4.0, 70.4])                      # measured: the hood's front edge runs y -6.5..-7 at z 68-69
        HS = 3.0
        fa, fb = -ax / HS, ((p - c0) @ upv) / HS               # the hood texture is mirrored left/right
        frontal = hood & ((n @ d) > 0.05)
        hface = frontal & (np.abs(fa) <= 1) & (np.abs(fb) <= 1) & (decal([(WOLF_FACE, 255)], fa, fb, 1.0) > 128)
        hpelt = frontal & (np.hypot(fa * 0.9, fb * 0.8) < 1.25)
        wolf_face(C, hface, hpelt & ~hface, fa, fb)
    C.layer(back & triquetra(x, z - 28.0, 2.6, 0.25), GOLD, outline=0.6, rim=0.9, pillow=1.6, spec=0.7)
    glyph_glow(C, (back & (np.hypot(x, z - 28.0) < 0.45)).astype(float), amt=0.5, blur=1.5)
    return C


# ------------------------------------------------------------------ helm: stock Mid Wolf Helm (midrp04), repainted
STOCK_HELM = None


HELMS = {"warlord": "midhelm03", "crown": "toahelm_zahur"}


def helm_tm(kind):
    name = helm_fit.helm("HN", "m", HELMS[kind])
    v, nn, uv, f = R.mesh(name)
    return bake_arrays.bake(v, nn, uv, f, size=512)


def paint_warlord(tm):
    """Norse spangenhelm (midHelm03): iron dome with gold spangen ridges, a gold crown band with points at the brow,
    a trinity knot and ice rune on the forehead, gold-rimmed spectacle guard, mail aventail, fur trim at the hem."""
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x); ang = np.arctan2(x, -y)
    C.chain(C.cov, ang * 4.0, z * 1.4)                                       # aventail and cheek mail
    dome = C.cov & (z > 66.4)
    C.layer(dome, IRON, seed=151)
    C.groove(dome)
    sp = dome & ((ax < 0.32) | (np.abs(y + 0.4) < 0.32)) & (z > 67.6)
    C.layer(sp, GOLD, outline=0.7, rim=1.0, pillow=1.8, spec=0.8)
    C.stud(rivets(ang * 3.2, z, 0.95, 67.6, 0.16) * dome * ~sp)
    pts = ang * 2.2
    peak = 0.25 + 1.25 * np.clip(1 - np.abs((pts % 1.0) - 0.5) * 3.0, 0, 1) * (np.cos(ang) > -0.2)
    band = C.cov & (z > 65.6) & (z < 66.9 + peak)
    C.layer(band, GOLD, outline=0.9, rim=1.4, pillow=2.5, spec=0.8, seed=152)
    plait(C, band & (z < 66.9), ang * 6.0, z, 65.75, 66.75, 1.1, stops=IRON, width=0.2)
    fr = C.cov & (y < -2.0)
    kc = 68.6
    C.layer(fr & triquetra(x, z - kc, 0.95, 0.12) & (z > 67.4), GOLD, outline=0.5, rim=0.7, pillow=1.2, spec=0.8)
    glyph_glow(C, (fr & (np.hypot(x, z - kc) < 0.22)).astype(float), amt=0.5, blur=1.0)
    spec_g = C.cov & (z <= 65.6) & (z > 63.0) & (y < -2.6)
    C.layer(spec_g, IRON, seed=153)
    C.layer(spec_g & (C.dist(spec_g) < 3.0), GOLD, outline=0.5, rim=0.7, pillow=1.2, spec=0.7)
    hem = C.cov & (z < 62.0) & ~spec_g
    fur(C, ragged(-z, -62.0, ang * 4.0, z * 0.2, 0.25, 154) & hem, -z * 2.5, ang * 4.0, seed=154)
    return C


def paint_crown(tm):
    """Rune crown on the Crown of Zahur mesh: iron band with an iron plait between gold rims, gold points tipped
    with ice-blue stones, dark fur-lined inside."""
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ang = np.arctan2(x, -y)
    cy = (np.percentile(y[C.cov], 2) + np.percentile(y[C.cov], 98)) / 2
    outward = (n[..., 0] * x + n[..., 1] * (y - cy)) > 0
    inner = C.cov & ~outward
    fur(C, inner, -z * 2.0, ang * 4.0, seed=161, tone=-0.12)
    band = C.cov & outward & (z < 68.4)
    C.layer(band, IRON, seed=162)
    C.layer(band & ((z < 66.35) | (z > 67.95)), GOLD, outline=0.7, rim=1.0, pillow=1.8, spec=0.8)
    plait(C, band, ang * 6.0, z, 66.45, 67.85, 1.2, stops=GOLD, width=0.18)
    pts = C.cov & outward & (z >= 68.4)
    C.layer(pts, GOLD, outline=0.9, rim=1.6, pillow=3.0, spec=0.85, seed=163)
    gem = pts & (z > 68.6) & (z < 69.6) & (np.abs(((ang * 8 / (2 * np.pi)) % 1.0) - 0.5) < 0.12)
    glyph_glow(C, gem.astype(float), amt=0.55, blur=1.2)
    return C


JOBS = {"body_m": ("b6_m", paint_body, (512, 1024)), "body_f": ("b6_f", lambda tm: paint_body(tm, female=True), (512, 1024)),
        "arms": ("arms", paint_arms, (512, 512)), "legs": ("legs", paint_legs, (512, 512)),
        "gloves": ("g6", paint_gloves, (512, 256)), "boots": ("bt6", paint_boots, (512, 256)),
        "cloak": ("cloak", paint_cloak, (512, 1024))}


# ------------------------------------------------------------------ preview sheets
def outfit(race, sex, tex, cloak=None, helm=True):
    parts = R.outfit(race, sex, tex, body=6, gloves=6, boots=6, cloak=cloak)
    if helm:
        kind = helm if isinstance(helm, str) else "warlord"
        parts.append((helm_fit.helm(race, sex, HELMS[kind]), tex["helm_" + kind]))
    return parts


def row(cells, gap=6):
    h = max(c.height for c in cells); w = sum(c.width for c in cells) + gap * (len(cells) - 1)
    out = Image.new("RGB", (w, h), (16, 18, 22)); x0 = 0
    for c in cells:
        out.paste(c, (x0, 0)); x0 += c.width + gap
    return out


def sheets(tex):
    V = R.VIEWS
    head = ("nor_m_head01", (176, 140, 116))
    r1 = R.render(outfit("HN", "m", tex, cloak=1), views=[V[0], V[1], V[3]], width=380, height=600,
                  label="Stormforged 'Rune-Warlord' (v7)  |  Norse male, warlord helm, cloak hood down")
    r1b = R.render(outfit("HN", "m", tex, cloak=2, helm=False), views=[V[1], V[2], V[3]], width=380, height=600,
                   label="Cloak hood up (/hood): the wolf's head over the wearer's head")
    r2 = row([R.render(outfit("HN", "m", tex, helm="crown"), views=[V[1], V[2]], label="Norse male, crown, no cloak"),
              R.render(outfit("HN", "f", tex), views=[V[0], V[1]], label="Norse female")])
    r3 = row([R.render(outfit("Tro", "m", tex), views=[V[1]], label="Troll male"),
              R.render(outfit("Dwa", "m", tex, helm="crown"), views=[V[1]], label="Dwarf male + crown"),
              R.render(outfit("Dwa", "f", tex, cloak=1), views=[V[3]], label="Dwarf female + cloak"),
              R.render(outfit("Tro", "f", tex, cloak=2, helm=False), views=[V[0]], label="Troll female, hood up")])
    R.stack([r1, r1b, r2, r3]).save(SHEETS / "rune_warlord.png")
    close = [("warlord helm (midHelm03 mesh)", [(helm_fit.helm("HN", "m", HELMS["warlord"]), tex["helm_warlord"]), head], [V[0], V[1], V[2]]),
             ("rune crown (Crown of Zahur mesh)", [(helm_fit.helm("HN", "m", HELMS["crown"]), tex["helm_crown"]), head], [V[0], V[1], V[2]]),
             ("wolf hood: up / down", [("Cloak02_nor_m@1", tex["cloak"]), head], [V[0], V[1]]),
             ("chest, fur shoulders, belt, tabard", [("Body06_HN_m", tex["body_m"])], [V[0], V[1], V[2]]),
             ("gloves and boots", [("Gloves06_HN_m", tex["gloves"]), ("Boots06_HN_m", tex["boots"])], [V[0], V[1], V[2]])]
    cells = [R.render(parts, views=views, width=340, height=360, label=lab) for lab, parts, views in close]
    cells[2] = row([cells[2], R.render([("Cloak01_nor_m@1", tex["cloak"])], views=[V[3]], width=340, height=360, label="hood down (back)")])
    R.stack(cells).save(SHEETS / "closeups.png")


def main(only=()):
    tex = {}
    for key, (bake_key, fn, size) in JOBS.items():
        if only and key not in only and (OUT / f"{key}.png").exists():
            tex[key] = Image.open(OUT / f"{key}.png"); continue
        tex[key] = fn(calib.bake(bake_key)).finish(size); tex[key].save(OUT / f"{key}.png"); print("painted", key)
    for kind, fn in (("warlord", paint_warlord), ("crown", paint_crown)):
        key = "helm_" + kind
        if only and key not in only and (OUT / f"{key}.png").exists():
            tex[key] = Image.open(OUT / f"{key}.png"); continue
        tex[key] = fn(helm_tm(kind)).finish((256, 256)); tex[key].save(OUT / f"{key}.png"); print("painted", key)
    sheets(tex)


if __name__ == "__main__":
    main(tuple(sys.argv[1:]))
