# Changelog

All notable changes to this project will be documented in this file.

Versions 1.0.0–2.0.0 were tagged and released at the time; those tags and releases no longer exist, so these sections are a record only.

## [Unreleased]

### Changed

- **Breaking:** **Pipeline shape is checked before a run** — a pipeline that is not one chain from a single source to a single output is refused, naming each offending node; before, an unconnected transform still ran on every file and every output received every file. A connection into a source or out of an output, and a node on no path from a source to an output, are refused with a message naming that connection or node; every unknown node type and every connection to a missing node is listed, one per line, where only the first was reported.
- **CLI executable name** — the CLI builds and publishes as `flowforge` (`flowforge.exe` on Windows), as the docs say; it was `FlowForge.CLI`. `dotnet run --project src/FlowForge.CLI` is unchanged.
- **Desktop app log files** — the desktop app writes its `logs` folder beside the executable instead of into the current working directory, and starts a new numbered file when a log reaches 10 MB instead of logging nothing more that day. The user guide names the dated files it writes.
- **README and user guide** — the README is restyled; no download or release-zip text until a release exists; supported platforms narrowed to Windows and Linux (macOS untested); dry-run wording matches what the code guarantees; the test count and the ".ffpipe stable across 2.x" sentence are gone. The guide names the .NET 10 SDK and shows how to build the `flowforge` command (`dotnet run --project src/FlowForge.CLI` or `dotnet publish src/FlowForge.CLI`), and its node tables gain an Example column. The no-network wording is narrowed to network calls of FlowForge's own; a folder on a network share is reached through the operating system like any other path.
- **Dependencies** — the desktop app's four Avalonia packages move to 11.3.20 in one change and the test project adds Avalonia.Headless.XUnit 11.3.20, so all five Avalonia packages share one version; MetadataExtractor 2.9.3, Microsoft.Extensions.* 10.0.13, Serilog 4.4.0, System.CommandLine 2.0.10 and Microsoft.NET.Test.Sdk 18.8.1. Meziantou.Analyzer moves to 3.x and the issues it flagged are fixed, among them a filter-node test that never awaited its assertion and so always passed. The Tmds.DBus.Protocol advisory suppression is dropped because Avalonia now resolves a patched version. The unused Bogus test dependency and the unused Avalonia.Diagnostics package are removed.
- **CI** — format, build-and-test, a Windows build-and-test leg and a dependency audit run as parallel jobs, with NuGet caching and per-job timeouts; CI also runs weekly and on demand, and a scheduled or manual run no longer cancels a push run on the same branch. Both test jobs fail when no test ran, when no UI test ran, or when the frames the headless UI tests rendered do not match the list of declared frames (a declared frame was not rendered, or an undeclared one was). The audit flags are passed on the command line, so NuGetAudit properties in the build files cannot switch the audit off; the audit fails when it scanned no project or when an advisory suppression matches no reported advisory. The CI and release workflows use the current major versions of the checkout, .NET setup and cache actions; the old pins targeted a runtime the CI runners now deprecate, with a notice on every run.
- **Stations lift on hover** — the station under the pointer rises by one pixel and settles back when the pointer leaves, so it is clear which station a click or drag will take.
- **The console shows DEMO mode** — while DEMO animates the canvas without a run, the console reads DEMO and "// demo — no files touched" instead of COLD and the idle caption, and shows no process id, since no run is in progress.
- **One progress bar** — the strip above the log no longer repeats the console's file count under a second, always-moving bar. It names what the run is doing: the scan count while files are found, `Stage n of m · title done/total` while a transform step works through its files (its place among the run's steps, its name, and how many of its files it has taken), then finished files over files found with the latest file's name.
- **Log rows show their time and node log** — each row on the Output, Errors and Skipped tabs leads with the time it was logged, and hovering a row lists what each node did to that file.
- **The Output tab lists the files that succeeded** — files a filter drops and files that fail are listed only on their own tabs, and the Warnings tab is now called Skipped, as the run summary and each row call those files. Dropped and failed files used to fill the Output tab ahead of every file the run produced, and after 5,000 of them no produced file appeared there at all.
- **The editor marks what the pipeline check reports** — a station the check before a run would report (one on no path from a source to an output, or of an unknown type) shows a `!` beside its status light, and a connection it reports (the same connection twice in a loaded file) is drawn dashed; hovering either shows the check's message. The marks follow every edit, undo and redo, so a problem shows before a run is refused.
- **The toolbar names the open file** — the toolbar shows the open file's folder and name, trimmed to fit with the full path on hover, and `untitled.ffpipe` for a pipeline not yet saved; it showed `/pipelines/` and the window title. The version beside the logo is the build's major version instead of a fixed number.
- **Unsaved changes are protected** — the window title and the toolbar's file name end in ` *` while the pipeline differs from its last save, and undoing back to the save clears the mark. New, Open, Open recent, a template card and closing the window first ask whether to save, discard or cancel; if the save is cancelled or fails, nothing is replaced. All five used to drop unsaved work without asking.
- **Keys to preview, run and stop** — F5 starts a dry run, Ctrl+Shift+Enter starts the live run (never a single key, since a run changes files) and Esc stops a run in progress; with no run in progress Esc does what it did before. All three are listed in the shortcuts dialog, IGNITE's tooltip names its chord, and the dialog grows to fit its rows instead of cutting off its Close button.

### Removed

- **Concept-render screenshots** — `screenshots/empty-state.png` and `screenshots/stations-running.png` were design renders, not app screenshots, and nothing referenced them.
- **Unused settings** — the unused `maxConcurrency`, `defaultInputFolder` and `defaultOutputFolder` settings; an existing settings file still loads, and the next save drops them.

### Fixed

- **Photo Import template runs as shipped** — its metadata step was saved without the EXIF date key, so every run stopped with "MetadataExtract: 'keys' is required."
- **Folder input skips links** — symbolic links and junctions are no longer yielded and linked folders are never scanned; an unreadable subfolder is skipped instead of triggering a top-level rescan; files whose names differ only by letter case are both found on case-sensitive file systems.
- **Correction to 1.6.0** — "FolderInputNode filters out symlinked files outside source root" held only for links whose path text pointed outside the root; links inside a recursive scan were followed. Fixed in this release.
- **Image conversion keeps other files** — converting `photo.png` to JPEG no longer overwrites and deletes an existing `photo.jpg`; that file fails with a reason instead. A change of extension case only (`photo.JPG` → `.jpg`) keeps the converted file.
- **Image compression no longer mislabels files** — a format override that differs from the file's extension now fails the file instead of writing, for example, JPEG bytes into a `.png`.
- **Case-only renames** — a rename that changes only letter case no longer delivers a different file's contents on case-sensitive file systems, and a pattern that reproduces the same name no longer renames `a.txt` to `a_1.txt`.
- **Atomic folder output** — each file is copied to a temporary name and renamed into place, so a cancelled or failed copy no longer truncates an existing destination; backups are an atomic swap; a destination that is a link is refused; `move` deletes the source only after the copy is in place, and never when source and destination are the same file.
- **Output folders with links** — folder output refuses to deliver through a linked folder below the destination, and preserve-structure no longer treats a sibling folder whose name merely starts with the base folder's name as inside it.
- **CLI exit codes** — the CLI now exits 0 on success, 1 when some files failed and 2 on failure or invalid arguments, as documented. Every run used to exit 0 and argument errors exited 1.
- **CLI progress order** — the scan count now always prints before "Processing N files…"; the two lines could appear out of order.
- **User guide JSON example** — lists the keys the CLI actually writes (`isDryRun`, `duration`, `jobs`); it showed `durationMs` and `files`.
- **One Serilog version** — the CLI references Serilog 4.4.0 directly, the version the desktop app uses, so both can share one folder.
- **User guide node reference** — field tables are generated from each node's schema; several listed fields the nodes never had (Filter, Sort, Folder Input).
- **Templates and samples docs** — the template table is generated from the template library (the Photo Import pattern was wrong); menu and button names match the toolbar (Open, Recent, DRY RUN, IGNITE); samples are no longer called ready-to-run while their nodes open stacked.
- **Third-party notices** — the font licence is described where it ships (`Assets/Fonts/OFL.txt` in the published folder), not in a release zip that did not exist.
- **Output failures show their reason** — when an output node fails, its message now becomes the file's error, so the CLI prints an error line for the file and the UI's error tab lists it; both were empty.
- **Every file reaches progress and the log** — files a filter drops and files a transform fails now print their `[SKIP]` or `[FAIL]` line in the CLI, appear in the UI's log (failures on the errors tab, dropped files on the skipped tab) and count toward the progress bar; they used to report nothing, so the bar stopped short when a filter dropped files.
- **Runs start off the UI thread** — the folder scan and the nodes of a run ran on the editor's UI thread until the first file operation that had to wait, so the window could stop responding while a large folder was scanned or previewed.
- **Copy mode no longer claims to keep originals** — rename and image nodes change files in the input folder itself, so Folder Output's `copy` description, the README and the user guide no longer say that copy keeps the originals or that path guards keep every write inside its target directory; the README, the user guide and the samples README warn to point the input at a copy of your files.
- **Settings with a null recent list** — a settings file whose `recentPipelines` is `null` now loads as an empty list instead of failing and being overwritten with defaults.
- **Sort keeps ties in order** — Sort keeps the input order of files whose sort keys are equal, in both directions.
- **Toolbar icon buttons have accessible names** — New Pipeline, Open Pipeline, Save, Recent Pipelines, Undo, Redo, Fit to Screen and Keyboard Shortcuts are named for screen readers; their automation names were the icon's type name (`Avalonia.Controls.PathIcon`) or `?`, and the tooltip is only help text.
- **IGNITE paints the forge palette** — IGNITE and the shortcuts dialog's Close button showed the base theme's blue instead of the molten gradient; they now show the gradient, a brighter one on hover and a darker one while pressed. Checked boxes and other controls the base theme draws in its accent colour are orange instead of blue.
- **Body text uses the bundled Inter font** — the body font named Inter but not the copy the app ships, so body text fell back to whichever system font was installed; it now always uses the bundled Inter.
- **Tightly set text keeps its full letters** — the FLOW · FORGE wordmark, the empty canvas headline, station titles and the inspector's station title are set with a line height equal to their font size, which cut off the tops and tails of their letters; they now draw in full, with no change to layout.
- **Every glyph comes from a bundled font** — the DRY RUN half-disc and IGNITE's play mark had no glyph in their font and fell back to whatever system font had one; the half-disc is now drawn as an icon and the play and stop marks use the bundled monospace font. Log rows and the shortcuts dialog's keys named fonts only some systems install; they now use the bundled monospace font too.
- **Station subtitles stay readable while running** — a running station's heat glow lit the body behind its subtitle to almost the subtitle's own colour; while a station runs, its subtitle is now light text on a dark plate.
- **Readings come from the run** — the toolbar's SRC count and the canvas HUD's latency and error count show the current or last run, and the console shows the app's real process id while a run is in progress; all four were fixed text. The console's file count reads finished files over the files found, so it never passes the total. Station gauges and the HUD's memory reading show a dash, since nothing measures them.
- **Correction to 2.0.0** — the "animated gauge strip" and the "system HUD with lat/mem/err stats" showed fixed numbers that no run changed (gauges 86, 72 and 43; 6.2ms, 142MB and 0 errors). Fixed in this release: the HUD shows the run's latency and errors, and the gauges and the memory reading show a dash.
- **Long runs keep the log light** — the Output, Errors and Skipped lists build only the rows in view; each list used to build a full row of controls for every file it held, up to 5,000 per list, on the UI thread.
- **A refused or failed run can be read in full** — the summary beside the log tabs is trimmed to the space left, hovering it shows the whole message, and each line of the message is also a row on the Errors tab; a long refusal used to run past the window's edge with no way to read the rest. A refused pipeline no longer reports a scan first, so the CLI no longer prints "Scanning..." on the line of its error.
- **Docs name nodes as the editor does** — the user guide's node headings give the node library's group (Input, Process, Save To) instead of Source and Transform, and the samples README lists each sample's nodes by the names the editor shows, joined by arrows, instead of by their type keys.
- **Undo and Redo follow the history** — the toolbar's Undo and Redo buttons are enabled only when there is a step to undo or redo, and their tooltips name that step, for example "Undo Add Rename (Pattern) (Ctrl+Z)"; both buttons were always enabled. A disabled toolbar button keeps the toolbar's background and dims its icon.
- **Template cards describe their templates** — the empty canvas's cards are built from the template library, so each shows its template's real station count and says what it does; three counts were wrong and every description named steps its template does not take. When the canvas is too short for them, the cards scroll into view instead of running off its edge.
- **Inspector fields show true hints and fit the panel** — an empty number field's hint is the field's own placeholder or default, or nothing, instead of "0" (Counter Start now hints "1"); check boxes no longer repeat the label shown above them; and the field cards fit inside the inspector, where they used to run past its right edge, so a long multi-line value wraps within the visible card.
- **Station previews follow edits** — the config value a station shows under its title changes when the field is edited in the inspector and when that edit is undone or redone; it used to keep the value the station was created with until the pipeline was opened again.
- **Run controls stay on screen in a narrow window** — below 1100 px the toolbar's STN, PIPE and SRC counts give way, so IGNITE and the shortcuts button stay visible down to the window's minimum width; a narrow window or a long file name used to push them off screen. In a narrow canvas the empty state's headline and hint wrap instead of being cut off, and the system readout no longer shows through the template cards.
- **File shortcuts work** — Ctrl+N, Ctrl+O, Ctrl+S and Ctrl+Shift+S, listed in the shortcuts dialog, now start a new pipeline, open, save and save as; they did nothing. A Save As button sits beside Save in the toolbar.

### Security

- **TIFF output is always LZW** — Image Convert and Image Resize write TIFF files with lossless LZW compression. A fax-compressed (CCITT T.4/T.6) source could be re-encoded with its own compression, and the CCITT encoders in SixLabors.ImageSharp 3.1.12 have out-of-bounds write advisories.
- **BigTIFF input is refused** — Image Resize, Image Convert and Image Compress read the file header before decoding and fail a BigTIFF file with a reason; the pinned decoder loops on a crafted BigTIFF header and ignores cancellation. BigTIFF input fails until the image library is replaced.
- **Known image-library advisories** — the five known SixLabors.ImageSharp 3.1.12 advisories are suppressed by advisory URL, so restore succeeds while any new advisory still fails it. The reachable ones, the CCITT encoders and the BigTIFF decoder, are closed by the two entries above.

## [2.0.0] - 2026-04-23

### Added

- **Molten Works redesign** — cast-iron stations with chrome pipes, forge-amber / cyan / magenta / lime / purple palette, and embedded Instrument Serif / Oswald / JetBrains Mono typography. 11 hand-drawn forge-themed SVG icons (pit, mold, die, chisel, brand, sieve, rack, mill, crucible, press, loupe). Rebuilt top bar (FLOW·FORGE logo, pipeline path, STN/PIPE stats, IGNITE / QUENCH / DRY RUN buttons), operations library, inspector, and bottom console to match the concept.
- **Traveling mercury beads** — `MwMercuryDroplet` custom control renders four staggered beads riding each pipe's cubic bezier while a pipeline is running. Uses the same midpoint formula as the pipe so the bead tracks the rendered curve by construction.
- **Custom pipe shape** — `MwPipeConnection` replaces Nodify's `Connection` so the pipe and droplets share the exact same geometry. Five layered strokes for the chrome look (shadow / outer / core / inner channel / liquid).
- **Running-state visuals** — category-colored aura behind each station, pulsing heat glow inside the iron body, pulsing LIVE dot, animated gauge strip (opacity + color change), and mercury inner-dot + glow on active ports.
- **Canvas backdrop** — radial forge glow, cyan HUD wash, magenta corner brackets, 40×40 / 200×200 grid overlay, animated cyan scanline, 14 staggered ember particles rising from the bottom, system HUD with lat/mem/err stats.
- **DEMO button** — toggles the running-state visuals on every station and pipe without executing a real pipeline. Disabled while an actual run is in flight.
- **Shared pipe / bead geometry helper** — new `MwGeometry.IsFinite` validators are wired into the `StyledProperty` registrations on both `MwPipeConnection` and `MwMercuryDroplet`, so the two ends of the shared cubic reject the same class of non-finite input at the property boundary.

### Changed

- **Dark-only theme** — light theme support removed. Placeholder `Light` `ResourceDictionary` and runtime toggle dropped; `App.axaml` locks `RequestedThemeVariant="Dark"`. Toolbar theme-toggle button, `ToggleTheme` command, `IsDarkTheme` / `ThemeIcon` properties, and the `ActualThemeVariantChanged` subscription removed. **Breaking change for anyone who was using the light theme.**
- **Screenshots refreshed** — three new shots matching the redesigned UI (empty state hero, populated editor, running stations). Old pre-redesign screenshots deleted.

### Fixed

- **`PathGuard.EnsureWithinDirectory` trailing-separator bug** — a user-typed trailing backslash on the output folder, or a drive-root / UNC-share selected as the allowed root (`C:\`, `\\server\share\`), doubled the separator in the prefix check and rejected every valid child as "resolves outside". `Path.TrimEndingDirectorySeparator` normalization plus a new `PathGuard.NormalizedRootPrefix` helper that preserves a root-anchored trailing separator rather than doubling it. Same fix routed through `FolderInputNode` enumeration.
- **Empty-value validation** — `FolderInput`, `FolderOutput`, `RenamePattern`, `RenameRegex`, and `ImageConvert` now throw a friendly `NodeConfigurationException` naming the station when a required field is empty or whitespace, instead of letting raw `ArgumentException` / IO failures fan out per file.
- **Bool / int config silent corruption** — `JsonElement.GetRawText()` returned lowercase `"true"` / `"false"` but `BoolStringConverter.ConvertBack` emits `"True"` / `"False"`. Case mismatch on the first CheckBox round-trip re-fired `OnValueChanged`, pushed another UndoRedo entry, rebuilt Fields, produced another mismatched VM, and looped until the UI thread starved. `ConfigFieldViewModel` now normalizes bool values in its constructor, and an unparseable bool or int string no longer lands in a typed slot where `GetBoolean` / `GetInt32` would throw on next load.
- **Inspector blanking after CheckBox toggle** — `OnUndoRedoStateChanged` called `RefreshPropertiesPanel` synchronously inside a binding setter's event dispatch, detaching the very CheckBox whose event was still on the stack. Refresh now deferred via `Dispatcher.UIThread.Post`.
- **PropertiesView empty-state layout** — DockPanel default-Left docking made the empty-state prompt a narrow strip while the Fields `ScrollViewer` took `LastChildFill`. Wrapped both in a `Grid` so `IsVisible` alone controls which shows.
- **Execution progress bar scale** — `ProgressBar.Maximum` was `1` while the VM's `Progress` property scales to `0..100`, so the bar clamped to full at roughly 1 % on every real run. Maximum is now `100`.
- **Mercury droplet layout side effects** — positioning via `Canvas.SetLeft` / `SetTop` happened inside `MeasureOverride`, which writes layout-affecting attached properties during the measure pass. Moved to `ArrangeOverride` with `AffectsArrange` only; render output is position-independent.
- **Mercury droplet progress overshoot** — beads briefly flew off the pipe at animation keyframe boundaries. `ComputeBezierPoint` clamps `Progress` to `[0, 1]`, and the `StyledProperty` validators on Source, Target, Progress, and the pipe's offsets reject NaN / ±Infinity so the cubic geometry cannot be silently poisoned.
- **Running-state thread safety** — `ExecutePipelineAsync`'s `finally` flipped `ExecutionLog.IsRunning` on whatever thread `RunAsync` resumed on (typically a thread-pool thread), then mutated the node / connection `ObservableCollection` from off-thread. `UpdateRunningVisual` now guards with `Dispatcher.UIThread.CheckAccess` and re-posts when off-thread; the `finally` awaits `Dispatcher.UIThread.InvokeAsync(...)` so the next execute call sees the flipped flag at its guard.
- **Running-state on graph mutations** — stations and pipes added after the canvas was already in the "forge lit" state (template load, undo of a delete, bulk `Clear + Add`) stayed visually idle. `OnEditorRunningCollectionChanged` now seeds `IsRunning` on `NewItems` and re-syncs the whole canvas on `Reset`.
- **30+ regression tests** — trailing-separator enumeration across drive-root / UNC / user-typed paths, empty-value configs, bool / int unparseable-input rejection, bool round-trip idempotency, running-state + demo propagation, every registered TypeKey has a bespoke MwOpsMap entry whose category bucket matches its `NodeRegistry.GetCategoryForTypeKey`.

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
