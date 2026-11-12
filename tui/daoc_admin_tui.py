#!/usr/bin/env python3
"""DAoC server admin screen (Textual, mouse works). Started by ./daoc-admin with the bundled Python.

Talks to the server's admin socket (JSON lines, run/admin.sock). Starts and stops the server through
daoc-server.sh. Keys: Ctrl+Q quit, F5 refresh.
Exit code 10 = "stop the server" was chosen (daoc-admin then waits for it to save and exit).
"""
import asyncio
import json
import os
import re
import subprocess
import sys
from pathlib import Path

from textual import on, work
from textual.app import App, ComposeResult
from textual.binding import Binding
from textual.containers import Horizontal, Vertical, VerticalScroll
from textual.screen import ModalScreen
from textual.widgets import Button, DataTable, Footer, Header, Input, Label, RichLog, Select, Static, TabbedContent, TabPane

sys.path.insert(0, str(Path(__file__).resolve().parent))
import client_tools  # noqa: E402
import client_patch  # noqa: E402  daoc-divided-linux: the full DividedRealms client patch (client-patch.json)

HOME = Path(os.environ.get("DAOC_HOME") or Path(__file__).resolve().parent.parent)
SOCKET = os.environ.get("DAOC_ADMIN_SOCKET", str(HOME / "run" / "admin.sock"))
LOG = Path(os.environ.get("DAOC_SERVER_LOG", HOME / "logs" / "console.out"))
PIDFILE = HOME / "run" / "server.pid"
SERVER_SH = HOME / "daoc-server.sh"
EXIT_STOP = 10


def server_port():
    """The game port from config/serverconfig.xml (10300 if not set)."""
    try:
        return int(re.search(r"<Port>(\d+)</Port>", (HOME / "config" / "serverconfig.xml").read_text()).group(1))
    except (OSError, AttributeError, ValueError):
        return 10300


REALMS = [("Albion", "albion"), ("Midgard", "midgard"), ("Hibernia", "hibernia"), ("All three", "all")]
ROLES = [("player", "player"), ("gm", "gm"), ("admin", "admin")]
LOG_FILTERS = ["All", "Warnings and errors", "Errors only"]
LOG_LINES = 300
LOG_TAIL_BYTES = 64 * 1024


class AdminError(Exception):
    pass


class Unreachable(AdminError):
    pass


class AdminLink:
    """One connection to the admin socket; commands are serialised and it reconnects after a failure."""

    def __init__(self, path):
        self.path = path
        self.reader = self.writer = None
        self.lock = asyncio.Lock()
        self.next_id = 0

    async def _close(self):
        if self.writer is not None:
            self.writer.close()
        self.reader = self.writer = None

    async def call(self, op, **args):
        async with self.lock:
            self.next_id += 1
            try:
                if self.writer is None or self.writer.is_closing():
                    if not os.path.exists(self.path):
                        raise Unreachable("server not running")
                    # A big roster (e.g. bots.list with hundreds of bots) can exceed asyncio's default 64 KiB line limit, which
                    # raises a plain ValueError instead of a connection error, so the reader needs a generous limit.
                    # (Fix found by ThatFontGuy's fork.)
                    self.reader, self.writer = await asyncio.wait_for(
                        asyncio.open_unix_connection(self.path, limit=1 << 23), 3)
                self.writer.write((json.dumps({"id": self.next_id, "op": op, "args": args}) + "\n").encode())
                await self.writer.drain()
                line = await asyncio.wait_for(self.reader.readline(), 15)
                if not line:
                    raise ConnectionError("the server closed the connection")
                reply = json.loads(line)
            except (OSError, ConnectionError, asyncio.TimeoutError, ValueError) as exc:
                await self._close()
                raise Unreachable(f"cannot reach the server ({exc or type(exc).__name__})") from exc
            if not reply.get("ok"):
                raise AdminError((reply.get("error") or {}).get("message", "failed"))
            return reply.get("result")


def pid_alive():
    try:
        pid = int(PIDFILE.read_text().strip())
        os.kill(pid, 0)
        return pid
    except (OSError, ValueError):
        return None


def tail_lines(path, limit):
    try:
        with open(path, "rb") as f:
            f.seek(0, os.SEEK_END)
            f.seek(max(0, f.tell() - LOG_TAIL_BYTES))
            text = f.read().decode("utf-8", "replace")
    except OSError:
        return []
    return text.splitlines()[-limit:]


def log_level(line):
    upper = line[:80].upper()
    if "ERROR" in upper or "FATAL" in upper:
        return 2
    if "WARN" in upper:
        return 1
    return 0


def pick(row, *names):
    """Field of a JSON row by name, ignoring case ('rvR' vs 'rvr')."""
    lowered = {k.lower(): v for k, v in row.items()}
    for name in names:
        if name.lower() in lowered:
            return lowered[name.lower()]
    return ""


def yes(value):
    return "yes" if value else ""


def uptime(seconds):
    d, rem = divmod(int(seconds), 86400)
    h, rem = divmod(rem, 3600)
    return (f"{d}d " if d else "") + f"{h}h {rem // 60}m"


def day(value):
    return str(value)[:16].replace("T", " ") if value else ""


class Confirm(ModalScreen):
    def __init__(self, title, text, ok="OK", danger=False):
        super().__init__()
        self.title_text, self.body, self.ok_label, self.danger = title, text, ok, danger

    def compose(self):
        with Vertical(classes="dialog"):
            yield Label(self.title_text, classes="dialog-title")
            yield Static(self.body)
            with Horizontal(classes="buttons"):
                yield Button(self.ok_label, id="ok", variant="error" if self.danger else "primary")
                yield Button("Cancel", id="cancel")

    @on(Button.Pressed)
    def pressed(self, event):
        self.dismiss(event.button.id == "ok")


class Form(ModalScreen):
    """A dialog of inputs and drop-downs. fields: (name, label, kind, default[, options]); dismisses with a dict or None."""

    def __init__(self, title, fields, ok="Apply", note=""):
        super().__init__()
        self.title_text, self.fields, self.ok_label, self.note = title, fields, ok, note

    def compose(self):
        with Vertical(classes="dialog"):
            yield Label(self.title_text, classes="dialog-title")
            if self.note:
                yield Static(self.note, classes="note")
            for name, label, kind, default, *rest in self.fields:
                yield Label(label)
                if kind == "select":
                    yield Select(rest[0], value=default, allow_blank=False, id=f"f-{name}")
                else:
                    yield Input(str(default), password=kind == "password", id=f"f-{name}")
            yield Static("", id="form-error", classes="error")
            with Horizontal(classes="buttons"):
                yield Button(self.ok_label, id="ok", variant="primary")
                yield Button("Cancel", id="cancel")

    @on(Button.Pressed)
    def pressed(self, event):
        if event.button.id != "ok":
            self.dismiss(None)
            return
        values = {name: self.query_one(f"#f-{name}").value for name, *_ in self.fields}
        self.dismiss(values)

    @on(Input.Submitted)
    def submitted(self, event):
        self.query_one("#ok", Button).press()


CLIENT_HINTS = {
    "classes": "Patches {n} files: game.dll (the extra class slots) and pregame/character_creation.xml (the creation screen's extra class buttons). "
               "Only needed to play the added classes; the classes of the stock 1.130 client work without it.",
    "world": "Patches {n} files: the zone table (without it entering a battleground sends you to the login screen), Abermenai and Marfach Caverns, "
             "door and building files for zones 0, 3, 100, 102, 206 and 207, the new items and skins, new catalog rows in gamedata.mpk and the loading screen. "
             "Needed whatever classes you play. Files the patch adds are removed again when you switch back.",
}


class DaocAdmin(App):
    TITLE = "DAoC Linux Server"
    BINDINGS = [Binding("ctrl+q", "quit_screen", "Quit"), Binding("f5", "refresh", "Refresh")]
    CSS = """
    Screen { background: $surface; }
    TabbedContent { height: 1fr; }
    TabPane { padding: 0 1; }
    DataTable { height: 1fr; }
    .buttons { height: 3; margin-top: 1; }
    .buttons Button { margin-right: 2; }
    .dialog { width: 70; height: auto; max-height: 90%; padding: 1 2; border: thick $primary; background: $panel; }
    .dialog-title { text-style: bold; color: $accent; margin-bottom: 1; }
    .error { color: $error; }
    .note { color: $text-muted; margin-bottom: 1; }
    ModalScreen { align: center middle; }
    #status-box { height: auto; padding: 1 2; border: round $primary; margin: 1 0; }
    #message { height: 1; color: $accent; }
    #pop-max { width: 14; }
    #client-path, #client-server, #client-account, #client-password { width: 100%; max-width: 70; }
    .hint { color: $text-muted; height: auto; margin-bottom: 1; }
    """

    def __init__(self):
        super().__init__()
        self.link = AdminLink(SOCKET)
        self.log_filter = 0
        self.log_paused = False
        self.log_seen = None
        self.busy = False
        self.exit_code = 0

    # ---- layout -------------------------------------------------------------------------------

    def compose(self) -> ComposeResult:
        yield Header()
        with TabbedContent(id="tabs"):
            with TabPane("Server", id="server"):
                yield Static("", id="status-box")
                with Horizontal(classes="buttons"):
                    yield Button("Start", id="srv-start", variant="success")
                    yield Button("Stop", id="srv-stop", variant="error")
                    yield Button("Restart", id="srv-restart", variant="warning")
                yield Static("Stop and Restart save everything first. Ctrl+Q closes this screen.", classes="hint")
            with TabPane("Bots", id="bots"):
                yield Static("", id="bot-summary")
                yield DataTable(id="bots-table", cursor_type="row", zebra_stripes=True)
                with Horizontal(classes="buttons"):
                    yield Button("Create bots...", id="bot-create", variant="primary")
                    yield Button("Delete selected", id="bot-delete")
                    yield Button("Delete ALL...", id="bot-delete-all", variant="error")
            with TabPane("Population", id="population"):
                yield Static("", id="pop-info")
                with Horizontal(classes="buttons"):
                    yield Button("Turn ON", id="pop-on", variant="success")
                    yield Button("Turn OFF", id="pop-off", variant="error")
                yield Label("Most bots in the world at once (0 = the whole roster)")
                with Horizontal(classes="buttons"):
                    yield Input("0", id="pop-max", type="integer")
                    yield Button("Apply", id="pop-apply", variant="primary")
            with TabPane("Accounts", id="accounts"):
                yield DataTable(id="acct-table", cursor_type="row", zebra_stripes=True)
                with Horizontal(classes="buttons"):
                    yield Button("Set role...", id="acct-role", variant="primary")
                    yield Button("Create account...", id="acct-create")
            with TabPane("Options", id="options"):
                yield Static("Bot and server switches. Select a row, press Enter or Change.", classes="hint")
                yield DataTable(id="opt-table", cursor_type="row", zebra_stripes=True)
                with Horizontal(classes="buttons"):
                    yield Button("Change selected...", id="opt-change", variant="primary")
                    yield Button("Edit bot goals...", id="goals-edit")
                yield Static("", id="goals-info")
            with TabPane("Server Rules", id="rules"):
                yield Static("Rates are multipliers (0.1 to 100). Changes apply at once and are saved.", classes="hint")
                yield DataTable(id="rule-table", cursor_type="row", zebra_stripes=True)
                with Horizontal(classes="buttons"):
                    yield Button("Change selected...", id="rule-change", variant="primary")
                    yield Button("Reset selected", id="rule-reset")
                    yield Button("Reset all", id="rule-reset-all", variant="warning")
            with TabPane("RvR", id="rvr"):
                yield Static("", id="rvr-info")
                yield DataTable(id="bg-table", cursor_type="row", zebra_stripes=True)
                yield DataTable(id="obj-table", cursor_type="row", zebra_stripes=True)
            with TabPane("Bracket Bots", id="bracket"):
                yield Static("Battleground bracket bots stay logged out until a player enters their battleground, then fill each realm and leave after the last player goes. "
                             "Settings (bots per realm, fill mode, keep even, grace time) are on the Options tab: bracket_bots_*.", classes="hint")
                yield DataTable(id="bracket-table", cursor_type="row", zebra_stripes=True)
                with Horizontal(classes="buttons"):
                    yield Button("Create bracket bots...", id="bracket-create", variant="primary")
            with TabPane("Client", id="client"):
                with VerticalScroll():
                    yield Static("Folder of your DAoC 1.130 client (in your Wine prefix, Lutris, Steam or Bottles). The client patch has two independent parts below; "
                                 "each keeps the replaced files as .orig, so you can switch it back at any time.", classes="hint")
                    yield Input("", placeholder="/path/to/Dark Age of Camelot", id="client-path")
                    with Horizontal(classes="buttons"):
                        yield Button("Check", id="client-check", variant="primary")
                    yield Label("1. Extra classes (Death Knight, the Sovereigns, Stormforged and the other added classes)", id="classes-title")
                    yield Static("", id="client-info")
                    with Horizontal(classes="buttons"):
                        yield Button("Switch to patched", id="client-toggle", variant="warning")
                    yield Static("", id="classes-hint", classes="hint")
                    yield Label("2. World and art (battlegrounds, new zones, doors and buildings, new items, loading screen)", id="world-title")
                    yield Static("", id="world-info")
                    with Horizontal(classes="buttons"):
                        yield Button("Switch to patched", id="world-toggle", variant="warning")
                    yield Static("", id="world-hint", classes="hint")
                    yield Label("Battleground fix: the plain 1.130 client sends you to the login screen when you enter a battleground (and has no Abermenai)", id="bgfix-title")
                    yield Static("", id="bgfix-info")
                    with Horizontal(classes="buttons"):
                        yield Button("Switch battleground fix on", id="bgfix-toggle", variant="warning")
                    yield Static("Replaces only zones/zones.mpk (about 10 KB) with the classic zone table and keeps the original as zones.mpk.orig. Switch it off to put the original back.", id="bgfix-hint", classes="hint")
                    yield Label("launcher.bat: the server never starts the game. This writes a launcher you run yourself (wine, Lutris, Steam...)")
                    yield Input("", placeholder="server host:port", id="client-server")
                    yield Input("", placeholder="account (blank = asked each time)", id="client-account")
                    yield Input("", placeholder="password (blank = asked each time)", password=True, id="client-password")
                    with Horizontal(classes="buttons"):
                        yield Button("Create launcher.bat", id="client-launcher", variant="success")
            with TabPane("Console", id="console"):
                with Horizontal(classes="buttons"):
                    yield Button("Show: All", id="log-filter")
                    yield Button("Pause", id="log-pause")
                yield RichLog(id="log", max_lines=LOG_LINES, markup=False, wrap=False)
        yield Static("", id="message")
        yield Footer()

    async def on_mount(self):
        for table, columns in (
            ("#bots-table", ["Name", "Realm", "Class", "Lvl", "Zone", "Activity", "Goal", "Online"]),
            ("#acct-table", ["Account", "Role", "Online", "Chars", "Last login"]),
            ("#opt-table", ["Option", "Value", "Default", "What it does"]),
            ("#rule-table", ["Rule", "Value", "Default", "What it does"]),
            ("#bg-table", ["Battleground", "Levels", "Keep owner", "Inside A/M/H", "On the way A/M/H"]),
            ("#obj-table", ["Kind", "Name", "Owner", "State", "Location", "Forces"]),
            ("#bracket-table", ["Battleground", "Players inside", "Albion roster/online", "Midgard roster/online", "Hibernia roster/online"]),
        ):
            self.query_one(table, DataTable).add_columns(*columns)
        self.query_one("#log", RichLog).can_focus = False
        remembered = client_tools.load_settings(HOME).get("client", "")
        self.query_one("#client-path", Input).value = remembered
        self.query_one("#client-server", Input).value = client_tools.load_settings(HOME).get("server") or f"127.0.0.1:{server_port()}"
        self.show_client()
        self.set_interval(2, self.tick)
        self.run_worker(self.refresh_all(), exclusive=True)

    # ---- helpers ------------------------------------------------------------------------------

    def say(self, text, error=False):
        box = self.query_one("#message", Static)
        box.update(("! " if error else "") + text)

    def active(self):
        return self.query_one("#tabs", TabbedContent).active

    async def api(self, op, **args):
        """Call the server; show a refusal in the message line. None on failure."""
        try:
            return await self.link.call(op, **args)
        except AdminError as exc:
            self.say(str(exc), error=True)
            return None

    def selected_key(self, table_id):
        table = self.query_one(table_id, DataTable)
        if table.row_count == 0:
            return None
        return table.coordinate_to_cell_key(table.cursor_coordinate).row_key.value

    def fill(self, table_id, rows, keys=None):
        table = self.query_one(table_id, DataTable)
        keep = table.cursor_row if table.row_count else 0
        table.clear()
        for i, row in enumerate(rows):
            table.add_row(*row, key=str(keys[i]) if keys else None)
        if table.row_count:
            table.move_cursor(row=min(keep, table.row_count - 1))

    # ---- refresh ------------------------------------------------------------------------------

    async def tick(self):
        tab = self.active()
        if tab == "console":
            self.load_console()
        elif tab in ("server", "bots", "population", "rvr"):
            await self.refresh_tab(tab)

    async def action_refresh(self):
        await self.refresh_all()
        self.say("Refreshed.")

    async def refresh_all(self):
        for tab in ("server", "population", "bots", "bracket", "accounts", "options", "rules", "rvr"):
            await self.refresh_tab(tab)
        self.load_console()

    @on(TabbedContent.TabActivated)
    async def tab_changed(self, event):
        await self.refresh_tab(event.pane.id)
        if event.pane.id == "console":
            self.load_console()

    async def refresh_tab(self, tab):
        try:
            if tab == "console":
                self.load_console()
            else:
                await getattr(self, f"load_{tab}")()
        except Unreachable:
            if tab == "server":
                await self.load_server()
        except AdminError as exc:
            self.say(str(exc), error=True)

    async def load_server(self):
        pid = pid_alive()
        try:
            st = await self.link.call("server.status")
        except Unreachable as exc:
            text = f"[b]Server: {'starting or not answering' if pid else 'stopped'}[/b]" + (f"  (pid {pid})" if pid else "")
            self.query_one("#status-box", Static).update(text + f"\n{exc}")
            self.sub_title = "stopped" if not pid else "not answering"
            return
        self.sub_title = f"{st['name']} - {st['playersOnline']} players, {st['botsOnline']} bots online"
        self.query_one("#status-box", Static).update(
            f"[b]Server: running[/b]  (pid {pid or '?'})\n"
            f"{st['name']}  {st['version']}  {st['edition']}\n"
            f"Up {uptime(st['uptimeSeconds'])}\n"
            f"Players online: {st['playersOnline']}\n"
            f"Bots: {st['botsOnline']} online of {st['botRoster']}   "
            f"Population: {'on' if st['populationEnabled'] else 'off'}, max {st['maxActiveBots'] or 'whole roster'}"
        )

    async def load_bots(self):
        bots = await self.link.call("bots.list")
        online = sum(1 for b in bots if b["online"])
        self.query_one("#bot-summary", Static).update(f"{len(bots)} bots, {online} online")
        self.fill("#bots-table",
                  [(b["name"], b["realm"], b["class"], b["level"], b["zone"], b["activity"], b["goal"],
                    "deleting" if b["deleting"] else yes(b["online"])) for b in bots],
                  [b["id"] for b in bots])

    async def load_bracket(self):
        rows = await self.link.call("bracket.status")
        self.fill("#bracket-table",
                  [(b["name"], b["playersInside"], f"{b['roster'][0]}/{b['online'][0]}", f"{b['roster'][1]}/{b['online'][1]}",
                    f"{b['roster'][2]}/{b['online'][2]}") for b in rows],
                  [b["name"] for b in rows])

    @on(Button.Pressed, "#bracket-create")
    @work
    async def _bracket_create(self):
        values = await self.push_screen_wait(Form("Create bracket bots", [
            ("bg", "Battleground", "select", "all", [("All four", "all"), ("Abermenai (15-19)", "Abermenai"), ("Thidranki (20-24)", "Thidranki"),
                                                     ("Murdaigean (25-29)", "Murdaigean"), ("Caledonia (30-35)", "Caledonia")]),
            ("count", "Bots per realm (1-100)", "text", "12"),
        ], ok="Create", note="They stay logged out until a player enters their battleground. Each realm gets this many for every battleground chosen."))
        if not values:
            return
        try:
            count = int(values["count"])
        except ValueError:
            self.say("Enter a whole number.", error=True)
            return
        result = await self.api("bracket.create", battleground=values["bg"], count=count)
        if result is not None:
            self.say(f"Created {len(result['names'])} bracket bot(s).")
            await self.load_bracket()

    async def load_population(self):
        p = await self.link.call("population.get")
        self.query_one("#pop-info", Static).update(
            f"Population is [b]{'ON' if p['enabled'] else 'OFF'}[/b]\n"
            f"Roster {p['roster']}   online {p['online']}   most at once: {p['maxActiveBots'] or 'whole roster'}")
        box = self.query_one("#pop-max", Input)
        if not box.has_focus:
            box.value = str(p["maxActiveBots"])

    async def load_accounts(self):
        rows = await self.link.call("accounts.list")
        self.fill("#acct-table", [(a["name"], a["role"], yes(a["online"]), a["characters"], day(a["lastLogin"])) for a in rows],
                  [a["name"] for a in rows])

    async def load_options(self):
        rows = await self.link.call("options.list")
        self.fill("#opt-table", [(o["key"], o["value"], o["default"], o["description"]) for o in rows], [o["key"] for o in rows])
        goals = await self.link.call("goals.get")
        lines = [f"  {g['bracket']:>6}:  solo {pick(g, 'soloPve')}%  group {pick(g, 'groupPve')}%  "
                 f"RvR {pick(g, 'rvr')}%  battlegrounds {pick(g, 'battlegrounds')}%" for g in goals["rows"]]
        self.query_one("#goals-info", Static).update(
            "Bot goals (" + ("bot-goals.json" if goals["saved"] else "built-in defaults") + "):\n" + "\n".join(lines))

    async def load_rules(self):
        rows = await self.link.call("rules.list")
        self.fill("#rule-table", [(o["key"], o["value"], o["default"], o["description"]) for o in rows], [o["key"] for o in rows])

    # ---- Client tab ---------------------------------------------------------------------------

    def client_folder(self):
        folder = self.query_one("#client-path", Input).value.strip()
        if folder:
            settings = client_tools.load_settings(HOME)
            if settings.get("client") != folder:
                settings["client"] = folder
                client_tools.save_settings(HOME, settings)
        return folder

    @staticmethod
    def set_button_label(button, label):
        # A Button keeps its old width when the label changes, which cut "Switch to original" down to "Switch to".
        button.label = label
        button.styles.width = len(label) + 4
        button.refresh(layout=True)

    def full_patch(self):
        """The release's client-patch.json (tools/), or None when this build does not ship it (then only the class-slot patch is used)."""
        file = HOME / "tools" / "client-patch.json"
        try:
            return client_patch.load(file) if file.is_file() else None
        except (OSError, ValueError, client_patch.PatchError):
            return None

    FULL_ONLY = ("#world-title", "#world-info", "#world-toggle", "#world-hint")
    LEGACY_ONLY = ("#bgfix-title", "#bgfix-info", "#bgfix-toggle", "#bgfix-hint")

    def show_client(self):
        folder = self.query_one("#client-path", Input).value.strip()
        box = self.query_one("#client-info", Static)
        full = self.full_patch()
        for sel in self.FULL_ONLY:   # the world switch only exists with the full patch, the old battleground fix only without it
            self.query_one(sel).display = bool(full)
        for sel in self.LEGACY_ONLY:
            self.query_one(sel).display = not full
        if not folder:
            box.update("Enter the client folder, then press Check.")
            return None
        try:
            info = client_tools.inspect(folder)
        except client_tools.ClientError as exc:
            box.update(str(exc))
            return None
        toggle = self.query_one("#client-toggle", Button)
        info["full"] = full
        if full:  # daoc-divided-linux: the two parts of DividedRealms' client patch, each with its own switch
            for group, toggle_id, info_id, hint_id in (("classes", "#client-toggle", "#client-info", "#classes-hint"),
                                                       ("world", "#world-toggle", "#world-info", "#world-hint")):
                part = client_patch.select(full, group)
                st = client_patch.status(folder, part)
                restore = st["state"] == "patched" or st["state"] == "blocked" and st["backups"]  # a half or old-patched part is patched the rest of the way
                info[group] = {"patch": part, "status": st, "restore": restore}
                button = self.query_one(toggle_id, Button)
                self.set_button_label(button, "Switch to original" if restore else "Switch to patched")
                button.disabled = st["state"] == "missing" or st["state"] == "blocked" and not st["backups"]
                self.query_one(info_id, Static).update(f"{st['state']}   ({st['total']} files)   backups: {'yes' if st['backups'] else 'no'}\n{st['message']}")
                self.query_one(hint_id, Static).update(CLIENT_HINTS[group].format(n=st["total"]))
            return info
        self.set_button_label(toggle, "Switch to original" if info["game"] == "patched" or info["layout"] == "patched" else "Switch to patched")
        toggle.disabled = not (info["can_patch"] or info["backups"])
        self.query_one("#classes-hint", Static).update("Switches the two client files (game.dll, pregame/character_creation.xml; about 6 MB) in place. The originals are kept as .orig.")
        bg = client_tools.bgfix_state(folder)
        self.query_one("#bgfix-info", Static).update({
            "live": "Battleground fix: OFF (the retail zone table: entering a battleground will send you to the login screen)",
            "classic": "Battleground fix: ON (classic zone table; all four battlegrounds work)",
            "other": "Battleground fix: zones/zones.mpk is not a known file; it will not be changed",
            "missing": "Battleground fix: zones/zones.mpk not found"}[bg])
        self.set_button_label(self.query_one("#bgfix-toggle", Button), "Switch battleground fix off" if bg == "classic" else "Switch battleground fix on")
        self.query_one("#bgfix-toggle", Button).disabled = bg not in ("live", "classic")
        box.update(f"game.dll: {info['game']}   character_creation.xml: {info['layout']}   backups: {'yes' if info['backups'] else 'no'}\n{info['message']}")
        return info

    @on(Button.Pressed, "#client-check")
    @on(Input.Submitted, "#client-path")
    def _client_check(self):
        self.client_folder()
        self.show_client()

    @on(Button.Pressed, "#client-toggle")
    @work
    async def _client_toggle(self):
        await self.toggle_part("classes")

    @on(Button.Pressed, "#world-toggle")
    @work
    async def _world_toggle(self):
        await self.toggle_part("world")

    async def toggle_part(self, group):
        folder = self.client_folder()
        info = self.show_client()
        if not info:
            return
        if info.get("full"):  # daoc-divided-linux: one part of the full patch (classes | world)
            part = info[group]
            st, patch = part["status"], part["patch"]
            name = "extra classes" if group == "classes" else "world and art"
            try:
                if part["restore"]:
                    if not await self.push_screen_wait(Confirm("Switch to original", f"Put the original {name} files back in\n{folder}\nfrom their .orig backups" + (", and remove the files the patch added?" if group == "world" else "?"), "Switch")):
                        return
                    changed = await asyncio.to_thread(client_patch.restore, folder, patch)
                    done = f"Client {name} switched to the original files ({len(changed)} files)."
                else:
                    if st["state"] not in ("ready", "partial"):
                        self.say(st["message"], error=True)
                        return
                    if not await self.push_screen_wait(Confirm("Switch to patched", f"Patch the {name} files ({st['total']}) in\n{folder}\nEvery replaced file is kept as .orig. Only stock 1.130 files are accepted; nothing is written unless all of them check out.", "Switch")):
                        return
                    written = await asyncio.to_thread(client_patch.apply, folder, patch)
                    done = f"Client {name} patched ({len(written)} files written)."
            except (client_patch.PatchError, OSError) as exc:
                self.say(str(exc), error=True)
                return
            self.show_client()
            self.say(done)
            return
        patched = info["game"] == "patched" or info["layout"] == "patched"
        try:
            if patched:
                if not await self.push_screen_wait(Confirm("Switch to original", f"Put the original game.dll and character_creation.xml back in\n{folder}?", "Switch")):
                    return
                await asyncio.to_thread(client_tools.restore_client, folder)
                done = "Client switched to the original files."
            else:
                if not info["can_patch"]:
                    self.say(info["message"], error=True)
                    return
                if not await self.push_screen_wait(Confirm("Switch to patched", f"Patch game.dll and character_creation.xml in\n{folder}\nThe originals are kept as .orig.", "Switch")):
                    return
                await asyncio.to_thread(client_tools.patch_client, folder)
                done = "Client switched to the patched files."
        except (client_tools.ClientError, OSError) as exc:
            self.say(str(exc), error=True)
            return
        self.show_client()
        self.say(done)

    @on(Button.Pressed, "#client-launcher")
    @work
    async def _client_launcher(self):
        folder = self.client_folder()
        server = self.query_one("#client-server", Input).value.strip()
        account = self.query_one("#client-account", Input).value.strip()
        password = self.query_one("#client-password", Input).value
        try:
            if not (folder and Path(folder).expanduser().is_dir()):
                raise client_tools.ClientError("Enter the client folder first.")
            made = await asyncio.to_thread(client_tools.write_launcher, folder, HOME / "tools" / client_tools.CONNECTOR,
                                           server, account, password)
        except (client_tools.ClientError, OSError) as exc:
            self.say(str(exc), error=True)
            return
        settings = client_tools.load_settings(HOME)
        settings["server"] = server
        client_tools.save_settings(HOME, settings)  # the account and password are never saved here
        self.query_one("#client-password", Input).value = ""
        self.say(f"Wrote {made}. Run it with your own Wine setup." + (" The password is stored in that file." if password else ""))

    @on(Button.Pressed, "#bgfix-toggle")
    @work
    async def _bgfix_toggle(self):
        folder = self.client_folder()
        try:
            if client_tools.bgfix_state(folder) == "classic":
                if not await self.push_screen_wait(Confirm("Battleground fix off", f"Put the original zones/zones.mpk back in\n{folder}?\nBattlegrounds will send you to the login screen again.", "Switch off")):
                    return
                await asyncio.to_thread(client_tools.bgfix_off, folder)
                done = "Battleground fix is off."
            else:
                if not await self.push_screen_wait(Confirm("Battleground fix on", f"Replace zones/zones.mpk in\n{folder}\nwith the classic zone table? The original is kept as zones.mpk.orig.", "Switch on")):
                    return
                await asyncio.to_thread(client_tools.bgfix_on, folder, None, bundled=HOME / "tools" / client_tools.CLASSIC_ZONES_FILE)
                done = "Battleground fix is on."
        except (client_tools.ClientError, OSError) as exc:
            self.say(str(exc), error=True)
            return
        self.show_client()
        self.say(done)

    async def load_client(self):
        self.show_client()

    async def load_rvr(self):
        r = await self.link.call("rvr.status")
        self.query_one("#rvr-info", Static).update(f"Snapshot refreshed every 30 s. Last: {day(r['updatedUtc'])} UTC")
        self.fill("#bg-table", [(b["name"], f"{b['minLevel']}-{b['maxLevel']}", b["owner"] or b["centralKeep"],
                                 f"{b['albionInside']}/{b['midgardInside']}/{b['hiberniaInside']}",
                                 f"{b['albionTravelling']}/{b['midgardTravelling']}/{b['hiberniaTravelling']}")
                                for b in r["battlegrounds"]])
        self.fill("#obj-table", [(o["kind"], o["name"], o["owner"], o["state"], o["location"], o["forces"]) for o in r["objectives"]])

    def load_console(self):
        if self.log_paused:
            return
        lines = [ln for ln in tail_lines(LOG, LOG_LINES) if log_level(ln) >= self.log_filter]
        if lines == self.log_seen:
            return
        self.log_seen = lines
        view = self.query_one("#log", RichLog)
        view.clear()
        for ln in lines:
            view.write(ln)

    # ---- server control -----------------------------------------------------------------------

    async def wait_stopped(self, seconds=120):
        for _ in range(seconds):
            if pid_alive() is None:
                return True
            await asyncio.sleep(1)
        return False

    async def start_server(self):
        if pid_alive():
            self.say("The server is already running.")
            return True
        self.say("Starting the server (a few seconds to a minute)...")
        proc = await asyncio.create_subprocess_exec(
            str(SERVER_SH), "start", "--detach", cwd=str(HOME),
            stdin=asyncio.subprocess.DEVNULL, stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.STDOUT)
        out, _ = await proc.communicate()
        if proc.returncode != 0:
            self.say(out.decode(errors="replace").strip().splitlines()[-1] if out else "start failed", error=True)
            return False
        self.say("Server started.")
        return True

    async def stop_server(self):
        if not pid_alive():
            self.say("The server is not running.")
            return True
        self.say("Saving and stopping the server...")
        proc = await asyncio.create_subprocess_exec(str(SERVER_SH), "stop", cwd=str(HOME),
                                                    stdout=asyncio.subprocess.DEVNULL, stderr=asyncio.subprocess.DEVNULL)
        await proc.wait()
        ok = await self.wait_stopped(5)
        if ok and proc.returncode != 0:  # daoc-divided-linux: gone, but without the final save (the script checks the log)
            self.say("The server exited WITHOUT finishing its save: changes since its last autosave are lost. See logs/console.out.", error=True)
            return True
        self.say("Server stopped." if ok else "The server is still stopping; check logs/console.out.", error=not ok)
        return ok

    async def server_action(self, what):
        if self.busy:
            return
        self.busy = True
        try:
            if what in ("stop", "restart"):
                if not await self.stop_server():
                    return
            if what in ("start", "restart"):
                await self.start_server()
        finally:
            self.busy = False
            await self.load_server()

    @work
    async def action_quit_screen(self):
        if not pid_alive():
            self.exit(0)
            return
        choice = await self.push_screen_wait(Form(
            "Quit", [("what", "The server is running. Close this screen and:", "select", "leave",
                      [("Leave the server running", "leave"), ("Stop the server too", "stop")])], ok="Quit",
            note="./daoc-admin reopens this screen later."))
        if choice is None:
            return
        # daoc-divided-linux: stop the server here, the same way the Stop button does, and close the screen only once it has saved and exited.
        # (This used to call self.exit(EXIT_STOP): Textual's first argument is a result, not the exit code, so the screen always exited 0 and
        # daoc-admin's "10 = stop" branch never ran. That branch only waits for the server anyway; nothing ever sent it the stop.)
        if choice["what"] == "stop" and not await self.stop_server():
            return  # still stopping: stay on the screen so the message stays visible
        self.exit()

    # ---- buttons ------------------------------------------------------------------------------

    @on(Button.Pressed, "#srv-start")
    def _srv_start(self):
        self.run_worker(self.server_action("start"))

    @on(Button.Pressed, "#srv-stop")
    @work
    async def _srv_stop(self):
        if await self.push_screen_wait(Confirm("Stop server", "Save everything and stop the server?", "Stop", danger=True)):
            self.run_worker(self.server_action("stop"))

    @on(Button.Pressed, "#srv-restart")
    @work
    async def _srv_restart(self):
        if await self.push_screen_wait(Confirm("Restart server", "Save, stop and start the server again?\nPlayers are disconnected.", "Restart")):
            self.run_worker(self.server_action("restart"))

    @on(Button.Pressed, "#bot-create")
    @work
    async def _bot_create(self):
        values = await self.push_screen_wait(Form("Create bots", [
            ("realm", "Realm", "select", "all", REALMS),
            ("level", "Level", "select", "1", [("1", "1"), ("50", "50")]),
            ("count", "How many (1-100)", "text", "1"),
            ("cls", "Class (blank = random)", "text", ""),
        ], ok="Create"))
        if not values:
            return
        args = {"realm": values["realm"], "level": int(values["level"]), "count": int(values["count"] or 1)}
        if values["cls"].strip():
            args["class"] = values["cls"].strip()
        result = await self.api("bots.create", **args)
        if result is not None:
            self.say(f"Created {len(result['names'])} bot(s)." + ("" if result["populationEnabled"] else " Population is off: they log in when it is on."))
            await self.load_bots()

    @on(Button.Pressed, "#bot-delete")
    @work
    async def _bot_delete(self):
        key = self.selected_key("#bots-table")
        if key is None:
            return
        row = self.query_one("#bots-table", DataTable).get_row(key)
        if await self.push_screen_wait(Confirm("Delete bot", f"Permanently delete {row[0]} ({row[1]} {row[2]} {row[3]})?\nIts character, gear and coins are removed.", "Delete", danger=True)):
            if await self.api("bots.delete", bot=key) is not None:
                self.say(f"Deleting {row[0]}.")
                await self.load_bots()

    @on(Button.Pressed, "#bot-delete-all")
    @work
    async def _bot_delete_all(self):
        values = await self.push_screen_wait(Form("Delete ALL bots", [("word", "Type DELETE to confirm", "text", "")], ok="Delete all",
                                                  note="This permanently deletes every bot character."))
        if values and values["word"] == "DELETE":
            if await self.api("bots.deleteAll", confirm=True) is not None:
                self.say("Deleting all bots.")
                await self.load_bots()
        elif values:
            self.say("Not confirmed: type DELETE in capitals.", error=True)

    async def set_population(self, **args):
        if await self.api("population.set", **args) is not None:
            await self.load_population()

    @on(Button.Pressed, "#pop-on")
    async def _pop_on(self):
        await self.set_population(enabled=True)

    @on(Button.Pressed, "#pop-off")
    @work
    async def _pop_off(self):
        if await self.push_screen_wait(Confirm("Population off", "Turning the population off logs every bot out of the world\n(their characters are kept). Continue?", "Turn off")):
            await self.set_population(enabled=False)

    @on(Button.Pressed, "#pop-apply")
    async def _pop_apply(self):
        text = self.query_one("#pop-max", Input).value
        if text.isdigit():
            await self.set_population(maxActiveBots=int(text))
        else:
            self.say("Enter a whole number.", error=True)

    @on(Button.Pressed, "#acct-role")
    @work
    async def _acct_role(self):
        key = self.selected_key("#acct-table")
        if key is None:
            return
        current = self.query_one("#acct-table", DataTable).get_row(key)[1]
        values = await self.push_screen_wait(Form(f"Role for {key}", [("role", "Role", "select", current, ROLES)]))
        if values and await self.api("accounts.setRole", name=key, role=values["role"]) is not None:
            self.say(f"{key} is now {values['role']}.")
            await self.load_accounts()

    @on(Button.Pressed, "#acct-create")
    @work
    async def _acct_create(self):
        values = await self.push_screen_wait(Form("Create account", [
            ("name", "Account name", "text", ""), ("password", "Password", "password", "")], ok="Create"))
        if values and await self.api("accounts.create", name=values["name"], password=values["password"]) is not None:
            self.say(f"Account {values['name']} created.")
            await self.load_accounts()

    async def change_setting(self, table_id, op, noun):
        key = self.selected_key(table_id)
        if key is None:
            return
        row = self.query_one(table_id, DataTable).get_row(key)
        switch = row[1] in ("on", "off")
        field = ("value", f"{noun}: {key}", "select", row[1], [("on", "on"), ("off", "off")]) if switch else ("value", f"{key} (default {row[2]})", "text", row[1])
        values = await self.push_screen_wait(Form(f"Change {key}", [field], note=row[3]))
        if values and await self.api(op, key=key, value=values["value"]) is not None:
            self.say(f"{key} = {values['value']}. Saved.")
            await self.refresh_tab("options" if op == "options.set" else "rules")

    @on(Button.Pressed, "#opt-change")
    @work
    async def _opt_change(self):
        await self.change_setting("#opt-table", "options.set", "Option")

    @on(Button.Pressed, "#rule-change")
    @work
    async def _rule_change(self):
        await self.change_setting("#rule-table", "rules.set", "Rule")

    @on(DataTable.RowSelected, "#opt-table")
    @work
    async def _opt_row(self):
        await self.change_setting("#opt-table", "options.set", "Option")

    @on(DataTable.RowSelected, "#rule-table")
    @work
    async def _rule_row(self):
        await self.change_setting("#rule-table", "rules.set", "Rule")

    @on(Button.Pressed, "#rule-reset")
    async def _rule_reset(self):
        key = self.selected_key("#rule-table")
        if key and await self.api("rules.reset", key=key) is not None:
            self.say(f"{key} is back to the default.")
            await self.load_rules()

    @on(Button.Pressed, "#rule-reset-all")
    @work
    async def _rule_reset_all(self):
        if await self.push_screen_wait(Confirm("Reset all rules", "Put every rule back to the server default?", "Reset all", danger=True)):
            if await self.api("rules.reset", key="all") is not None:
                self.say("All rules are back to the defaults.")
                await self.load_rules()

    @on(Button.Pressed, "#goals-edit")
    @work
    async def _goals_edit(self):
        values = await self.push_screen_wait(Form("Edit bot goals", [
            ("bracket", "Level bracket", "select", "1-19", [("1-19", "1-19"), ("20-49", "20-49"), ("50", "50")]),
            ("solo", "Solo PvE %", "text", "0"), ("group", "Group PvE %", "text", "0"),
            ("rvr", "RvR %", "text", "0"), ("bg", "Battlegrounds %", "text", "0")],
            note="Percentages must add up to 100. Battlegrounds are for levels 15-35; RvR starts at 20."))
        if not values:
            return
        try:
            nums = {k: int(values[k]) for k in ("solo", "group", "rvr", "bg")}
        except ValueError:
            self.say("Goals must be whole numbers.", error=True)
            return
        if await self.api("goals.set", bracket=values["bracket"], solo=nums["solo"], group=nums["group"],
                          rvr=nums["rvr"], battlegrounds=nums["bg"]) is not None:
            self.say(f"Goals for {values['bracket']} saved.")
            await self.load_options()

    @on(Button.Pressed, "#log-filter")
    def _log_filter(self, event):
        self.log_filter = (self.log_filter + 1) % len(LOG_FILTERS)
        event.button.label = f"Show: {LOG_FILTERS[self.log_filter]}"
        self.log_seen = None
        self.load_console()

    @on(Button.Pressed, "#log-pause")
    def _log_pause(self, event):
        self.log_paused = not self.log_paused
        event.button.label = "Resume" if self.log_paused else "Pause"
        self.load_console()


def main():
    app = DaocAdmin()
    app.run()
    return app.return_code or 0


if __name__ == "__main__":
    sys.exit(main())
