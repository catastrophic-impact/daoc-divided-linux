"""Headless smoke test for the admin screen against a fake admin socket (no real server needed).
Run: python/bin/python3 -I tui/test_tui.py"""
import asyncio, json, os, sys, tempfile
tmp = tempfile.mkdtemp()
os.environ["DAOC_ADMIN_SOCKET"] = os.path.join(tmp, "admin.sock")
os.environ["DAOC_HOME"] = tmp
os.environ["DAOC_SERVER_LOG"] = os.path.join(tmp, "console.out")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import daoc_admin_tui as t

OPT = lambda k, v: {"key": k, "kind": "number", "value": v, "default": "1", "description": f"about {k}"}
RESULTS = {
    "server.status": {"name": "Test", "version": "1", "edition": "Classic", "uptimeSeconds": 4000, "playersOnline": 1, "botsOnline": 2, "botRoster": 5, "populationEnabled": True, "maxActiveBots": 0},
    "bots.list": [{"id": 7, "name": "Botty", "realm": "albion", "class": "Cleric", "race": "Briton", "level": 5, "zone": "Camelot Hills", "activity": "idle", "goal": "solo", "online": True, "deleting": False}],
    "population.get": {"enabled": True, "maxActiveBots": 3, "roster": 5, "online": 2},
    "accounts.list": [{"name": "bob", "role": "player", "online": False, "characters": 2, "created": "2026-01-01T00:00:00Z", "lastLogin": "2026-10-01T10:00:00Z"}],
    "options.list": [OPT("siege", "on")], "rules.list": [OPT("xp_rate", "2")],
    "goals.get": {"rows": [{"bracket": "1-19", "soloPve": 50, "groupPve": 50, "rvR": 0, "battlegrounds": 0}], "saved": False},
    "bracket.status": [{"name": "Thidranki", "region": 252, "playersInside": 1, "roster": [12, 12, 12], "online": [11, 12, 12]}],
    "rvr.status": {"updatedUtc": "2026-10-29T00:00:00Z", "objectives": [{"kind": "Keep", "name": "Bledmeer", "owner": "Midgard", "state": "ok", "location": "x", "forces": "3"}],
                   "battlegrounds": [{"name": "Thidranki", "minLevel": 20, "maxLevel": 24, "centralKeep": "k", "owner": "Albion", "albionInside": 1, "midgardInside": 2, "hiberniaInside": 3, "albionTravelling": 0, "midgardTravelling": 0, "hiberniaTravelling": 0}]},
}
calls = []

async def handle(r, w):
    while line := await r.readline():
        req = json.loads(line); calls.append(req["op"])
        w.write((json.dumps({"id": req["id"], "ok": True, "result": RESULTS.get(req["op"], OPT("xp_rate", "5"))}) + "\n").encode()); await w.drain()

async def main():
    srv = await asyncio.start_unix_server(handle, os.environ["DAOC_ADMIN_SOCKET"])
    open(os.environ["DAOC_SERVER_LOG"], "w").write("INFO ok\nWARN careful\nERROR bad\n")
    app = t.DaocAdmin()
    async with app.run_test(size=(120, 40)) as pilot:
        await pilot.pause(0.5)
        for tab in ("bots", "population", "bracket", "accounts", "options", "rules", "rvr", "client", "console", "server"):
            app.query_one("#tabs").active = tab; await pilot.pause(0.3)
        assert app.query_one("#bots-table").row_count == 1
        assert app.query_one("#rule-table").row_count == 1
        assert app.query_one("#bracket-table").row_count == 1
        assert app.query_one("#bg-table").row_count == 1 and app.query_one("#obj-table").row_count == 1
        app.query_one("#tabs").active = "rules"; await pilot.pause(0.2)
        await app.query_one("#rule-table").focus() if False else None
        await pilot.click("#rule-change"); await pilot.pause(0.3)
        await pilot.press("escape") if False else await pilot.click("#cancel"); await pilot.pause(0.2)
        app.query_one("#tabs").active = "console"; await pilot.pause(0.5)
        assert app.log_seen and len(app.log_seen) == 3, app.log_seen
        # Quit -> "Stop the server too" must stop the server itself (it used to exit the screen and leave the server running)
        stopped = []
        async def fake_stop(): stopped.append(1); return True
        app.stop_server = fake_stop; t.pid_alive = lambda: 123
        await pilot.press("ctrl+q"); await pilot.pause(0.4)
        app.screen.query_one("#f-what").value = "stop"; await pilot.pause(0.2)
        await pilot.click("#ok"); await pilot.pause(0.6)
        assert stopped == [1], "Quit -> Stop the server too did not stop the server"
        app.save_screenshot(os.path.join(tmp, "shot.svg"))
    assert "rules.list" in calls and "rvr.status" in calls, calls
    print("OK", sorted(set(calls)))
asyncio.run(main())
