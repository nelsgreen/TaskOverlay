# Changelog

## v2 daily attention MVP

### Added

- Added persisted project colors and one-time KazChess, PLHIV, TaskOverlay, and
  Personal project seeding.
- Added Todo, InWork, Waiting, and Done task statuses with `waitingFor` data.
- Added reminder fields, practical presets, 30-second in-app processing,
  repeating schedules, snooze, and Still waiting actions.
- Added Quick Add from tray and `Ctrl+Alt+Q`, plus project-aware clipboard
  defaults.
- Added project, WAIT, and DUE overlay accents and due-first sorting.
- Extended Task Details with project, status, reminder time/repeat, and waiting
  fields.

### Compatibility

- Existing schema v2 files load additively; legacy `Completed`/`InWork` values
  normalize to the new status model.
- Windows toast notifications are deferred; reminder notification is in-app.
- Overlay modes, collapsed handle behavior, Tree Manager, workflows, and Go v1
  are preserved.

## v2 unified overlay modes and reliable handle dragging

### Added

- Replaced independent collapsed and pinned flags with persisted
  `AutoQuestTracker`, `CollapsedHandle`, and `PinnedExpanded` modes.
- Added a radio-style tray submenu and a right-click handle mode menu.
- Added backward-compatible migration from the old collapsed/pinned fields.
- Added Core coverage for mode serialization, legacy migration, and the
  click-versus-drag threshold.

### Fixed

- Gave the handle an independent mouse capture and five-DIP drag threshold.
- Prevented parent overlay dragging from stealing handle gestures after the
  first pixel.
- Preserved the collapsed anchor through screen-bound expansion and mode
  changes.

## v2 pinned active mode and interaction-safe collapse

### Added

- Added persisted `OverlaySettings.pinnedActiveMode`, defaulting to `false` for
  existing state files.
- Added checked tray command **Keep expanded** and handle-click pin toggling.
- Kept the activation handle visible with distinct collapsed, expanded, and
  pinned styling.
- Added a tested Core collapse policy covering pinning, task editor, context
  menu, Settings, modal dialogs, and dragging.

### Fixed

- Prevented automatic collapse while Task Details or Settings is open.
- Prevented collapse behind delete confirmations and validation dialogs.
- Preserved normal 500 ms collapse after the final interaction closes.
- Preserved pinned presentation across tray/hotkey hide and show cycles.

### Preserved

- Task interactions, collapsed anchors, snapping, clipboard hotkeys, JSON
  recovery, both WPF artifacts, and Go v1 remain unchanged.

## v2 task interaction and independent collapsed anchor

### Added

- Added marker-only completion and body-click in-work interaction.
- Added persisted `MultipleTasks` and `SingleTask` in-work modes with a Settings
  selector and backward-compatible defaults.
- Added in-work row highlighting, persisted description expansion, and
  active-only description display.
- Added task row actions for Edit, description visibility, in-work, completion,
  and confirmed deletion.
- Added a separate task details window with Save, Cancel, complete, in-work, and
  delete behavior.
- Added Core tests for focus modes, editing, completion, deletion, new settings,
  old-state defaults, and collapsed-anchor serialization.

### Fixed

- Separated normal overlay placement from the collapsed strip anchor.
- Prevented temporary screen-bound expansion offsets from overwriting the
  collapsed anchor.
- Kept marker/body actions from firing after a drag gesture.

### Preserved

- Clipboard modes, global hotkeys, tray lifecycle, 500 ms hover return, JSON
  recovery, both WPF artifacts, and Go v1 remain unchanged.

## v2 overlay placement and screen bounds

### Added

- Added drag movement for the active overlay and collapsed activation strip,
  while preserving click-to-complete for non-drag task clicks.
- Added 16-DIP snapping to every edge of the current monitor work area.
- Added persisted placement restoration, negative-coordinate support, and
  off-screen correction.
- Added monitor-bound expansion, task-list height constraints, and wrapping for
  long task titles.
- Added dependency-free geometry tests for snapping, negative monitor
  coordinates, and off-screen correction.

### Preserved

- Collapsed/passive hover behavior, 500 ms return timing, task storage,
  clipboard intake, global hotkeys, artifacts, and Go v1 remain unchanged.

## v2 collapsed quest tracker mode

### Added

- Added persistent `OverlaySettings.collapsedMode` with backward-compatible
  loading for existing schema v1 state files.
- Added a compact WPF activation strip that expands to the active task overlay
  on hover and collapses again after 500 ms.
- Added checked tray command **Toggle collapsed mode** and an informational
  Settings status line.
- Added tests for collapsed-setting persistence, serialization, and old-state
  default behavior.

### Preserved

- `Ctrl+Alt+T` still shows or hides the entire overlay.
- Passive mode, clipboard intake, global hotkeys, JSON recovery, artifacts, and
  the Go v1 application are unchanged.

## v2 clipboard modes and global hotkeys

### Added

- Added separate tray actions for clipboard lines, a collapsed single task, and
  a task with description.
- Added fixed global hotkeys: `Ctrl+Alt+A`, `Ctrl+Alt+S`, `Ctrl+Alt+D`, and
  `Ctrl+Alt+T`.
- Added independent hotkey registration diagnostics, no-repeat handling, and
  shutdown unregistration.
- Added a Settings placeholder section listing the fixed hotkeys.
- Expanded Core tests for all clipboard creation modes and empty-line handling.

### Not changed

- Hotkeys are not editable yet.
- The Go v1 application, state, and workflow remain untouched.

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
- Full task editing, editable hotkeys, themes, reminders, and v1 migration
  remain outside this prototype scope.

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
