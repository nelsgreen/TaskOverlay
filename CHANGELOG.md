# Changelog

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
