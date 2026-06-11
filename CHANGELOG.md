# Changelog

## v2 clipboard task creation

### Added

- Added **Create task from clipboard** to the WPF v2 tray menu.
- Added tested clipboard parsing for single-line titles and multi-line
  title/description input.
- Added immediate atomic persistence and overlay reveal for newly created tasks.
- Added diagnostics for successful creation, empty clipboard text, and clipboard
  access failures.

### Not changed

- The Go v1 application and state remain untouched.
- Full task editing, hotkeys, themes, reminders, and v1 migration remain outside
  this prototype scope.

## v2 runtime stability diagnostics

### Fixed

- Stopped and detached the hover `DispatcherTimer` before overlay shutdown.
- Prevented storage writes and UI callbacks after shutdown begins.
- Made tray disposal and application shutdown idempotent and exception-safe.
- Kept state load, save, corrupted-state recovery, and backup failures from
  escaping into the WPF dispatcher.

### Added

- Added runtime logging and crash logs under
  `%APPDATA%\TaskOverlayV2\logs`.
- Added handlers for WPF dispatcher, AppDomain, and unobserved task exceptions.
- Added startup, state, task completion, tray command, and shutdown diagnostics.
- Added crash-log coverage to the dependency-free v2 Core tests.

## v2 local task state

### Added

- Added versioned `AppState`, `TaskItem`, `OverlaySettings`, and
  `WindowPlacement` models for the WPF prototype.
- Added atomic JSON persistence at `%APPDATA%\TaskOverlayV2\state.json`, with
  overwrite backups and corrupted-state recovery.
- Added first-run seed tasks and click-to-complete behavior for active overlay
  tasks.
- Added dependency-free tests for default state creation, save/load roundtrips,
  and corrupted-state backups.

### Not changed

- The Go v1 application, its state file, and its build workflow remain
  untouched.
- Full task editing and settings UI are still outside the prototype scope.

## v2 WPF prototype

### Added

- Added an isolated Windows-only .NET 8 WPF prototype under `v2/`.
- Added a transparent passive overlay with three static tasks and hover-active
  background behavior.
- Added a 500 ms return to passive mode.
- Added a tray icon with Show overlay, Hide overlay, Settings, and Exit commands.
- Added a separate settings window and Per-Monitor V2 DPI manifest.
- Added separate v2 architecture documentation and a path-scoped Windows CI
  workflow.

### Not changed

- The Go v1 application and its build workflow remain untouched.

## v13-refactor

Architecture-only refactor.

### Changed

- Split the original single `main.go` into focused source files.
- Preserved the `package main` build target to avoid behavior changes.
- Added repository structure.
- Added Windows build script.
- Added GitHub Actions workflow.
- Added README and architecture notes.
- Preserved original v13 source in `legacy/main_v13.go`.

### Not changed

- Runtime behavior.
- State file path.
- Log file path.
- Task model compatibility.
- Portable distribution model.
