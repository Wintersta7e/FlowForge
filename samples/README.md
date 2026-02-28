# FlowForge Sample Pipelines

Ready-to-run example pipelines for FlowForge. Edit the input/output paths before running.

## Pipelines

### photo-import-by-date.ffpipe
Extracts EXIF date from photos and renames them with the date prefix.
- **Nodes:** FolderInput -> MetadataExtract -> RenamePattern -> FolderOutput
- **Filter:** `*.jpg;*.jpeg;*.png;*.heic`

### batch-sequential-rename.ffpipe
Renames all files with sequential numbering (001, 002, ...).
- **Nodes:** FolderInput -> RenamePattern -> FolderOutput

### image-web-export.ffpipe
Resizes images to max 1920px wide and converts to WebP format.
- **Nodes:** FolderInput -> Filter -> ImageResize -> ImageConvert -> FolderOutput

### bulk-image-compress.ffpipe
Compresses JPEG images to 80% quality.
- **Nodes:** FolderInput -> Filter -> ImageCompress -> FolderOutput

## Usage

### CLI
```
dotnet run --project src/FlowForge.CLI -- run samples/photo-import-by-date.ffpipe --input ./my-photos --output ./sorted --dry-run
```

### UI
1. Open FlowForge
2. File -> Open -> select a `.ffpipe` file from this directory
3. Edit the Source Folder and Output Folder in the Properties panel
4. Click Preview to simulate, then Run to execute
