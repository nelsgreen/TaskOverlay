# Changelog

## v14.0.0

First quest-tracker passive overlay mode.

### Added

- Passive rendering with transparent window background and task-only rows.
- Passive marker styles: dot, dash, arrow, and checkbox.
- Hover activation with a three-second delayed return to passive mode.
- Setting to show or hide completed tasks in active mode.

### Changed

- Completed tasks and the completed section are always hidden in passive mode.
- App title, settings, close, add, checkbox, border, and resize controls are hidden in passive mode.
- The per-task subtask action uses a branch marker so active mode has one primary `+` Add button.
- State schema advanced to version 14 while preserving existing task and setting fields.
- Passive mode now shrinks the real window and hover hitbox to the visible task content bounds.
- Active mode restores the saved active window bounds without persisting passive dimensions.
- Background opacity is rendered independently from text instead of using whole-window alpha.

### Diagnostics

- Mode changes log active/passive bounds and whether global window alpha is enabled.
- Diagnostic exports include active bounds, passive bounds, mode, and opacity-model status.

### Preserved

- Task editing, descriptions, subtasks, due time and blinking, priority, in-work status, topmost behavior, diagnostics, and the Windows build workflow.

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
