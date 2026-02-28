# Changelog

All notable changes to this project will be documented in this file.

## [1.1.0] - 2026-02-28

### Added

- **File browser dialogs** — native OS open/save dialogs for pipeline files
- **Recent pipelines menu** — MRU list persisted across sessions with clear option
- **Backup before overwrite** — FolderOutput can create `.bak` (or custom suffix) backups of destination files before overwriting
- **Zoom-to-fit** — toolbar button to fit the entire graph into the viewport
- **Keyboard shortcuts help** — dialog showing all available keyboard shortcuts
- **JSON CLI output** — `--format json` flag on CLI runner for machine-readable output
- **Config field tooltips** — hover descriptions on all node configuration fields
- **Sample pipelines** — 4 ready-to-run `.ffpipe` files in `samples/` directory
- **236 tests** — expanded coverage for new features and edge cases

### Fixed

- **Backup suffix validation** — reject lone `"."` suffix that causes data loss on NTFS (trailing dots stripped)
- **Stale recent path removal** — case-insensitive matching and persist removal to settings file
- **Serilog stdout contamination** — route log output to stderr in `--format json` mode
- **Init race condition** — gate settings writes until initial load completes
- **MenuItem event handler leaks** — unsubscribe Click handlers before rebuilding recent menu
- **Business logic in code-behind** — moved `Path.GetFileName` from ToolbarView into ViewModel
- **Explicit CancellationToken** — pass `CancellationToken.None` intentionally on UI-initiated loads
- **Silent test pass** — sample pipeline tests now log skips and guard against false-green in CI
- **Event handler leaks** — prevent accumulation in CanvasView and ToolbarView on DataContext changes
- **Path traversal** — block `backupSuffix` values containing path separators or traversal sequences
- **AppSettings validation** — input validation and safe defaults for all settings
- **MetadataExtract** — accept string keys, remove async void, preserve graph name on load
- **Dead code removal** — cleaned up unused code across the codebase

### Changed

- Microsoft.NET.Test.Sdk 18.0.1 → 18.3.0

## [1.0.0] - 2026-02-20

### Added

- **Visual Node Editor** — drag-and-drop canvas with pan, zoom, wire connections, and rubber-band selection (Nodify.Avalonia)
- **11 built-in nodes**:
  - Input: Folder Input (recursive, glob patterns)
  - Process: Rename Pattern, Rename Regex, Rename Add Affix, Filter, Sort, Image Resize, Image Convert, Image Compress, Metadata Extract
  - Output: Folder Output (copy/move with timestamp preservation)
- **Pipeline engine** — topological sort execution, async concurrency, dry-run mode, cancellation support
- **Pipeline serialization** — `.ffpipe` JSON format with atomic writes
- **Node library sidebar** — categorized (Input/Process/Save To) with search
- **Properties panel** — auto-generated config forms from node schemas (text, number, boolean, file/folder picker, dropdown)
- **VS-style output panel** — tabbed Output/Errors/Warnings with badge counts, resizable via GridSplitter
- **Pipeline templates** — one-click starters: Photo Import, Batch Rename, Web Export, Compress
- **Midnight theme** — custom dark theme with GitHub Dark-inspired color palette
- **CLI runner** — `flowforge run <pipeline.ffpipe>` with `--input`, `--output`, `--dry-run`, `--verbose` flags
- **App settings** — cross-platform JSON persistence with sensible defaults
- **Structured logging** — Serilog with file and console sinks
- **202 tests** — full coverage across all nodes, runner, serializer, registry, templates, settings
- **Static analysis** — StyleCop + TreatWarningsAsErrors + EditorConfig enforced
- **CI/CD** — GitHub Actions for build/test/format, release automation, Dependabot
