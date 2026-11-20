# NMSFE Startup Splash Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show the approved NMSFE splash while initial game databases load, then open the existing editor.

**Architecture:** Replace App.xaml's StartupUri flow with App.OnStartup. App shows a borderless SplashWindow, creates and loads a SaveSession on a worker thread, and passes that session into MainWindow. WPF embeds the generated transparent PNG as a Resource.

**Tech Stack:** .NET 10 WPF; existing System.Threading.Tasks and existing app/session types; no new dependencies.

**Spec:** docs/superpowers/specs/2026-11-14-nmsfe-startup-splash-design.md

## Global Constraints

- Keep the existing editor and save behavior intact.
- Preserve true alpha outside the splash artwork.
- Keep all source and preview artifacts under AgentWorkspace/NMSFE.
- No new packages.

## Review Focus

- Database initialization failure: splash closes and a readable error is shown.
- Database loading must not block the WPF dispatcher.
- The splash must close after MainWindow is shown, without triggering application shutdown.
- The first window stays centered and hidden from the taskbar.
- Resource lookup works from the built Preview6 directory.

---

### Task 1: Add the splash visual resource

**Files:**
- Create: `Desktop/Assets/NMSFE-Splash.png`
- Create: `Desktop/SplashWindow.xaml`
- Modify: `Desktop/Desktop.csproj`

- [ ] Copy the approved generated artwork into Desktop/Assets/NMSFE-Splash.png.
- [ ] Add a borderless, transparent, centered SplashWindow that displays the image at a comfortable fixed size and is hidden from the taskbar.
- [ ] Embed the image as a WPF Resource.

### Task 2: Sequence application startup

**Files:**
- Modify: `Desktop/App.xaml`
- Modify: `Desktop/App.xaml.cs`
- Modify: `Desktop/MainWindow.xaml.cs`

- [ ] Remove StartupUri and start through App.OnStartup.
- [ ] Show SplashWindow, resolve the packaged Resources directory, initialize a SaveSession and call LoadDatabase via Task.Run.
- [ ] Show a readable initialization error and shut down cleanly if database loading fails.
- [ ] Pass the initialized SaveSession into MainWindow and preserve the existing OnLoaded save discovery.

### Task 3: Build and verify Preview6

**Files:**
- Create: `Build/Preview6/`

- [ ] Run `dotnet build Desktop/Desktop.csproj -o Build/Preview6`.
- [ ] Confirm Preview6/NMSFE.exe exists and the build reports zero warnings and errors.
- [ ] Launch Preview6 and confirm the splash appears before the main editor and closes after startup completes.
