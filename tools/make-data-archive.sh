#!/usr/bin/env bash
# make-data-archive.sh [tag] [out-dir] - pack the world data the build needs into one tar.xz, for a GitHub release of this repo.
#
#   tools/make-data-archive.sh DR-dev-0.1g          -> .cache/data-archive/divided-data-DR-dev-0.1g.tar.xz
#
# Input is the cache that tools/fetch-divided-release.py fills from DividedRealms' release (.cache/divided/<tag>/).
# Only what build-release.sh uses goes in: world.sqlite, navmesh/, server-data/, tools/connect-local1130.exe, client-patch.json.
# No client files. Upload the result as an asset of a release tagged divided-data-<tag> on this repo; fetch-divided-release.py
# tries that first, so a new machine does not depend on DividedRealms keeping its old release online.
# GitHub allows 2 GB per release asset: the script stops if the archive is bigger.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TAG="${1:-DR-dev-0.1g}"
OUTDIR="${2:-$REPO/.cache/data-archive}"
SRC="$REPO/.cache/divided/$TAG"
OUT="$OUTDIR/divided-data-$TAG.tar.xz"
LIMIT=$((2 * 1024 * 1024 * 1024 - 1))

ITEMS=(world.sqlite navmesh server-data tools/connect-local1130.exe client-patch.json)
for i in "${ITEMS[@]}"; do
  [[ -e $SRC/$i ]] || { echo "make-data-archive: $SRC/$i is missing (run tools/fetch-divided-release.py $TAG first)" >&2; exit 1; }
done

mkdir -p "$OUTDIR"
echo "== Packing $TAG ($(du -sh --apparent-size "$SRC" | cut -f1) before compression) -> $OUT"
tar -C "$SRC" -cf - "${ITEMS[@]}" | xz -T0 -6 > "$OUT.part"
mv "$OUT.part" "$OUT"
size=$(stat -c %s "$OUT")
echo "== $(du -h "$OUT" | cut -f1) written"
if ((size > LIMIT)); then
  echo "make-data-archive: the archive is over GitHub's 2 GB asset limit; it has to be split before upload" >&2
  exit 2
fi
