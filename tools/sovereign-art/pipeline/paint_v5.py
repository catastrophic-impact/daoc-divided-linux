"""Stormforged 'Thunderwrought' v5 - repainted in the game's own style.

v4 read as out of place next to stock Midgard armor: too flat and dark (lum spread 0.08-0.16 vs stock 0.21-0.26,
highlights 0.4-0.8 vs 0.9+), half the fine detail, mushy chain, flat bands, and neon glow no stock set uses.
This painter works like the stock art: every plate, trim and inlay is a REGION shaded with a painted bevel - dark
outline, bright top-lit rim, pillowed centre - from a texture-space distance field (UV seams excluded), metals get
painted highlights, chain is crisp ~1 unit rings, and the storm motif is gold inlay with blue enamel, not light.

python paint_v5.py [piece ...] -> out_v5/*.png, sheets_v5/*.png. Preview only: nothing touches the client or server."""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter
from scipy.ndimage import distance_transform_edt

from paint_sf import fbm3, smoothstep, lerp, zigzag, rivets
from paint_sf_parts import rune_band, helm_tm
import calib
import render as R

HERE = Path(__file__).resolve().parent
import paths  # noqa: E402
OUT, SHEETS = paths.WORK / "out_v5", paths.WORK / "sheets_v5"
OUT.mkdir(exist_ok=True); SHEETS.mkdir(exist_ok=True)
LIGHT = np.array([0.30, -0.55, 0.78]) / np.linalg.norm([0.30, -0.55, 0.78])

# ------------------------------------------------------------------ palette (gradient stops dark -> light)
GUNMETAL = [(10, 11, 14), (40, 43, 50), (90, 96, 108), (168, 174, 188), (246, 248, 252)]
BRONZE = [(34, 18, 5), (92, 54, 16), (166, 112, 42), (228, 182, 100), (255, 238, 186)]
LEATHER = [(14, 10, 9), (40, 28, 22), (74, 54, 42), (112, 86, 66)]
CLOTH = [(6, 10, 22), (16, 30, 62), (34, 62, 118), (70, 110, 176)]
ENAMEL = [(10, 30, 80), (30, 90, 200), (90, 170, 255), (210, 240, 255)]


def ramp(t, stops):
    stops = np.asarray(stops, float); k = len(stops) - 1
    x = np.clip(t, 0, 1) * k
    i = np.minimum(np.floor(x).astype(int), k - 1); f = (x - i)[..., None]
    return stops[i] * (1 - f) + stops[i + 1] * f


def lit(n):
    return np.clip((n * LIGHT).sum(-1), 0, 1)


class Canvas:
    """Paint regions with painted bevels. d is measured in bake texels; seams are not edges."""

    def __init__(self, tm):
        self.tm, self.p, self.n = tm, tm.pos, tm.nrm
        self.cov = tm.covered
        self.col = np.zeros(tm.pos.shape)
        self.up = np.clip(tm.nrm[..., 2] * 0.5 + 0.5, 0, 1)
        self.scale = tm.covered.shape[0] / 1024.0

    def dist(self, mask):
        return distance_transform_edt(mask | ~self.cov) / self.scale

    def layer(self, mask, stops, tone=None, outline=3.5, rim=4.5, pillow=28.0, spec=0.6, rim_amt=0.75, grain=0.10, seed=1):
        if not mask.any():
            return
        k = 1.0 if outline >= 3.0 else 1.6                     # small details get proportionally wider bevels
        outline, rim, pillow = outline * k, rim * k, pillow * k
        p, n = self.p, self.n
        d = self.dist(mask)
        pil = smoothstep(0.0, pillow, d) ** 0.8
        g = fbm3(p, 2.2, 3, seed)
        wear = fbm3(p, 0.45, 2, seed + 5) - 0.5                 # fine hammer pits and scuffs
        t = (0.08 + 0.34 * pil + 0.16 * lit(n) + 0.16 * (self.up - 0.5) + grain * (g - 0.5) + 0.12 * wear) if tone is None else tone
        c = ramp(t, stops)
        hue = fbm3(p, 3.5, 2, seed + 13) - 0.5                  # slight warm/cool drift, like hand-mixed paint
        c = c * (1 + np.stack([0.10 * hue, 0.02 * hue, -0.10 * hue], -1))
        c = lerp(c, ramp(np.full(t.shape, 0.95), stops), spec * lit(n) ** 10)           # painted specular
        rimm = smoothstep(outline, outline + 0.8, d) * (1 - smoothstep(outline + rim * 0.5, outline + rim, d))
        c = lerp(c, ramp(np.full(t.shape, 0.85), stops), rim_amt * rimm * (0.35 + 0.65 * self.up))
        c = lerp(c, ramp(np.full(t.shape, 0.0), stops) * 0.6, 0.9 * (1 - smoothstep(outline * 0.4, outline, d)))
        self.col = np.where(mask[..., None], c, self.col)

    def groove(self, mask, width=1.4, amt=0.65):
        """An engraved line along the inside of a region's border (painted groove: dark line, light lip below)."""
        d = self.dist(mask)
        line = mask & (np.abs(d - 7.0) < width)
        lip = mask & (np.abs(d - 7.0 - width * 1.4) < width * 0.6)
        self.col = np.where(line[..., None], self.col * (1 - amt), self.col)
        self.col = np.where(lip[..., None], lerp(self.col, np.array([230, 236, 245.0]), 0.25), self.col)

    def scroll(self, mask, a, b, inset=14.0):
        """Engraved Norse scrollwork inside a region (a, b: mirror-safe surface coords): thin dark grooves, light lips."""
        d = self.dist(mask)
        inner = mask & (d > inset)
        w = np.sin(0.95 * a + 1.4 * np.sin(0.55 * b)) * np.cos(0.75 * b + 0.8 * np.sin(0.6 * a))
        line = inner & (np.abs(w) < 0.07)
        lip = inner & (np.abs(w - 0.11) < 0.035)
        self.col = np.where(line[..., None], self.col * 0.45, self.col)
        self.col = np.where(lip[..., None], lerp(self.col, np.array([235, 238, 245.0]), 0.22), self.col)

    def stitch(self, mask, along, inset=4.5, dash=0.45):
        """Light stitching just inside a leather region's border (dashes along the coordinate 'along')."""
        d = self.dist(mask)
        on = mask & (np.abs(d - inset) < 0.9) & ((along / dash) % 1.0 < 0.55)
        self.col = np.where(on[..., None], lerp(self.col, np.array([150, 128, 100.0]), 0.7), self.col)

    def chain(self, mask, u, v, period=1.05, seed=9):
        """Crisp interlocking rings: bright upper arcs, dark gaps, lower rows overlap the upper."""
        if not mask.any():
            return
        row = np.floor(v / (period * 0.62))
        uu = u / period + 0.5 * (row % 2)
        fu = uu - np.floor(uu) - 0.5
        fv = v / (period * 0.62) - row - 0.5
        d = np.sqrt(fu ** 2 + (fv * 0.62) ** 2 * 2.6)
        ring = smoothstep(0.20, 0.27, d) * (1 - smoothstep(0.40, 0.47, d))
        hi = np.clip(0.5 + 0.9 * (-fv) + 0.4 * (-fu), 0, 1)
        t = 0.03 + ring * (0.22 + 0.62 * hi ** 1.5 * (0.6 + 0.4 * lit(self.n))) + 0.05 * (fbm3(self.p, 3.0, 2, seed) - 0.5)
        c = ramp(t, GUNMETAL)
        self.col = np.where(mask[..., None], c, self.col)

    def stud(self, centres, radius=4.0):
        """Domed bronze rivets: centres is a 0..1 mask of rivet cores (any shape); painted as small bevelled domes."""
        m = centres > 0.5
        if m.any():
            self.layer(m, BRONZE, outline=1.0, rim=1.6, pillow=3.0, spec=0.9, rim_amt=0.8)

    def finish(self, size):
        a = self.col.copy()
        a[~self.cov] = a[self.cov].mean(0) if self.cov.any() else 0
        img = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))
        img = img.filter(ImageFilter.UnsharpMask(radius=1.2, percent=60, threshold=2))
        return img.resize(size, Image.LANCZOS)


# ------------------------------------------------------------------ shapes
def mjolnir(a, b, s):
    """Thor's hammer in a local frame (a across, b up) of size s: (head, handle, grip bands, pommel)."""
    ha, hb = a / s, b / s
    head = (hb > 0.12) & (hb < 0.60) & (np.abs(ha) < 0.38 + 0.22 * smoothstep(0.12, 0.6, np.abs(hb - 0.36) * 2.4 + 0.12))
    handle = (np.abs(ha) < 0.085) & (hb <= 0.12) & (hb > -0.62)
    grip = handle & (((hb + 0.62) / 0.11) % 1.0 < 0.35)
    pommel = (np.hypot(ha, hb + 0.70) < 0.11)
    return head, handle, grip, pommel


def bolt_mask(a, b, width, period, amp):
    return zigzag(a, b, period, amp) < width


# ------------------------------------------------------------------ body
def paint_body(tm, female=False):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    if female:
        # measured on the stock layout: her belt sits 2.0 higher (44.7-47) and her hem 1.5 higher (33-36);
        # remap heights below the chest onto the male bands so every band lands where the layout expects it
        z = z - 2.0 * smoothstep(50.5, 47.5, z) + 0.5 * smoothstep(42.0, 35.0, z)
    ax = np.abs(x); front = y < 0.5
    ang = np.arctan2(x, -y); u = ang * 7.5
    C.chain(C.cov, u, z)

    # breast/back plate: gunmetal, a raised keel, an engraved border line, bronze rims
    # curved, not boxy: a scooped neckline, sides that taper to the waist, a pointed lower edge
    plate_low = 47.2 - 2.4 * (ax / 9.0) ** 2
    plate_top = 57.6 + 1.6 * (ax / 9.4) ** 2
    side = 9.6 - 1.9 * smoothstep(55.0, 47.0, z)
    plate = (z > plate_low) & (z < plate_top) & (ax < side) & (z > 45.4)
    C.layer(plate, GUNMETAL, seed=21)
    C.scroll(plate & (ax > 6.6), ax * 1.4, z * 1.4)
    C.groove(plate)
    keel = plate & (ax < 0.55) & (z < plate_top - 0.8) & (z > plate_low + 0.9) & ((z < 46.5) | (z > 58.0))
    C.layer(keel, GUNMETAL, outline=1.2, rim=2.0, pillow=3.0, seed=22)
    rim_band = plate & ((z - plate_low < 0.85) | (plate_top - z < 0.8) | (side - ax < 0.75))
    C.layer(rim_band, BRONZE, outline=1.4, rim=2.0, pillow=4.0, seed=23)
    # chest sigil: bronze Mjolnir with a blue enamel core, gold bolts either side (built from |x|: mirror-safe)
    s = 6.2; zc = 52.2
    head, handle, grip, pommel = mjolnir(x, z - zc, s)
    sig = plate & front
    C.layer(sig & (head | handle | pommel), BRONZE, outline=1.6, rim=2.4, pillow=6.0, seed=24)
    C.layer(sig & grip, LEATHER, outline=0.8, rim=0.8, pillow=1.5)
    core = sig & head & (np.abs(x) < 0.16 * s) & (np.abs(z - zc - 0.36 * s) < 0.12 * s)
    C.layer(core, ENAMEL, outline=0.8, rim=1.2, pillow=3.0, spec=0.8)
    bolts = sig & bolt_mask(ax - 0.98 * s, z - zc + s, 0.16 * s, 0.62 * s, 0.17 * s) & (z > zc - 0.95 * s) & (z < zc + 0.75 * s)
    C.layer(bolts, BRONZE, outline=1.2, rim=1.8, pillow=3.0, spec=0.8, seed=25)
    bcore = bolts & (zigzag(ax - 0.98 * s, z - zc + s, 0.62 * s, 0.17 * s) < 0.05 * s)
    C.layer(bcore, ENAMEL, outline=0.4, rim=0.6, pillow=1.2, spec=0.9)
    C.stud(rivets(u, z, 3.4, 57.7, 0.32) * plate * (ax > 3.2) * (ax < 8.2))
    # back: a vertical gold bolt pair down the spine
    bb = plate & ~front & bolt_mask(ax - 1.4, z, 0.38, 3.0, 0.45) & (z < 57.8) & (z > plate_low + 1.2)
    C.layer(bb, BRONZE, outline=1.2, rim=1.8, pillow=3.0, seed=26)

    # gorget: gunmetal ring, bronze lips, engraved bronze runes
    collar = z > 59.4
    C.layer(collar, GUNMETAL, seed=31)
    C.layer(collar & ((z < 59.9) | (z > 62.9)), BRONZE, outline=1.0, rim=1.6, pillow=3.0)
    C.layer(collar & (rune_band(u * 1.2, z, 60.2, 62.6, 1.3, width=0.12) > 0.5), BRONZE, outline=0.8, rim=1.0, pillow=2.0, spec=0.8)

    # pauldrons: stacked gunmetal lames, each with its own bevel and bronze lower edge, bronze studs
    should = (ax > 10.8) & (z > 51.5)
    k = np.floor((z - 51.5) / 2.3); lf = (z - 51.5) / 2.3 - k
    pu = np.arctan2(z - 55.0, -y) * 4.0
    for i in range(6):
        lame = should & (k == i)
        C.layer(lame & (lf >= 0.16), GUNMETAL, seed=41 + i)
        C.scroll(lame & (lf >= 0.16), np.abs(pu) * 2.0, z * 2.0, inset=5.0)
        C.layer(lame & (lf < 0.16), BRONZE, outline=1.0, rim=1.6, pillow=3.0, seed=47 + i)
    C.stud(rivets(pu, z, 2.4, 51.5 + 2.3 * k + 1.25, 0.26) * should)

    # belt: leather strap, a few large bronze plaques, Mjolnir buckle
    belt = (z > 42.6) & (z < 45.4)
    C.layer(belt, LEATHER, outline=1.4, rim=1.8, pillow=6.0, spec=0.2, seed=51)
    C.stitch(belt, u)
    plq = belt & (np.abs(((u / 5.2) - np.floor(u / 5.2)) - 0.5) < 0.17) & (z > 43.0) & (z < 45.0) & (ax > 2.6)
    C.layer(plq, BRONZE, outline=1.2, rim=2.0, pillow=4.0, seed=52)
    C.layer(plq & (rune_band(u * 2.0, z, 43.3, 44.7, 1.0, width=0.14) > 0.5), GUNMETAL, outline=0.5, rim=0.5, pillow=1.0)
    buckle = front & belt & (ax < 2.0)
    C.layer(buckle, GUNMETAL, outline=1.4, rim=2.0, pillow=4.0, seed=53)
    bh, bhd, bg, bp = mjolnir(x, z - 43.85, 1.45)
    C.layer(buckle & (bh | bhd | bp), BRONZE, outline=0.9, rim=1.2, pillow=2.5, spec=0.8)

    # tassets over the chain skirt
    low = z <= 42.6
    tas_u = u / 4.4; tf = tas_u - np.floor(tas_u); half = np.clip(np.abs(tf - 0.5) / 0.4, 0, 1)
    shaped = z > 35.0 + 2.0 * (1 - np.sqrt(np.clip(1 - half ** 2, 0, 1)))
    tas = low & shaped & (half < 1) & ((np.abs(ang) < 0.9) | (np.abs(np.abs(ang) - np.pi) < 0.9))
    tk = np.floor((42.6 - z) / 2.3)
    for i in range(4):
        C.layer(tas & (tk == i), GUNMETAL, seed=61 + i)
    C.layer(tas & (((42.6 - z) / 2.3) % 1.0 > 0.86), BRONZE, outline=0.8, rim=1.2, pillow=2.0)

    # hem: bronze-bound gunmetal band with gold runes and a thin blue enamel line
    hem = (z > 29.4) & (z < 34.0)
    C.layer(hem, GUNMETAL, seed=63)
    C.layer(hem & ((z < 29.9) | (np.abs(z - 31.0) < 0.25) | (z > 33.5)), BRONZE, outline=0.9, rim=1.4, pillow=2.5)
    C.layer(hem & (rune_band(u * 1.1, z, 31.4, 33.3, 1.3, width=0.12) > 0.5), BRONZE, outline=0.7, rim=0.9, pillow=1.5, spec=0.8)
    # long-robe variants: a long chain hauberk with a steel and bronze hem. Not cloth: some of these texels are also
    # drawn on the female Body05 skirt edge (shared/wrapped UVs), where blue cloth showed as stray patches
    robe = z <= 29.4
    C.chain(robe, u, z)
    rhem = robe & (z < 9.2) & (z > 6.6)
    C.layer(rhem, GUNMETAL, seed=72)
    C.layer(rhem & ((z < 7.1) | (z > 8.7)), BRONZE, outline=0.8, rim=1.2, pillow=2.0)
    # texels the stock layout draws on two far-apart surfaces: plain chain, which reads right on both
    conflict = getattr(tm, "conflict", None)
    if conflict is not None and conflict.any():
        grown = distance_transform_edt(~conflict) < 3 * C.scale
        C.chain(grown & C.cov, u, z)
    return C


# ------------------------------------------------------------------ arms
def paint_arms(tm):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x)
    u = np.arctan2(z - 50.5, -y) * 2.6
    C.chain(C.cov, u * 1.0, ax)
    outer = (ax < 15.2) & (ax > 9.5) & (z > 50.3)
    k = np.floor((ax - 9.5) / 1.9); lf = (ax - 9.5) / 1.9 - k
    for i in range(3):
        C.layer(outer & (k == i) & (lf >= 0.14), GUNMETAL, seed=83 + i)
        C.layer(outer & (k == i) & (lf < 0.14), BRONZE, outline=0.9, rim=1.3, pillow=2.5)
    C.stud(rivets(u * 2.0, ax, 1.4, 9.5 + 1.9 * k + 0.55, 0.22) * outer)
    cop_d = np.abs(ax - 16.6)
    C.layer(cop_d < 1.5, GUNMETAL, seed=82)
    C.layer((cop_d < 1.5) & (cop_d > 1.15), BRONZE, outline=0.9, rim=1.3, pillow=2.5)
    return C


# ------------------------------------------------------------------ legs
def paint_legs(tm):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x); ang = np.arctan2(ax - 3.7, -y); u = ang * 3.2
    C.chain(C.cov, u, z)
    cuisse = (np.abs(ang) < 1.0) & (z > 25.5) & (z < 36.0)
    C.layer(cuisse, GUNMETAL, seed=92)
    C.groove(cuisse)
    C.layer(cuisse & ((np.abs(ang) > 0.86) | (z < 26.2)), BRONZE, outline=1.0, rim=1.5, pillow=3.0)
    bolt = cuisse & bolt_mask(np.abs(ang) * 3.2 - 0.8, z, 0.22, 2.8, 0.32) & (z > 27.2) & (z < 34.6)
    C.layer(bolt, BRONZE, outline=0.9, rim=1.3, pillow=2.5, spec=0.8)
    return C


# ------------------------------------------------------------------ gloves
def paint_gloves(tm):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x)
    C.layer(C.cov, LEATHER, spec=0.15, rim_amt=0.2, seed=101)
    vam = (ax < 19.4) & (z > 42.5)
    zc = 44.1 + (ax - 16.0) * 0.8
    vu = np.arctan2(z - zc, -(y - 1.3))
    C.layer(vam, GUNMETAL, seed=102)
    C.scroll(vam & (np.abs(vu - 0.6) > 0.7), np.abs(vu - 0.6) * 5.0, ax * 2.0, inset=5.0)
    C.layer(vam & ((np.abs(ax - 15.2) < 0.5) | (np.abs(ax - 19.0) < 0.45)), BRONZE, outline=1.0, rim=1.4, pillow=2.5)
    C.stud(rivets(vu * 3.0, ax, 1.1, 15.75, 0.2) * vam)
    C.stud(rivets(vu * 3.0, ax, 1.1, 18.45, 0.2) * vam)
    bolt = vam & bolt_mask(np.abs(vu - 0.6) * 2.4 - 0.6, ax, 0.22, 1.7, 0.3) & (ax > 15.9) & (ax < 18.4)
    C.layer(bolt, BRONZE, outline=0.8, rim=1.2, pillow=2.0, spec=0.8)
    hand = (ax >= 19.4) | (z <= 42.5)
    back = n[..., 0] * np.sign(x) > -0.1
    C.layer(hand & back & (z > 38.0), GUNMETAL, seed=103)
    fk = np.floor((38.0 - z) / 0.9)
    for i in range(5):
        C.layer(hand & back & (z <= 38.0) & (fk == i), GUNMETAL, outline=1.0, rim=1.4, pillow=3.0, seed=104 + i)
    C.stud(rivets(y, z, 1.3, 40.6, 0.24) * hand * back * (z > 38.0))
    return C


# ------------------------------------------------------------------ boots
def paint_boots(tm):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x); ang = np.arctan2(ax - 5.4, -(y - 2.6))
    C.layer(C.cov, LEATHER, spec=0.15, rim_amt=0.2, seed=111)
    C.stitch(C.cov & (z <= 4.2), y * 1.5 + ang)
    greave = z > 4.2
    C.layer(greave, GUNMETAL, seed=112)
    # two raised flutes either side of the bolts, instead of scrollwork (which read as cracks here)
    for a0 in (1.12, 1.55):
        C.layer(greave & (np.abs(np.abs(ang) - a0) < 0.07) & (z > 5.2) & (z < 13.8), GUNMETAL,
                outline=0.8, rim=1.2, pillow=2.0, spec=0.9, seed=117)
    C.groove(greave & (z < 14.0))
    bands = greave & ((z < 4.9) | (np.abs(z - 14.5) < 0.55) | (z > 23.2))
    C.layer(bands, BRONZE, outline=1.0, rim=1.5, pillow=3.0)
    bolts = greave & (np.abs(ang) < 0.9) & bolt_mask(np.abs(ang) * 3.0 - 0.9, z, 0.22, 2.6, 0.32) & (z > 5.4) & (z < 11.6)
    C.layer(bolts, BRONZE, outline=0.9, rim=1.3, pillow=2.5, spec=0.8)
    C.layer(bolts & (zigzag(np.abs(ang) * 3.0 - 0.9, z, 2.6, 0.32) < 0.07), ENAMEL, outline=0.3, rim=0.5, pillow=1.0, spec=0.9)
    C.layer(greave & (rune_band(ang * 3.0, z, 12.0, 13.9, 1.0, width=0.13) > 0.5), BRONZE, outline=0.6, rim=0.8, pillow=1.5)
    foot = (z <= 4.2) & (y < -0.5)
    sk = np.floor((-y - 0.5) / 1.1)
    for i in range(6):
        C.layer(foot & (sk == i), GUNMETAL, outline=1.0, rim=1.4, pillow=3.0, seed=113 + i)
    C.stud(rivets(ang * 3.0, z, 1.6, 5.6, 0.22) * greave * (z < 6.5))
    return C


# ------------------------------------------------------------------ cloak
def paint_cloak(tm):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x)
    fold = fbm3(p * np.array([2.2, 2.2, 0.22]), 1.0, 3, 121)
    t = 0.25 + 0.45 * fold + 0.18 * lit(n) + 0.06 * (fbm3(p, 0.6, 2, 122) - 0.5)
    C.layer(C.cov, CLOTH, tone=t, outline=0, rim=0, rim_amt=0, spec=0.05)
    border = distance_transform_edt(C.cov) / C.scale
    C.layer(C.cov & (border < 20) & (border > 3), BRONZE, outline=1.2, rim=1.8, pillow=5.0, seed=127)
    back = y > 0
    head, handle, grip, pommel = mjolnir(x, z - 44.0, 7.5)
    C.layer(back & (head | handle | pommel), BRONZE, outline=1.6, rim=2.4, pillow=8.0, seed=129)
    C.layer(back & grip, LEATHER, outline=0.8, rim=0.8, pillow=1.5)
    # one jagged strike from the upper right onto the hammer head (the cloak is not mirrored, so it can be asymmetric)
    from paint_sf_parts import _seg
    pts = [(5.6, 12.4), (2.6, 8.4), (4.2, 8.0), (1.4, 4.4), (2.4, 4.3), (0.6, 2.2)]
    bx, bz = -x, z - 44.0                                       # seen from behind: +x on the wearer's back is screen-left
    dd = np.min([_seg(bx, bz, *pts[i], *pts[i + 1]) for i in range(len(pts) - 1)], axis=0)
    taper = 0.75 - 0.35 * smoothstep(13.6, 3.0, bz)
    bl = back & (dd < taper) & ~(head | pommel)
    C.layer(bl, BRONZE, outline=1.2, rim=1.6, pillow=3.0, spec=0.8, seed=130)
    C.layer(bl & (dd < taper * 0.5), ENAMEL, outline=0.6, rim=0.8, pillow=2.0, spec=0.9)
    C.layer(back & (ax < 10.5) & (rune_band(np.arctan2(x, y) * 9.0, z, 15.0, 17.6, 1.5, width=0.12) > 0.5), BRONZE,
            outline=0.8, rim=1.0, pillow=2.0)
    return C


# ------------------------------------------------------------------ winged helm
def paint_helm(tm):
    C = Canvas(tm); p, n = C.p, C.n
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x)
    S = C.cov.shape[0]
    uu = np.arange(S)[None, :].repeat(S, 0) / S
    wings = (uu > 0.64) & C.cov
    shell = C.cov & ~wings
    cz = np.median(z[shell]); low = np.percentile(z[shell], 6)
    C.layer(shell, GUNMETAL, seed=131)
    C.scroll(shell & (ax > 1.6) & (z > low + 1.2), ax * 2.6, z * 2.6, inset=6.0)
    C.layer(shell & (z < low + 0.55), BRONZE, outline=1.0, rim=1.6, pillow=3.0)
    C.layer(shell & (ax < 0.5) & (z > cz), BRONZE, outline=1.0, rim=1.4, pillow=2.5)
    brow = shell & (y < -2.0) & (z > cz - 1.0) & (z < cz + 2.6) & bolt_mask(ax - 0.45, z, 0.16, 1.6, 0.18)
    C.layer(brow, BRONZE, outline=0.8, rim=1.0, pillow=2.0, spec=0.8)
    C.layer(brow & (zigzag(ax - 0.45, z, 1.6, 0.18) < 0.06), ENAMEL, outline=0.3, rim=0.4, pillow=0.8, spec=0.9)
    C.stud(rivets(np.arctan2(x, -y) * 4.0, z, 1.2, low + 1.05, 0.2) * shell)
    wa = np.arctan2(z - cz, y) * 5.0
    fk = np.floor(wa)
    for i in range(int(fk[wings].min()) if wings.any() else 0, int(fk[wings].max()) + 1 if wings.any() else 0):
        C.layer(wings & (fk == i), GUNMETAL, outline=1.2, rim=2.0, pillow=4.0, seed=140 + (i % 7))
    C.layer(wings & ((wa - fk) > 0.86), BRONZE, outline=0.6, rim=0.8, pillow=1.5)
    return C


JOBS = {"body_m": (paint_body, (512, 1024)), "body_f": (lambda tm: paint_body(tm, female=True), (512, 1024)), "arms": (paint_arms, (512, 512)),
        "legs": (paint_legs, (512, 512)), "gloves": (paint_gloves, (512, 256)), "boots": (paint_boots, (512, 256)),
        "cloak": (paint_cloak, (512, 1024))}


def main(only=()):
    import paint_sf_parts as SF
    tex = {}
    for key, (fn, size) in JOBS.items():
        if only and key not in only and (OUT / f"{key}.png").exists():
            tex[key] = Image.open(OUT / f"{key}.png"); continue
        tex[key] = fn(calib.bake(key)).finish(size); tex[key].save(OUT / f"{key}.png"); print("painted", key)
    if only and "helm" not in only and (OUT / "helm.png").exists():
        tex["helm"] = Image.open(OUT / "helm.png")
    else:
        tex["helm"] = paint_helm(helm_tm()).finish((512, 256)); tex["helm"].save(OUT / "helm.png"); print("painted helm")
    SF.SHEETS = SHEETS
    import paint_sf_parts
    paint_sf_parts.SHEETS = SHEETS
    paint_sf_parts.sheets(tex)


if __name__ == "__main__":
    main(tuple(sys.argv[1:]))
