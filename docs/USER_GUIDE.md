# FlowForge User Guide

## Getting Started

### Installation

Download the latest release from [GitHub Releases](https://github.com/Wintersta7e/FlowForge/releases) or build from source:

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
- **Zoom** — scroll wheel
- **Select** — click a node, or rubber-band select multiple nodes
- **Move** — drag selected nodes
- **Connect** — drag from an output pin to an input pin to create a wire
- **Delete** — select a node or connection and press Delete
- **Zoom to Fit** — click the toolbar button to fit the entire graph into view

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

### Execution Log

The bottom panel shows pipeline execution results:

- **Output tab** — per-file status (success, failed, skipped) with details
- **Errors tab** — files that failed processing
- **Warnings tab** — files that were skipped
- **Progress bar** — live progress during execution
- **Summary line** — total files, success/fail/skip counts, and duration

## Nodes

### Folder Input (Source)

Enumerates files from a directory.

| Field | Description |
|-------|-------------|
| `path` | Directory to scan |
| `recursive` | Include subdirectories |
| `pattern` | Glob filter (e.g., `*.jpg`, `*.png`) |

### Rename Pattern (Transform)

Renames files using token-based patterns.

| Token | Expands to |
|-------|-----------|
| `{name}` | Original filename (without extension) |
| `{ext}` | Original extension |
| `{counter}` | Auto-incrementing number |
| `{date:FORMAT}` | Current date (e.g., `{date:yyyy-MM-dd}`) |
| `{meta:KEY}` | Metadata value (requires upstream Metadata Extract node) |

| Field | Description |
|-------|-------------|
| `pattern` | Rename pattern using tokens above |
| `startIndex` | Starting value for `{counter}` (default: 1) |

### Rename Regex (Transform)

Regex find-and-replace on filenames.

| Field | Description |
|-------|-------------|
| `pattern` | Regex pattern to match |
| `replacement` | Replacement string (supports `$1`, `$2` capture groups) |
| `scope` | Apply to `filename` (default) or `fullpath` |

### Rename Add Affix (Transform)

Adds a prefix and/or suffix to filenames.

| Field | Description |
|-------|-------------|
| `prefix` | Text to prepend to filename |
| `suffix` | Text to append before the extension |

### Filter (Transform)

Filters files by conditions. Files that don't match are either skipped or dropped.

| Field | Description |
|-------|-------------|
| `field` | What to filter on: `extension`, `size`, `name`, `date` |
| `operator` | Comparison: `equals`, `contains`, `startsWith`, `endsWith`, `greaterThan`, `lessThan`, `matches` (regex) |
| `value` | Value to compare against |
| `action` | `skip` (mark as skipped) or `drop` (remove from pipeline) |

### Sort (Transform)

Reorders files in the pipeline.

| Field | Description |
|-------|-------------|
| `sortBy` | Sort key: `name`, `extension`, `size` |
| `descending` | Reverse sort order (default: false) |

### Image Resize (Transform)

Resizes image files.

| Field | Description |
|-------|-------------|
| `width` | Target width in pixels |
| `height` | Target height in pixels |
| `mode` | Resize mode: `max` (fit within bounds), `exact` (stretch to exact size) |
| `maintainAspect` | Preserve aspect ratio (default: true) |
| `dpi` | Output DPI (default: 0 = keep original) |

### Image Convert (Transform)

Converts images between formats.

| Field | Description |
|-------|-------------|
| `format` | Target format: `jpg`, `png`, `webp`, `bmp`, `tiff` |

### Image Compress (Transform)

Compresses images with quality control.

| Field | Description |
|-------|-------------|
| `quality` | Compression quality 1-100 (default: 75) |
| `format` | Target format (optional — defaults to original) |

### Metadata Extract (Transform)

Reads EXIF and file metadata into pipeline variables for use in downstream Rename Pattern nodes via `{meta:KEY}`.

| Field | Description |
|-------|-------------|
| `keys` | Comma-separated metadata keys to extract (e.g., `DateTaken`, `CameraModel`) |

### Folder Output (Save To)

Copies or moves processed files to a destination.

| Field | Description |
|-------|-------------|
| `path` | Destination directory |
| `mode` | `copy` or `move` |
| `overwrite` | Overwrite existing files (default: false) |
| `preserveStructure` | Maintain source directory hierarchy (default: false) |
| `enableBackup` | Create backup of destination file before overwriting (default: false) |
| `backupSuffix` | Backup file suffix (default: `.bak`) |

## Running Pipelines

### Preview (Dry Run)

Click **Preview** to simulate the pipeline without touching any files. The execution log shows what *would* happen — useful for verifying your pipeline before committing to changes.

### Run

Click **Run** to execute the pipeline for real. Files are processed according to the node configuration. Progress is shown live in the execution log.

### Cancel

Click **Cancel** during execution to stop processing. Files already processed are not rolled back.

## Saving and Loading

- **Save** (Ctrl+S) — save the current pipeline to a `.ffpipe` file
- **Save As** (Ctrl+Shift+S) — save to a new file
- **Open** (Ctrl+O) — load a pipeline from a `.ffpipe` file
- **Recent Pipelines** — the File menu lists recently opened pipelines for quick access

Pipeline files are human-readable JSON and can be version-controlled or shared.

## CLI Runner

Run pipelines from the command line for automation:

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
  "totalFiles": 10,
  "succeeded": 9,
  "failed": 1,
  "skipped": 0,
  "durationMs": 1234,
  "files": [ ... ]
}
```

## Templates

FlowForge includes 4 pipeline templates:

| Template | What it does |
|----------|-------------|
| **Photo Import** | Folder Input + Metadata Extract + Rename Pattern (`{date:yyyy-MM-dd}_{name}`) + Folder Output |
| **Batch Rename** | Folder Input + Rename Pattern (`{counter}_{name}`) + Folder Output |
| **Web Export** | Folder Input + Image Resize (1920px) + Image Convert (WebP) + Folder Output |
| **Compress** | Folder Input + Image Compress (quality 75) + Folder Output |

## Sample Pipelines

The `samples/` directory contains ready-to-run `.ffpipe` files demonstrating common workflows. Open them from the File menu or run them via CLI.

## Logging

FlowForge uses structured logging via `Microsoft.Extensions.Logging` with Serilog as the provider:

- **UI app** — logs to console and rolling file (`logs/flowforge.log`, daily rotation, 7 files max, 10 MB limit)
- **CLI** — logs to console (stderr in `--format json` mode)

Log files are useful for diagnosing issues — include them when reporting bugs.
