#!/usr/bin/env bash
# The local pre-commit gate: restore with the audit flags CI uses, build, run
# every test, verify formatting. Fails closed at the first broken step and
# prints the counts it saw before failing. A filtered run never prints PASS.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

usage() {
    echo "usage: scripts/gate.sh [--filter <expression>]" >&2
    exit 2
}

filter=""
if [ $# -gt 0 ]; then
    if [ "$1" = "--filter" ] && [ $# -eq 2 ] && [ -n "$2" ]; then
        filter="$2"
    else
        usage
    fi
fi

echo "== 1 state"
sha=$(git rev-parse HEAD)
dirty=$(git status --porcelain | wc -l | tr -d ' ')
echo "sha=$sha dirty=$dirty"

echo "== 2 restore (audit flags pinned)"
dotnet restore -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=low -p:TreatWarningsAsErrors=true

echo "== 3 build"
dotnet build FlowForge.sln -c Release --no-restore

echo "== 4 clear previous results"
rm -rf TestResults/visual-prev
if [ -d TestResults/visual ]; then
    mv TestResults/visual TestResults/visual-prev
fi
rm -rf TestResults/gate

echo "== 5 test"
test_args=(test FlowForge.sln -c Release --no-build --logger "trx;LogFileName=gate.trx" --results-directory TestResults/gate)
if [ -n "$filter" ]; then
    test_args+=(--filter "$filter")
fi
test_rc=0
dotnet "${test_args[@]}" || test_rc=$?
# A full run must also have executed the visual tests; a filtered run of
# other tests legitimately runs none.
trx_args=(TestResults/gate)
if [ -z "$filter" ]; then
    trx_args+=(--require-prefix FlowForge.Tests.Visual.)
fi
trx_rc=0
trx_out=$(bash scripts/check-trx.sh "${trx_args[@]}" 2>&1) || trx_rc=$?
printf '%s\n' "$trx_out"
if [ "$test_rc" -ne 0 ]; then
    echo "FAIL: dotnet test exited $test_rc" >&2
    exit 1
fi
if [ "$trx_rc" -ne 0 ]; then
    exit 1
fi
passed=$(printf '%s\n' "$trx_out" | grep -o ' passed=[0-9]*' | grep -o '[0-9][0-9]*')
total=$(printf '%s\n' "$trx_out" | grep -o '^total=[0-9]*' | grep -o '[0-9][0-9]*')

echo "== 6 frames"
# The visual tests write one PNG per declared frame plus the manifest of
# declared ids; a full run fails unless the two sets are equal.
frame_dir="$PWD/TestResults/visual"
manifest="$frame_dir/expected-frames.txt"
found_ids=""
if [ -d "$frame_dir" ]; then
    found_ids=$(find "$frame_dir" -maxdepth 1 -type f -name '*.png' -exec basename {} .png \; | LC_ALL=C sort)
fi
found=$(printf '%s\n' "$found_ids" | grep -c . || true)
if [ -n "$filter" ]; then
    echo "frames=$found"
else
    if [ ! -f "$manifest" ]; then
        echo "frames=$found"
        echo "FAIL: no frame manifest at $manifest" >&2
        exit 1
    fi
    expected_ids=$(LC_ALL=C sort "$manifest")
    expected=$(printf '%s\n' "$expected_ids" | grep -c . || true)
    echo "frames=$found/$expected"
    if [ "$expected" -eq 0 ]; then
        echo "FAIL: the frame manifest lists no frames: $manifest" >&2
        exit 1
    fi
    missing=$(LC_ALL=C comm -23 <(printf '%s\n' "$expected_ids") <(printf '%s\n' "$found_ids") | grep . || true)
    extra=$(LC_ALL=C comm -13 <(printf '%s\n' "$expected_ids") <(printf '%s\n' "$found_ids") | grep . || true)
    if [ -n "$missing" ]; then
        printf '%s\n' "$missing" | sed 's/^/missing /'
    fi
    if [ -n "$extra" ]; then
        printf '%s\n' "$extra" | sed 's/^/extra /'
    fi
    if [ -n "$missing" ] || [ -n "$extra" ]; then
        echo "FAIL: written frames differ from the manifest" >&2
        exit 1
    fi
fi

echo "== 7 format"
dotnet format FlowForge.sln --verify-no-changes --no-restore

echo "== 8 result"
if [ -n "$filter" ]; then
    echo "PARTIAL filter=$filter sha=$sha dirty=$dirty tests=$passed/$total frames=$found"
else
    echo "GATE PASS sha=$sha dirty=$dirty tests=$passed/$total frames=$found dir=$frame_dir"
fi
