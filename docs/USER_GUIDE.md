# FlowForge User Guide

## Getting Started

### Installation

Build from source (requires the .NET 10 SDK):

```bash
git clone https://github.com/Wintersta7e/FlowForge.git
cd FlowForge
dotnet run --project src/FlowForge.UI
```

### Your First Pipeline

1. Launch FlowForge
2. Click a **template button** on the empty canvas (Photo Import, Batch Rename, Web Export, or Compress) to start with a pre-wired pipeline
3. Or drag nodes from the **Node Library** sidebar onto the canvas to build from scratch

## The Editor

### Canvas

- **Pan** — right-click drag or middle-click drag
- **Zoom** — scroll wheel, from 25% to 400%
- **Select** — click a node, or rubber-band select multiple nodes
- **Move** — drag selected nodes
- **Connect** — drag from an output pin to an input pin to create a wire
- **Disconnect** — Alt+click a pin to remove every wire on it in one undo step
- **Delete** — select a node and press Delete
- **Undo** — Ctrl+Z to undo the last action (up to 25 steps)
- **Redo** — Ctrl+Y to redo the last undone action
- **Zoom to Fit** — click the toolbar button to fit the entire graph into view

### Keyboard Shortcuts

Press F1, or click the toolbar's **?** button, to show this list in the app:

<!-- shortcuts -->
- **Ctrl+N** — New pipeline
- **Ctrl+O** — Open pipeline
- **Ctrl+S** — Save
- **Ctrl+Shift+S** — Save as
- **Delete** — Remove selected nodes
- **Ctrl+0** — Zoom to fit all nodes
- **Middle/Right-drag** — Pan canvas
- **Scroll** — Zoom in/out
- **Alt+Click port** — Disconnect
- **Ctrl+Z** — Undo
- **Ctrl+Y** — Redo
- **F5** — Dry run (no files changed)
- **Ctrl+Shift+Enter** — Run pipeline
- **Esc** — Stop the running pipeline
- **F1** — Show this help
<!-- /shortcuts -->

### Node Library

The left sidebar lists all available nodes grouped by category:

- **Input** — nodes that enumerate files (Folder Input)
- **Process** — nodes that transform, filter, or sort files
- **Save To** — nodes that write output (Folder Output)

Type in the search box to filter nodes by name.

### Properties Panel

Click any node to see its configuration in the Properties panel. Fields are auto-generated from the node's schema and include:

- Text inputs, number inputs, booleans (checkboxes)
- File and folder pickers (native OS dialogs)
- Dropdowns for enumerated options
- Hover any field label to see its tooltip description

### Undo/Redo

FlowForge tracks all editor actions and supports full undo/redo:

- **Supported actions** — add node, delete node(s), move node, connect, disconnect, and config field changes
- **History depth** — 25 steps (oldest actions are dropped when the limit is reached)
- **Config coalescing** — rapid edits to the same config field (e.g., typing a path) are merged into a single undo entry
- **Keyboard shortcuts** — Ctrl+Z (undo), Ctrl+Y (redo)

Undo/redo state is cleared when you load a new pipeline or create a new one.

### Execution Log

The bottom panel shows pipeline execution results:

- **Output tab** — files that succeeded
- **Errors tab** — files that failed processing
- **Skipped tab** — files that were skipped
- **Progress bar** — live progress during execution
- **Summary line** — total files, success/fail/skip counts, and duration
- **Real-time progress** — file scanning count updates live during enumeration, followed by per-file processing status

## Nodes

Rename and image nodes never change the input folder — rename nodes copy nothing and change only the delivered name, and image nodes write their results into a private run folder. Only Folder Output writes to your folders. With `copy` the input files stay as they were; `move` deletes each one only after every delivery of it succeeded.

The private run folder is created in `%TEMP%\FlowForge\runs` on Windows, and in `FlowForge/runs` under your per-user data folder elsewhere (usually `~/.local/share/FlowForge/runs` on Linux). Each Process node handles every file before the first file is delivered, so the folder needs room for every image the run rewrites: a PNG conversion of 12 photos held 22 MB there for 24 MB delivered. When the output folder is the input folder or inside it and `overwrite` is on, it also holds a copy of each input that another file is delivered onto. The folder is deleted when the run ends; one a run could not delete is removed by the next live run.

### Folder Input (Input)

Enumerates files from a directory.

<!-- node-fields:FolderInput -->
| Field | Label | Required | Default | Options | Example | Description |
|---|---|---|---|---|---|---|
| `path` | Source Folder | yes | — | — | — | Root folder to scan for files |
| `recursive` | Include Subfolders | no | `false` | — | — | Search subdirectories recursively |
| `filter` | File Filter | no | `*` | — | `*.jpg;*.png` | Semicolon-separated glob patterns (e.g. \*.jpg;\*.png) |
<!-- /node-fields -->

### Rename (Pattern) (Process)

Renames files using token-based patterns.

| Token | Expands to |
|-------|-----------|
| `{name}` | Original filename (without extension) |
| `{ext}` | Original extension |
| `{counter}` | Auto-incrementing number |
| `{date:FORMAT}` | Current date (e.g., `{date:yyyy-MM-dd}`) |
| `{meta:KEY}` | Metadata value (requires upstream Metadata Extract node) |

A `{meta:KEY}` token whose key the file does not carry expands to an empty string; the file is still processed.

<!-- node-fields:RenamePattern -->
| Field | Label | Required | Default | Options | Example | Description |
|---|---|---|---|---|---|---|
| `pattern` | Rename Pattern | yes | — | — | `{name}_{counter:000}{ext}` | Tokens: {name}, {ext}, {counter:000}, {date:yyyy-MM-dd}, {meta:KEY} |
| `startIndex` | Counter Start | no | `1` | — | — | Starting number for {counter} token |
<!-- /node-fields -->

### Rename (Regex) (Process)

Regex find-and-replace on filenames.

<!-- node-fields:RenameRegex -->
| Field | Label | Required | Default | Options | Example | Description |
|---|---|---|---|---|---|---|
| `pattern` | Regex Pattern | yes | — | — | `\d+` | Regular expression to match against filename (e.g. \\d+ or \\.jpe?g$) |
| `replacement` | Replacement | yes | — | — | — | Replacement string ($1, $2 for capture groups) |
| `scope` | Scope | no | `filename` | `filename`, `fullpath` | — | filename: match name only, fullpath: match entire path |
<!-- /node-fields -->

### Rename (Add Affix) (Process)

Adds a prefix and/or suffix to filenames.

<!-- node-fields:RenameAddAffix -->
| Field | Label | Required | Default | Options | Example | Description |
|---|---|---|---|---|---|---|
| `prefix` | Prefix | no | — | — | — | Text to prepend before filename |
| `suffix` | Suffix | no | — | — | — | Text to append after filename (before extension) |
<!-- /node-fields -->

### Filter (Process)

Filters files by conditions. Files that don't match are either skipped or dropped.

<!-- node-fields:Filter -->
| Field | Label | Required | Default | Options | Example | Description |
|---|---|---|---|---|---|---|
| `conditions` | Conditions (JSON) | yes | — | — | `[{"field":"extension","operator":"equals","value":".jpg"}]` | JSON array of conditions. Fields: extension, filename, size, createdAt, modifiedAt. Operators: equals, notequals, contains, startswith, endswith, greaterthan, lessthan, matches |
<!-- /node-fields -->

### Sort (Process)

Reorders files in the pipeline.

<!-- node-fields:Sort -->
| Field | Label | Required | Default | Options | Example | Description |
|---|---|---|---|---|---|---|
| `field` | Sort Field | no | `filename` | `filename`, `extension`, `size`, `createdAt`, `modifiedAt` | — | File property to sort by |
| `direction` | Direction | no | `asc` | `asc`, `desc` | — | asc: smallest/oldest first, desc: largest/newest first |
<!-- /node-fields -->

### Image Resize (Process)

Resizes image files.

<!-- node-fields:ImageResize -->
| Field | Label | Required | Default | Options | Example | Description |
|---|---|---|---|---|---|---|
| `width` | Width (px) | no | — | — | — | Target width in pixels (leave blank to auto-calculate from height) |
| `height` | Height (px) | no | — | — | — | Target height in pixels (leave blank to auto-calculate from width) |
| `mode` | Resize Mode | no | `max` | `max`, `min`, `crop`, `pad`, `stretch` | — | max: fit within bounds, crop: fill and trim, pad: fit with margins, stretch: distort to exact size |
| `maintainAspect` | Maintain Aspect Ratio | no | `true` | — | — | Preserve original width-to-height ratio |
| `dpi` | DPI | no | — | — | — | Output resolution in dots per inch (leave blank to keep original) |
<!-- /node-fields -->

### Image Convert (Process)

Converts images between formats.

<!-- node-fields:ImageConvert -->
| Field | Label | Required | Default | Options | Example | Description |
|---|---|---|---|---|---|---|
| `format` | Target Format | yes | — | `jpg`, `jpeg`, `png`, `webp`, `bmp`, `tiff` | — | Output image format |
<!-- /node-fields -->

### Image Compress (Process)

Compresses images with quality control.

<!-- node-fields:ImageCompress -->
| Field | Label | Required | Default | Options | Example | Description |
|---|---|---|---|---|---|---|
| `quality` | Quality (1-100) | yes | — | — | `1-100` | JPEG/WebP quality level (1 = smallest file, 100 = best quality) |
| `format` | Output Format | no | — | `jpg`, `jpeg`, `png`, `webp` | — | Override output format (leave blank to keep original format) |
<!-- /node-fields -->

### Metadata Extract (Process)

Reads EXIF and file metadata into pipeline variables for use in downstream Rename Pattern nodes via `{meta:KEY}`. A `{meta:KEY}` token takes the full key as written in `keys`, for example `{meta:EXIF:DateTaken}`.

<!-- node-fields:MetadataExtract -->
| Field | Label | Required | Default | Options | Example | Description |
|---|---|---|---|---|---|---|
| `keys` | Metadata Keys | yes | — | — | `EXIF:DateTaken, File:SizeBytes` | Comma-separated keys. File: SizeBytes, CreatedAt, ModifiedAt. EXIF: DateTaken, CameraModel, CameraMake, GPS, FocalLength, ISO |
<!-- /node-fields -->

### Folder Output (Save To)

Copies or moves processed files to a destination.

<!-- node-fields:FolderOutput -->
| Field | Label | Required | Default | Options | Example | Description |
|---|---|---|---|---|---|---|
| `path` | Output Folder | yes | — | — | — | Destination folder for processed files |
| `mode` | Mode | no | `copy` | `copy`, `move` | — | copy: leave the input files unchanged, move: delete each input file once every delivery of it succeeded |
| `overwrite` | Overwrite Existing | no | `false` | — | — | Replace existing files at destination |
| `preserveStructure` | Preserve Folder Structure | no | `false` | — | — | Recreate source folder hierarchy in output |
| `sourceBasePath` | Source Base Path | no | — | — | — | Root path for computing relative subdirectories |
| `enableBackup` | Backup Before Overwrite | no | `false` | — | — | Create a backup copy of existing destination files before overwriting |
| `backupSuffix` | Backup Suffix | no | `.bak` | — | — | File extension appended to backup copies (e.g. .bak, .orig) |
<!-- /node-fields -->

## Running Pipelines

The top-right toolbar carries three run-related buttons — **Dry Run**, **Ignite**, and (while a run is in flight) **Quench** — plus a **Demo** toggle.

Files travel only along the wires: a pipeline may branch and merge, and each output receives only what is wired to it. A node with several outgoing wires sends its files down every one of them, and a node with several incoming wires receives the files from all of them.

### Dry Run (Preview)

Click **Dry Run** to simulate the pipeline; a dry run writes, moves and deletes nothing; lists folders and reads file sizes and dates; opens no file content. The execution log shows what *would* happen — useful for verifying your pipeline before committing to changes.

### Ignite (Run)

Click **Ignite** to execute the pipeline for real. Files are processed according to the node configuration. Progress is shown live in the execution log, and each running station glows in its category colour while mercury beads travel the chrome pipes.

### Quench (Cancel)

While a pipeline is running, the Ignite button is replaced by **Quench**. Click it to stop processing. Files already processed are not rolled back.

### Demo

The **Demo** button on the toolbar toggles the "forge lit" running visuals (station aura, pipe liquid, traveling mercury beads) on every station and pipe without actually executing a real pipeline. It is disabled while a real run is in flight, and is useful for previewing how a graph will look during execution.

## Saving and Loading

- **Save** (Ctrl+S) — save the current pipeline to a `.ffpipe` file
- **Save As** (Ctrl+Shift+S) — save to a new file
- **Open** (Ctrl+O) — load a pipeline from a `.ffpipe` file
- **Recent Pipelines** — the toolbar's Recent flyout lists recently opened pipelines for quick access

Pipeline files are human-readable JSON and can be version-controlled or shared.

## CLI Runner

Run pipelines from the command line for automation. From the source tree, run the CLI project directly:

```bash
dotnet run --project src/FlowForge.CLI -- run pipeline.ffpipe
```

Or publish it, which produces `flowforge` (`flowforge.exe` on Windows) in `<folder>`; the examples below call that command:

```bash
dotnet publish src/FlowForge.CLI -c Release -o <folder>
```

```bash
# Basic run
flowforge run pipeline.ffpipe

# Override input/output directories
flowforge run pipeline.ffpipe --input /photos/raw --output /photos/processed

# Preview without changing files
flowforge run pipeline.ffpipe --dry-run

# Verbose logging
flowforge run pipeline.ffpipe --verbose

# Machine-readable JSON output
flowforge run pipeline.ffpipe --format json
```

A dry run's summary ends with a `Checked at run or unresolved: n` line: the number of files whose preview may still differ from the real run, because an image node opens them only in the real run, or because their name or order depends on something only the real run can read. With `--format json`, each such job's `previewMarker` says which (`checkedAtRun` or `unresolved`); it is `null` for every other job and in a real run.

### Exit Codes

| Code | Meaning |
|------|---------|
| 0 | All files processed successfully |
| 1 | Partial failure (some files failed) |
| 2 | Total failure or invalid arguments |

### JSON Output

With `--format json`, the CLI writes structured JSON to stdout (logs go to stderr):

```json
{
  "isDryRun": false,
  "totalFiles": 10,
  "succeeded": 9,
  "failed": 1,
  "skipped": 0,
  "duration": "00:00:01.2340000",
  "jobs": []
}
```

`duration` is a `TimeSpan` string, and `jobs` holds one entry per processed file (empty here).

## Templates

FlowForge includes these pipeline templates:

<!-- templates -->
| Template | Nodes |
|---|---|
| Photo Import — Rename by EXIF Date | Folder Input → Metadata Extract → Rename (Pattern) → Folder Output |
| Batch Sequential Rename | Folder Input → Rename (Pattern) → Folder Output |
| Image Web Export (Resize + Convert to WebP) | Folder Input → Filter → Image Resize → Image Convert → Folder Output |
| Bulk Image Compress | Folder Input → Filter → Image Compress → Folder Output |
<!-- /templates -->

## Sample Pipelines

The `samples/` directory contains example `.ffpipe` files demonstrating common workflows. Open them with the toolbar's Open button, or run them through the CLI.

## Logging

FlowForge uses structured logging via `Microsoft.Extensions.Logging` with Serilog as the provider:

- **UI app** — logs to console and to rolling files in a `logs` folder beside the executable: a new file each day, named by date (for example `flowforge20261014.log`), and a new numbered file (`flowforge20261014_001.log`, …) whenever one reaches 10 MB; the 7 newest files are kept
- **CLI** — logs to console (stderr in `--format json` mode)

Log files are useful for diagnosing issues — include them when reporting bugs.
