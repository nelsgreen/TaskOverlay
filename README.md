# TaskOverlay

Portable Windows 10/11 desktop overlay for editable tasks.

The app is intentionally local-only:
- no installer required;
- no server;
- no internet dependency;
- state is stored in `%APPDATA%\TaskOverlay\state.json`;
- logs are stored in `%APPDATA%\TaskOverlay\logs`.

## Quest-tracker overlay

TaskOverlay starts in passive mode:

- only non-completed task text and simple markers are visible;
- the window background, borders, title, and controls are hidden;
- completed tasks are never shown.

Move the mouse into the overlay area to enter active mode. The normal background,
editing controls, settings access, task details, and task actions appear. After the
mouse leaves, TaskOverlay returns to passive mode after three seconds unless an edit
is active.

Passive marker styles are available in settings:

- dot (default);
- dash;
- arrow;
- checkbox.

Completed tasks can be shown or hidden in active mode. They are shown by default.

## Existing behavior preserved

- editable task title;
- task description;
- subtasks;
- done status;
- completed section;
- priority;
- in-work marker;
- due time / blink notification;
- local JSON state;
- diagnostics export;
- topmost overlay;
- resize;
- visual settings.

## Repository layout

```text
TaskOverlay/
  cmd/taskoverlay/       Windows app source
  assets/                Icon and static assets
  build/                 Local build scripts and generated artifacts
  docs/                  Architecture notes
  legacy/                Original v13 single-file source for comparison
  .github/workflows/     GitHub Actions build workflow
```

## Build locally on Windows

Requirements:
- Go 1.23 or newer;
- Windows 10/11.

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\build\build_windows.ps1
```

Output:

```text
build\dist\TaskOverlay.exe
build\dist\TaskOverlay_portable.zip
```

## Cross-build from Linux/macOS

```bash
GOOS=windows GOARCH=amd64 go build -ldflags="-H windowsgui" -o build/TaskOverlay.exe ./cmd/taskoverlay
```

## Release policy

Use semantic tags:

```text
v13.1.0
v14.0.0
```

Recommended release artifact:

```text
TaskOverlay_portable.zip
```

The portable ZIP should contain:

```text
TaskOverlay.exe
README.txt
CHANGELOG.txt
```

## State compatibility

The state path remains:

```text
%APPDATA%\TaskOverlay\state.json
```

The original v13 source is preserved in:

```text
legacy/main_v13.go
```

This allows behavior comparison during later changes.

Version 14 adds optional settings for the passive marker and completed-task
visibility. Existing version 13 state files are migrated in place without changing
task data.
