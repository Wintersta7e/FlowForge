#!/usr/bin/env bash
# Checks the frames a test run rendered against the manifest of declared ids
# the run wrote beside them: prints the counts, then fails when the manifest is
# missing or empty, or when a declared frame was not written or an undeclared
# one was.
set -euo pipefail

usage() {
    echo "usage: scripts/check-frames.sh <frame-dir>" >&2
    exit 2
}

[ $# -eq 1 ] && [ -n "$1" ] || usage
dir="$1"
manifest="$dir/expected-frames.txt"

found_ids=""
if [ -d "$dir" ]; then
    found_ids=$(find "$dir" -maxdepth 1 -type f -name '*.png' -printf '%f\n' | awk '{ sub(/\.png$/, ""); print }' | LC_ALL=C sort)
fi
found=$(printf '%s\n' "$found_ids" | grep -c . || true)
if [ ! -f "$manifest" ]; then
    echo "frames=$found"
    echo "FAIL: no frame manifest at $manifest" >&2
    exit 1
fi
expected_ids=$(tr -d '\r' <"$manifest" | LC_ALL=C sort)
expected=$(printf '%s\n' "$expected_ids" | grep -c . || true)
echo "frames=$found/$expected"
if [ "$expected" -eq 0 ]; then
    echo "FAIL: the frame manifest lists no frames: $manifest" >&2
    exit 1
fi
missing=$(LC_ALL=C comm -23 <(printf '%s\n' "$expected_ids") <(printf '%s\n' "$found_ids") | grep . || true)
extra=$(LC_ALL=C comm -13 <(printf '%s\n' "$expected_ids") <(printf '%s\n' "$found_ids") | grep . || true)
if [ -n "$missing" ]; then
    printf '%s\n' "$missing" | awk '{ print "missing " $0 }'
fi
if [ -n "$extra" ]; then
    printf '%s\n' "$extra" | awk '{ print "extra " $0 }'
fi
if [ -n "$missing" ] || [ -n "$extra" ]; then
    echo "FAIL: written frames differ from the manifest" >&2
    exit 1
fi
