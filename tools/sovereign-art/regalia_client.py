"""Shared helpers for the Sovereign Regalia client art installers (tara/, camelot/): catalog rows, DDS encoders,
the client-closed check and rollback. Moved here from the Stormforged concept kit (install_test_copy.py) so the
installers run from the repository; the archive and encoders come from tools/pet-art and tools/asset-tool."""
from __future__ import annotations

import csv
import hashlib
import io
import json
import sys
from pathlib import Path

PET = Path(__file__).resolve().parents[1] / "pet-art"
sys.path.insert(0, str(PET))
from daoc_catalog import archive  # noqa: E402,F401
from install_pet_art import encode_dds  # noqa: E402,F401
from install_sluagh_armor import encode_dxt3  # noqa: E402,F401

# client folders the installers refuse; the first is the owner's modern test client, allowed only with --modern-test
REAL = [Path(r"C:\Users\natex\Desktop\DAoC Offline\Source\artifacts\modern-client-test\runtime\client-modern\app"),
        Path(r"C:\Users\natex\Desktop\DAoC Offline\Stormforged\runtime\client-opendaoc\app")]
MODERN_TEST = REAL[0]


def need(ok, msg):
    if not ok:
        raise SystemExit("REFUSED: " + msg)


def table(data):
    return {r[0]: r for r in csv.reader(io.StringIO(data.decode("latin1"))) if r and r[0].isdigit()}


def line_of(row):
    s = io.StringIO(); csv.writer(s, lineterminator="").writerow(row); return s.getvalue()


def add_row(data, row):
    """Insert a new catalog row in ID order; refuses an ID that is already used."""
    lines = data.decode("latin1").split("\r\n")
    numeric = [(i, int(l.split(",", 1)[0])) for i, l in enumerate(lines) if l.split(",", 1)[0].strip().isdigit()]
    need(all(n != int(row[0]) for _, n in numeric), f"ID {row[0]} already used")
    at = next((i for i, n in numeric if n > int(row[0])), numeric[-1][0] + 1)
    return "\r\n".join(lines[:at] + [line_of(row)] + lines[at:]).encode("latin1")


CATALOGS = ("mskins.csv", "pskins.csv", "objects.csv", "items.csv")


def catalog_rows(files: dict, kit) -> dict:
    """The kit's new catalog rows, {catalog: [row, ...]} in the order they are added, cloned from the stock rows of
    `files` (gamedata.mpk's members by lower-case name). `kit` is an installer module (MSKINS, PSKINS, OBJECTS,
    PSKIN_ARC, MSKIN_ARC, LABEL). Shared by the installers and the release client patch (make_client_patch.py)."""
    T = {k: table(files[k]) for k in CATALOGS}
    rows = {k: [] for k in CATALOGS}
    for mid, tex, _, _ in kit.MSKINS:
        row = list(T["mskins.csv"]["2327"]); row[0], row[1], row[2], row[4], row[5] = str(mid), tex, tex, str(kit.MSKIN_ARC), "0"
        rows["mskins.csv"].append(row)
    for pid, tex, _, _, stock, base, _ in kit.PSKINS:
        row = list(T["pskins.csv"][stock])
        row += [""] * max(0, 7 - len(row))
        row[0], row[1], row[2], row[4], row[5], row[6] = str(pid), kit.LABEL + " " + tex[:-4], tex, str(kit.PSKIN_ARC), "0", str(base)
        rows["pskins.csv"].append(row)
    for iid, stock, name in getattr(kit, "ITEMS", ()):            # NIF rows for the kit's private meshes (MESHES)
        row = list(T["items.csv"][stock]); row[0], row[1], row[2] = str(iid), name, name
        rows["items.csv"].append(row)
    for oid, stock, label, changes in kit.OBJECTS:
        row = list(T["objects.csv"][stock]); row += [""] * (98 - len(row))
        row[0], row[1] = str(oid), label
        for c, v in changes.items():
            row[c] = v
        rows["objects.csv"].append(row)
    return rows


def skin_archives(client: Path, kit, stamp: int) -> tuple:
    """The kit's two new archives (pskinNNN.mpk, mskinNNN.mpk) built from its textures; the stock DDS files in `client`
    only lend their header layout. A fixed `stamp` makes the same bytes on every build."""
    ref = next(e.data for e in archive.read((client / "items/pskins/pskin008.mpk").read_bytes())[1]
               if e.name.lower().endswith(".dds") and e.data[84:88] == b"DXT1")
    ref3 = next(e.data for e in archive.read((client / "items/pskins/pskin001.mpk").read_bytes())[1] if e.name.lower() == "cloak.dds")
    ptex = sorted(((t, encode_dxt3(kit.img(src, size), ref3) if dxt3 else encode_dds(kit.img(src, size), ref))
                   for _, t, src, size, _, _, dxt3 in kit.PSKINS), key=lambda x: x[0].lower())
    mtex = sorted(((t, encode_dds(kit.img(src, size), ref)) for _, t, src, size in kit.MSKINS), key=lambda x: x[0].lower())
    pak = archive.write(f"pskin{kit.PSKIN_ARC:03d}.mpk".encode(), [archive.Entry(n, d, stamp, 4) for n, d in ptex])
    mpak = archive.write(f"mskin{kit.MSKIN_ARC:03d}.mpk".encode(), [archive.Entry(n, d, stamp, 4) for n, d in mtex])
    for b in (pak, mpak):
        archive.verify_memory_image(b)
    return pak, mpak


def mesh_files(client: Path, kit) -> list:
    """The kit's private meshes: [(target, source, stock bytes, our bytes)], paths relative to the client folder. Each is a
    copy of a stock NIF with one byte run replaced (kit.MESHES: target, source, stock sha256, old hex, new hex); the
    stock file must be the exact 1.130 one."""
    out = []
    for target, source, sha, old, new in getattr(kit, "MESHES", ()):
        stock = (client / source).read_bytes()
        need(hashlib.sha256(stock).hexdigest() == sha, f"{source} is not the stock 1.130 file")
        old_b, new_b = bytes.fromhex(old), bytes.fromhex(new)
        need(len(old_b) == len(new_b) and stock.count(old_b) == 1, f"{source}: the bytes to replace are not there once")
        out.append((target, source, stock, stock.replace(old_b, new_b)))
    return out


def skin_targets(kit) -> tuple:
    """Where the kit's archives go, relative to the client folder."""
    return f"items/pskins/pskin{kit.PSKIN_ARC:03d}.mpk", f"figures/Mskins/mskin{kit.MSKIN_ARC:03d}.mpk"


def build(client: Path, kit, stamp: int):
    """gamedata.mpk with the kit's rows added (every other member byte for byte), and its two archives."""
    game = client / "gamedata.mpk"
    name, entries = archive.read(game.read_bytes())
    files = {e.name.lower(): e.data for e in entries}
    upd = {k: files[k] for k in CATALOGS}
    added = catalog_rows(files, kit)
    for k in CATALOGS:
        for row in added[k]:
            upd[k] = add_row(upd[k], row)
    blob = archive.write(name, [archive.Entry(e.name, upd.get(e.name.lower(), e.data), e.timestamp, e.flags) for e in entries])
    archive.verify_memory_image(blob)
    _, dec = archive.read(blob)
    for old, new in zip(entries, dec):
        if old.name.lower() not in upd:
            need(old.data == new.data, f"unrelated entry changed: {old.name}")
    for k in upd:
        old_rows = files[k].decode("latin1").split("\r\n")
        new_rows = upd[k].decode("latin1").split("\r\n")
        need([l for l in new_rows if l in set(old_rows)] == old_rows, f"stock {k} line changed")
        need(len(new_rows) - len(old_rows) == len(added[k]), f"{k}: unexpected row count")
    pak, mpak = skin_archives(client, kit, stamp)
    return blob, pak, mpak


def update(client: Path, kit, stamp: int):
    """Bring an earlier install of this kit up to date in place: the kit's rows that the catalog already has must be
    identical, the others are added; the two skin archives are rebuilt (they must already be there); the private meshes
    are new. Returns (gamedata bytes, {replaced path: bytes}, {new path: bytes}), paths relative to the client."""
    game = client / "gamedata.mpk"
    name, entries = archive.read(game.read_bytes())
    files = {e.name.lower(): e.data for e in entries}
    upd = {k: files[k] for k in CATALOGS}
    added = catalog_rows(files, kit)
    count = {}
    for k in CATALOGS:
        have = table(upd[k])
        for row in added[k]:
            if row[0] in have:
                need(line_of(have[row[0]]) == line_of(row), f"{k} {row[0]} holds another row than this kit's")
            else:
                upd[k] = add_row(upd[k], row); count[k] = count.get(k, 0) + 1
    blob = archive.write(name, [archive.Entry(e.name, upd.get(e.name.lower(), e.data), e.timestamp, e.flags) for e in entries])
    archive.verify_memory_image(blob)
    for old, new in zip(entries, archive.read(blob)[1]):
        if old.name.lower() not in upd:
            need(old.data == new.data, f"unrelated entry changed: {old.name}")
    for k in upd:
        old_rows = files[k].decode("latin1").split("\r\n")
        new_rows = upd[k].decode("latin1").split("\r\n")
        need([l for l in new_rows if l in set(old_rows)] == old_rows, f"{k}: an existing line changed")
        need(len(new_rows) - len(old_rows) == count.get(k, 0), f"{k}: unexpected row count")
    replaced = dict(zip(skin_targets(kit), skin_archives(client, kit, stamp)))
    for rel in replaced:
        need((client / rel).exists(), f"{rel} is not there: install the kit, not update it")
    new = {target: ours for target, _, _, ours in mesh_files(client, kit)}
    for rel in new:
        need(not (client / rel).exists(), f"{rel} already exists")
    return blob, replaced, new


def client_closed():
    import subprocess
    out = subprocess.run(["tasklist"], capture_output=True, text=True).stdout.lower()
    need("connect-local1130" not in out and "camelot.exe" not in out, "close the game client first")


def rollback(client: Path, folder: Path):
    """Restore gamedata.mpk from an install backup and remove the archives that install added."""
    client_closed()
    m = json.loads((folder / "manifest.json").read_text(encoding="utf-8"))
    game = client / "gamedata.mpk"
    need(hashlib.sha256(game.read_bytes()).hexdigest() in (m["gamedata_before"], m["gamedata_after"]),
         "later catalog changes detected; refuse to overwrite them")
    game.write_bytes((folder / "gamedata.mpk").read_bytes())
    for rel in m["new_files"]:
        (client / rel).unlink(missing_ok=True)
    for rel in m.get("replaced_files", []):                     # an update's backup holds the files it replaced
        (client / rel).write_bytes((folder / "replaced" / rel).read_bytes())
    print("rolled back", client)
