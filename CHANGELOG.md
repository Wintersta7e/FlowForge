# Changelog

All notable changes to this project will be documented in this file.

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
