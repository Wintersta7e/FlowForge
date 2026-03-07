# FlowForge

A visual node-based file processing pipeline tool. Build reusable workflows for renaming, resizing, converting, and filtering files — no scripting required.

[![CI](https://github.com/Wintersta7e/FlowForge/actions/workflows/ci.yml/badge.svg)](https://github.com/Wintersta7e/FlowForge/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)
![Avalonia](https://img.shields.io/badge/Avalonia-11.3-8B44AC?logo=dotnet)
![C#](https://img.shields.io/badge/C%23-14-239120?logo=csharp)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](./LICENSE)

<p align="center">
  <img src="screenshots/editor-overview.png" alt="FlowForge Editor" width="800">
</p>

## Overview

FlowForge lets you visually connect source, transform, and output nodes to build file processing pipelines:

- **Visual Node Editor** — Drag-and-drop canvas with pan, zoom, and wire connections
- **Undo/Redo** — Full undo/redo for all editor actions (add, delete, move, connect, disconnect, config changes) with Ctrl+Z / Ctrl+Y
- **11 Built-in Nodes** — Sources, transforms, and outputs covering common file operations
- **Real-time Progress** — Live scanning count, per-file processing status, and throughput reporting
- **Pipeline Templates** — Pre-wired workflows for common tasks (photo import, batch rename, web export, compression)
- **CLI Runner** — Execute pipelines from the command line for automation and scripting
- **Cross-platform** — Runs on Windows, macOS, and Linux via Avalonia UI

## Features

### Node Types

| Category | Node | Description |
|----------|------|-------------|
| **Input** | Folder Input | Enumerate files from a directory with recursive and filter options |
| **Process** | Rename Pattern | Token-based renaming (`{name}`, `{date}`, `{counter}`, `{meta}`) |
| **Process** | Rename Regex | Regex find-and-replace with capture group support |
| **Process** | Rename Add Affix | Add prefix and/or suffix to filenames |
| **Process** | Filter | Conditional filtering by extension, size, date, or regex |
| **Process** | Sort | Reorder files by name, extension, or size |
| **Process** | Image Resize | Resize images with aspect ratio control |
| **Process** | Image Convert | Convert between JPEG, PNG, WebP, BMP, and TIFF |
| **Process** | Image Compress | Quality-based compression for JPEG, PNG, and WebP |
| **Process** | Metadata Extract | Read EXIF and file metadata into pipeline variables |
| **Save To** | Folder Output | Copy or move processed files with optional backup before overwrite |

### Pipeline Editor

- **Canvas** — Nodify-powered node graph with pan, zoom, drag, and rubber-band selection
- **Node Library** — Categorized sidebar with search and drag-to-canvas support
- **Properties Panel** — Auto-generated config forms from node schemas (text, number, boolean, file/folder picker, dropdown)
- **Undo/Redo** — Ctrl+Z / Ctrl+Y for all editor actions with 25-step history; config field edits coalesce into single undo entries
- **Execution Log** — Live progress with success/fail/skip counts and per-file details
- **Templates** — One-click pipeline starters: Photo Import, Batch Rename, Web Export, Compress
- **Recent Pipelines** — MRU menu with quick access to recently opened files
- **Keyboard Shortcuts** — Help dialog showing all available shortcuts
- **Zoom-to-Fit** — Toolbar button to fit the entire graph into the viewport
- **Config Tooltips** — Hover descriptions on all node configuration fields
- **Molten Forge Theme** — Custom dark theme with warm amber accent and category-colored nodes

<p align="center">
  <img src="screenshots/node-pipeline.png" alt="Node Pipeline" width="600">
</p>
<p align="center">
  <img src="screenshots/node-library.png" alt="Node Library" width="300">
  <img src="screenshots/properties-panel.png" alt="Properties Panel" width="300">
</p>

### CLI Runner

```bash
flowforge run pipeline.ffpipe [--input <dir>] [--output <dir>] [--dry-run] [--verbose] [--format json]
```

- Override input/output directories per run
- Dry-run mode for safe previewing
- JSON output mode (`--format json`) for machine-readable results
- Structured logging via `ILogger<T>` (routed to stderr in JSON mode)
- Exit codes: 0 (success), 1 (partial failure), 2 (total failure / invalid arguments)

### Pipeline Format

Pipelines are saved as `.ffpipe` files (human-readable JSON, UTF-8):

```json
{
  "name": "Photo Import",
  "nodes": [
    { "id": "...", "typeKey": "FolderInput", "config": { "path": "/photos" } }
  ],
  "connections": [
    { "sourceId": "...", "targetId": "..." }
  ]
}
```

## Tech Stack

| Technology | Purpose |
|------------|---------|
| [.NET 10](https://dotnet.microsoft.com) | Runtime and build system |
| [Avalonia 11](https://avaloniaui.net) | Cross-platform desktop UI framework |
| [Nodify.Avalonia](https://github.com/miroiu/nodify) | Node graph editor control |
| [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm) | MVVM framework |
| [SixLabors.ImageSharp](https://sixlabors.com/products/imagesharp) | Image processing (resize, convert, compress) |
| [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet) | EXIF and file metadata reading |
| [Microsoft.Extensions.Logging](https://learn.microsoft.com/dotnet/core/extensions/logging) | Logging abstraction (`ILogger<T>`) |
| [Serilog](https://serilog.net) | Logging provider (console + rolling file) |
| [Microsoft.Extensions.DependencyInjection](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection) | IoC container |
| [System.CommandLine](https://learn.microsoft.com/dotnet/standard/commandline) | CLI argument parsing |
| [xUnit](https://xunit.net) + [FluentAssertions](https://fluentassertions.com) | Testing framework |

## Project Structure

```
FlowForge/
├── FlowForge.sln
├── src/
│   ├── FlowForge.Core/           # Business logic (no UI references)
│   │   ├── DependencyInjection/  # AddFlowForgeCore() service registration
│   │   ├── Execution/            # PipelineRunner, NodeRegistry
│   │   ├── Models/               # FileJob, ExecutionResult
│   │   ├── Nodes/
│   │   │   ├── Base/             # Interfaces, ConfigField, exceptions
│   │   │   ├── Sources/          # FolderInputNode
│   │   │   ├── Transforms/       # 8 transform nodes
│   │   │   └── Outputs/          # FolderOutputNode
│   │   ├── Pipeline/             # PipelineGraph, Serializer, Templates
│   │   └── Settings/             # AppSettings, AppSettingsManager
│   ├── FlowForge.UI/             # Avalonia desktop app (MVVM)
│   │   ├── ViewModels/           # 14 view models
│   │   ├── Views/                # 5 view pairs + template selector
│   │   ├── UndoRedo/             # Command pattern undo/redo system
│   │   ├── Themes/               # MoltenForgeTheme.axaml
│   │   └── Services/             # DialogService
│   └── FlowForge.CLI/            # CLI runner (System.CommandLine)
└── tests/
    └── FlowForge.Tests/          # 309 xUnit tests
        ├── DependencyInjection/  # DI registration tests
        ├── Nodes/                # 11 node test files
        ├── Execution/            # Runner + progress tests
        ├── Pipeline/             # Serializer + template tests
        ├── Models/               # FileJob tests
        ├── Settings/             # AppSettings tests
        ├── UndoRedo/             # UndoRedoManager + command tests
        ├── ViewModels/           # ViewModel tests
        └── Helpers/              # TempDirectory, TestFileFactory, PipelineBuilder
```

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later

### Build

```bash
# Clone the repository
git clone https://github.com/Wintersta7e/FlowForge.git
cd FlowForge

# Build all projects
dotnet build

# Run the desktop app
dotnet run --project src/FlowForge.UI

# Run the CLI
dotnet run --project src/FlowForge.CLI -- run pipeline.ffpipe --dry-run
```

### Run Tests

```bash
# Run all 309 tests
dotnet test --logger "console;verbosity=normal"

# Run specific test class
dotnet test --filter "FullyQualifiedName~RenamePatternNodeTests"
```

## Quick Start

1. **Launch the app** — `dotnet run --project src/FlowForge.UI`
2. **Use a template** — Click a template button on the empty canvas (Photo Import, Batch Rename, Web Export, or Compress)
3. **Or build from scratch** — Drag nodes from the sidebar onto the canvas
4. **Connect nodes** — Drag from an output pin to an input pin to create a wire
5. **Configure** — Select a node and edit its settings in the Properties panel
6. **Preview** — Click Preview to simulate the run without changing any files
7. **Run** — Click Run to execute the pipeline
8. **Save** — Save your pipeline as a `.ffpipe` file to reuse later

## Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Ensure `dotnet build` passes with zero warnings
5. Ensure `dotnet test` passes
6. Submit a pull request

## License

MIT License — see [LICENSE](./LICENSE) for details.

## Support

- [Report issues](../../issues) or suggest features
- Star this repository if you find it useful
