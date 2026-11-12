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
        txt = lambda i: str(app.query_one(i).render())
        label = lambda i: str(app.query_one(i).label)
        assert "(2 files)" in txt("#client-info") and "(38 files)" in txt("#world-info"), (txt("#client-info"), txt("#world-info"))
        assert label("#client-toggle") == label("#world-toggle") == "Switch to patched"
        assert not app.query_one("#bgfix-toggle").display and app.query_one("#world-toggle").display
        async def press(i):
            await pilot.click(i); await pilot.pause(0.5); await pilot.click("#ok"); await pilot.pause(2.5)
        await press("#client-toggle")   # classes only
        assert label("#client-toggle") == "Switch to original" and label("#world-toggle") == "Switch to patched"
        assert "patched" in txt("#client-info") and not (work / "zones/zone253/ter253.mpk").exists()
        await press("#world-toggle")    # then the world part
        assert label("#world-toggle") == "Switch to original" and (work / "zones/zone253/ter253.mpk").exists()
        await press("#client-toggle")   # classes back to original; the world part stays patched
        assert label("#client-toggle") == "Switch to patched" and label("#world-toggle") == "Switch to original"
        await press("#world-toggle")
        assert label("#world-toggle") == "Switch to patched" and not (work / "zones/zone253/ter253.mpk").exists()
    print("OK: Client tab: classes and world parts switch on and off independently")
try:
    asyncio.run(main())
finally:
    shutil.rmtree(tmp, ignore_errors=True)
