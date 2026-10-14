# Image fixtures

Synthetic images for tests that need input the in-repo image library cannot produce. Each row is enough to regenerate the file with any independent encoder.

| File | Bytes | SHA-256 | Recipe |
|---|---|---|---|
| `ccitt-g3.tiff` | 494 | `d51b280e7721436f799fae406ea33b8c1a0390a99e750382c04e7559f6267d71` | Classic little-endian TIFF, 64×48, 1 bit per sample, 1 sample per pixel, PhotometricInterpretation 1 (BlackIsZero), Compression 3 (CCITT T.4, one-dimensional, no T4Options tag), one strip of 48 rows. Pixel (x, y) is black when ⌊(x + y) / 8⌋ is even, else white. Deterministic, no seed. No resolution, EXIF, ICC or XMP tags. |
| `ccitt-g4.tiff` | 282 | `382d3e07761349e40f6286288fd72f3130c5403ab72f06e0f45602499f60c980` | As `ccitt-g3.tiff`, with Compression 4 (CCITT T.6). |
