# DriveBox user guide

## The window

```text
┌───────────────┬──────────────────────────────────────────┬───────────────┐
│ Sidebar       │ Top bar: ↑ Up · breadcrumb · 📈 Sensors   │ Live sensors  │
│  theme / fast │──────────────────────────────────────────│  1m 5m 30m …  │
│  path box     │ Dashboard cards for the current folder   │  Temperature  │
│  quick access │  (2D charts, then 3D charts)             │  Power        │
│  partitions   │                                          │               │
│  🎮 Apps      │                                          │               │
│  contents     │                                          │               │
└───────────────┴──────────────────────────────────────────┴───────────────┘
```

### Sidebar
* **Light / Dark**: switches the theme (remembered).
* **⚡ Fast mode**: restarts DriveBox as administrator so drives can be scanned by reading the NTFS master file table. Once active the button reads "Fast mode ON".
* **Path box + Go**: type or paste a folder and press Enter.
* **Browse…, Open in Explorer, Rescan**: pick a folder, show the current one in Explorer, scan it again.
* **Quick access**: Home, Desktop, Documents, Downloads, AppData.
* **Partitions**: one button per drive with a usage bar. Click to scan the whole drive.
* **🎮 Apps & games storage**: see [Apps & games](#apps--games).
* **Contents**: the current folder's sub-folders and biggest files with a size bar. Click a folder to open it, double-click a file to show it in Explorer.

### Top bar
* **↑ Up** goes to the parent folder (or back from the Apps & games page).
* The **breadcrumb** jumps to any folder above the current one.
* **📈 Sensors** shows or hides the right-hand sensor panel.

## Scanning
When you pick a drive or folder, a live panel lists each top-level folder as it is discovered; bars grow while it is scanned and turn green with a ✓ when finished. The status line shows files per second. **Cancel** stops the scan.

After the scan, everything is in memory, so moving between folders is instant. *Rescan* reads the disk again.

### Fast mode
Fast mode reads the NTFS master file table straight from the volume, which is far quicker than walking folders. It needs administrator rights and only applies when you scan the **root of an NTFS drive** (for example `C:\`). If it cannot be used for any reason, DriveBox silently uses the normal scanner.

## Reading the charts
All numbers refer to the **folder you are looking at**, including everything inside it.

* **Disk map**: tile area is the folder's share of the partition (or of the folder if the drive size is unknown). The top bar shows the same folders as coloured segments; grey is everything smaller.
* **Storage growth**: cumulative size by file *modified date*, split into categories (Media, Code, Documents, Archives, System, Other). The three markers show total size at three points in time. The top figures compare files changed in the last 7 days / 30 days / year with the period before.
* **Modified by month**: colour is the number of files modified in that month (green few, red many); the card always shows the current year and the four before it.
* **How old are your files?**: files grouped by last-modified age. "Untouched 1+ year" is a good starting point for cleaning up.
* **Largest files**: click a dot to show that file in Explorer.
* **Folders: files vs file size**: horizontal position is the number of files, vertical position the average file size, square size the total size. Click a square to open the folder.
* **3D cards**: drag to rotate, hover to spin slowly.

Hover any 2D card to lift it and replay its animation.

## Apps & games
Click **🎮 Apps & games storage**. DriveBox builds the list from:

1. **Steam** libraries (`appmanifest_*.acf`),
2. **Epic Games** manifests,
3. **Xbox** (`XboxGames` folders),
4. the **Windows installed programs** list (this also catches Ubisoft Connect, GOG, Riot, Battle.net and ordinary apps).

For each entry it measures the **install folder** and then matches **data folders** by name in `%LocalAppData%`, `%AppData%`, `%ProgramData%`, `LocalLow`, *Documents*, *Documents\My Games* and *Saved Games*. The orange part of each bar is the program, the purple part is its data.

* Filter with **All / Games / Apps**, sort by **Total / Program / Data**, search by name or publisher.
* Click a row to open its install folder; hover for the data folders that were found.
* Entries under 1 MB are hidden. Data matching is by name and can miss or over-match, so treat it as an estimate.

## Live sensors
* Choose **1m, 5m, 30m, 1h, 12h or 24h**.
* Temperature card: CPU and GPU in °C with average and maximum for the window.
* Power card: CPU, GPU and total in watts, average, peak and the **energy** used in the window.
* Hover a chart to read the value at that time.
* GPU readings work immediately. For **CPU** temperature and power use *Enable CPU sensors (restart as admin)*.
* The 12 h and 24 h views fill up as DriveBox keeps running; history is saved every minute, so it survives restarts.

## Files DriveBox writes
| Path | Content |
|---|---|
| `%AppData%\DriveBox\theme.txt` | `light` or `dark` |
| `%AppData%\DriveBox\sensors.csv` | one line per minute for the last 24 h |

Delete the folder to reset everything. Uninstalling the MSI removes the program; these two files are left behind.

## Troubleshooting
| Problem | What to do |
|---|---|
| SmartScreen warning | The app is not signed yet. *More info → Run anyway*. |
| Scan of `C:\` misses some folders | Some folders are protected. Run DriveBox as administrator (⚡ Fast mode). |
| CPU temperature shows "—" | Needs administrator rights; use *Enable CPU sensors*. |
| Apps & games lists little data for a game | Data is matched by folder name. Some games keep saves under a different name. |
| Window looks blurry on a high-DPI screen | Right-click the exe → Properties → Compatibility → change high DPI settings → *Application*. |
