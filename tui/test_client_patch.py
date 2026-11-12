"""Tests for client_patch.py (the full DividedRealms client patch).
Run: python/bin/python3 -I tui/test_client_patch.py [client folder] [client-patch.json]
Part 1 uses small made-up files (always runs). Part 2 runs the REAL patch on a scratch copy of the files it needs from a real
1.130 client folder (only read; skipped when none is given). The real client is never written to."""
import base64, hashlib, os, shutil, struct, sys, tempfile, zlib
from pathlib import Path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import client_patch as cp


def sha(b): return hashlib.sha256(b).hexdigest()


def make_archive(files):
    """A tiny MPAK v2 archive: files = {name: bytes}, written like the game does (zlib per file)."""
    payloads, rows, pos, mem = [], [], 0, 0
    for name, data in files.items():
        pl = zlib.compress(data)
        row = bytearray(ROW := cp.ROW_SIZE)
        row[:len(name)] = name.encode()
        struct.pack_into("<5I", row, 264, mem, len(data), pos, len(pl), zlib.crc32(pl))
        rows.append(bytes(row)); payloads.append(pl); pos += len(pl); mem += len(data)
    directory = zlib.compress(b"".join(rows))
    header = struct.pack("<4I", zlib.crc32(directory), len(directory), 0, len(rows))
    return b"MPAK\x02" + bytes(v ^ i for i, v in enumerate(header)) + directory + b"".join(payloads)


def edit(at, remove, insert): return {"At": at, "Remove": remove, "Insert": base64.b64encode(insert).decode()}


def entry_for(path, stock, out, edits, **extra):
    e = {"Path": path, "InputSha256": sha(stock), "OutputSha256": sha(out), "OutputSize": len(out), "Edits": edits}
    e.update(extra)
    return e


# ---- part 1: made-up files
arc = make_archive({"a.csv": b"one,two\r\n", "b.csv": b"1,x\r\n3,z\r\n"})
assert cp.member_of(arc, "b.csv") == b"1,x\r\n3,z\r\n"
bigger = cp.with_member(arc, b"1,x\r\n2,y\r\n3,z\r\n", "b.csv")
assert cp.member_of(bigger, "b.csv") == b"1,x\r\n2,y\r\n3,z\r\n" and cp.member_of(bigger, "a.csv") == b"one,two\r\n"
assert cp.with_member(bigger, b"1,x\r\n3,z\r\n", "b.csv") != arc  # stored blocks, not the game's zlib: bytes differ, content is the same
assert cp.member_of(cp.with_member(bigger, b"1,x\r\n3,z\r\n", "b.csv"), "b.csv") == b"1,x\r\n3,z\r\n"
assert cp.with_rows("1,x\r\n3,z", ["2,y"]) == "1,x\r\n2,y\r\n3,z"
assert cp.with_rows("1,x\r\n2,OTHER\r\n3,z", ["2,y"]) is None  # one of our IDs holds another row: leave the file alone
assert cp.with_rows("1,x\r\n2,y", ["2,y"]) == "1,x\r\n2,y"

stock_dll, stock_txt = b"A" * 100 + b"B" * 20, b"hello world"
out_dll = stock_dll[:50] + b"PATCH" + stock_dll[55:]
out_txt = b"hello there"
member_out = cp.with_member(arc, cp.splice(cp.member_of(arc, "a.csv"), [edit(0, 3, b"ONE")]), "a.csv")
rows_out = cp.with_member(arc, b"1,x\r\n2,y\r\n3,z\r\n", "b.csv")
patch = {"Format": 1, "ClientVersion": "1.130", "Files": [
    entry_for("game.dll", stock_dll, out_dll, [edit(50, 5, b"PATCH")]),
    entry_for("new/made.txt", stock_txt, out_txt, [edit(6, 5, b"there")], From="stock/original.txt"),
    entry_for("z/arc.mpk", arc, member_out, [edit(0, 3, b"ONE")], Member="a.csv"),
    entry_for("gamedata.mpk", arc, rows_out, [], AnyBuild=True, Members=[{"Member": "b.csv", "Edits": [edit(5, 0, b"2,y\r\n")], "Rows": ["2,y"]}]),
]}
# the Members entry's real edits: insert "2,y\r\n" after the first row
patch["Files"][3]["OutputSha256"], patch["Files"][3]["OutputSize"] = sha(rows_out), len(rows_out)
assert cp.apply_entry(arc, patch["Files"][2]) == member_out
assert cp.apply_entry(stock_dll, patch["Files"][0]) == out_dll
try:
    cp.apply_entry(b"x" * 120, patch["Files"][0]); raise SystemExit("a non-stock file was accepted")
except cp.PatchError:
    pass

tmp = Path(tempfile.mkdtemp())
try:
    root = tmp / "client"
    def put(rel, data):
        (root / rel).parent.mkdir(parents=True, exist_ok=True); (root / rel).write_bytes(data)
    put("game.dll", stock_dll); put("stock/original.txt", stock_txt); put("z/arc.mpk", arc); put("gamedata.mpk", arc)
    st = cp.status(root, patch); assert st["state"] == "ready", st
    written = cp.apply(root, patch)
    assert sorted(written) == sorted(["game.dll", "new/made.txt", "z/arc.mpk", "gamedata.mpk"]), written
    assert (root / "game.dll").read_bytes() == out_dll and (root / "game.dll.orig").read_bytes() == stock_dll
    assert (root / "new/made.txt").read_bytes() == out_txt and (root / "stock/original.txt").read_bytes() == stock_txt
    assert not (root / "new/made.txt.orig").exists()
    assert cp.status(root, patch)["state"] == "patched" and cp.apply(root, patch) == []
    cp.restore(root, patch)
    assert (root / "game.dll").read_bytes() == stock_dll and not (root / "new/made.txt").exists()
    assert (root / "z/arc.mpk").read_bytes() == arc and (root / "gamedata.mpk").read_bytes() == arc
    assert cp.status(root, patch)["state"] == "ready"
    cp.apply(root, patch)  # switch back on from the .orig backups
    # a client patched the old way: the file holds something else, the .orig is stock -> upgrade from the .orig
    put("game.dll", b"OLD PATCH"); assert cp.status(root, patch)["state"] == "partial"
    cp.apply(root, patch); assert (root / "game.dll").read_bytes() == out_dll
    # a file that is neither stock nor has a good .orig: blocked, and nothing at all is written
    cp.restore(root, patch)
    put("game.dll", b"modified by something else"); (root / "game.dll.orig").unlink()
    before = {p: p.read_bytes() for p in root.rglob("*") if p.is_file()}
    st = cp.status(root, patch); assert st["state"] == "blocked" and "game.dll" in st["blocked"][0], st
    try:
        cp.apply(root, patch); raise SystemExit("a blocked client was patched")
    except cp.PatchError:
        pass
    assert before == {p: p.read_bytes() for p in root.rglob("*") if p.is_file()}, "blocked apply wrote files"
    # a missing input is blocked too; an Eden client is named
    (root / "stock/original.txt").unlink(); put("game.dll", stock_dll)
    assert cp.status(root, patch)["state"] == "blocked"
    put("eden.dll", b"x"); put("stock/original.txt", b"changed")
    assert "Eden" in cp.status(root, patch)["blocked"][0]
    print("part 1 (made-up files): OK")

    # ---- part 2: the real patch on the real client's files (copied to scratch, never written back)
    client = Path(sys.argv[1]) if len(sys.argv) > 1 else None
    pfile = Path(sys.argv[2]) if len(sys.argv) > 2 else Path(__file__).resolve().parent.parent / ".cache/divided/DR-dev-0.1g/client-patch.json"
    if not (client and (client / "game.dll").is_file() and pfile.is_file()):
        print("part 2 (real client): SKIP (give a client folder and, if needed, a client-patch.json)"); sys.exit(0)
    real = cp.load(pfile)
    work = tmp / "real"
    for e in real["Files"]:
        for rel in {cp.input_path(e), e["Path"]}:
            for name in (rel, rel + cp.BACKUP):
                src = client / name
                if src.is_file():
                    (work / name).parent.mkdir(parents=True, exist_ok=True); shutil.copy2(src, work / name)
    st = cp.status(work, real); assert st["state"] in ("ready", "partial"), st
    n = len(cp.apply(work, real))
    assert cp.status(work, real)["state"] == "patched"
    for e in real["Files"]:
        if not e.get("AnyBuild"):
            assert sha((work / e["Path"]).read_bytes()) == e["OutputSha256"], e["Path"]
    assert cp.apply(work, real) == []
    cp.restore(work, real); assert cp.status(work, real)["state"] in ("ready", "partial")
    assert len(cp.apply(work, real)) >= n - 1
    # the two parts are independent: patch one, the other stays as it was
    cp.restore(work, real)
    cls, wld = cp.select(real, "classes"), cp.select(real, "world")
    assert [e["Path"] for e in cls["Files"]] == list(cp.CLASS_FILES) and len(wld["Files"]) == len(real["Files"]) - 2
    cp.apply(work, cls)
    assert cp.status(work, cls)["state"] == "patched" and cp.status(work, wld)["state"] in ("ready", "partial")
    assert not (work / "zones/zone253/ter253.mpk").exists()
    cp.apply(work, wld); assert cp.status(work, real)["state"] == "patched"
    cp.restore(work, cls)
    assert cp.status(work, cls)["state"] in ("ready", "partial") and cp.status(work, wld)["state"] == "patched"
    print(f"part 2 (real client, {len(real['Files'])} files, {n} written): OK")
finally:
    shutil.rmtree(tmp, ignore_errors=True)
