# How DriveBox works

DriveBox is a WPF application (C#, .NET 10). The UI is built mostly in code; there is no charting library. Charts are drawn with WPF shapes, or with `DrawingContext` for the 3D view, and animated with WPF animations that run on the GPU compositor.

## Source map

| File | Responsibility |
|---|---|
| `MainWindow.xaml(.cs)` | Window, sidebar, navigation, scan progress view, theme toggle, Apps page host, sensor panel host |
| `Scanner.cs` | Parallel directory scanner (`FileSystemEnumerator`), builds the in-memory `DirNode` tree; `SizeOf` for quick folder totals |
| `MftScanner.cs` | NTFS master-file-table reader (administrator only); returns the same `DirNode` tree, or `null` to fall back |
| `Stats.cs` | One pass over a subtree: extensions, categories, ages, months, size buckets, largest files, per-folder aggregates |
| `Dashboard.cs` | Card shell (shadow, hover float, GPU texture cache), the first set of 2D cards, animation helpers (`Pop`, `Grow`, `Bounce`) and section headings |
| `Charts2.cs` | Second set of cards: flow charts, age columns, stacked years, donut, square bubbles, dark 3D cards |
| `Chart3D.cs` | Small 3D renderer (perspective + rotation), grid box, spheres, pins, ridge terrain |
| `AppScanner.cs`, `AppsPage.cs` | Installed apps and games, their size and their data; the Apps & games page |
| `Sensors.cs`, `SensorPanel.cs` | Once-a-second CPU/GPU sampling (LibreHardwareMonitor), 1 h / 24 h history, the right-hand panel |
| `Theme.cs` | Light/dark palettes (window resources) and persistence |

## Design notes

**Scan once, navigate instantly.** A scan produces a tree of `DirNode` objects (sizes already summed). Clicking around only re-computes `Stats` for the chosen subtree on a background thread.

**Parallel scanner.** Directories are enumerated with `FileSystemEnumerator<T>`, which avoids allocating a `FileInfo` per file, and sub-folders are processed with `Parallel.For` at every level (up to 16 at a time). Each top-level folder reports a `LiveItem` so the UI can show progress.

**MFT mode.** With administrator rights the volume (`\\.\C:`) is opened and the `$MFT` is read in large sequential chunks while the previous chunk is parsed on all cores. Records give name, parent, size and modified time; the tree is rebuilt from parent references. Anything unexpected returns `null` and the normal scanner takes over.

**Charts and animation.** `ChartCanvas` redraws on size change. While drawing, every entrance animation registers itself in `Replays`, so a hover can replay the animations on the existing shapes without rebuilding the chart. Once a card has settled, its body is cached as a single GPU texture (`BitmapCache`), so the hover float is a texture move.

**3D.** `Chart3D` projects points with a yaw/pitch rotation and perspective divide, sorts by depth and draws through `DrawingContext`. `Intro` and `Spin` are dependency properties with `AffectsRender`, so animating them re-renders automatically.

**Theme.** Colours used by code come from `G.*` properties that check `Theme.Dark`; XAML uses `DynamicResource` keys set by `Theme.Apply()`. The 3D cards force the dark palette while they are built.

**Apps & games.** Install locations come from Steam manifests, Epic manifests, `XboxGames` and the uninstall registry keys. Data folders are matched by normalised name against the top-level folders of AppData, ProgramData, LocalLow, Documents and Saved Games; each data folder is assigned to the single best-matching app.

**Sensors.** `SensorService` polls LibreHardwareMonitor once a second, keeps 3600 one-second samples and 1440 one-minute averages (persisted as CSV), and `Get(range)` averages a window down to at most 150 points for drawing.
