# Notchi

A lightweight Windows "notch" overlay inspired by the macOS notch and Dynamic Island. It floats above your desktop, stays out of your way when idle, and expands on hover to show media controls.

## Features

- **Media controls everywhere** — track title, artist, album art, progress, and play/pause/skip via the Windows System Media Transport Controls (SMTC) API. Works with Spotify, YouTube in Chrome/Edge, and other apps that expose a media session.
- **Smart thumbnail handling** — Chrome sometimes reports its app icon before the video artwork is ready. Notchi re-reads the thumbnail for 5 seconds after a track change so the real artwork replaces the fallback, and retries failed loads instead of caching an empty result.
- **Idle dashboard** — when no media session is active, the collapsed capsule shows the time, day, and date. Hovering reveals the full date, battery status, and local CPU/RAM/disk usage.
- **Settings window** — toggle CPU/RAM/disk/battery metrics and enable or disable Windows autostart. Changes are saved immediately.
- **Auto-start at login** — optional `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entry, disabled by default. The saved path is refreshed automatically when Notchi launches from a new location.
- **Quick file drop shelf** — drag files or folders onto the notch to open a compact staging shelf with **Buka**, **Tampilkan di Explorer**, **Salin path**, **Buka semua**, and **Bersihkan** actions. The shelf stays open until you close it, and dropping more files adds them to the same list.
- **Hover animation** — smooth expand/collapse using `QuinticEase` (EaseOut, 0.32 s).
- **Drag & drop with snapping** — drag the pill anywhere; while dragging it renders as a clean capsule, and on release it snaps to the nearest anchor:
  - Top center
  - Left center (side-docked vertical capsule)
  - Right center (side-docked vertical capsule)
- **Persistent position** — the chosen anchor is saved and restored on startup.
- **Low footprint** — native WPF with no Electron/webview; idle CPU measured at `0.07%` and private memory below `100 MB`.
- **Right-click to quit.**

## Requirements

- Windows 10 version 2004 (build 19041) or later, or Windows 11 (x64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build from source
- .NET 8 Desktop Runtime only if you use a framework-dependent publish

## Getting Started

```powershell
git clone https://github.com/risuunava/Notchi.git
cd Notchi
dotnet run
```

Build without running:

```powershell
dotnet build -c Debug
```

The executable is produced at:

```text
bin\Debug\net8.0-windows10.0.19041.0\Notchi.exe
```

## Usage

| Action | Result |
| --- | --- |
| Hover over the notch | Expands to the media player, or the idle clock/metrics dashboard |
| Hover the icon next to the notch | Opens the feature menu (Catatan / Pengaturan / Keluar) |
| Left-click and drag | Moves the notch as a capsule |
| Release after dragging | Snaps to the nearest anchor (top center / left / right) |
| Drag files or folders onto the notch | Opens the file drop shelf; drop more files to add them to the list |
| Right-click | Closes the app |

The saved anchor lives at:

```text
%LOCALAPPDATA%\Notchi\settings.json
```

Example:

```json
{
  "Anchor": "RightCenter",
  "ShowCpu": true,
  "ShowRam": true,
  "ShowDisk": true,
  "ShowBattery": true,
  "AutoStart": false
}
```

## Publish

Build a single-file, self-contained executable that runs on another Windows laptop without installing the .NET runtime:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o .\publish\win-x64
```

Output:

```text
publish\win-x64\Notchi.exe
```

For a smaller framework-dependent build, use `--self-contained false`; the target machine must then have the .NET 8 Desktop Runtime installed.

## Project Structure

```text
Notchi/
├── App.xaml / App.xaml.cs      # Application entry point
├── MainWindow.xaml             # Notch layout, shapes, and control templates
├── MainWindow.xaml.cs          # Window positioning, anchors, drag & drop, animations
├── SettingsWindow.xaml/.cs     # Metric toggles and autostart settings
├── DropShelfWindow.xaml/.cs    # File/folder quick actions shelf
├── AppSettings.cs              # Persisted settings model
├── SettingsStore.cs            # JSON load/save for settings
├── StartupService.cs           # HKCU Run registry integration
├── CrashLogger.cs              # Local crash log writer
├── SmtcService.cs              # SMTC media session wrapper (metadata, artwork, controls)
├── SystemStatusService.cs      # Local CPU/RAM/disk/battery sampling for the idle dashboard
├── Notchi.csproj               # .NET 8 WPF project
└── notch_application_prd_roadmap.md  # Product requirements and roadmap
```

## Notes & Limitations

- Album art and metadata depend on what the source app publishes through SMTC. If an app never provides artwork, no thumbnail can be shown.
- Autostart uses the current user's registry hive, so it needs no administrator rights. If the executable is moved after autostart is enabled, launch it once from the new location to refresh the saved path.
- The file drop shelf is session-only; its list is cleared when the shelf closes. It does not move or upload files. The shelf does not close when it loses focus, so you can drag more files onto the notch and add them to the same list.
- System metrics are read locally and are only shown in the expanded idle dashboard. There is no network, weather, or internet integration. Each metric can be hidden from the Settings window or with the `ShowCpu`, `ShowRam`, `ShowDisk`, and `ShowBattery` settings.
- The side-docked collapsed capsule intentionally shows only the time, day, and date; CPU/RAM/disk/battery appear after hovering.

## Troubleshooting

If Notchi closes unexpectedly, check the local crash log:

```text
%LOCALAPPDATA%\Notchi\notchi.log
```

The log records unhandled UI, application-domain, and background-task exceptions. When it grows beyond 1 MB, the previous file is kept as `notchi.log.old`.

## License

Not for free use. All rights reserved.
