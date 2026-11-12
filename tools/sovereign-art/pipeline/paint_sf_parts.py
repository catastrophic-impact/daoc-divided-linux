"""Stormforged 'Thunderwrought' piece painters (helpers and palette in paint_sf.py).

python paint_sf_parts.py [piece ...]  -> out_sf/*.png and sheets_sf/*.png. Preview only."""
import sys

import numpy as np
from PIL import Image

from paint_sf import (OUT, SHEETS, STEEL_DARK, CLOTH, fbm3, smoothstep, lerp, steel, bronze, trim, leather, chain,
                      cloth, glow, zigzag, rivets, hot_rivets, sigil_mask, finish)
import bake_arrays
import calib
import render as R


def _hash1(i, seed=0):
    h = np.sin(i * 127.1 + seed * 311.7) * 43758.5453
    return h - np.floor(h)


def _seg(a, b, a0, b0, a1, b1):
    da, db = a1 - a0, b1 - b0
    t = np.clip(((a - a0) * da + (b - b0) * db) / (da * da + db * db + 1e-9), 0, 1)
    return np.hypot(a - (a0 + t * da), b - (b0 + t * db))


def rune_band(u, v, v0, v1, cell, width=0.09):
    """Procedural Futhark-like glyphs in a band (a stave plus hashed branches per cell). Returns a 0..1 stroke mask."""
    inside = (v > v0) & (v < v1)
    a = (u / cell) % 1.0; b = np.clip((v - v0) / (v1 - v0), 0, 1)
    idx = np.floor(u / cell)
    d = np.abs(a - 0.5)
    d = np.where((b > 0.12) & (b < 0.88), d, 9.0)
    for k, (a0, b0, a1, b1) in enumerate([(0.5, 0.85, 0.82, 0.6), (0.5, 0.6, 0.82, 0.35), (0.5, 0.85, 0.18, 0.6),
                                          (0.5, 0.45, 0.82, 0.7), (0.18, 0.35, 0.82, 0.75)]):
        on = _hash1(idx, k + 1) > 0.55
        d = np.where(on, np.minimum(d, _seg(a, b, a0, b0, a1, b1)), d)
    gap = _hash1(idx, 9) < 0.12                                # word gaps
    return np.where(inside & ~gap, smoothstep(width, width * 0.35, d), 0.0)


# -------------------------------------------------------------- body (torso + skirt + robe variants)
def paint_body(tm):
    p, n = tm.pos, tm.nrm
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x); front = y < 0.5
    ang = np.arctan2(x, -y); u = ang * 7.5
    col = chain(p, n, u * 1.6, z * 1.6)                        # chain shirt under everything

    # breastplate and backplate
    torso = z > 45.4
    plate_low = 47.2 - 2.4 * (ax / 9.0) ** 2
    plate = torso & (z > plate_low) & (z < 59.4) & (ax < 9.4)
    ridge = 1 - smoothstep(0.0, 1.5, ax)
    prel = 0.55 + 0.25 * ridge + 0.2 * smoothstep(plate_low, plate_low + 2.5, z)
    col = np.where(plate[..., None], steel(p, n, np.clip(prel, 0, 1), seed=21), col)
    edge = plate & (((z - plate_low) < 0.8) | (np.abs(ax - 9.4) < 0.55) | (z > 58.7))
    col = trim(col, p, n, edge.astype(float) * 0.95)
    # storm channels: two zigzags sweeping down beside the sigil, one down the back
    ch = plate & front & (z < 56.5) & (z > plate_low + 1.0)
    col = glow(col, ch * smoothstep(0.32, 0.08, zigzag(ax - 5.4, z, 3.2, 0.7)) * 0.95)
    back_ch = plate & ~front & (z < 58.0)
    col = glow(col, back_ch * smoothstep(0.30, 0.08, zigzag(ax - 2.2, z, 3.0, 0.55)) * 0.9)
    # chest sigil: bronze hammer flanked by glowing bolts (mirror-safe)
    hm, bl = sigil_mask(x, z - 53.0, 3.8, twin=True)
    sig = plate & front
    col = np.where((sig & (hm > 0))[..., None], bronze(p, n, 0.95, 11), col)
    col = glow(col, sig * bl, amount=1.1)
    col = hot_rivets(col, rivets(u, z, 3.4, 57.6, 0.30) * plate * (ax > 3.0) * (ax < 8.0))

    # gorget with a glowing rune ring
    collar = z > 59.4
    col = np.where(collar[..., None], steel(p, n, 0.55 + 0.35 * smoothstep(59.4, 61.5, z), seed=31), col)
    col = trim(col, p, n, (collar & ((z < 60.0) | (z > 62.6))).astype(float))
    col = glow(col, rune_band(u * 1.2, z, 60.2, 62.4, 1.3) * collar, amount=0.9)

    # pauldrons: stacked lames, bronze edges, forge-hot rivets, storm runes on one lame
    should = (ax > 10.8) & (z > 51.5)
    lame = (z - 51.5) / 2.3; lf = lame - np.floor(lame)
    pd = steel(p, n, 0.35 + 0.6 * smoothstep(0.0, 0.25, lf) * (1 - 0.35 * smoothstep(0.75, 1.0, lf)), seed=41)
    col = np.where(should[..., None], pd, col)
    col = trim(col, p, n, (should & (lf < 0.16)).astype(float) * 0.9)
    pu = np.arctan2(z - 55.0, -y) * 4.0
    col = hot_rivets(col, rivets(pu, z, 2.4, 51.5 + 2.3 * np.floor(lame) + 1.3, 0.26) * should)
    col = glow(col, rune_band(pu * 1.3, z, 51.5 + 2.3 * 2 + 0.4, 51.5 + 2.3 * 3 - 0.2, 1.1) * should, amount=0.8)

    # belt: leather, bronze plates, sigil buckle
    belt = (z > 42.6) & (z < 45.4)
    blt = leather(p, n, seed=51)
    bp = (np.abs(((u / 3.0) - np.floor(u / 3.0)) - 0.5) < 0.28) & (z > 43.0) & (z < 45.0)
    blt = np.where(bp[..., None], bronze(p, n, 0.8, 52), blt)
    buckle = front & (ax < 1.9) & (z > 42.7) & (z < 45.3)
    blt = np.where(buckle[..., None], steel(p, n, np.full(z.shape, 0.8), seed=53), blt)
    bh, bb = sigil_mask(x, z - 44.0, 1.35, twin=True)
    blt = np.where((buckle & (bh > 0))[..., None], bronze(p, n, 0.95, 54), blt)
    blt = glow(blt, buckle * bb, amount=1.1)
    col = np.where(belt[..., None], blt, col)
    col = trim(col, p, n, ((np.abs(z - 42.7) < 0.25) | (np.abs(z - 45.3) < 0.25)).astype(float) * 0.7)

    # below the belt: chain skirt with front/back plate tassets, then a bronze-bound rune hem
    low = z <= 42.6
    tas_u = u / 4.4; tf = tas_u - np.floor(tas_u); half = np.clip(np.abs(tf - 0.5) / 0.4, 0, 1)
    shaped = z > 35.0 + 2.0 * (1 - np.sqrt(np.clip(1 - half ** 2, 0, 1)))
    tasset = low & shaped & (half < 1) & ((np.abs(ang) < 0.9) | (np.abs(np.abs(ang) - np.pi) < 0.9))
    tas = steel(p, n, 0.4 + 0.5 * smoothstep(0.4, 0.2, np.abs(tf - 0.5)), seed=61)
    tl = ((42.6 - z) / 2.3) % 1.0
    tas = lerp(tas, STEEL_DARK, 0.6 * (tl < 0.08))
    tas = trim(tas, p, n, ((tl > 0.08) & (tl < 0.18)).astype(float) * 0.6)
    col = np.where(tasset[..., None], tas, col)
    hem = (z > 30.6) & (z < 34.0)
    col = np.where(hem[..., None], steel(p, n, np.full(z.shape, 0.6), seed=63), col)
    col = trim(col, p, n, (hem & ((z < 31.1) | (z > 33.5))).astype(float))
    col = glow(col, rune_band(u * 1.1, z, 31.2, 33.4, 1.3) * hem, amount=0.95)
    # long-robe variants below the hem: storm-blue cloth with bronze bands and a faint bolt down the front
    robe = z <= 30.6
    cl = cloth(p, n)
    cl = trim(cl, p, n, ((np.abs(z - 29.8) < 0.35) | (np.abs(z - 8.0) < 0.5)).astype(float) * 0.9)
    cl = glow(cl, front * smoothstep(0.28, 0.08, zigzag(x, z, 4.0, 0.9)) * (z < 28) * (z > 10), amount=0.6)
    col = np.where(robe[..., None], cl, col)
    return col


# -------------------------------------------------------------- arms (upper arms)
def paint_arms(tm):
    p, n = tm.pos, tm.nrm
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x)
    ang = np.arctan2(z - 50.5, -y); u = ang * 2.6
    col = chain(p, n, u * 2.2, ax * 1.6)
    outer = (ax < 15.2) & (ax > 9.5) & (z > 50.3)               # rerebrace lames on the outer upper arm
    lame = (ax - 9.5) / 1.9; lf = lame - np.floor(lame)
    col = np.where(outer[..., None], steel(p, n, 0.4 + 0.5 * smoothstep(0, 0.3, lf), seed=83), col)
    col = trim(col, p, n, (outer & (lf < 0.14)).astype(float) * 0.85)
    cop_d = np.abs(ax - 16.6)                                  # couter at the elbow
    cop = cop_d < 1.5
    col = np.where(cop[..., None], steel(p, n, 0.5 + 0.45 * (1 - smoothstep(0, 1.5, cop_d)), seed=82), col)
    col = trim(col, p, n, (np.abs(cop_d - 1.35) < 0.22).astype(float) * 0.9)
    return col


# -------------------------------------------------------------- legs
def paint_legs(tm):
    p, n = tm.pos, tm.nrm
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x); ang = np.arctan2(ax - 3.7, -y); u = ang * 3.2
    col = chain(p, n, u * 1.8, z * 1.6)
    front = np.abs(ang) < 1.0
    cuisse = front & (z > 25.5) & (z < 36.0)
    col = np.where(cuisse[..., None], steel(p, n, 0.55 + 0.3 * (1 - smoothstep(0, 1.0, np.abs(ang))), seed=92), col)
    col = trim(col, p, n, (cuisse & ((np.abs(np.abs(ang) - 0.95) < 0.07) | (np.abs(z - 25.7) < 0.3))).astype(float))
    col = glow(col, cuisse * smoothstep(0.22, 0.06, zigzag(ang * 3.2, z, 2.6, 0.45)) * (z > 27) * (z < 34.5), amount=0.85)
    return col


# -------------------------------------------------------------- gloves (vambrace + gauntlet)
def paint_gloves(tm):
    p, n = tm.pos, tm.nrm
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x)
    col = leather(p, n, seed=101)
    # Gloves02 (measured): forearm x 15-19 rising z 43.4 -> 46.5, axis y ~1.3; the hand hangs below (x 19.5-24, z 34-42)
    vam = (ax < 19.4) & (z > 42.5)
    zc = 44.1 + (ax - 16.0) * 0.8
    vu = np.arctan2(z - zc, -(y - 1.3))                       # 0 = front of the forearm, +-pi/2 = top/bottom
    col = np.where(vam[..., None], steel(p, n, 0.5 + 0.35 * smoothstep(1.4, 0.0, np.abs(vu - 0.6)), seed=102), col)
    col = trim(col, p, n, (vam & ((np.abs(ax - 15.2) < 0.45) | (np.abs(ax - 19.0) < 0.4))).astype(float))
    col = glow(col, vam * smoothstep(0.34, 0.10, zigzag(np.abs(vu - 0.6) * 2.4, ax, 1.6, 0.5)) * (ax > 15.8) * (ax < 18.4), amount=1.0)
    hand = (ax >= 19.4) | (z <= 42.5)
    back = (n[..., 0] * np.sign(x) > -0.1)                       # the outward face of the hanging hand
    plate = hand & back & (z > 38.0)
    col = np.where(plate[..., None], steel(p, n, np.full(x.shape, 0.7), seed=103), col)
    col = hot_rivets(col, rivets(y * 1.0, z, 1.3, 40.6, 0.24) * plate)
    fingers = hand & back & (z <= 38.0)
    fl = ((38.0 - z) / 0.9) % 1.0
    col = np.where(fingers[..., None], steel(p, n, 0.35 + 0.55 * smoothstep(0, 0.35, fl), seed=104), col)
    col = trim(col, p, n, (fingers & (fl < 0.12)).astype(float) * 0.6)
    return col


# -------------------------------------------------------------- boots (greaves + sabatons)
def paint_boots(tm):
    p, n = tm.pos, tm.nrm
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x); ang = np.arctan2(ax - 5.4, -(y - 2.6))    # shin axis measured on Boots02 (x 3.5-7.4, y -0.1-5.5)
    col = leather(p, n, seed=111)
    greave = z > 4.2
    col = np.where(greave[..., None], steel(p, n, 0.5 + 0.35 * (1 - smoothstep(0, 1.6, np.abs(ang))), seed=112), col)
    # Boots02 (the set's style) stops at z 15.1; the taller variants carry the same details higher up
    col = trim(col, p, n, ((np.abs(z - 4.4) < 0.35) | (np.abs(z - 14.5) < 0.45) | (z > 23.4)).astype(float) * 0.95)
    bolt = (np.abs(ang) < 0.9) & (z > 5.2) & (z < 11.6)        # the storm bolt down the shin
    col = glow(col, bolt * smoothstep(0.30, 0.08, zigzag(np.abs(ang) * 3.0 - 0.9, z, 2.6, 0.32)), amount=1.1)   # twin bolts (mirror-safe)
    col = glow(col, rune_band(ang * 3.0, z, 12.0, 13.9, 1.0) * (z > 11.8), amount=0.85)
    col = glow(col, rune_band(ang * 3.0, z, 21.0, 23.0, 1.1) * (z > 20.6), amount=0.8)
    col = hot_rivets(col, rivets(ang * 3.0, z, 1.6, 5.5, 0.22) * greave)
    foot = (z <= 4.2) & (y < -0.5)
    sl = ((-y - 0.5) / 1.1) % 1.0
    col = np.where(foot[..., None], steel(p, n, 0.35 + 0.55 * smoothstep(0, 0.3, sl), seed=113), col)
    col = trim(col, p, n, (foot & (sl < 0.12)).astype(float) * 0.7)
    col = glow(col, smoothstep(0.9, 0.2, z) * 0.9, amount=0.9)  # charged sole edge
    return col


# -------------------------------------------------------------- cloak (UV set 1)
def paint_cloak(tm):
    from scipy.ndimage import distance_transform_edt
    p, n = tm.pos, tm.nrm
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x)
    col = cloth(p, n, seed=121)
    swirl = fbm3(p * np.array([1, 1, 0.6]), 6.0, 3, 125)       # storm-cloud mottling
    col = lerp(col, CLOTH * 0.4, 0.35 * smoothstep(0.5, 0.75, swirl))
    S = tm.covered.shape[0]
    border = distance_transform_edt(tm.covered) / S * 1024
    band = (border < 22) & (border > 4)
    col = np.where(band[..., None], bronze(p, n, 0.8, 127) * 0.85, col)
    col = glow(col, ((border > 23) & (border < 27)) * 0.7, amount=0.7)
    back = y > 0
    hm, bl = sigil_mask(x, z - 44.0, 7.0)
    col = np.where((back & (hm > 0))[..., None], bronze(p, n, 0.9, 129), col)
    col = glow(col, back * bl, amount=1.1)
    col = glow(col, rune_band(np.arctan2(x, y) * 9.0, z, 15.0, 17.5, 1.5) * back * (ax < 10.5), amount=0.85)
    hood = z > 63.5
    col = np.where(hood[..., None], lerp(col, CLOTH * 0.5, 0.4), col)
    return col


# -------------------------------------------------------------- winged helm
def helm_tm():
    import helm_fit
    name = helm_fit.helm("HN", "m")
    v, nn, uv, f = R.mesh(name)
    return bake_arrays.bake(v, nn, uv, f, size=512)


def paint_helm(tm):
    p, n = tm.pos, tm.nrm
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    ax = np.abs(x)
    S = tm.covered.shape[0]
    u = np.arange(S)[None, :].repeat(S, 0) / S
    wings = u > 0.64
    shell = tm.covered & ~wings
    cz = np.median(z[shell]); top = np.percentile(z[shell], 97); low = np.percentile(z[shell], 6)
    col = steel(p, n, 0.45 + 0.4 * smoothstep(cz - 2, top, z), seed=131)
    col = trim(col, p, n, ((z < low + 0.5) & ~wings).astype(float))                  # bronze rim
    col = trim(col, p, n, ((ax < 0.45) & (z > cz) & ~wings).astype(float) * 0.9)     # crest ridge
    browb = (y < -2.0) & (z > cz - 1.0) & (z < cz + 2.6) & ~wings                    # brow bolt
    col = glow(col, browb * smoothstep(0.22, 0.06, zigzag(x, z, 1.9, 0.35)), amount=1.1)
    wa = np.arctan2(z - cz, y) * 5.0                                                # wing feathers
    ff = wa - np.floor(wa)
    feather = steel(p, n, 0.35 + 0.55 * smoothstep(0.0, 0.35, ff), seed=133)
    feather = trim(feather, p, n, (ff < 0.12).astype(float) * 0.9)
    tip = smoothstep(top - 1.0, top + 4.0, z)
    feather = glow(feather, tip * (0.5 + 0.5 * smoothstep(0.85, 1.0, ff)), amount=0.9)
    return np.where(wings[..., None], feather, col)


JOBS = {"body_m": (paint_body, (512, 1024)), "body_f": (paint_body, (512, 1024)), "arms": (paint_arms, (512, 512)),
        "legs": (paint_legs, (512, 512)), "gloves": (paint_gloves, (512, 256)), "boots": (paint_boots, (512, 256)),
        "cloak": (paint_cloak, (512, 1024))}


def outfit(race, sex, tex, cloak=None, helm=True):
    import helm_fit
    parts = R.outfit(race, sex, tex, cloak=cloak)
    if helm:
        parts.append((helm_fit.helm(race, sex), tex["helm"]))
    return parts


def sheets(tex):
    import helm_fit
    import paint2 as P
    V = R.VIEWS
    r1 = R.render(outfit("HN", "m", tex), label="Stormforged 'Thunderwrought' - original set  |  Norse male")
    r2 = P.row([R.render(outfit("HN", "m", tex, cloak=1), views=[V[1], V[3]], label="Norse male + cloak"),
                R.render(outfit("HN", "f", tex), views=[V[0], V[1]], label="Norse female")])
    r3 = P.row([R.render(outfit("Tro", "m", tex), views=[V[1]], label="Troll male"),
                R.render(outfit("Dwa", "m", tex), views=[V[1]], label="Dwarf male"),
                R.render(outfit("Dwa", "f", tex, cloak=1), views=[V[3]], label="Dwarf female + cloak"),
                R.render(outfit("Tro", "f", tex), views=[V[0]], label="Troll female")])
    R.stack([r1, r2, r3]).save(SHEETS / "thunderwrought.png")
    close = [("helm", [(helm_fit.helm("HN", "m"), tex["helm"]), ("nor_m_head01", (176, 140, 116))], [V[0], V[1], V[2]]),
             ("chest and belt / back", [("Body05_HN_m", tex["body_m"])], [V[0], V[3]]),
             ("gloves and boots", [("Gloves02_HN_m", tex["gloves"]), ("Boots02_HN_m", tex["boots"])], [V[0], V[1]])]
    cells = [R.render(parts, views=views, width=320, height=320, label=lab) for lab, parts, views in close]
    R.stack(cells).save(SHEETS / "closeups.png")


def main(only=()):
    tex = {}
    for key, (fn, size) in JOBS.items():
        if only and key not in only and (OUT / f"{key}.png").exists():
            tex[key] = Image.open(OUT / f"{key}.png"); continue
        tm = calib.bake(key)
        tex[key] = finish(fn(tm), tm.covered, size); tex[key].save(OUT / f"{key}.png"); print("painted", key)
    if only and "helm" not in only and (OUT / "helm.png").exists():
        tex["helm"] = Image.open(OUT / "helm.png")
    else:
        tm = helm_tm()
        tex["helm"] = finish(paint_helm(tm), tm.covered, (512, 256)); tex["helm"].save(OUT / "helm.png"); print("painted helm")
    sheets(tex)


if __name__ == "__main__":
    main(tuple(sys.argv[1:]))
