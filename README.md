# TaskOverlay

Portable Windows 10/11 desktop overlay for editable tasks.

## Status

WPF v2 is the active product. All new development happens in `v2/`.

Go v1 (`cmd/taskoverlay/`) is a legacy prototype. It is no longer built or distributed.
Do not use Go v1 for testing or reporting issues.

## Windows WPF v2

A Windows-only WPF app lives in `v2/`. It validates transparent
overlay rendering, hover activation, tray lifecycle, a separate settings window,
and practical DPI-aware monitor placement.

Build and run:

```powershell
dotnet restore .\v2\TaskOverlay.sln --configfile .\v2\NuGet.Config
dotnet build .\v2\TaskOverlay.sln --configuration Release --no-restore
dotnet run --project .\v2\tests\TaskOverlay.Core.Tests\TaskOverlay.Core.Tests.csproj --configuration Release --no-build
dotnet run --project .\v2\src\TaskOverlay.App\TaskOverlay.App.csproj
```

V2 stores its independent local state at
`%APPDATA%\TaskOverlayV2\state.json`. On first run, the three prototype tasks are
created as seed data. Click a task marker or row to complete it; completed tasks
are saved and removed from the overlay. The Go v1 state is not read or modified.

V2 provides three clipboard intake modes through the tray and fixed global
hotkeys:

- `Ctrl+Alt+A` creates one task for every non-empty clipboard line.
- `Ctrl+Alt+S` creates one task and collapses clipboard lines into its title.
- `Ctrl+Alt+D` creates one task whose first non-empty line is the title and
  remaining text is the description.
- `Ctrl+Alt+T` shows or hides the overlay.

Created tasks are saved together in one atomic state update and the overlay is
shown. Empty clipboard text is ignored and logged.

V2 runtime and crash logs are stored under
`%APPDATA%\TaskOverlayV2\logs`. Unhandled exceptions create a dedicated
`crash-<timestamp>.log` containing the exception chain, stack traces, state path,
shutdown status, and current overlay mode.

## Download and run

Go to the Actions tab and open the latest successful build.
Download TaskOverlayV2_WPF_FrameworkDependent.
Extract the archive.
Run TaskOverlay.V2.exe.

Requirement: .NET Desktop Runtime 8.0 must be installed. If you are not sure whether it is installed, run:

```powershell
dotnet --list-runtimes
```

Look for Microsoft.WindowsDesktop.App 8.0.x in the output.

Note: If you see TaskOverlay.exe instead of TaskOverlay.V2.exe, you downloaded the wrong artifact or an old release. Do not use it.

See `docs/V2_ARCHITECTURE.md` for scope and limitations.

The app is intentionally local-only:
- no installer required;
- no server;
- no internet dependency;
- state is stored in `%APPDATA%\TaskOverlay\state.json`;
- logs are stored in `%APPDATA%\TaskOverlay\logs`.

## Current behavior preserved from v13

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

Legacy (Go v1 prototype - not maintained)

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

Legacy (Go v1 prototype - not maintained)

```bash
GOOS=windows GOARCH=amd64 go build -ldflags="-H windowsgui" -o build/TaskOverlay.exe ./cmd/taskoverlay
```

## Release policy

Legacy (Go v1 prototype - not maintained)

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

The refactor does not change the state path:

```text
%APPDATA%\TaskOverlay\state.json
```

The original v13 source is preserved in:

```text
legacy/main_v13.go
```

This allows behavior comparison during later changes.
