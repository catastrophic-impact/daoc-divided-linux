"""Stormforged armor set, painted from scratch on the 3D surface of the stock Midgard Catacombs meshes (Norse male
reference; every race/gender shares the layouts). Materials and techniques follow the Dubh Sluagh painter
(tools/pet-art/work/sluagh-armor/paint_sluagh_armor.py); the design is new:

  'Thunderwrought' - hammered blackened steel plate over gunmetal chain, bronze trim, dark leather; storm-blue
  glowing channels and runes, a hammer-and-bolt sigil on the chest, belt, cloak and helm brow; forge-hot rivets.

python paint_sf.py  -> out_sf/*.png and sheets_sf/*.png. Preview only: nothing touches the client or server."""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

import paths  # noqa: E402,F401  (helper paths in this repository)
from armorpaint import fbm3, smoothstep, lerp, chainmail  # noqa: E402
import calib  # noqa: E402
import render as R  # noqa: E402
import bake_arrays  # noqa: E402

HERE = Path(__file__).resolve().parent
OUT, SHEETS = paths.WORK / "out_sf", paths.WORK / "sheets_sf"
OUT.mkdir(exist_ok=True); SHEETS.mkdir(exist_ok=True)

# ------------------------------------------------------------------ palette
STEEL = np.array([46, 49, 58.0]); STEEL_DARK = np.array([13, 14, 18.0]); STEEL_HI = np.array([165, 172, 188.0])
BRONZE = np.array([168, 116, 52.0]); BRONZE_DARK = np.array([62, 38, 14.0]); BRONZE_HI = np.array([250, 214, 140.0])
LEATHER = np.array([26, 20, 18.0]); LEATHER_HI = np.array([62, 48, 40.0])
CLOTH = np.array([16, 26, 46.0])                       # storm-blue cloth
STORM = np.array([70, 150, 255.0]); SPARK = np.array([228, 242, 255.0]); FORGE = np.array([255, 150, 50.0])
LIGHT = np.array([0.35, -0.55, 0.76]) / np.linalg.norm([0.35, -0.55, 0.76])
RUNES = "ᛋᛏᛟᚱᛗᚠᛟᚱᚷᛖᛞ"


def lit(n):
    return np.clip((n * LIGHT).sum(-1), 0, 1)


def shade(col, n, relief):
    up = np.clip(n[..., 2] * 0.5 + 0.5, 0, 1)
    return col * (0.50 + 0.34 * relief + 0.30 * lit(n) + 0.10 * (up - 0.5))[..., None]


def steel(p, n, relief, seed=1):
    """Hammered blackened steel: peen marks, grime in low spots, bright worn edges."""
    peen = fbm3(p, 0.55, 2, seed + 7)                         # small hammer dimples
    dent = fbm3(p, 1.2, 3, seed + 31)
    relief = np.clip(relief + 0.20 * (dent - 0.5) + 0.12 * (peen - 0.5), 0, 1)
    grain = fbm3(p, 1.6, 3, seed)
    grime = fbm3(p, 4.0, 3, seed + 17)
    col = lerp(STEEL, STEEL_DARK, np.clip(0.55 * (1 - relief) + 0.18 * (1 - grain) + 0.15 * grime, 0, 1))
    col = shade(col, n, relief)
    edge = smoothstep(0.62, 0.92, relief)
    shine = lit(n) ** 6 * smoothstep(0.35, 0.9, relief)
    col = lerp(col, STEEL_HI * (0.85 + 0.3 * grain)[..., None], np.clip(0.30 * edge + 0.35 * shine, 0, 0.6))
    return col


def bronze(p, n, relief=0.85, seed=3):
    g = fbm3(p, 2.5, 3, seed)
    patina = smoothstep(0.62, 0.8, fbm3(p, 3.5, 2, seed + 9))
    t = lerp(BRONZE_DARK, BRONZE, 0.55 + 0.35 * g)
    t = lerp(t, np.array([60, 96, 86.0]), 0.25 * patina)      # a little verdigris in the low spots
    t = shade(t, n, np.full(g.shape, relief) if np.isscalar(relief) else relief)
    return lerp(t, BRONZE_HI, 0.55 * lit(n) ** 5)


def trim(col, p, n, mask, seed=3):
    return lerp(col, bronze(p, n, 0.85, seed), np.clip(mask, 0, 1))


def leather(p, n, seed=5):
    g = fbm3(p, 2.4, 4, seed)
    crease = fbm3(p * np.array([1, 1, 4.0]), 3.0, 2, seed + 3)
    col = lerp(LEATHER, LEATHER_HI, 0.30 * g + 0.25 * lit(n) ** 3)
    col = lerp(col, LEATHER * 0.5, 0.35 * smoothstep(0.55, 0.8, crease))
    return shade(col, n, 0.6 + 0.2 * g)


def chain(p, n, u, v, seed=9):
    ring = chainmail(u, v, ring=0.62, light=np.array([120, 126, 138.0]), dark=np.array([16, 17, 22.0]))
    return shade(ring, n, np.full(u.shape, 0.75)) * 1.05


def cloth(p, n, seed=7):
    g = fbm3(p * np.array([1, 1, 3.0]), 2.0, 3, seed)
    fold = fbm3(p * np.array([2.5, 2.5, 0.25]), 1.0, 2, seed + 5)
    col = lerp(CLOTH * 0.55, CLOTH * 1.9, 0.25 * g + 0.45 * fold)
    return shade(col, n, 0.5 + 0.3 * g)


def glow(col, mask, colour=STORM, core=SPARK, amount=1.0):
    """A glowing line: coloured halo where mask > 0, white-hot core near 1."""
    m = np.clip(mask, 0, 1) * amount
    col = lerp(col, colour, np.clip(m * 1.4, 0, 0.9))
    return lerp(col, core, smoothstep(0.65, 1.0, m))


def zigzag(a, b, period, amp):
    """Distance to a vertical lightning zigzag: a across, b along."""
    ph = (b / period) % 1.0
    tri = np.abs(ph * 2 - 1) * 2 - 1                         # -1..1 triangle wave
    return np.abs(a - tri * amp)


def rivets(u, v, spacing, row_v, radius):
    fu = (u / spacing) - np.floor(u / spacing) - 0.5
    d = np.sqrt((fu * spacing) ** 2 + (v - row_v) ** 2)
    return smoothstep(radius, radius * 0.35, d)


def hot_rivets(col, r):
    col = lerp(col, FORGE, 0.8 * smoothstep(0.0, 0.5, r))
    return lerp(col, np.array([255, 236, 180.0]), smoothstep(0.6, 1.0, r))


def sigil_mask(a, b, size, twin=False):
    """Hammer head struck by lightning, in a local 2D frame (a across, b up), unit = size. Returns (hammer, bolt).
    twin=True for mirrored UV layouts (the body): a bolt either side of the hammer, built from |a| so the mirror
    cannot fold it into diamonds; otherwise one bolt down through the hammer (the cloak)."""
    ha, hb = a / size, b / size
    head = (np.abs(ha) < 0.62) & (hb > 0.18) & (hb < 0.62)
    handle = (np.abs(ha) < 0.10) & (hb <= 0.18) & (hb > -0.75)
    pommel = (np.abs(ha) < 0.2) & (hb <= -0.75) & (hb > -0.9)
    hammer = (head | handle | pommel).astype(float)
    if twin:
        bolt = smoothstep(0.14, 0.05, zigzag(np.abs(ha) - 0.95, hb + 1.0, 0.6, 0.16)) * (hb < 0.9) * (hb > -1.0)
    else:
        bolt = smoothstep(0.16, 0.06, zigzag(ha, hb + 1.0, 0.55, 0.22)) * (np.abs(hb) < 0.95)
    return hammer, bolt


def finish(atlas, covered, size_xy):
    atlas = atlas.copy()
    atlas[~covered] = atlas[covered].mean(0) if covered.any() else 0
    img = Image.fromarray(np.clip(atlas, 0, 255).astype(np.uint8))
    soft = img.filter(ImageFilter.GaussianBlur(0.7))
    arr = np.asarray(img).astype(float) * 0.65 + np.asarray(soft).astype(float) * 0.35
    return Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8)).resize(size_xy, Image.LANCZOS)
