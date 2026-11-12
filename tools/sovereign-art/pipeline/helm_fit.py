"""Seat the per-race Catacombs winged helm (toaHelm_Winged_<race>_<sex>) on its skeleton's Bip01 Head, the way the
client does: helm shapes are in head-bone space, scaled by items.csv 'Cata % Helm Scale'. Read-only on the client."""
import os
import sys
from pathlib import Path

import numpy as np

import paths  # noqa: E402  (helper paths in this repository)
CLIENT = paths.CLIENT
import daoc_catalog as d  # noqa: E402
from daoc_catalog import archive  # noqa: E402
from netimmerse import read_nif  # noqa: E402
import nif_static  # noqa: E402
import render as R  # noqa: E402

PREFIX = {"HN": "nor", "Tro": "tro", "Dwa": "dwa"}
SCALE = {"nor_f": 103, "dwa_f": 103}          # items.csv col 25 for model 2224's rows (blank = 100)
_skel = {}


def head_world(prefix):
    if prefix not in _skel:
        es = {e.name.lower(): e.data for e in archive.read((CLIENT / "figures/fig3/sfig001.mpk").read_bytes())[1]}
        sk = read_nif(es[f"{prefix}_skeleton.nif"])
        world = {}

        def walk(b, parent):
            m = nif_static.local_matrix(b) @ parent
            world[b.get("name")] = m
            for c in b.get("children") or []:
                if c is not None and c.type == "NiNode":
                    walk(c, m)
        for r in sk.roots:
            if r is not None and r.type == "NiNode":
                walk(r, np.identity(4))
        _skel[prefix] = world
    w = _skel[prefix]          # the helm attach node, turned so helm NIFs sit upright (as fig3_figure.gd uses it)
    return w["Bip01 Helmet"] if "Bip01 Helmet" in w else w["Bip01 Head"]


STEM_SCALE = {"midrp04": {"dwa_m": 102}}


def helm(race, sex, stem="toahelm_winged"):
    prefix = f"{PREFIX[race]}_{sex}"
    race3 = PREFIX[race]                          # most helms are <stem>_<race>_<sex>, the Lug helmet <stem>_<sex>_<race>
    names = {f"{stem}_{prefix}.nif".lower(), f"{stem}_{sex}_{race3}.nif".lower()}
    path = next(p for p in (CLIENT / "items").iterdir() if p.name.lower() in names)
    shapes, _ = nif_static.collect(read_nif(path.read_bytes()))
    M = head_world(prefix); sc = (STEM_SCALE[stem] if stem in STEM_SCALE else SCALE).get(prefix, 100) / 100.0
    vs, uvs, fs, off = [], [], [], 0
    for b, data, m, props in shapes:
        v = np.asarray(data["vertices"], float)[:, :3]
        p = (np.c_[v, np.ones(len(v))] @ m)[:, :3] * sc
        w = (np.c_[p, np.ones(len(p))] @ M)[:, :3]
        uv = np.asarray(data["uv_sets"][0], float)[:, :2]
        tri = nif_static.triangles_of(data)
        vs.append(w); uvs.append(uv); fs.append(tri + off); off += len(w)
    name = f"{stem}_{prefix}"
    R.register(name, np.concatenate(vs), np.concatenate(uvs), np.concatenate(fs))
    return name
