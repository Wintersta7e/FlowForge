# Changelog

All notable changes to this project will be documented in this file.

## [1.6.0] - 2026-04-02

### Fixed

- **Node connections broken since v1.4.0** — dragging a connection wire from any connector was offset and unusable; caused by `RotateTransform(45°)` applied directly to the Nodify Connector control, which rotated Nodify's internal coordinate calculations; moved rotation into a custom ControlTemplate Border so the diamond shape is preserved without affecting drag math
- **Atomic writes for image nodes** — ImageConvertNode and ImageResizeNode now use temp-file-then-rename pattern, preventing data loss on I/O errors or cancellation (previously wrote directly to target/original path)
- **Path traversal hardening** — PathGuard now validates backup paths in FolderOutputNode; RenameRegexNode fullpath scope uses PathGuard instead of hand-rolled check; FolderInputNode filters out symlinked files outside source root; CLI validates --input/--output directories exist
- **FilterNode thread safety** — removed mutable `_dryRun` instance field; parameter is now passed directly to helper methods
- **Semaphore disposal race** — PipelineRunner now fully drains in-flight tasks before disposing the semaphore
- **Buffered transform double-counting** — SortNode jobs were counted as both Skipped and Failed when flush failed; runner now skips early disposition for IBufferedTransformNode
- **ImageResizeNode missing ErrorMessage** — file-too-large failure now sets `job.ErrorMessage` (was null in UI)
- **FilterNode inconsistent date defaults** — missing-file paths now return `DateTime.MinValue` consistently (was `string.Empty` for live runs)
- **SortNode stale buffer** — buffer is cleared in Configure to prevent cancelled-run jobs bleeding into next run
- **NodeLibrary theme refresh** — brushes now update on theme toggle; search filter reapplied after refresh
- **Runner log diagnostics** — failure log messages now include `job.ErrorMessage`

### Changed

- **Full codebase audit** — 2-round review across 6 dimensions (security, leaks, performance, architecture, bugs, coverage) with 45+ issues resolved
- **Performance** — MetadataExtractNode reads EXIF once per file (was once per key); FilterNode uses single FileInfo per file; SortByKey uses index-based sorting; HashSet for SupportedFormats and InvalidFileNameChars lookups; static ValidFormats in ImageConvertNode
- **RecentPipelines validation** — AppSettings.Validate() now filters entries with null bytes, excessive length, relative paths, or whitespace
- **342 tests** — 20 new tests for FilterNode operators, date fields, RenamePattern conflict resolution, SortNode date sorting, RenameRegex fullpath I/O, null deserialization, RecentPipelines validation; cross-platform temp paths replacing hardcoded `/tmp/`; removed 2 redundant tests; strengthened assertions on FolderOutput content and SortNode ordering
- CommunityToolkit.Mvvm 8.4.1 → 8.4.2

## [1.5.0] - 2026-03-26

### Added

- **Meziantou.Analyzer** — added as a second analyzer alongside StyleCop for string correctness, regex safety, collection abstraction, and method complexity checks

### Changed

- **Stricter editorconfig** — re-enabled SA1503 (braces required), SA1402 (one type per file), SA1649 (filename matches type), SA1518 (trailing newline); upgraded `var` and accessibility modifier rules from suggestion to warning; added IDE0005 (unused usings) and CA1001 (IDisposable) enforcement
- **CA2007 enforcement in Core** — ConfigureAwait(false) now enforced by analyzer for all Core async methods
- **Per-project editorconfigs** — Core enforces CA2007, UI suppresses MA0004/CA2007 (needs sync context), CLI suppresses MA0047 (top-level statements), Tests relax MA0002/MA0005
- **One type per file** — split 12 types into separate files across Core (NodeCategory, ExecutionPhase, PhaseChanged, FilesDiscovered, FileProcessed, FileJobStatus, ConfigFieldType, NodeDefinition, Connection, CanvasPosition, PipelineTemplate) and UI (RecentPipelineItem)
- **Collection abstractions** — interface and model return types changed from `List<T>`/`Dictionary<K,V>` to `IReadOnlyList<T>`/`IList<T>`/`IDictionary<K,V>` throughout Core and consuming projects
- **String correctness** — added `StringComparer.Ordinal` to all dictionary/hashset constructors, `string.Equals` with `StringComparison` for all string comparisons, `CultureInfo.InvariantCulture` for all `TryParse` calls
- **Regex safety** — added `RegexOptions.ExplicitCapture` to FilterNode and RenamePatternNode regexes; added timeout to RenamePatternNode
- **Method complexity** — extracted helpers from PipelineRunner (6 methods) and CLI Program (7 methods) to stay under 80-line threshold
- **CLI structure** — refactored monolithic 228-line handler into focused methods: ConfigureLogging, LoadAndConfigurePipelineAsync, ApplyNodeOverride, CreateProgressReporter, PrintFileResult, PrintSummary, ToExitCode
- MetadataExtractor 2.9.0 → 2.9.2, FluentAssertions 8.8.0 → 8.9.0, coverlet.collector 8.0.0 → 8.0.1, CommunityToolkit.Mvvm 8.4.0 → 8.4.1

## [1.4.1] - 2026-03-14

### Fixed

- **Critical crash on bool config fields** — ToggleSwitch crashes with `PART_MovingKnobs` KeyNotFoundException when Molten Forge theme is active; replaced with CheckBox
- **Memory leaks** — PipelineNodeViewModel and PipelineConnectorViewModel event subscriptions to `ActualThemeVariantChanged` and `PropertyChanged` were never unsubscribed, preventing GC; added `Detach()` cleanup on node removal
- **Path traversal** — crafted filenames could escape output directories via RenamePatternNode, RenameAddAffixNode, RenameRegexNode (filename mode), and FolderOutputNode; added `PathGuard.EnsureWithinDirectory()` checks
- **ReDoS vulnerability** — RenameRegexNode compiled user-supplied regex with no timeout; added 2-second timeout matching FilterNode
- **Dry-run file I/O** — MetadataExtractNode, FilterNode, and SortNode performed disk reads during dry-run; now return defaults without file access
- **ImageCompressNode data loss** — overwrote original file in-place; now saves to temp file and swaps atomically
- **Output node error isolation** — first output node failure no longer prevents subsequent outputs from running
- **Silently dropped jobs** — transforms returning empty with Processing status are now counted as skipped
- **CTS race condition** — `Cancel()` and `Dispose()` on `_cts` could race; fixed with `Interlocked.Exchange`
- **Predictable temp files** — PipelineSerializer and AppSettingsManager used `.tmp` suffix; now use random GUID suffix
- **Serializer TOCTOU** — removed redundant `File.Exists` check before `ReadAllTextAsync`
- **Sync File.Exists on UI thread** — removed blocking call from `OpenRecentAsync`

### Added

- **Path traversal protection** — `PathGuard.EnsureWithinDirectory()` helper used by all rename nodes and FolderOutputNode
- **Decompression bomb guard** — 500 MB file size check and `MaxFrames = 1` decoder option on all image nodes
- **ImageResize dimension bounds** — width/height validated to 1-32768 in `Configure()`
- **Crash log handler** — unhandled exceptions write to `crash.log` in app directory
- **13 new tests** — cancellation, dry-run, path traversal, ReDoS timeout, bounds validation, serializer edge cases (322 total)

### Changed

- **ConfigureAwait(false)** — added to all Core async methods to avoid unnecessary UI thread marshaling
- **SortNode performance** — pre-compute sort keys to eliminate O(n log n) filesystem calls
- **FileJob property caching** — `Extension`, `FileName`, `DirectoryName` cached with lazy invalidation
- **Streaming serialization** — PipelineSerializer and AppSettingsManager stream to FileStream instead of string buffer
- **Execution log batching** — buffer FileProcessed events with 50ms DispatcherTimer flush to reduce UI layout thrashing
- **FilterNode normalization** — operator/field strings lowercased at configure time instead of per-file
- **ImageConvertNode encoder caching** — encoder created once in `Configure()` instead of per file
- **DRY refactoring** — extracted `ThemeHelper`, `NodeIconMap`, shared `ConfigHelper` test helper
- **Named event handlers** — anonymous lambdas replaced with named methods in EditorViewModel and MainWindowViewModel
- **NodeLibrary filtering** — reuses group VMs with `ApplyFilter()` instead of recreating per keystroke
- Microsoft.Extensions.* 10.0.3 → 10.0.5, System.CommandLine 2.0.3 → 2.0.5

## [1.4.0] - 2026-03-07

### Added

- **Molten Forge theme** — complete visual overhaul with warm amber accent, category-colored nodes (blue/green/amber), gradient tinted backgrounds, and diamond-shaped connectors
- **Light/dark theme toggle** — toolbar button to switch between dark and light variants at runtime; nodes, connectors, and all panels update dynamically
- **Custom node template** — rounded corners, emoji icons, config preview text, and category-colored headers replacing Nodify built-in node control
- **Node library icons** — colored icon boxes with category headers in the sidebar
- **Properties badge** — category-colored badge on the properties panel header

### Changed

- Theme resource keys renamed from `Midnight*` to `Forge*` across all views
- Node brushes rebuild dynamically on theme change via `ActualThemeVariantChanged`
- Hardcoded dark-mode colors replaced with theme resource lookups throughout XAML
- Updated screenshots for Molten Forge theme (editor-overview, node-pipeline, node-library, properties-panel)

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
