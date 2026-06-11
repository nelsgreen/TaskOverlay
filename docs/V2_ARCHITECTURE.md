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
- The tray can create a task from Windows clipboard text and reveal it
  immediately in the overlay.
- Pointer entry enables a simple background panel; pointer exit returns to passive
  mode after 500 ms.
- `SettingsWindow` validates independent settings-window lifecycle.
- The manifest requests Per-Monitor V2 DPI awareness.

No v1 migration, full editing, hotkeys, network access, or advanced themes are
included.

## Clipboard task creation

The tray command **Create task from clipboard** reads Unicode text on the WPF UI
dispatcher. A single trimmed line becomes the title. For multiple lines, the
first non-empty line becomes the title and the remaining trimmed text becomes
the description; internal description line breaks are preserved.

The Core `ClipboardTaskFactory` owns parsing and initializes the stable ID,
normal priority, active status, UTC creation timestamp, and empty completion/due
timestamps. The app appends the task to `AppState`, saves it atomically, shows
the overlay, updates its active collection, and briefly reveals the active
background. Empty clipboard text and clipboard access failures are logged and
do not create a task or crash the app.

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

## Diagnostics and lifecycle

Runtime diagnostics are written to:

```text
%APPDATA%\TaskOverlayV2\logs\runtime-<date>.log
```

WPF dispatcher, AppDomain, and unobserved task exceptions are captured in:

```text
%APPDATA%\TaskOverlayV2\logs\crash-<timestamp>.log
```

Crash logs include the exception and inner-exception chain, stack traces, state
path, shutdown status, and current overlay mode. Logging is fail-safe and never
throws back into the application.

Shutdown is idempotent. It blocks new storage writes, stops and detaches the
hover `DispatcherTimer`, captures placement once, performs one final guarded
save, disposes the tray icon and menu, and then closes the windows. Timer and UI
callbacks check the overlay lifecycle before touching controls.

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
- tray task creation from single-line and multi-line clipboard text;
- settings window recreation after close;
- first-run seed state, save/load roundtrip, and corrupted-state recovery;
- completed tasks disappearing immediately and remaining completed after restart;
- placement inside the working area of the monitor containing the pointer;
- behavior under mixed DPI and multiple monitors.

## Manual idle stress test

1. Launch the app and confirm startup/state-load entries in the runtime log.
2. Leave it idle for at least 60 minutes.
3. Use tray Show, Hide, and Settings after the idle period.
4. Hover the overlay, leave it, and confirm the 500 ms passive transition.
5. Complete a task after the idle period and confirm it is saved.
6. Exit through the tray.
7. Confirm the process exits, the tray icon disappears, and no new
   `crash-*.log` was created.

## Known limitations

- There is no full UI for editing, restoring, or viewing completed tasks.
- Passive hit testing covers the prototype window bounds.
- Overlay settings are persisted but not editable in the placeholder settings
  window.
- The tray uses the standard Windows application icon.
- No single-instance guard is implemented.
- WPF transparent-window performance still needs testing on varied GPUs and
  remote-desktop sessions.
