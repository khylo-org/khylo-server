# Khylo Server — Unified Media Model Specification

**Document Version:** 1.0.0  
**Date:** September 2026  
**Status:** Canonical Media Domain Design

---

## 1. Unified Media Domain Abstraction

Previous media server generations enforced a rigid movie/series hierarchy, forcing music, photos, and books to imitate television episodes or songs. Khylo models media as a **pluggable composite hierarchy**:

```text
MediaItem (Core Entity)
├── Video
│   ├── Movie
│   ├── Series -> Season -> Episode
│   ├── MusicVideo
│   ├── Trailer
│   └── OpticalDiscStructure (DVD / Blu-ray)
│
├── Audio
│   ├── MusicTrack (Artist -> Album -> Track)
│   ├── Audiobook (Author -> Series -> Book -> Chapter)
│   └── Podcast (Syndicated Feed -> Episode)
│
├── Reading
│   ├── Ebook (EPUB, PDF, MOBI, AZW, AZW3, FB2, DOCX)
│   └── Comic (CBZ, CBR, CB7, CBT -> Series -> Volume -> Issue)
│
├── Image
│   ├── StandardPhoto (JPEG, PNG, GIF, WebP, AVIF, HEIC)
│   └── CameraRaw (CR2, CR3, NEF, ARW, ORF, RW2, DNG)
│
└── Game
    ├── RomImage (NES, SNES, GB/GBA, Genesis, N64, MS-DOS)
    └── GameDiscImage (PS1, Saturn, Dreamcast, PC Engine CD)
```

---

## 2. Media Families & Identification Rules

### 2.1. Video & Optical Disc Media
- **Containers:** MKV, MP4, WebM, AVI, MOV, TS, M2TS, VOB, ISO, BDMV folder sets.
- **Context-Aware ISO Handling:** An `.iso` file is never assumed to be a movie. The scanner inspects directory context, volume descriptors, and internal file layouts:
  - If containing `VIDEO_TS.IFO` -> DVD-Video.
  - If containing `BDMV\index.bdmv` -> Blu-ray.
  - If located in a Game library or containing `SYSTEM.CNF` -> PlayStation / Disc Game.

### 2.2. Audio Platform (Music, Audiobooks, Podcasts)
- **Music:** Hierarchical `MusicArtist -> MusicAlbum -> Audio (Track)`. Supports ID3v2, Vorbis Comments, FLAC tags, Opus tags.
- **Audiobooks:** First-class entity `AudioBook` distinct from music songs. Supports author and narrator relationships, chapter marks, variable playback speed metadata, and dedicated resume bookmarks.
- **Podcasts:** Independent feed synchronization model tracking RSS/Atom feeds, audio/video enclosure downloads, publication timestamps, and played/unplayed status.

### 2.3. Reading Platform (Ebooks & Comics)
- **Ebooks:** Native parsing of OPF metadata, EPUB covers, PDF title/author metadata, and table of contents. Server maintains reading progress tracking page index and EPUB CFI (Canonical Fragment Identifier).
- **Comics:** Archive inspection of CBZ (ZIP) and CBR (RAR) containers without extracting entire archives to disk. Parses `ComicInfo.xml` when present (series, issue number, volume, story arcs, pencillers, colorists).

### 2.4. Photo & Camera RAW Platform
- **Standard Imagery:** Fast metadata extraction (EXIF camera model, lens, ISO, shutter, aperture, GPS) and multi-resolution thumbnail generation.
- **Camera RAW:** Dedicated processing pipeline extracting embedded JPEG thumbnail/preview streams from RAW formats (CR2, CR3, NEF, ARW, DNG) for sub-second gallery loading without memory bloat.

### 2.5. Games & Emulation Platform
- **Game Files:** Classified by file signature (magic bytes) and library context across classic platforms (Nintendo NES, SNES, N64, Game Boy; Sega Genesis, Master System, Saturn, Dreamcast; Sony PS1; MS-DOS).
- **Metadata:** Title, platform, developer, publisher, release year, region, genre, box art, cartridge scan, and controller mapping requirements.
- **Save State Decoupling:** Stores save states and memory card data indexed by game checksum and user ID.
