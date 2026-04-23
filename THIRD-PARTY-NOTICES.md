# Third-party notices

FlowForge is distributed under the MIT License (see [`LICENSE`](./LICENSE)). It redistributes the following third-party components under their own licenses:

## Fonts

The FlowForge desktop app embeds three open-source font families under the SIL Open Font License, Version 1.1. The full licence text and the per-font copyright notices are shipped alongside the font files at [`src/FlowForge.UI/Assets/Fonts/OFL.txt`](./src/FlowForge.UI/Assets/Fonts/OFL.txt) and are copied into the published binary next to the `.ttf` files in every release zip.

| Font | Upstream | Licence |
| --- | --- | --- |
| Instrument Serif (Regular + Italic) | <https://github.com/Instrument/instrument-serif> | SIL OFL 1.1 |
| Oswald (Variable) | <https://github.com/googlefonts/OswaldFont> | SIL OFL 1.1 |
| JetBrains Mono (Variable) | <https://github.com/JetBrains/JetBrainsMono> | SIL OFL 1.1 |

Inter ships via the `Avalonia.Fonts.Inter` NuGet package and is not redistributed here directly.

## NuGet packages

Runtime and build-time dependencies are declared in the `*.csproj` files under [`src/`](./src) and [`tests/`](./tests). Each package retains its own licence — see each package's listing on <https://nuget.org> for details. Notable runtime dependencies:

- Avalonia (MIT)
- CommunityToolkit.Mvvm (MIT)
- SixLabors.ImageSharp (Apache 2.0 / Six Labors Split License)
- MetadataExtractor (Apache 2.0)
- Serilog (Apache 2.0)
- Nodify.Avalonia (MIT)
- Microsoft.Extensions.* (MIT)
- System.CommandLine (MIT)
