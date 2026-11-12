#!/usr/bin/env python3
"""test_options_coverage.py - every server setting our own code defines must be on the Options tab.

daoc-divided-linux settings live in source/server/GameServer/bots/bracket and bots/governor; the Options tab shows only the keys
listed in Admin.Host/OptionsAdmin.cs. A setting missing from that list cannot be changed from the admin screen. Reads source only.
Run: python3 tui/test_options_coverage.py
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent / "source/server"
OURS = [ROOT / "GameServer/bots/bracket", ROOT / "GameServer/bots/governor"]
TESTING_ONLY = {"bracket_bots_debug_presence_region"}  # pretends a player is in a battleground; not for the screen

defined = {}
for folder in OURS:
    for path in sorted(folder.glob("*.cs")):
        for key in re.findall(r'\[ServerProperty\("[^"]+",\s*"([a-z_0-9]+)"', path.read_text()):
            defined[key] = path.name
assert defined, "found no ServerProperty definitions: did the folders move?"

options = (ROOT / "Admin.Host/OptionsAdmin.cs").read_text()
block = options.split("private static readonly string[] Keys", 1)[1].split("];", 1)[0]
listed = set(re.findall(r'"([a-z_0-9]+)"', block))

missing = sorted(k for k in defined if k not in listed and k not in TESTING_ONLY)
if missing:
    print("Settings defined by our code but missing from the Options tab (add them to Keys in Admin.Host/OptionsAdmin.cs):")
    for k in missing:
        print(f"  {k}  ({defined[k]})")
    sys.exit(1)
print(f"ok: all {len(defined) - len(TESTING_ONLY & defined.keys())} settings of ours are on the Options tab")
