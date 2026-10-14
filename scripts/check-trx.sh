#!/usr/bin/env bash
# Checks the one TRX file a test run wrote: prints its counters, then fails
# when the run executed no test, skipped or failed any test, or (with
# --require-prefix) executed no test whose class starts with the prefix.
set -euo pipefail

usage() {
    echo "usage: scripts/check-trx.sh <results-dir> [--require-prefix <class-prefix>]" >&2
    exit 2
}

[ $# -ge 1 ] || usage
dir="$1"
shift
prefix=""
if [ $# -gt 0 ]; then
    if [ "$1" = "--require-prefix" ] && [ $# -eq 2 ] && [ -n "$2" ]; then
        prefix="$2"
    else
        usage
    fi
fi

trx_files=()
if [ -d "$dir" ]; then
    while IFS= read -r f; do trx_files+=("$f"); done < <(find "$dir" -type f -name '*.trx')
fi
echo "trx_files=${#trx_files[@]} dir=$dir"
if [ "${#trx_files[@]}" -ne 1 ]; then
    echo "FAIL: expected exactly one .trx under $dir" >&2
    exit 1
fi
trx="${trx_files[0]}"

counters=$(tr -d '\r' <"$trx" | grep -o '<Counters [^>]*>' || true)
attr() { printf '%s\n' "$counters" | grep -o " $1=\"[0-9]*\"" | grep -o '[0-9][0-9]*' || true; }
total=$(attr total)
executed=$(attr executed)
passed=$(attr passed)
failed=$(attr failed)
not_executed=$(attr notExecuted)
if [ -z "$total" ] || [ -z "$executed" ] || [ -z "$passed" ] || [ -z "$failed" ] || [ -z "$not_executed" ]; then
    echo "FAIL: no readable <Counters> in $trx" >&2
    exit 1
fi
echo "total=$total executed=$executed passed=$passed failed=$failed notExecuted=$not_executed"
if [ "$total" -eq 0 ]; then
    echo "FAIL: the run executed no test (total=0)" >&2
    exit 1
fi
if [ "$passed" -ne "$total" ]; then
    echo "FAIL: passed=$passed of total=$total (failed, errored or skipped tests)" >&2
    exit 1
fi

if [ -n "$prefix" ]; then
    matched=$(tr -d '\r' <"$trx" | awk -v prefix="$prefix" '
        function val(line, name,    m) {
            if (match(line, " " name "=\"[^\"]*\"")) {
                m = substr(line, RSTART + length(name) + 3, RLENGTH - length(name) - 4)
                return m
            }
            return ""
        }
        /<UnitTestResult / { id = val($0, "testId"); if (val($0, "outcome") != "NotExecuted") ran[id] = 1 }
        /<UnitTest /       { current = val($0, "id") }
        /<TestMethod /     { if (index(val($0, "className"), prefix) == 1) cls[current] = 1 }
        END { n = 0; for (id in cls) if (id in ran) n++; print n }')
    echo "prefix=$prefix executed=$matched"
    if [ "$matched" -eq 0 ]; then
        echo "FAIL: no executed test class starts with $prefix" >&2
        exit 1
    fi
fi
