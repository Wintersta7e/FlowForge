# FlowForge Sample Pipelines

Example pipelines for FlowForge. Their nodes open stacked on the canvas, so drag them apart to see the wiring. Edit the input/output paths before running.

**Work on a copy.** Rename and image nodes change files in the input folder itself — they rename, resize, recompress or convert the originals before Folder Output delivers them — so point the input at a copy of your files.

## Pipelines

### photo-import-by-date.ffpipe
Extracts EXIF date from photos and renames them with the date prefix.
- **Nodes:** Folder Input → Metadata Extract → Rename (Pattern) → Folder Output
- **Filter:** `*.jpg;*.jpeg;*.png;*.heic`

### batch-sequential-rename.ffpipe
Renames all files with sequential numbering (001, 002, ...).
- **Nodes:** Folder Input → Rename (Pattern) → Folder Output

### image-web-export.ffpipe
Resizes images to max 1920px wide and converts to WebP format.
- **Nodes:** Folder Input → Filter → Image Resize → Image Convert → Folder Output

### bulk-image-compress.ffpipe
Compresses JPEG images to 80% quality.
- **Nodes:** Folder Input → Filter → Image Compress → Folder Output

## Usage

### CLI
```
dotnet run --project src/FlowForge.CLI -- run samples/photo-import-by-date.ffpipe --input ./my-photos --output ./sorted --dry-run
```

### UI
1. Open FlowForge
2. Click Open in the toolbar and select a `.ffpipe` file from this directory
3. Edit the Source Folder and Output Folder in the Properties panel
4. Click DRY RUN to simulate, then IGNITE to run
