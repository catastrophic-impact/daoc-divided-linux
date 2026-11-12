#!/usr/bin/env bash
# daoc-server.sh - run a portable DAoC server folder. Everything lives next to this script, so
# the folder can be unpacked or moved anywhere. No .NET install is needed: bin/ is self-contained.
#
#   ./daoc-server.sh start            start the server in the background and open the admin
#                                     screen here (quit it with Ctrl+Q: stop the server or leave it running)
#   ./daoc-server.sh start --console  raw server console in the foreground instead (Ctrl+C, SIGTERM
#                                     or "exit" stop it); also used automatically without a terminal
#   ./daoc-server.sh start --detach   server in the background, no admin screen (used by the admin screen's Start/Restart)
#   ./daoc-server.sh stop      stop a running server cleanly (saves first)
#   ./daoc-server.sh status    is it running?
#   ./daoc-admin               manage bots, population and accounts (admin screen or commands)
#
# Folder layout:
#   bin/        server program (replaced on upgrade)
#   defaults/   templates copied into config/ on first start
#   config/     serverconfig.xml, logconfig.xml, invalidnames.txt   (yours; kept on upgrade)
#   data/       opendaoc.sqlite3.db: the world and all saves (yours; kept on upgrade). Created on
#               first start from defaults/world.sqlite, a fresh Classic + SI world.
#   navmesh/    zone*.nav and the bot route files                      (kept on upgrade)
#   logs/  run/
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DAOC_HOME="${DAOC_HOME:-$HERE}"
PIDFILE="$DAOC_HOME/run/server.pid"

die() { echo "daoc-server: $*" >&2; exit 1; }

running_pid() {
  local pid
  [[ -f $PIDFILE ]] || return 1
  pid=$(<"$PIDFILE")
  [[ $pid =~ ^[0-9]+$ ]] && kill -0 "$pid" 2>/dev/null && echo "$pid"
}

# The game port from config/serverconfig.xml (10300 if not set yet).
game_port() {
  local port
  port=$(sed -n 's:.*<Port>\([0-9]*\)</Port>.*:\1:p' "$DAOC_HOME/config/serverconfig.xml" 2>/dev/null | head -1)
  echo "${port:-10300}"
}

port_in_use() { (exec 3<>"/dev/tcp/127.0.0.1/$(game_port)") 2>/dev/null; }

# world_update [--check]: tools/world_update.py on the bundled Python. No-op without data/ or world-updates/.
world_update() {
  [[ -f $DAOC_HOME/data/opendaoc.sqlite3.db && -f $DAOC_HOME/world-updates/world-updates.json ]] || return 0
  [[ -x $DAOC_HOME/python/bin/python3 && -f $DAOC_HOME/tools/world_update.py ]] || { echo "world update skipped: python/ or tools/world_update.py is missing" >&2; return 0; }
  "$DAOC_HOME/python/bin/python3" -I "$DAOC_HOME/tools/world_update.py" "$DAOC_HOME" "$@"
}

start() {
  local mode=${1:-}
  # --console, or no terminal (init system, redirect): run the raw server console in the foreground.
  local console=false
  [[ $mode == --console || ! -t 0 || ! -t 1 ]] && console=true
  [[ $mode == --detach ]] && console=false   # background even without a terminal (the admin screen starts it this way)

  if running_pid >/dev/null; then
    [[ $mode == --detach ]] && { echo "already running (pid $(running_pid))"; return 0; }
    $console && die "already running (pid $(running_pid))"
    exec "$DAOC_HOME/daoc-admin"   # already running: just open the admin screen
  fi
  [[ -x $DAOC_HOME/bin/CoreServer ]] || die "no server program in $DAOC_HOME/bin"
  compgen -G "$DAOC_HOME/navmesh/*.nav" >/dev/null || die "no navmeshes in navmesh/ (they ship with the release folder; rebuild with ./build.sh or copy the zone*.nav files in)"

  mkdir -p "$DAOC_HOME"/{config,data,logs,run}
  if [[ ! -f $DAOC_HOME/data/opendaoc.sqlite3.db ]]; then
    echo "First start: creating a fresh world in data/opendaoc.sqlite3.db"
    cp "$DAOC_HOME/defaults/world.sqlite" "$DAOC_HOME/data/opendaoc.sqlite3.db"
  fi
  for f in serverconfig.xml logconfig.xml invalidnames.txt MailConfig.xml; do
    [[ -e $DAOC_HOME/config/$f ]] || cp "$DAOC_HOME/defaults/$f" "$DAOC_HOME/config/"
  done
  # An existing world (data/) from an older build: bring its content up to this release, keeping the characters
  # (backup first; see README). A new world needs nothing: it is copied from defaults/ above.
  world_update || die "the world update failed; the server was not started (see above; the backup is in backups/)"
  ! port_in_use || die "port $(game_port) is already in use (another server is running; see <Port> in config/serverconfig.xml)"

  # Self-contained .NET still needs the system ICU library, and the server uses the en-US
  # culture, so invariant mode is not an option. (No grep -q here: with pipefail it makes
  # ldconfig fail on SIGPIPE.)
  if ! ldconfig -p 2>/dev/null | grep 'libicuuc\.so' >/dev/null; then
    die "the ICU library (libicu) is missing. Install it, or run this server inside a distrobox."
  fi

  # The admin socket (used by ./daoc-admin) lives in run/.
  export DAOC_ADMIN_SOCKET="$DAOC_HOME/run/admin.sock"
  cd "$DAOC_HOME/bin"

  if $console; then
    echo $$ > "$PIDFILE"
    # exec keeps this PID, so the pid file and signals reach the server itself.
    exec ./CoreServer --start "-config=$DAOC_HOME/config/serverconfig.xml"
  fi

  # Default: server in the background (its own session, so closing this terminal does not stop
  # it), then the admin screen in this terminal.
  local log="$DAOC_HOME/logs/console.out" pid i
  [[ -f $log ]] && mv -f "$log" "$DAOC_HOME/logs/console.prev.out"
  # >> (append) so the trimmer below can truncate the file while the server keeps writing.
  setsid ./CoreServer --start "-config=$DAOC_HOME/config/serverconfig.xml" </dev/null >>"$log" 2>&1 &
  pid=$!
  echo "$pid" > "$PIDFILE"
  # console.out is the server's raw console; keep it from growing forever (20 MB cap, checked every 5 min).
  setsid bash -c 'while kill -0 "$1" 2>/dev/null; do sleep 300; [[ $(stat -c %s "$2" 2>/dev/null || echo 0) -gt 20971520 ]] && : > "$2"; done' _ "$pid" "$log" </dev/null >/dev/null 2>&1 &
  printf 'Starting server (pid %s)' "$pid"
  for ((i = 0; i < 180; i++)); do
    if ! kill -0 "$pid" 2>/dev/null; then
      echo; echo "The server did not start. Last lines of $log:" >&2
      tail -n 25 "$log" >&2
      rm -f "$PIDFILE"
      exit 1
    fi
    [[ -S $DAOC_ADMIN_SOCKET ]] && grep -q "Server is now listening" "$log" && break
    printf '.'
    sleep 1
  done
  echo
  [[ $mode == --detach ]] && return 0
  exec "$DAOC_HOME/daoc-admin"
}

stop() {
  local pid i
  pid=$(running_pid) || { echo "not running"; return 0; }
  kill -TERM "$pid"
  for ((i = 0; i < 120; i++)); do
    if ! kill -0 "$pid" 2>/dev/null; then
      rm -f "$PIDFILE" "$DAOC_HOME/run/admin.sock"
      # daoc-divided-linux: a clean stop ends the log with "Stopped" (after the final save and the database checkpoint). Without it the
      # process was killed or crashed mid-shutdown: the database file is still consistent, but changes since the last autosave are lost.
      local log="$DAOC_HOME/logs/console.out"
      if [[ -s $log ]] && ! tail -n 60 "$log" | grep -q 'DOL.GS.GameServer | Stopped'; then
        echo "stopped, but the server did NOT finish saving (no 'Stopped' line in logs/console.out). Changes since its last autosave are lost; see the log." >&2
        return 1
      fi
      echo "stopped"; return 0
    fi
    sleep 1
  done
  die "server (pid $pid) did not stop within 120 s"
}

case "${1:-}" in
  start)  start "${2:-}" ;;
  stop)   stop ;;
  world-update)
    ! running_pid >/dev/null || die "stop the server first"
    [[ -f $DAOC_HOME/data/opendaoc.sqlite3.db ]] || die "no world yet (data/opendaoc.sqlite3.db); it is created on the first start"
    world_update ${2:+"$2"} ;;
  status) if pid=$(running_pid); then echo "running (pid $pid)"; else echo "stopped"; fi ;;
  *) echo "usage: $0 start [--console|--detach] | stop | status | world-update [--check]"; exit 2 ;;
esac
