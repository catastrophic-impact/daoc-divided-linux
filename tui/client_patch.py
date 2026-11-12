"""The full DividedRealms client patch (client-patch.json), applied in place to a 1.130 client folder.

daoc-divided-linux: this is our own file (nothing of it is in upstream). It is a Python port of the algorithm in upstream's
Windows launcher (source/tools/OfflineDaoc.Launcher/ModernClientSetup.cs; make_client_patch.py has the same code in Python).
Keep it in step with those two when upstream changes the patch format ("Format": 1 is the only one handled here).

What the patch is: a list of files, each with the SHA-256 of the stock 1.130 file it starts from, the SHA-256 of the result,
and the byte edits between them. Entry kinds:
  plain      edits to the file itself (game.dll, the creation layout, the splash)
  Member     edits to one file inside an MPAK v2 archive (zones.mpk, the zone dat/csv archives); the archive is rebuilt
  Members    several files inside one archive, one after the other (gamedata.mpk: catalog rows)
  From       a NEW file made from another stock file (zone 253 and 276 terrain, the Tara helms, the skins)
  AnyBuild   gamedata.mpk may be any build; our catalog rows are added by ID instead of by offset (Adapt)

Rules this module keeps (the same as the launcher's):
  * Only stock input is accepted: an entry's input must hash to InputSha256 (any build for AnyBuild). Anything else is
    reported as blocked and NOTHING at all is written.
  * Every output is built and hash-checked in memory before the first file is replaced.
  * A file is backed up as <name>.orig the first time it is replaced (an existing .orig is the stock file and is kept).
    An .orig also serves as the input when the file itself holds an older patch (this client's old class-slot patch, or the
    battleground fix's zones.mpk), so a client patched the old way upgrades without the original install.
  * Files the patch creates (From) have no backup; restoring deletes them, and only when they are the patch's own output.
Only the standard library is used.
"""
import base64
import hashlib
import json
import os
import shutil
import struct
import zlib
from pathlib import Path

BACKUP = ".orig"
EDEN_MARKER = "eden.dll"  # a folder with the Eden client's DLL has Eden's versions of game files the patch needs stock
ROW_SIZE = 284      # one directory row of an MPAK v2 archive
STORED_BLOCK = 65535


class PatchError(Exception):
    pass


# The patch has two independent parts, offered as two switches on the Client tab:
CLASS_FILES = ("game.dll", "pregame/character_creation.xml")  # the extra classes: class slots and the creation buttons
GROUPS = ("classes", "world")  # world = everything else: zone table, zones, doors, gamedata rows, art, splash


def select(patch, group):
    """The patch cut down to one part ("classes" or "world"); anything else (None) is the whole patch."""
    if group is None:
        return patch
    if group not in GROUPS:
        raise PatchError(f"unknown patch part {group!r}")
    want = group == "classes"
    return {**patch, "Files": [e for e in patch["Files"] if (e["Path"] in CLASS_FILES) == want]}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def load(path):
    patch = json.loads(Path(path).read_text(encoding="utf-8"))
    if patch.get("Format") != 1 or not patch.get("Files"):
        raise PatchError("This client patch has a format this admin screen does not know.")
    return patch


# --- MPAK v2 archives (mirrors make_client_patch.py / ModernClientSetup.cs) -------------------------------------------

def stored_zlib(data):
    """A zlib stream of stored blocks: unlike real compression, every writer makes exactly these bytes."""
    out = bytearray(b"\x78\x01")
    for start in range(0, len(data), STORED_BLOCK):
        block = data[start:start + STORED_BLOCK]
        last = 1 if start + STORED_BLOCK >= len(data) else 0
        out += struct.pack("<BHH", last, len(block), len(block) ^ 0xFFFF) + block
    return bytes(out + struct.pack(">I", zlib.adler32(data)))


def archive_parts(archive):
    """(name size, directory rows, data start) of an MPAK v2 archive."""
    if archive[:5] != b"MPAK\x02" or len(archive) < 21:
        raise PatchError("not an MPAK v2 archive")
    _, directory_size, name_size, count = struct.unpack("<4I", bytes(v ^ i for i, v in enumerate(archive[5:21])))
    directory = zlib.decompress(archive[21 + name_size:21 + name_size + directory_size])
    rows = [bytearray(directory[at:at + ROW_SIZE]) for at in range(0, count * ROW_SIZE, ROW_SIZE)]
    return name_size, rows, 21 + name_size + directory_size


def row_of(rows, member):
    """The named file's directory row; with no name, the only one."""
    found = [r for r in rows if member is None or r[:256].split(b"\0")[0].decode("latin1").lower() == member.lower()]
    if len(found) != 1:
        raise PatchError(f"no single {member or 'file'} in the MPAK v2 archive")
    return found[0]


def member_of(archive, member=None):
    """A file inside an MPAK v2 archive; with no name, the file of a single-file archive."""
    _, rows, start = archive_parts(archive)
    offset, packed = struct.unpack_from("<2I", row_of(rows, member), 272)
    return zlib.decompress(archive[start + offset:start + offset + packed])


def with_member(archive, data, member=None):
    """The archive with one file replaced, written with stored blocks; every name, timestamp and other file is kept."""
    name_size, rows, start = archive_parts(archive)
    target = row_of(rows, member)
    payloads, position, memory = [], 0, 0
    for row in rows:
        size, offset, packed, crc = struct.unpack_from("<4I", row, 268)
        payload = archive[start + offset:start + offset + packed]
        if row is target:
            payload = stored_zlib(data)
            size, crc = len(data), zlib.crc32(payload)
        struct.pack_into("<5I", row, 264, memory, size, position, len(payload), crc)
        payloads.append(payload)
        position += len(payload)
        memory += size
    directory = stored_zlib(b"".join(rows))
    header = struct.pack("<4I", zlib.crc32(directory), len(directory), name_size, len(rows))
    return (archive[:5] + bytes(v ^ i for i, v in enumerate(header)) + archive[21:21 + name_size] + directory +
            b"".join(payloads))


# --- applying one entry -------------------------------------------------------------------------------------------------

def splice(source, changes):
    out, position = bytearray(), 0
    for edit in changes:
        if edit["At"] < position or edit["At"] + edit["Remove"] > len(source):
            raise PatchError("the client patch is damaged (edit out of range)")
        out += source[position:edit["At"]]
        out += base64.b64decode(edit["Insert"])
        position = edit["At"] + edit["Remove"]
    return bytes(out + source[position:])


def row_id(line):
    first = line.split(",", 1)[0].strip()
    return int(first) if first.isascii() and first.isdigit() else None


def with_rows(table, rows):
    """Each catalog row goes before the first higher ID; None when one of our IDs already holds a different row."""
    lines = table.split("\r\n")
    for row in rows:
        numbered = [(at, row_id(line)) for at, line in enumerate(lines) if row_id(line) is not None]
        if not numbered:
            return None
        same = [lines[at] for at, n in numbered if n == row_id(row)]
        if same:
            if any(line != row for line in same):
                return None
            continue
        lines.insert(next((at for at, n in numbered if n > row_id(row)), numbered[-1][0] + 1), row)
    return "\r\n".join(lines)


def adapt(original, entry):
    """An AnyBuild archive that isn't the stock one gets our rows by ID, or is left as it is."""
    out = original
    for part in entry.get("Members") or []:
        table = member_of(out, part["Member"]).decode("latin1")
        added = with_rows(table, part.get("Rows") or [])
        if added is None:
            return original
        if added != table:
            out = with_member(out, added.encode("latin1"), part["Member"])
    return out


def apply_entry(original, entry):
    """The patched file from its input bytes; the input and the output are both hash-checked."""
    if sha(original) != entry["InputSha256"]:
        if entry.get("AnyBuild"):
            return adapt(original, entry)
        raise PatchError(f"{input_path(entry)} is not the stock file")
    member = entry.get("Member")
    if entry.get("Members"):
        out = original
        for part in entry["Members"]:
            out = with_member(out, splice(member_of(out, part["Member"]), part["Edits"]), part["Member"])
    elif member:
        out = with_member(original, splice(member_of(original, member), entry["Edits"]), member)
    else:
        out = splice(original, entry["Edits"])
    if len(out) != entry["OutputSize"] or sha(out) != entry["OutputSha256"]:
        raise PatchError(f"patching {entry['Path']} did not give the expected file")
    return out


def input_path(entry):
    return entry.get("From") or entry["Path"]


# --- the client folder --------------------------------------------------------------------------------------------------

def _read(path):
    try:
        return path.read_bytes()
    except OSError:
        return None


def _is_current(data, entry):
    """The file already is this patch's output (for AnyBuild: already as patched as it can be)."""
    if data is None:
        return False
    if sha(data) == entry["OutputSha256"]:
        return True
    if not entry.get("AnyBuild"):
        return False
    try:
        return adapt(data, entry) == data
    except (PatchError, zlib.error, struct.error):
        return False


def _find_input(root, entry):
    """(bytes, where) of the stock input: the file itself, else its .orig. (None, reason) when neither is usable."""
    rel = input_path(entry)
    seen = False
    for name in (rel, rel + BACKUP):
        data = _read(root / name)
        if data is None:
            continue
        seen = True
        if entry.get("AnyBuild"):
            try:
                adapt(data, entry)
                return data, name
            except (PatchError, zlib.error, struct.error):
                continue
        if sha(data) == entry["InputSha256"]:
            return data, name
    if not seen:
        return None, f"{rel} is missing"
    if (root / EDEN_MARKER).is_file():
        return None, f"{rel} is Eden's version, not the stock 1.130 file"
    return None, f"{rel} is not the stock 1.130 file (and there is no good {rel}{BACKUP})"


def status(folder, patch):
    """-> {"current": n, "ready": n, "blocked": [reasons], "total": n, "backups": bool, "state": str, "message": str}
    state: patched (every file is the patch's output) | ready (can be patched) | partial (some files current, some not,
    nothing blocked) | blocked (a file is not the stock one: nothing would be written)."""
    root = Path(folder).expanduser()
    if not (root / "game.dll").is_file():
        return {"state": "missing", "current": 0, "ready": 0, "blocked": [], "total": len(patch["Files"]), "backups": False,
                "message": "Not a DAoC client folder (game.dll is missing)."}
    current, ready, blocked = 0, 0, []
    for entry in patch["Files"]:
        if _is_current(_read(root / entry["Path"]), entry):
            current += 1
            continue
        data, where = _find_input(root, entry)
        if data is None:
            blocked.append(where)
        else:
            ready += 1
    total = len(patch["Files"])
    backups = any((root / (e["Path"] + BACKUP)).is_file() for e in patch["Files"] if not e.get("From"))
    if blocked:
        state, message = "blocked", f"Cannot patch: {blocked[0]}" + (f" (and {len(blocked) - 1} more)" if len(blocked) > 1 else "") + ". Nothing will be changed."
    elif current == total:
        state, message = "patched", f"Fully patched ({total} files)." + (" Backups are present." if backups else "")
    elif current == 0:
        state, message = "ready", f"Ready to patch ({total} files)." + (" Backups are present." if backups else "")
    else:
        state, message = "partial", f"{current} of {total} files are patched; patching will do the other {ready}."
    return {"state": state, "current": current, "ready": ready, "blocked": blocked, "total": total, "backups": backups, "message": message}


def apply(folder, patch):
    """Patch the folder. Returns the list of files written. Writes nothing unless every output verifies."""
    root = Path(folder).expanduser()
    info = status(root, patch)
    if info["state"] in ("missing", "blocked"):
        raise PatchError(info["message"])
    built = []  # (entry, output bytes) in patch order, built from the stock inputs on disk as they are now
    for entry in patch["Files"]:
        if _is_current(_read(root / entry["Path"]), entry):
            continue
        data, where = _find_input(root, entry)
        if data is None:
            raise PatchError(where)
        built.append((entry, apply_entry(data, entry)))
    written = []
    for entry, data in built:  # only now touch the disk
        target = root / entry["Path"]
        target.parent.mkdir(parents=True, exist_ok=True)
        if target.is_file() and not Path(str(target) + BACKUP).exists() and not entry.get("From"):
            shutil.copy2(target, str(target) + BACKUP)  # the first backup is the stock file; it is never overwritten
        tmp = Path(str(target) + ".part")
        tmp.write_bytes(data)
        os.replace(tmp, target)
        written.append(entry["Path"])
    after = status(root, patch)
    if after["state"] != "patched":
        raise PatchError("The patched files did not verify; use Switch to original.")
    return written


def restore(folder, patch):
    """Put the stock files back from their .orig backups and delete the files the patch made. -> list of what changed."""
    root = Path(folder).expanduser()
    changed = []
    for entry in patch["Files"]:
        target = root / entry["Path"]
        if entry.get("From") and entry["From"] != entry["Path"]:
            if target.is_file() and sha(target.read_bytes()) == entry["OutputSha256"]:
                target.unlink()
                changed.append(entry["Path"])
            continue
        orig = Path(str(target) + BACKUP)
        if orig.is_file():
            shutil.copy2(orig, target)  # the .orig stays, so the patch can be switched back on
            changed.append(entry["Path"])
    if not changed:
        raise PatchError("There are no .orig backups in that folder (it was never patched).")
    return changed
