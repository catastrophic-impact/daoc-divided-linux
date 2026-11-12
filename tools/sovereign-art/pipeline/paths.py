"""Where the Sovereign art pipeline finds its helpers, the game client and its work folder.

  helpers   tools/pet-art (armorpaint, texelmap, nifmesh, daoc_catalog) and source/new-client/pipeline (netimmerse,
            nif_static), both in this repository
  client    OFFLINE_DAOC_CLIENT, else the owner's modern test client; read only
  work      SOVEREIGN_ART_WORK, else tools/sovereign-art/.work (git-ignored): meshes fetched from the client (nif/),
            the stock helm texture (tex/), mesh bakes (bakes/, rebuilt on first use) and painter output
"""
import os
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ART = HERE.parent                                   # tools/sovereign-art
REPO = ART.parents[1]
PET = REPO / "tools" / "pet-art"
NEWCLIENT = REPO / "source" / "new-client" / "pipeline"
CLIENT = Path(os.environ.get("OFFLINE_DAOC_CLIENT",
                             r"C:\Users\natex\Desktop\DAoC Offline\Source\artifacts\modern-client-test\runtime\client-modern\app"))
os.environ.setdefault("OFFLINE_DAOC_CLIENT", str(CLIENT))
os.environ.setdefault("DAOC_CLIENT", str(CLIENT))
WORK = Path(os.environ.get("SOVEREIGN_ART_WORK", ART / ".work"))
NIF, TEX, BAKES = WORK / "nif", WORK / "tex", WORK / "bakes"
for d in (WORK, NIF, TEX, BAKES):
    d.mkdir(parents=True, exist_ok=True)
for p in (PET, NEWCLIENT, HERE):
    if str(p) not in sys.path:
        sys.path.insert(0, str(p))
