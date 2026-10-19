# Product Requirement Document (PRD) & Execution Roadmap
**Project Name:** Windows Notch (Notchi)  
**Target Platform:** Windows 10 / Windows 11 (Architecture x64)  
**Minimum Target Spec:** Laptop RAM 8GB DDR4, Intel Core i3 / Ryzen 3  
**Technology Stack:** C#, .NET 8.0, WPF (Windows Presentation Foundation)  

---

## 1. Project Overview & Objective
Develop a lightweight, responsive, and aesthetic overlay notch application for Windows. It provides quick access to media controls, system metrics, and productivity tools right from a top-center floating overlay, mimicking macOS/iOS dynamic notch behaviors without heavy memory consumption (Target RAM usage $< 60$ MB).

---

## 2. Core Features & Functional Requirements

### 2.1 Core UI & Animations
- **Frameless Overlay Window:** Topmost, borderless, transparent window positioned at the top-center of the primary monitor.
- **Hardware-Accelerated Animations:** Smooth 60 FPS expand/collapse transitions using WPF `DoubleAnimation` and `QuadraticEase` GPU acceleration on mouse hover.
- **Quick Close Control:** Mouse right-click event (`MouseRightButtonDown`) to terminate the process instantly during development and usage.

### 2.2 Widgets & Integrations
- **Universal Media Controls:** Display current playback track, artist name, album art, and controls (Play/Pause/Skip) via Windows System Media Transport Controls (SMTC) API (Spotify, Chrome, YouTube, etc.).
- **System Metrics Display:** Real-time indicator for battery percentage, CPU usage, and RAM usage inside the expanded notch view.
- **Quick File Drop Zone:** Temporary drag-and-drop zone to hold files for easy moving/sharing.

### 2.3 System & Performance Constraints
- **Startup Integration:** Optional "Start on Boot" registry entry (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`).
- **Low Footprint:** Idle RAM usage $< 60$ MB, CPU usage $< 1\%$ when collapsed.

---

## 3. Project Architecture & File Structure

```text
WindowsNotch/
├── App.xaml
├── App.xaml.cs
├── MainWindow.xaml       # UI Layout & Visual Elements
├── MainWindow.xaml.cs    # Window Logic, Position & Animations
└── WindowsNotch.csproj   # Project Configuration & Dependencies
```

---

## 4. Phased Execution Roadmap

### Phase 1: Refactoring UI & Layout Structure
- [x] Create basic frameless top-center floating window in WPF.
- [x] Implement smooth `DoubleAnimation` expand/collapse transition on hover.
- [x] Add right-click app termination handler.
- [ ] Refactor `MainWindow.xaml` to include structured Grid layouts for time, media info, and action buttons.
- [ ] Add drop-shadow or soft rounded borders for desktop integration.

### Phase 2: Media Player & System API Integration
- [ ] Add `Microsoft.Windows.SDK.Contracts` package dependency for native Windows API calls.
- [ ] Integrate `GlobalSystemMediaTransportControlsSessionManager` to read active media sessions.
- [ ] Connect play/pause/skip buttons in `MainWindow.xaml` to SMTC commands.
- [ ] Hook `SystemInformation.PowerStatus` to render real-time battery stats.

### Phase 3: Autostart, Optimization & Deployment
- [ ] Implement registry helper for Auto-Start on Windows boot.
- [ ] Verify memory/CPU usage limits in Task Manager during idle and active animation states.
- [ ] Compile self-contained single-file binary using `dotnet publish`:
  ```bash
  dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
  ```

---

## 5. Technical Guidelines for AI Agent
When generating or modifying code for this repository, adhere to the following rules:
1. **Framework:** Keep using .NET 8.0 WPF (`dotnet new wpf`).
2. **Performance First:** Avoid heavy third-party UI libraries or webview solutions (Electron/Tauri). Stick to native WPF controls and Win32 interop where necessary.
3. **Animations:** Always use GPU-backed WPF animations (`DoubleAnimation`, `Storyboard`) rather than UI thread sleep loops.
4. **Namespace Safety:** Ensure XML namespaces in `MainWindow.xaml` use `http://schemas.microsoft.com/winfx/2006/xaml`.