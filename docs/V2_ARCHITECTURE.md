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
- The tray and fixed global hotkeys provide three clipboard task intake modes
  and reveal created tasks immediately in the overlay.
- Pointer entry enables a simple background panel; pointer exit returns to passive
  mode after 500 ms.
- `SettingsWindow` validates independent settings-window lifecycle.
- The manifest requests Per-Monitor V2 DPI awareness.

No v1 migration, full editing, editable hotkey bindings, network access, or
advanced themes are included.

## Clipboard task creation

The tray exposes three actions:

- **Create tasks from clipboard lines** creates one task per trimmed non-empty
  line.
- **Create one task from clipboard** collapses trimmed non-empty lines into one
  title with an empty description.
- **Create one task with description** uses the first non-empty line as the
  title and the remaining text as the description, preserving internal line
  breaks.

The Core `ClipboardTaskFactory` owns parsing and initializes the stable ID,
normal priority, active status, UTC creation timestamp, and empty completion/due
timestamps. The app appends the resulting task batch to `AppState`, saves once,
shows the overlay, updates its active collection, and briefly reveals the active
background. Empty clipboard text and clipboard access failures are logged and
do not create tasks or crash the app.

## Fixed global hotkeys

`GlobalHotkeyManager` owns a message-only Win32 window and registers:

```text
Ctrl+Alt+A  Create tasks from clipboard lines
Ctrl+Alt+S  Create one task from clipboard
Ctrl+Alt+D  Create one task with description
Ctrl+Alt+T  Show or hide overlay
```

Registrations use `MOD_NOREPEAT` so one held keypress does not repeatedly invoke
an action. Each registration succeeds or fails independently; collisions with
other applications are logged and do not stop TaskOverlay. Registered hotkeys
are unregistered and the message window is destroyed during shutdown.

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
save, unregisters global hotkeys, disposes the tray icon and menu, and then
closes the windows. Timer and UI callbacks check the overlay lifecycle before
touching controls.

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
- all three tray and global-hotkey clipboard creation modes;
- global overlay show/hide hotkey and graceful registration collisions;
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

## Manual hotkey test

1. Copy three non-empty lines separated by blank lines and press `Ctrl+Alt+A`;
   confirm three tasks appear and only one state save is logged.
2. Copy multi-line text and press `Ctrl+Alt+S`; confirm one task appears with a
   single-line title and empty description.
3. Copy a title followed by description lines and press `Ctrl+Alt+D`; confirm
   one task is persisted with its description.
4. Press `Ctrl+Alt+T` repeatedly and confirm it alternates overlay visibility
   once per keypress.
5. Start a second TaskOverlay instance to force registration collisions; confirm
   it stays running and logs each unavailable hotkey.
6. Exit both instances and confirm the hotkeys can be registered again on the
   next launch.

## Known limitations

- There is no full UI for editing, restoring, or viewing completed tasks.
- Hotkey bindings are fixed and cannot yet be edited.
- A fixed hotkey that is already owned by Windows or another application remains
  unavailable until the conflict is removed; the other hotkeys continue working.
- Passive hit testing covers the prototype window bounds.
- Overlay settings are persisted but not editable in the placeholder settings
  window.
- The tray uses the standard Windows application icon.
- No single-instance guard is implemented.
- WPF transparent-window performance still needs testing on varied GPUs and
  remote-desktop sessions.
