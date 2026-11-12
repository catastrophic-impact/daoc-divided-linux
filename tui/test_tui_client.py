"""Headless test of the Client tab with the full client patch: press Check / Switch to patched / Switch to original.
Run: python/bin/python3 -I tui/test_tui_client.py <client folder to copy files from> [client-patch.json]
Copies the patch's files from the given client into a scratch folder (read only); skips when no client is given."""
import asyncio, os, shutil, sys, tempfile
from pathlib import Path
here = Path(__file__).resolve().parent
client = Path(sys.argv[1]) if len(sys.argv) > 1 else None
pfile = Path(sys.argv[2]) if len(sys.argv) > 2 else here.parent / ".cache/divided/DR-dev-0.1g/client-patch.json"
if not (client and (client / "game.dll").is_file() and pfile.is_file()):
    print("SKIP: give a client folder"); sys.exit(0)
tmp = Path(tempfile.mkdtemp())
(tmp / "tools").mkdir(); shutil.copy2(pfile, tmp / "tools/client-patch.json")
os.environ.update(DAOC_HOME=str(tmp), DAOC_ADMIN_SOCKET=str(tmp / "a.sock"), DAOC_SERVER_LOG=str(tmp / "c.out"))
sys.path.insert(0, str(here))
import daoc_admin_tui as t, client_patch as cp
work = tmp / "client"
for e in cp.load(pfile)["Files"]:
    for rel in {cp.input_path(e), e["Path"]}:
        for name in (rel, rel + ".orig"):
            if (client / name).is_file():
                (work / name).parent.mkdir(parents=True, exist_ok=True); shutil.copy2(client / name, work / name)

async def main():
    app = t.DaocAdmin()
    async with app.run_test(size=(120, 40)) as pilot:
        await pilot.pause(0.3)
        app.query_one("#tabs").active = "client"; await pilot.pause(0.3)
        app.query_one("#client-path").value = str(work)
        await pilot.click("#client-check"); await pilot.pause(0.5)
        info = str(app.query_one("#client-info").render())
        assert "Full client patch (40 files)" in info, info
        assert str(app.query_one("#client-toggle").label) == "Switch to patched", app.query_one("#client-toggle").label
        await pilot.click("#client-toggle"); await pilot.pause(0.5)
        await pilot.click("#ok"); await pilot.pause(2.5)
        info = str(app.query_one("#client-info").render())
        assert "patched" in info and "Fully patched (40 files)" in info, info
        assert str(app.query_one("#client-toggle").label) == "Switch to original"
        assert "included in the full client patch" in str(app.query_one("#bgfix-info").render())
        await pilot.click("#client-toggle"); await pilot.pause(0.5)
        await pilot.click("#ok"); await pilot.pause(2.5)
        assert str(app.query_one("#client-toggle").label) == "Switch to patched", app.query_one("#client-toggle").label
        assert not (work / "zones/zone253/ter253.mpk").exists()
    print("OK: Client tab applied and removed the full patch")
try:
    asyncio.run(main())
finally:
    shutil.rmtree(tmp, ignore_errors=True)
