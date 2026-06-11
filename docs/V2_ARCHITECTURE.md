# TaskOverlay v2 WPF prototype

TaskOverlay v2 is a Windows-only experiment under `v2/`. The Go application in
`cmd/taskoverlay/` remains the v1 implementation and is not referenced by v2.

## Prototype responsibilities

- `App.xaml.cs` owns process lifetime, the tray icon, and window creation.
- `TaskOverlay.Core` owns the versioned state model and local JSON persistence.
- `OverlayWindow` is a transparent, borderless, always-on-top WPF window.
- Passive mode renders active, non-completed task marker/text rows.
- Clicking a task row marks it completed, persists the change, and removes it
  from the overlay.
- Pointer entry enables a simple background panel; pointer exit returns to passive
  mode after 500 ms.
- `SettingsWindow` validates independent settings-window lifecycle.
- The manifest requests Per-Monitor V2 DPI awareness.

No v1 migration, full editing, hotkeys, clipboard integration, network access,
or advanced themes are included.

## State and storage

V2 never reads or writes the Go v1 state. Its state is stored at:

```text
%APPDATA%\TaskOverlayV2\state.json
```

`AppState` contains a schema version, tasks, overlay settings, window placement,
and timestamps. `TaskItem` includes a stable ID, title, description, completion,
priority, in-work state, creation/completion timestamps, and an optional due
time.

Writes use a temporary file in the same directory followed by an atomic replace
or move. Replacing a valid state creates `state.backup.json`. Invalid JSON is
preserved as `state.corrupt.<timestamp>.json`; the app then loads and writes the
three prototype tasks as fresh seed data. Storage recovery does not prevent the
overlay from starting if a backup or recovery write fails.

## Build and run

Requirements:

- Windows 10 or 11;
- .NET 8 SDK.

```powershell
dotnet restore .\v2\TaskOverlay.sln --configfile .\v2\NuGet.Config
dotnet build .\v2\TaskOverlay.sln --configuration Release --no-restore
dotnet run --project .\v2\tests\TaskOverlay.Core.Tests\TaskOverlay.Core.Tests.csproj --configuration Release --no-build
dotnet run --project .\v2\src\TaskOverlay.App\TaskOverlay.App.csproj
```

The v2 workflow is separate from the existing Go workflow and runs only when v2
or its architecture documentation changes.

## Validation targets

- stable WPF transparency without flicker;
- hover activation and 500 ms passive delay;
- topmost behavior;
- tray Show, Hide, Settings, and Exit commands;
- settings window recreation after close;
- first-run seed state, save/load roundtrip, and corrupted-state recovery;
- completed tasks disappearing immediately and remaining completed after restart;
- placement inside the working area of the monitor containing the pointer;
- behavior under mixed DPI and multiple monitors.

## Known limitations

- There is no UI for creating, editing, restoring, or viewing completed tasks.
- Passive hit testing covers the prototype window bounds.
- Overlay settings are persisted but not editable in the placeholder settings
  window.
- The tray uses the standard Windows application icon.
- No single-instance guard is implemented.
- WPF transparent-window performance still needs testing on varied GPUs and
  remote-desktop sessions.
