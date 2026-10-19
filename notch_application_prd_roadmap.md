# Product Requirement Document (PRD) & Execution Roadmap
**Project Name:** Windows Notch (Notchi)  
**Target Platform:** Windows 10 / Windows 11 (Architecture x64)  
**Minimum Target Spec:** Laptop RAM 8GB DDR4, Intel Core i3 / Ryzen 3  
**Technology Stack:** C#, .NET 8.0, WPF (Windows Presentation Foundation)  

---

## 1. Project Overview & Objective
Develop a lightweight, responsive, and aesthetic overlay notch application for Windows. It provides quick access to media controls, system metrics, and productivity tools right from a top-center floating overlay, mimicking macOS/iOS dynamic notch behaviors without heavy memory consumption (Target RAM usage $< 100$ MB).

---

## 2. Core Features & Functional Requirements

### 2.1 Core UI & Animations
- **Frameless Overlay Window:** Topmost, borderless, transparent window, initially positioned at the top-center of the primary monitor.
- **Drag & Drop Repositioning:** The collapsed pill can be dragged freely with the left mouse button; while dragging it is rendered as a clean capsule (fully rounded ends). On release it snaps to the nearest anchor: `TopLeft`, `TopCenter`, `TopRight`, `LeftCenter`, or `RightCenter`. The chosen anchor is persisted to `%LOCALAPPDATA%\Notchi\settings.json`.
- **Side-Docked Mode:** Left/right anchors use a compact vertical layout (album art + pause control, or clock) that sits flush against the screen edge. The expanded state still uses the horizontal media layout.
- **Hardware-Accelerated Animations:** Smooth 60 FPS expand/collapse transitions using WPF `DoubleAnimation` with `QuinticEase` (EaseOut, 0.32s) on mouse hover.
- **Quick Close Control:** Mouse right-click event (`MouseRightButtonDown`) to terminate the process instantly during development and usage.

### 2.2 Widgets & Integrations
- **Universal Media Controls:** Display current playback track, artist name, album art, and controls (Play/Pause/Skip) via Windows System Media Transport Controls (SMTC) API (Spotify, Chrome, YouTube, etc.).
- **Clock Fallback:** When no media session is active, the collapsed notch displays the current time (`HH:mm`).
- **System Metrics Display:** *Deferred.* Real-time battery percentage, CPU usage, and RAM usage inside the expanded notch view are out of scope for the current release; the expanded view stays a pure media player.
- **Quick File Drop Zone:** *Deferred* to a later phase.

### 2.3 System & Performance Constraints
- **Startup Integration:** Optional "Start on Boot" registry entry (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`).
- **Low Footprint:** Idle CPU usage $< 1\%$ when collapsed. RAM is measured as private bytes; the original $< 60$ MB goal proved unreachable for .NET 8 WPF (an empty WPF template already uses ~79 MB private bytes), so the practical target is $< 100$ MB private bytes.

---

## 3. Project Architecture & File Structure

```text
WindowsNotch/
├── App.xaml
├── App.xaml.cs
├── MainWindow.xaml       # UI Layout & Visual Elements
├── MainWindow.xaml.cs    # Window Logic, Position & Animations
├── SmtcService.cs        # Windows Media Session (SMTC) Integration
└── Notchi.csproj         # Project Configuration & Dependencies
```

---

## 4. Phased Execution Roadmap

### Phase 1: Refactoring UI & Layout Structure
- [x] Create basic frameless top-center floating window in WPF.
- [x] Implement smooth `DoubleAnimation` expand/collapse transition on hover.
- [x] Add right-click app termination handler.
- [x] Refactor `MainWindow.xaml` to include structured Grid layouts for time, media info, and action buttons.
- [x] Add drag & drop repositioning with five snap anchors (top-left/center/right, left-center, right-center).
  - *Drag visual:* capsule shape with full corner radius while moving; anchor-specific silhouette (top flares or flush screen edge) once dropped.
  - *Persistence:* anchor saved to `%LOCALAPPDATA%\Notchi\settings.json` and restored on startup.
- [x] Add side-docked vertical mini layout for left/right anchors.
- [ ] Add drop-shadow or soft rounded borders for desktop integration.
  - *Dropped:* shadow removed intentionally; the notch uses a solid black `Path` silhouette with outward top flares and 18px bottom radius.

### Phase 2: Media Player & System API Integration
- [x] Add native Windows API access for SMTC.
  - *Implementation:* TFM raised to `net8.0-windows10.0.19041.0`, so `Windows.Media.Control` is available without the `Microsoft.Windows.SDK.Contracts` package.
- [x] Integrate `GlobalSystemMediaTransportControlsSessionManager` to read active media sessions.
- [x] Connect play/pause/skip buttons in `MainWindow.xaml` to SMTC commands.
  - *Also implemented:* seek via progress slider, album art, source-app badge, and clock fallback when no session exists.
- [ ] Hook `SystemInformation.PowerStatus` to render real-time battery stats.
  - *Dropped:* expanded view is a pure media player by design; battery/CPU/RAM metrics are out of scope.

### Phase 3: Autostart, Optimization & Deployment
- [ ] Implement registry helper for Auto-Start on Windows boot.
  - *Deferred* to a later phase.
- [x] Verify memory/CPU usage limits in Task Manager during idle and active animation states.
  - *Measured (Release, single-file, idle 40s):* CPU `0.07%` average, private bytes `74.9 MB`, working set `146.6 MB`, 17 threads.
  - *Reference:* an empty .NET 8 WPF app measures `78.6 MB` private bytes / `122.8 MB` working set on the same machine, so Notchi is already at or below the framework baseline.
- [x] Compile self-contained single-file binary using `dotnet publish`:
  ```bash
  dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
  ```
  - *Result:* `bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/Notchi.exe` (25.6 MB). Requires the .NET 8 Desktop Runtime on the target machine.

---

## 5. Technical Guidelines for AI Agent
When generating or modifying code for this repository, adhere to the following rules:
1. **Framework:** Keep using .NET 8.0 WPF (`dotnet new wpf`).
2. **Performance First:** Avoid heavy third-party UI libraries or webview solutions (Electron/Tauri). Stick to native WPF controls and Win32 interop where necessary.
3. **Animations:** Always use GPU-backed WPF animations (`DoubleAnimation`, `Storyboard`) rather than UI thread sleep loops.
4. **Namespace Safety:** Ensure XML namespaces in `MainWindow.xaml` use `http://schemas.microsoft.com/winfx/2006/xaml`.