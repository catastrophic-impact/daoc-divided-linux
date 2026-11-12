"""Multi-part software renderer: several NIF meshes, each with its own texture, near-flat lit
(the game shows texture colours almost flat). Adapted from tools/pet-art/texelmap.render_views."""
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

import paths  # noqa: E402,F401  (helper paths in this repository)
from nifmesh import load  # noqa: E402

HERE = Path(__file__).resolve().parent
NIF = paths.NIF
SS = 2
_cache = {}


def mesh(name):
    """'Cloak01_nor_m@1' loads UV set 1 (cata cloak skins use it)."""
    if name not in _cache:
        file, _, uv_set = name.partition("@")
        shapes, _ = load(NIF / f"{file}.nif", int(uv_set or 0))
        shapes = [s for s in shapes if np.abs(s.uvs).sum()]
        v = np.concatenate([s.vertices for s in shapes]); n = np.concatenate([s.normals for s in shapes])
        uv = np.concatenate([s.uvs for s in shapes])
        f, off = [], 0
        for s in shapes:
            f.append(s.faces + off); off += len(s.vertices)
        _cache[name] = (v, n, uv, np.concatenate(f))
    return _cache[name]


def register(name, v, uv, f):
    """Add raw geometry (e.g. an old 4.x item NIF read by nif4_geom) with computed vertex normals."""
    n = np.zeros_like(v)
    fn = np.cross(v[f[:, 1]] - v[f[:, 0]], v[f[:, 2]] - v[f[:, 0]])
    for k in range(3):
        np.add.at(n, f[:, k], fn)
    n /= np.maximum(np.linalg.norm(n, axis=1)[:, None], 1e-9)
    _cache[name] = (v, n, uv, f)


VIEWS = [("front", (0, 1, 0)), ("3/4", (-0.7, 0.7, 0.1)), ("side", (-1, 0, 0)), ("back", (0, -1, 0))]


def render(parts, views=VIEWS, width=360, height=560, label=None, bg=(34, 38, 44)):
    """parts: list of (mesh name, PIL image or RGB tuple)."""
    loaded = []
    for name, tex in parts:
        v, n, uv, f = mesh(name)
        if isinstance(tex, tuple):
            t = np.zeros((4, 4, 4), np.uint8); t[..., :3] = tex; t[..., 3] = 255
        else:
            t = np.asarray(tex.convert("RGBA"))
        loaded.append((v, n, uv, f, t))
    allv = np.concatenate([p[0] for p in loaded])
    pics = []
    out_w, out_h = width, height
    width, height = width * SS, height * SS                    # supersample, then filter down (like mipmapping)
    for vname, direction in views:
        view = np.asarray(direction, float); view /= np.linalg.norm(view)
        up = np.array((0, 0, 1.0))
        right = np.cross(-view, up); right /= np.linalg.norm(right)
        up2 = np.cross(right, -view)
        proj = np.stack((allv @ right, allv @ up2), 1)
        mid = (proj.min(0) + proj.max(0)) / 2; ext = proj.max(0) - proj.min(0)
        scale = min((width - 30) / max(ext[0], 1e-6), (height - 40) / max(ext[1], 1e-6))
        zb = np.full((height, width), -np.inf); rgb = np.zeros((height, width, 3), np.uint8)
        light = -view + np.array((0.3, 0.0, 0.6)); light /= np.linalg.norm(light)
        for v, n, uv, f, t in loaded:
            th, tw = t.shape[:2]
            p2 = np.stack((v @ right, v @ up2), 1)
            scr = np.stack(((p2[:, 0] - mid[0]) * scale + width / 2, height / 2 - (p2[:, 1] - mid[1]) * scale), 1)
            depth = -(v @ view)
            for tri in f:
                xy = scr[tri]; (x0, y0), (x1, y1), (x2, y2) = xy
                den = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
                if abs(den) < 1e-9:
                    continue
                l = max(0, int(math.floor(xy[:, 0].min()))); r = min(width - 1, int(math.ceil(xy[:, 0].max())))
                tp = max(0, int(math.floor(xy[:, 1].min()))); b = min(height - 1, int(math.ceil(xy[:, 1].max())))
                if l > r or tp > b:
                    continue
                gy, gx = np.mgrid[tp:b + 1, l:r + 1]
                pxx, pyy = gx + 0.5, gy + 0.5
                w0 = ((y1 - y2) * (pxx - x2) + (x2 - x1) * (pyy - y2)) / den
                w1 = ((y2 - y0) * (pxx - x2) + (x0 - x2) * (pyy - y2)) / den
                w2 = 1 - w0 - w1
                ins = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
                z = w0 * depth[tri[0]] + w1 * depth[tri[1]] + w2 * depth[tri[2]]
                pz = zb[tp:b + 1, l:r + 1]
                vis = ins & (z > pz)
                if not vis.any():
                    continue
                suv = w0[vis, None] * uv[tri[0]] + w1[vis, None] * uv[tri[1]] + w2[vis, None] * uv[tri[2]]
                tx = np.rint((suv[:, 0] % 1.0) * (tw - 1)).astype(int); ty = np.rint((suv[:, 1] % 1.0) * (th - 1)).astype(int)
                col = t[ty, tx, :3].astype(float)
                nn = w0[vis, None] * n[tri[0]] + w1[vis, None] * n[tri[1]] + w2[vis, None] * n[tri[2]]
                nn /= np.maximum(np.linalg.norm(nn, axis=1)[:, None], 1e-9)
                shade = 0.78 + 0.22 * np.clip(nn @ light, -1, 1)
                pz[vis] = z[vis]
                rgb[tp:b + 1, l:r + 1][vis] = np.clip(col * shade[:, None], 0, 255).astype(np.uint8)
        pic = Image.fromarray(rgb).resize((out_w, out_h), Image.LANCZOS)
        mask = Image.fromarray((np.isfinite(zb) * 255).astype(np.uint8)).resize((out_w, out_h), Image.BILINEAR)
        canvas = Image.new("RGB", (out_w, out_h), bg); canvas.paste(pic, (0, 0), mask)
        ImageDraw.Draw(canvas).text((8, 6), vname, fill=(200, 205, 215))
        pics.append(canvas)
    width, height = out_w, out_h
    top = 28 if label else 0
    sheet = Image.new("RGB", (width * len(pics) + 4 * (len(pics) - 1), height + top), (16, 18, 22))
    if label:
        ImageDraw.Draw(sheet).text((10, 8), label, fill=(240, 240, 250))
    for i, p in enumerate(pics):
        sheet.paste(p, (i * (width + 4), top))
    return sheet


def outfit(race, sex, tex, body=5, gloves=2, boots=2, head=True, cloak=None):
    heads = {"HN": "nor", "Tro": "tro", "Dwa": "dwa"}
    if cloak and "cloak" in tex:
        extra = [(f"Cloak{cloak:02d}_{heads[race]}_{sex}@1", tex["cloak"])]
    else:
        extra = []
    parts = [(f"Body{body:02d}_{race}_{sex}", tex["body_" + sex]), (f"Legs01_{race}_{sex}", tex["legs"]),
             (f"Arms01_{race}_{sex}", tex["arms"]), (f"Gloves{gloves:02d}_{race}_{sex}", tex["gloves"]),
             (f"Boots{boots:02d}_{race}_{sex}", tex["boots"])]
    if head:
        parts.append((f"{heads[race]}_{sex}_head01", (176, 140, 116)))
    return parts + extra


def stack(sheets, gap=6):
    w = max(s.width for s in sheets); h = sum(s.height for s in sheets) + gap * (len(sheets) - 1)
    out = Image.new("RGB", (w, h), (16, 18, 22)); y = 0
    for s in sheets:
        out.paste(s, (0, y)); y += s.height + gap
    return out
