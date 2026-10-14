# scripts
- `check-trx.sh <results-dir> [--require-prefix <class-prefix>]` — fails unless exactly one TRX exists, at least one test ran and every test passed; the gate and both CI test jobs call it.
- `gate.sh [--filter <expression>]` — the local gate: restore with CI's pinned audit flags, Release build, tests with the TRX counter check, format check; prints `GATE PASS …` or fails closed. A filtered run prints `PARTIAL …` and never satisfies a commit's gate.
