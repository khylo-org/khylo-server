# Khylo Server — Master Implementation Roadmap

**Document Version:** 1.0.0  
**Date:** September 2026  
**Status:** Living Engineering Roadmap  
**Scope:** Server Backend, Media Platform, Administration Interface & Architecture

---

## 1. Phase Progression & Status

| Phase | Description | Status | Primary Deliverables |
| :--- | :--- | :--- | :--- |
| **Phase 0** | **Project Control, Forensics & Rebrand Foundation** | **COMPLETED** | Baseline build verification, test suite execution, root logo asset inventory, Calico color system specification, initial architecture documents (`docs/architecture/*`). |
| **Phase 1** | **Khylo Server Architecture & Unified Media Domain** | In Progress | Core `MediaItem` domain abstraction, media capability flags, `MediaType` and `CollectionType` enumeration expansions. |
| **Phase 2** | **Server Administration UI Overhaul** | Ready | Dedicated server admin console built with the Calico design system, system health dashboard, live session monitor, storage inspector. |
| **Phase 3** | **Library Engine & Partial Scanning** | Queued | Intelligent file detection, event-driven change monitoring, incremental partial tree indexing. |
| **Phase 4** | **Metadata Platform & Asset Pipeline** | Queued | Multi-provider orchestration, local caching, normalized metadata relationships, unified artwork pipeline. |
| **Phase 5** | **Video Platform & Optical Media** | Queued | Container/codec detection, DVD/Blu-ray disc structure parsing, context-aware ISO classification. |
| **Phase 6** | **Audio Platform (Music)** | Queued | Artist/Album/Track hierarchy, audio stream analysis, replaygain support. |
| **Phase 7** | **Audiobook Backend** | Queued | Author/Narrator/Series/Book model, independent listening progress, chapter resume bookmarks. |
| **Phase 8** | **Podcast Backend** | Queued | RSS feed sync, episode download queue, played/unplayed synchronization. |
| **Phase 9** | **Ebook Platform** | Queued | EPUB/PDF/MOBI/AZW3 parser, reading progress tracking with page/CFI persistence. |
| **Phase 10** | **Comic Platform** | Queued | CBZ/CBR/CBT archive reader, `ComicInfo.xml` metadata extraction, issue ordering. |
| **Phase 11** | **Photo & Camera RAW Platform** | Queued | EXIF/IPTC/XMP extraction, RAW preview extraction pipeline (CR2/CR3/NEF/ARW/DNG). |
| **Phase 12** | **Game Library & Emulator Abstraction** | Queued | Classic platform models (NES to Dreamcast, MS-DOS), ROM classification, emulator core abstractions. |
| **Phase 13** | **Playback Backend & Hardware Transcoding** | Queued | Free hardware acceleration dashboard, subtitle defaults, intro/credit detection APIs, playlist watch state separation. |
| **Phase 14** | **Users, Authentication & Invitations** | Queued | Granular permissions, invitation token generator with auto-expiry and library limits. |
| **Phase 15** | **Activity, Events & Webhooks** | Queued | Categorized event streams (operational, security, media, audit), WebSocket notifications. |
| **Phase 16–21**| **Infrastructure, Storage, Caching, Diagnostics** | Queued | Multi-disk/SMB/NFS volume manager, automated verified backups, sensitive data log redaction. |
| **Phase 22–27**| **Migration Tooling, Testing & API Contract** | Queued | Non-destructive SQLite migration runbook, comprehensive test suites, finalized OpenAPI client contract. |

---

## 2. Architectural Decision Records (ADRs)

- **ADR-001: Server-First Boundary:** Client applications (mobile, TV, web viewer) are strictly deferred to Phase 2 of the broader Khylo ecosystem. This project delivers the backend foundation and the administrative console only.
- **ADR-002: Calico Design System:** Visual styling must adhere to the tri-color natural calico palette (Warm Orange `#D97732`, Deep Black `#171717`, Warm White `#FAF8F5`) without cartoon or animal-print textures.
- **ADR-003: Playlist Watch State Decoupling:** Playlists maintain independent playback progress (`PlaylistPlaybackState`) to prevent distorting episodic series "Continue Watching" queues.
- **ADR-004: Non-Destructive Database Evolution:** Auxiliary tables extend `BaseItemEntity` via foreign keys rather than continuously adding columns to the monolithic table.
