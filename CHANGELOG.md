# Changelog

All notable changes to DiskLens. The format follows [Keep a Changelog](https://keepachangelog.com/);
versions follow [SemVer](https://semver.org/).

## [0.2.1] – 2026-09-12

### Changed
- **Right-click now opens the Explorer menu** on Windows, with DiskLens' *Zoom treemap here* /
  *Zoom out* / *Reveal in tree* merged in on top. DiskLens' own menu moved to Shift+right-click.
- Files deleted through the Explorer menu disappear from the tree without a rescan.

### Fixed
- The Explorer menu did nothing in the published (trimmed) build: trimming disables classic COM
  interop. The shell integration now uses source-generated COM (`GeneratedComInterface`), which
  is trim-safe.

## [0.2.0] – 2026-09-12

Performance release: same features, roughly a third of the memory, a third less scan time, and a
UI that costs nothing while you look at it. Plus the Explorer context menu on Windows.

Measured on a 1.3 TB NTFS system drive with 3.75 million entries:

| | 0.1.0 | 0.2.0 |
|---|---|---|
| GC heap after scan | 553 MB | 210 MB |
| Working set after scan (MFT) | ~500 MB | ~250 MB |
| MFT scan | 5.6 s | 3.6 s |
| Idle wake-ups per second | 125 | 0 |
| Download (win-x64) | 43 MB zip, 115 MB exe | 49 MB unpacked (33 MB exe + natives) |

### Added
- **Explorer context menu** (Windows): Shift+right-click a file or folder in the tree or treemap –
  or pick *Explorer menu…* from our menu – to get the real shell menu with "Open with",
  "Send to", "Properties" and your installed shell extensions. Implemented over
  `IShellFolder`/`IContextMenu3` with the window's message stream hooked for lazy submenus.
- `--debug` switch for verbose logging; `--bench` prints memory and name statistics.

### Changed
- **Names are interned** in a de-duplicated UTF-8 pool (`NamePool`). Only 31 % of names on a
  typical drive are distinct; the pool stores each once and nodes keep a 4-byte id.
- **Directory aggregates moved to a side table**; per-node columns shrank from 92 B to 40 B
  (+32 B per directory). Timestamps are stored as seconds, depth as `ushort`.
- **MFT scanner parses straight into the tree's name pool** – no intermediate strings, no second
  interning pass. Parsing is sequential on the reader thread (it was never the bottleneck), reads
  keep four blocks in flight.
- **Treemap**: colours and labels are resolved once at layout time; extension classification is
  allocation-free; hit-testing uses a 48 px grid; paints are reused across frames. Hovering no
  longer allocates 80 000 strings per frame.
- **Render loop is event-driven**: the process blocks in the OS event queue when idle, animations
  wake it per frame, tooltips and caret blink use a one-shot timer. Partial repaints: an element's
  `Invalidate()` repaints only its region into a persistent offscreen scene.
- One aggressive, compacting GC after each scan returns scan-time buffers to the OS.
- Published builds are trimmed (`TrimMode=partial`) and ship native libraries next to the
  executable instead of extracting them to `%TEMP%` on first start.
- Navigating away from a running scan cancels it.

### Fixed
- Orphaned animations (e.g. drive tiles after navigating away) kept the loop spinning until they
  expired.
- `ChunkedArray` re-checked every chunk on each append, making tree building O(n·chunks). This
  alone halved the walk scanner's time.
- Deleted subtrees are excluded from statistics.

## [0.1.0] – 2026-09-12

First public release.

- Drive picker, tree view with size/share/items/date, nested squarified treemap, details panel with
  type breakdown and largest files, live updates while scanning.
- Scanner plug-ins: NTFS MFT reader (administrator, whole drive in seconds) and a portable parallel
  directory walk.
- Context menu: open in file manager, copy path, zoom, Recycle Bin / delete.
- Custom title bar on Windows (drag, Snap Layouts, double-click maximise), dark/light theme, DPI
  awareness, own Skia-based UI toolkit – no UI framework.
- CI on Windows and Ubuntu; release workflow producing win-x64, win-arm64 and linux-x64 packages.
