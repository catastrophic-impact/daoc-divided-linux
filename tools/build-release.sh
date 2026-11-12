#!/usr/bin/env bash
# build-release.sh - build the portable Linux server folder and tarball.
#
#   tools/build-release.sh            -> dist/daoc-divided/ and dist/daoc-divided-<version>-linux-x64.tar.gz
#
# Needs the .NET 10 SDK, cmake, g++, curl and python3 (the build box has them). The result needs none of them:
# it bundles the .NET runtime and a Python for the admin screen, and links the pathing library's C++ runtime
# statically. World DB, navmeshes and the quest data files are DividedRealms' own, read out of its release zip by
# tools/fetch-divided-release.py (cached in .cache/). No client files are included.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="$REPO/source/server"
DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
VERSION="${VERSION:-$(git -C "$REPO" describe --tags --always 2>/dev/null || echo dev)}"
RELEASE_TAG="${RELEASE_TAG:-DR-dev-0.1g}"   # DividedRealms release the world data comes from
DIST="${DIST:-$REPO/dist}"   # output folder; set DIST=/elsewhere to build without touching dist/ (for testing)
OUT="$DIST/daoc-divided"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

missing=()
[[ -x $DOTNET ]] && "$DOTNET" --list-sdks 2>/dev/null | grep -q '^10\.' || missing+=(".NET 10 SDK")
for c in cmake g++ curl python3; do command -v $c >/dev/null || missing+=("$c"); done
if ((${#missing[@]})); then
  echo "build-release: missing: ${missing[*]} (run tools/setup-build-box.sh on the host)" >&2
  exit 1
fi

[[ -f $SRC/CoreServer/config/serverconfig.xml ]] ||
  cp "$SRC/CoreServer/config/serverconfig.example.xml" "$SRC/CoreServer/config/serverconfig.xml"

echo "== World data from DividedRealms $RELEASE_TAG"
python3 -I "$REPO/tools/fetch-divided-release.py" "$RELEASE_TAG" "$REPO/.cache/divided"
DATA="$REPO/.cache/divided/$RELEASE_TAG"

# Rebuild the program files only. If someone plays straight from dist/daoc-divided, their world, characters and settings
# (data/, config/, logs/, run/, backups/, editor-data/) must survive a rebuild, and must never end up in the tarball.
KEEP=(data config logs run backups editor-data)
if [[ -d $OUT ]]; then
  for entry in "$OUT"/* "$OUT"/.[!.]*; do
    [[ -e $entry ]] || continue
    name=$(basename "$entry")
    [[ " ${KEEP[*]} " == *" $name "* ]] || rm -rf "$entry"
  done
fi
echo "== Publishing server (self-contained linux-x64)"
"$DOTNET" publish "$SRC/CoreServer/CoreServer.csproj" -c Release -r linux-x64 --self-contained true \
  -o "$OUT/bin" -nologo -v quiet -clp:ErrorsOnly

echo "== Publishing daoc-admin (command line)"
"$DOTNET" publish "$SRC/DaocAdmin/DaocAdmin.csproj" -c Release -r linux-x64 --self-contained true \
  -o "$OUT/bin" -nologo -v quiet -clp:ErrorsOnly

echo "== Building native pathing library"
cmake -S "$SRC/Pathing/Detour" -B "$SRC/Pathing/Detour/build" -DCMAKE_BUILD_TYPE=Release >/dev/null
cmake --build "$SRC/Pathing/Detour/build" >/dev/null
mkdir -p "$OUT/bin/lib"
# LibraryImport("lib/Detour") has a folder in its name, so .NET on Linux probes lib/Detour.so.
cp -L "$SRC/Pathing/Detour/build/libDetour.so" "$OUT/bin/lib/Detour.so"

# A post-build step copies translations into source/server/Release/ only; publish needs them too.
(cd "$SRC/GameServer/language" && find . -name '*.txt' -exec install -D -m 644 {} "$OUT/bin/languages/{}" \;)

# Quest/marker data DividedRealms ships next to the server program (the server runs without them, with markers off).
cp "$DATA"/server-data/*.json "$OUT/bin/"

echo "== World"
mkdir -p "$OUT/defaults"
cp "$DATA/world.sqlite" "$OUT/defaults/world.sqlite"
mkdir -p "$OUT/world-updates" && cp "$REPO"/source/tools/release-baselines/modern/world-updates/* "$OUT/world-updates/"   # the steps in git match the server code; the zip supplies the world and navmeshes

# The server reads config/ next to its program and navmesh/ from its working directory (bin/).
rm -rf "$OUT/bin/config"
ln -s ../config "$OUT/bin/config"
ln -s ../navmesh "$OUT/bin/navmesh"
ln -s ../logs "$OUT/bin/logs"

cp "$REPO/deploy/serverconfig.template.xml" "$OUT/defaults/serverconfig.xml"
cp "$SRC/GameServer/config/logconfig.xml" "$SRC/GameServer/config/invalidnames.txt" "$OUT/defaults/"
cp "$REPO/deploy/MailConfig.xml" "$OUT/defaults/"   # DividedRealms ships it (mail off); the server reads ./config/MailConfig.xml
cp "$REPO/deploy/daoc-server.sh" "$REPO/deploy/daoc-admin" "$REPO/deploy/release.conf" "$OUT/"
cp "$REPO/deploy/README-release.md" "$OUT/README.md"
echo "$VERSION (DividedRealms $RELEASE_TAG)" > "$OUT/VERSION"

echo "== Admin screen (bundled Python + Textual)"
"$REPO/tools/fetch-python.sh" "$OUT/python"
mkdir -p "$OUT/tui"
cp "$REPO/tui/daoc_admin_tui.py" "$REPO/tui/client_tools.py" "$REPO/tui/client_patch.py" "$OUT/tui/"   # client_patch.py: daoc-divided-linux, the full client patch
mkdir -p "$OUT/tui/classslots" "$OUT/tools"
cp "$REPO/tools/world_update.py" "$OUT/tools/"
cp "$SRC/../tools/classslots/patch_class_slots.py" "$OUT/tui/classslots/"
cp "$DATA/tools/connect-local1130.exe" "$OUT/tools/"
cp "$DATA/client-patch.json" "$OUT/tools/"   # DividedRealms' client patch (about 19 MB, no retail file in it); the Client tab applies it
cp "$REPO/deploy/zones-classic.mpk" "$OUT/tools/"   # the classic zone table the Battleground fix puts in the client

echo "== World Workshop editor (runs on the bundled Python)"
mkdir -p "$OUT/editor/source/tools" "$OUT/editor/source/new-client"
cp -r "$SRC/../tools/world_editor" "$OUT/editor/source/tools/"
cp "$SRC/../tools/modern_world_overlay.py" "$OUT/editor/source/tools/"
cp -r "$SRC/../new-client/pipeline" "$OUT/editor/source/new-client/"   # the editor's client-reading modules (daoc_mpk, NIF readers...)
mkdir -p "$OUT/editor/tools" "$OUT/editor/source/server/GameServer/craft"
cp -r "$REPO/tools/asset-tool" "$OUT/editor/tools/"
cp -r "$SRC/GameServer/Enums" "$OUT/editor/source/server/GameServer/"            # item-column names the editor reads
cp "$SRC/GameServer/craft/CraftingMgr.cs" "$OUT/editor/source/server/GameServer/craft/"
rm -rf "$OUT/editor/source/tools/world_editor"/{build_package.py,patch_launcher_button.py,test_editor.py,__pycache__}
cp "$REPO/deploy/world-editor.sh" "$OUT/"

echo "== Navmeshes"
mkdir -p "$OUT/navmesh"
cp -r "$DATA"/navmesh/. "$OUT/navmesh/"

tarball="$DIST/daoc-divided-$VERSION-linux-x64.tar.gz"
tar -C "$DIST" "${KEEP[@]/#/--exclude=daoc-divided/}" -czf "$tarball" daoc-divided
# Keep only the newest tarball: each build is ~1 GB and they pile up in dist/.
find "$DIST" -maxdepth 1 -name 'daoc-divided-*-linux-x64.tar.gz' ! -path "$tarball" -delete
echo "== Done: $OUT"
echo "         $tarball ($(du -h "$tarball" | cut -f1))"
