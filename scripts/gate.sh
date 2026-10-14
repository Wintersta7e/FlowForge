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
# declared ids; a full run fails unless the two sets are equal. A filtered run
# only counts what it wrote.
frame_dir="$PWD/TestResults/visual"
if [ -n "$filter" ]; then
    found=0
    if [ -d "$frame_dir" ]; then
        found=$(find "$frame_dir" -maxdepth 1 -type f -name '*.png' | grep -c . || true)
    fi
    echo "frames=$found"
else
    frames_rc=0
    frames_out=$(bash scripts/check-frames.sh "$frame_dir" 2>&1) || frames_rc=$?
    printf '%s\n' "$frames_out"
    if [ "$frames_rc" -ne 0 ]; then
        exit 1
    fi
    found=$(printf '%s\n' "$frames_out" | grep -o '^frames=[0-9]*' | grep -o '[0-9][0-9]*')
fi

echo "== 7 format"
dotnet format FlowForge.sln --verify-no-changes --no-restore

echo "== 8 result"
if [ -n "$filter" ]; then
    echo "PARTIAL filter=$filter sha=$sha dirty=$dirty tests=$passed/$total frames=$found"
else
    echo "GATE PASS sha=$sha dirty=$dirty tests=$passed/$total frames=$found dir=$frame_dir"
fi
