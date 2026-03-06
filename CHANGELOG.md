# Changelog

All notable changes to this project will be documented in this file.

## [1.3.0] - 2026-03-06

### Added

- **Undo/redo system** — full command-pattern undo/redo for all editor actions: add node, delete node(s), move node, connect, disconnect, and config changes
- **UndoRedoManager** — linked-list-backed stack with 25-entry cap, `StateChanged` event, and `PushOrCoalesce` for keystroke coalescing
- **6 undoable commands** — `AddNodeCommand`, `RemoveNodesCommand`, `MoveNodeCommand`, `ConnectCommand`, `DisconnectCommand`, `ChangeConfigCommand`, plus `CompositeCommand` for batch operations
- **Keyboard shortcuts** — Ctrl+Z (undo) and Ctrl+Y (redo) wired in the editor
- **Real-time progress reporting** — `PipelineProgressEvent` discriminated union (`PhaseChanged`, `FilesDiscovered`, `FileProcessed`) with live UI and CLI updates
- **Progress phases** — Enumerating → Processing → Complete with file discovery count throttled every 100 files
- **CLI progress output** — live scanning count and per-file status with lock-based thread safety
- **Execution log cap** — output, error, and warning tabs capped at 5,000 entries to prevent memory growth on large pipelines
- **309 tests** — 66 new tests covering undo/redo manager, all 6 commands, progress reporting, execution log view model, and editor undo/redo integration

### Fixed

- **SemaphoreSlim disposal** — proper await of in-flight tasks before disposing semaphore under cancellation
- **Command loss on exception** — undo/redo executes the operation before modifying the stack, preserving commands on failure
- **IsConnected recalculation** — `RemoveNodesCommand` splits removal and recalculation into two passes for correct multi-connection handling
- **Selection state restoration** — `RemoveNodesCommand.Undo()` restores node selection state from before deletion
- **Properties panel sync** — undo/redo refreshes the properties panel via `StateChanged` subscription instead of per-command callbacks
- **Pipeline Complete event** — only reported on successful completion, not on cancellation or failure

## [1.2.0] - 2026-03-05

### Changed

- **Dependency injection** — full DI container (`Microsoft.Extensions.DependencyInjection`) across Core, UI, and CLI projects
- **Structured logging** — replaced static `Serilog.Log.Logger` with dependency-injected `ILogger<T>` via `Microsoft.Extensions.Logging`; Serilog remains as the provider behind the abstraction
- **Core DI registration** — new `AddFlowForgeCore()` extension method as single source of truth for service registration (NodeRegistry, PipelineRunner, AppSettingsManager)
- **Node logger injection** — all 11 nodes receive typed `ILogger<T>` via `ILoggerFactory` in `NodeRegistry.CreateDefault()`
- **Test logging** — migrated all tests from Serilog boilerplate to `NullLogger<T>.Instance`
- **App startup safety** — guarded `App.Services` property throws descriptive error on DI failure instead of NRE
- **Shutdown resilience** — disposal wrapped in try-catch with `Log.CloseAndFlush()` fallback
- **Constructor guards** — `ArgumentNullException.ThrowIfNull` on all node, runner, and view model constructors
- **Diagnostic logging** — `LogDebug` in Configure methods and `LogWarning` on error paths for all nodes
- **DI cleanup** — `IDialogService` and `IServiceProvider` injected via constructor instead of service locator
- **Captive dependency fix** — `EditorViewModel` registered as singleton to match its actual lifetime in singleton `MainWindowViewModel`
- **Error handling** — replaced bare `catch {}` in `AppSettingsManager.SaveAsync`, narrowed CLI catch scope for DI vs pipeline errors
- **243 tests** — 7 new DI registration tests verifying service resolution and lifetimes

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
