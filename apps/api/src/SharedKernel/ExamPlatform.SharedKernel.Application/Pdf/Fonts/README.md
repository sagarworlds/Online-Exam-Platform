# PDF fonts

These files are embedded in the assembly as resources and read by the PDF writer
(`PdfDevanagari`), so Devanagari (Hindi, Marathi) text prints in PDF exports (FR-38, FR-51, FR-10).
Latin text does not use them: it stays in the standard Helvetica faces, which need no file.

## Noto Sans Devanagari, Regular: `NotoSansDevanagari-Regular.ttf`

| | |
|---|---|
| Version | 2.007 (the font's name table reads "Version 2.007") |
| Source | notofonts/devanagari release `NotoSansDevanagari-v2.007`: `NotoSansDevanagari/full/ttf/NotoSansDevanagari-Regular.ttf`, from `https://github.com/notofonts/devanagari/releases/download/NotoSansDevanagari-v2.007/NotoSansDevanagari-v2.007.zip` |
| Also listed by | Google Fonts, `ofl/notosansdevanagari` (its metadata names the same release archive) |
| SHA-256 | `573e525d9f4923d34645fe752475419f45f8dec5a8e43f3e83c5c5f43d1f2ff9` |
| Licence | SIL Open Font License 1.1, in `OFL.txt` (copied from the same release). Copyright 2022 The Noto Project Authors. |

The file is the full TrueType build, unmodified. Its glyph IDs are the ones the PDF writer shapes with HarfBuzzSharp and writes into the
PDF, so replacing the file with another version changes which glyphs are drawn. Update the version and the hash here when it changes.

## Licence

`OFL.txt` is the licence that applies to the font. Keep it with the font when either is copied or redistributed.
