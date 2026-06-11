# TaskOverlay

Portable Windows 10/11 desktop overlay for editable tasks.

## Windows WPF v2 prototype

A separate Windows-only WPF prototype lives in `v2/`. It validates transparent
overlay rendering, hover activation, tray lifecycle, a separate settings window,
and practical DPI-aware monitor placement without replacing the Go v1 app.

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

GitHub Actions publishes two Windows artifacts:

- `TaskOverlayV2_WPF_FrameworkDependent` requires the .NET 8 Desktop Runtime.
- `TaskOverlayV2_WPF_SelfContained` includes the Windows x64 runtime, so a
  separate .NET installation is not required.

Open a successful **Build Windows WPF v2 prototype** workflow run and download
one artifact from its **Artifacts** section. Extract the downloaded zip once;
the app files are directly inside it, with no nested zip. Run
`TaskOverlay.V2.exe`.

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

The refactor does not change the state path:

```text
%APPDATA%\TaskOverlay\state.json
```

The original v13 source is preserved in:

```text
legacy/main_v13.go
```

This allows behavior comparison during later changes.
