<p align="center"><img src="docs/icon.png" width="96" alt="DiskLens"></p>

# DiskLens

[![CI](https://github.com/NiceAustrian/DiskLens/actions/workflows/ci.yml/badge.svg)](https://github.com/NiceAustrian/DiskLens/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Disk space analyser in the spirit of TreeSize / WizTree / WinDirStat – C# on .NET 10, with its own
GPU-rendered UI (SkiaSharp, no UI framework) that runs on Windows, Linux **and Android**, and an
NTFS MFT reader that scans a 1.3 TB drive in ~4 seconds.

![Scan view](docs/scan-view.png)

## Features

- **Tree view** with size, share bar, item count and modification date; keyboard navigation.
- **Nested treemap** (squarified) with labelled folder frames, type colouring, hover/select sync,
  zoom by double-click, breadcrumb.
- **Details panel**: headline numbers, breakdown by file type, largest files.
- **Live updates** while the scan runs.
- **Scanners are plug-ins** chosen per target:
  - `ntfs-mft` – reads the Master File Table directly (Windows, administrator). 3.75 M entries on
    a 1.3 TB drive in ~3.6 s.
  - `generic-walk` – portable parallel directory walk on top of `FileSystemEnumerator`. Works
    everywhere; ~15 s (warm cache) for the same drive.
- Right-click on Windows opens the real Explorer menu (shell extensions, "Open with", "Send to",
  "Properties") with DiskLens' zoom/reveal items on top; deletions made there update the tree.
  Shift+right-click (or right-click elsewhere) gives DiskLens' own menu: open, copy path, zoom,
  Recycle Bin / delete.
- Custom title bar on Windows (drag, Snap Layouts, double-click maximise all still work), dark and
  light theme, DPI aware. Event-driven render loop: 0 wake-ups when idle, partial repaints on hover.

## Android

The same UI, scanner and treemap run on Android (API 30+). Download `DiskLens-x.y.z-android.apk`
from a release, install it, and grant *All files access* from the banner so the scan can see the
whole device. Tap a folder to expand it, long-press for the menu, the ⓘ button opens the details
sheet; rotate for the two-pane layout.

Build it yourself: `dotnet workload install android`, then
`dotnet build src/DiskLens.Android -c Release` (needs an Android SDK with platform 36 and a JDK 21;
`dotnet build -t:InstallAndroidDependencies -p:AcceptAndroidSDKLicenses=true` fetches the SDK parts).

## Build & run

```
dotnet run --project src/DiskLens.App                 # start with the drive picker
dotnet run --project src/DiskLens.App -- C:\Users     # scan a folder immediately
dotnet run --project src/DiskLens.App -- --light      # light theme
dotnet run --project src/DiskLens.App -- --bench C:\ --scanner ntfs-mft   # headless benchmark + memory stats
dotnet test
```

Requires the .NET 10 SDK. On Linux you need an OpenGL 3.3 capable driver (Mesa is fine); native
SkiaSharp binaries for Linux are pulled in via NuGet.

## Architecture

```
src/
  DiskLens.Core              model, scanner abstractions, treemap layout, statistics – no UI, no platform
  DiskLens.Scanners.Generic  portable walk scanner + DriveInfo volume provider
  DiskLens.Scanners.Windows  NTFS MFT scanner, elevation, Recycle Bin, Explorer menu
  DiskLens.Scanners.Android  storage volumes, "All files access", file ops
  DiskLens.Scanners.Posix    (placeholder for /proc/mounts, statx, ...)
  DiskLens.UI                the toolkit: element tree, flex layout, widgets, animation, touch
  DiskLens.UI.Desktop        Silk.NET window host (GLFW + OpenGL), custom Windows title bar
  DiskLens.Presentation      shell, views, treemap/tree/details controls – shared by every host
  DiskLens.App               desktop composition root
  DiskLens.Android           Android app: activity + GL view hosting the same UI
tests/DiskLens.Tests
tools/DiskLens.IconGen       renders the app icon (PNG + ICO) from code
```

### Model

`FsTree` is a struct-of-arrays: one `ChunkedArray<T>` per column (parent, name id, size, flags, …),
40 bytes per node plus a 32-byte side table entry per directory for the aggregates. Chunks never
move, so the UI can read the tree while a scanner keeps appending; totals are propagated up with
interlocked adds as entries arrive. Names live in a de-duplicated UTF-8 `NamePool`. 3.75 M nodes
take ~210 MB.

### Scanners

`IFileSystemScanner.ProbeAsync` tells the registry how well a scanner fits a target
(`NotApplicable` / `Fallback` / `Supported` / `Preferred`, plus `RequiresElevation`); the best
applicable one wins. Scanners write into an `IScanSink`, which owns node ids – they never touch the
tree directly. Adding a scanner (SMB, restic, S3, …) is one class plus a DI registration.

### UI toolkit

Retained element tree with absolute bounds, two-pass flex layout, per-frame animation ticks, and
Skia drawing straight into the GL framebuffer. Widgets: labels, buttons, scroll view, virtual rows,
text box, progress bar, popups. Fonts: Inter and JetBrains Mono (embedded, OFL). Icons: Lucide
paths (ISC).

## Licence

MIT for the code. Fonts under the SIL Open Font License, icons under ISC (see `src/DiskLens.UI/Assets`).
