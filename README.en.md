<p align="center">
  <img src="assets/banner.png" alt="Notchi banner" width="100%" />
</p>

<h1 align="center">Notchi</h1>

<p align="center">
  <a href="README.md">Bahasa Indonesia</a> | <a href="README.en.md">English</a>
</p>

Notchi is a notch overlay for Windows 10/11 inspired by macOS and Dynamic Island. It floats above the desktop, stays compact when idle, and expands on hover to show media controls or system information. Built natively with WPF without Electron, so it stays lightweight.

## Features in v1.0.1

- Media controls via Windows SMTC: title, artist, album art, progress, play/pause, and skip.
- Idle dashboard with clock, date, battery, and local CPU, RAM, and disk usage.
- Feature menu with Notes, Settings, and Quit.
- Notes with automatic saving.
- File drop shelf to open files, show them in Explorer, copy paths, open all, or clear the list.
- Drag the notch anywhere, then it snaps to the top center, left, or right.
- The last position is saved and restored on startup.
- Settings for visible metrics and Windows autostart.
- Lightweight, with idle CPU around 0.07% and private memory below 100 MB.
- Single instance only; launching the app twice does not create duplicate notches.

## Fixes in v1.0.1

- Prevented two Notchi instances from running at the same time.
- Removed accidental quitting from a right-click.
- Moved notes to `%LOCALAPPDATA%\Notchi` with automatic migration from Downloads.
- Added application and installer version metadata and icons, plus running-instance detection during setup.

## Stack

- Language: C# with .NET 8.
- UI: WPF and XAML, QuinticEase animations.
- Media: Windows System Media Transport Controls.
- System metrics: Win32 interop and DriveInfo.
- Storage: System.Text.Json at `%LOCALAPPDATA%\Notchi\settings.json`, notes at `%LOCALAPPDATA%\Notchi\Notchi Note.txt`, and logs at `%LOCALAPPDATA%\Notchi\notchi.log`.
- Autostart: HKCU registry.
- Installer: Inno Setup, per-user.

## Contributing

This project is open for contributors. Bug reports, feature ideas, documentation improvements, and pull requests are welcome. For larger changes, open an issue first so it can be discussed.

## Contact

- Owner: risuunava
- Email: [risuunava@gmail.com](mailto:risuunava@gmail.com)
- GitHub: [@risuunava](https://github.com/risuunava)
- Repository: [github.com/risuunava/Notchi](https://github.com/risuunava/Notchi)

