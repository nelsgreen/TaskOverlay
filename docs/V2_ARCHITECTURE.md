# TaskOverlay v2 WPF prototype

TaskOverlay v2 is a Windows-only experiment under `v2/`. The Go application in
`cmd/taskoverlay/` remains the v1 implementation and is not referenced by v2.

## Prototype responsibilities

- `App.xaml.cs` owns process lifetime, the tray icon, and window creation.
- `TaskOverlay.Core` owns the versioned state model and local JSON persistence.
- `OverlayWindow` is a transparent, borderless, always-on-top WPF window.
- Passive mode renders active, non-completed task marker/text rows.
- Marker clicks complete tasks; body clicks apply the configured in-work mode.
- Row context actions and `TaskDetailsWindow` provide focused task editing
  without turning the overlay into a full management window.
- The tray and fixed global hotkeys provide three clipboard task intake modes
  and reveal created tasks immediately in the overlay.
- A single persisted overlay mode selects automatic passive tracking, a compact
  collapsed handle, or pinned expansion.
- Pointer entry enables a simple background panel; pointer exit returns to passive
  mode after 500 ms.
- `SettingsWindow` owns the editable SingleTask/MultipleTasks selection.
- The manifest requests Per-Monitor V2 DPI awareness.

No v1 migration, subtask editing, editable hotkey bindings, network access, or
advanced themes are included.

## Daily attention MVP

`ProjectItem.colorHex` persists a compact project accent. A one-time
`MvpProjectSeeder` adds KazChess, PLHIV, TaskOverlay, and Personal without
recreating projects the user later deletes. `TaskItem.status` adds Todo,
InWork, Waiting, and Done while `StateMigrator` keeps the legacy
`Completed`/`InWork` fields synchronized for old files and existing services.

Tasks persist `remindAtUtc`, `remindEveryMinutes`, `lastReminderAtUtc`,
`waitingFor`, and `reminderActive`. `ReminderService` owns presets, due
activation, repeat advancement, snooze, and Still waiting behavior. A single
30-second `DispatcherTimer` in `App` processes reminders, saves one batch, and
refreshes/reveals the overlay. Repeating reminders schedule their next time but
remain visibly active until snoozed, marked Still waiting, or completed. This
MVP uses in-app `DUE` highlighting; Windows toast notifications are deferred.

`QuickAddWindow` creates tasks through `TaskCaptureService`; clipboard capture
uses the same last-project/Personal fallback and starts as Todo with no reminder.
`TaskDetailsWindow` edits project assignment through `ProjectService`, status,
waiting-for text, presets, explicit local reminder time, and the two supported
repeat intervals. The overlay remains compact: project stripe/badge, WAIT/DUE
badges, title, and active-only description/waiting details.

## Task interaction model

`TaskInteractionService` owns task mutations independently of WPF. Marker clicks
call `Complete`, clear in-work state, add the completion timestamp, save
immediately, and remove the task from the active projection. Body clicks call
`ActivateFromClick`: `MultipleTasks` toggles only that task, while `SingleTask`
sets it in work and clears every other in-work task.

The row context menu provides Edit, Show/Hide description, Mark as in work,
Mark completed, and Delete. `TaskDetailsWindow` edits a copy of title,
description, in-work, and completed values. Save applies the values through the
Core service and persists; Cancel closes without mutation; Delete confirms and
removes the task. The editor is owned by the overlay but has an independent
lifecycle.

`TaskItem.descriptionExpanded` is persisted. Passive projection never shows
descriptions. Active projection shows a non-empty description when the task is
expanded or in work. A lightweight `TaskRowViewModel` snapshot keeps WPF
presentation concerns out of the JSON model.

`OverlaySettings.inWorkMode` is serialized as `multipleTasks` or `singleTask`.
The enum orders `MultipleTasks` as its default value, so schema v1 state files
that omit the property load safely without migration.

## Overlay modes and interaction guards

`OverlaySettings.overlayMode` stores one of `autoQuestTracker`,
`collapsedHandle`, or `pinnedExpanded`. The state loader migrates schema v1
files without changing their schema version: old `pinnedActiveMode=true` maps
to `pinnedExpanded`, otherwise old `collapsedMode=true` maps to
`collapsedHandle`, and missing/false flags map to `autoQuestTracker`. Legacy
flags are removed on the next save.

The tray exposes these values as a radio-style **Overlay mode** submenu. The
persistent handle opens the same choices on right-click. A handle left-click
switches to `pinnedExpanded`, or returns a pinned overlay to
`collapsedHandle`.

`OverlayCollapseGuard` is the Core policy for automatic collapse. Collapse is
allowed only when the mode is not pinned and no task editor, task context menu, Settings
window, modal dialog, or drag interaction is active. WPF lifecycle callbacks
update these flags and schedule the normal 500 ms timer when the final blocker
closes. Task Details reports both delete confirmation and validation message
boxes as modal interactions, so the overlay cannot collapse behind them.

## Collapsed handle

The handle is always present as the first window row. When collapsed mode is
resting, `OverlayPanel` and all task rows are collapsed, leaving only the handle.
Because the window uses `SizeToContent`, its bounds shrink to that handle.
Pointer entry reveals the full active panel below it. Pointer exit starts the
existing 500 ms dispatcher timer, which returns to the handle unless a collapse
guard is active. In `autoQuestTracker`, the same timer returns to the normal
transparent passive task list while retaining the handle.

The handle uses distinct collapsed, expanded, and pinned colors without icon
glyphs. Settings displays the current unified mode.
`Ctrl+Alt+T` continues to show or hide the whole overlay and does not change
the persisted mode.

## Window placement and layout

The active panel keeps its pointer-threshold drag handling. The handle has an
independent gesture that captures the mouse on press and waits for five DIPs of
movement before treating the action as a drag. Parent panel drag handlers ignore
handle-originated events, preventing WPF button/capture conflicts. Collapsed
hover expansion is disabled while the handle gesture is active.

At drag completion, the window snaps within 16 DIPs of the left, right, top, or
bottom edge of the current monitor work area. Normal overlay drags persist
`WindowPlacement.left/top`; collapsed-strip drags persist the independent
`collapsedLeft/collapsedTop` anchor. Work-area calculations preserve negative
coordinates and account for taskbar space. Saved positions are clamped to a
visible work area at startup.

Collapsed expansion keeps the compact strip position as its resting anchor but
shifts the larger active panel left or upward when necessary to remain inside
the same monitor. This temporary expanded position is never written over the
collapsed anchor. After the 500 ms return, the strip is restored to the original
snapped edge. The task content width and scrollable height are bounded by that
monitor work area. Titles and visible descriptions wrap inside a stretch layout
instead of increasing the overlay width.

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
Ctrl+Alt+Q  Open Quick Add task
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
and timestamps. `TaskItem` includes a stable ID, title, description,
description-expansion state, completion, priority, in-work state,
creation/completion timestamps, and an optional due time.

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

**Build WPF v2** runs automatically for v2, documentation, README, changelog,
and workflow changes. Automatic runs publish only the framework-dependent
development artifact. The self-contained Windows x64 artifact is opt-in through
the workflow's manual dispatch input. The legacy **Build Go v1 portable**
workflow is manual-only.

## Validation targets

- stable WPF transparency without flicker;
- hover activation and 500 ms passive delay;
- topmost behavior;
- tray Show, Hide, Settings, and Exit commands;
- all three tray and global-hotkey clipboard creation modes;
- global overlay show/hide hotkey and graceful registration collisions;
- unified overlay-mode persistence and backward-compatible legacy migration;
- collapsed activation-strip expansion and 500 ms return behavior;
- exclusive tray/handle mode selection and checked state;
- persistent handle styling in collapsed, expanded, and pinned states;
- collapse guards for editor, context menu, Settings, modal dialogs, and drag;
- marker-only completion and body-click in-work behavior;
- SingleTask/MultipleTasks settings and persistence;
- task context actions and details editor Save/Cancel/Delete behavior;
- active-only expanded/in-work descriptions;
- active-panel and collapsed-strip dragging with edge snapping;
- independent normal/collapsed placement restoration and off-screen correction;
- long-title wrapping and monitor-bound expansion;
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

## Manual overlay-mode test

1. Select each **Overlay mode** tray item and confirm exactly one item is checked.
2. In **Collapsed handle**, hover the handle and confirm the active overlay appears.
3. Select **Pinned expanded**, move the pointer away, and confirm the panel stays
   expanded.
4. Select **Collapsed handle** and confirm the handle returns after about 500 ms.
5. Confirm the handle remains visible and changes from collapsed yellow to
   expanded blue to pinned green.
6. Hide and show the overlay with `Ctrl+Alt+T`; confirm the selected mode remains
   correct.
7. Open Task Details and move the pointer away; confirm the overlay stays
   active. Click Delete and keep the confirmation open; confirm it still does
   not collapse.
8. Repeat with a task context menu and Settings open, then close them and
   confirm normal delayed collapse resumes.
9. Restart the app and confirm the selected overlay mode is restored.
10. Confirm clipboard hotkeys still create and reveal tasks.
11. Select **Auto quest tracker** and confirm the passive task list and hover
   behavior return.

## Manual placement test

1. Drag the active panel from its background and confirm it follows the pointer.
2. Drag from a marker or task body past the Windows drag threshold and confirm
   neither completion nor in-work state changes.
3. Drag within about 16 DIPs of every work-area edge and confirm edge snapping.
4. Select **Collapsed handle**, drag the handle freely beyond five DIPs, and
   confirm it follows the pointer without changing mode.
5. Put the strip at the right and bottom edges and confirm expansion shifts
   left/up without crossing the work area, then confirm the strip returns to
   its original right/bottom anchor after collapse.
6. Repeat on a secondary monitor, including one arranged left or above the
   primary monitor, then restart and confirm the position is restored.
7. Create a task with a very long title and an unbroken string; confirm text
   wraps or trims without extending the window past the work area.

## Manual task interaction test

1. Click only a task marker and confirm the task completes, disappears, and is
   still completed after restart.
2. Click task text in MultipleTasks mode and confirm each task toggles in-work
   independently with a visible highlight.
3. Select SingleTask in Settings, click several task bodies, and confirm only
   the most recently clicked task remains in work after restart.
4. Right-click a row and exercise Edit, Show/Hide description, Mark as in work,
   Mark completed, and Delete.
5. In Task details, edit title and description and click Save; reopen and
   confirm persistence. Repeat with Cancel and confirm no change.
6. Delete from Task details and confirm the task is removed after confirmation.
7. Confirm passive mode shows titles only; in active mode, expand a description
   and mark a described task in work to confirm wrapped description display.

## MEET recording pipeline

New recordings read the machine-local format setting when Start begins. The
default Compact path asks WASAPI shared-mode capture for normalized PCM16 and
queues microphone and system frames without blocking capture callbacks. A
bounded in-memory timeline aligns both sources and produces the mixed PCM
track. Three independent `IMFSinkWriter` instances encode the source and mixed
tracks directly as AAC-LC in M4A containers; no full-meeting WAV is written in
Compact mode. Lossless mode uses the same queues and mixer with independent WAV
writers instead.

Each AAC track writer owns a dedicated background MTA thread. That thread calls
`CoInitializeEx(COINIT_MULTITHREADED)`, creates and configures the Sink Writer,
handles every queued sample, finalizes the container, releases all related COM
objects, and then uninitializes COM. `StartAsync`, capture callbacks,
`CompleteAsync`, and `AbortAsync` exchange bounded commands with that owner;
the `IMFSinkWriter` RCW never leaves its thread. Diagnostics record recording
id, track kind, managed owner-thread id, apartment, lifecycle operation, and COM
initialization result without logging audio content.

During capture, output uses `*.current.m4a` or `*.current.wav`. Stop first stops
capture, drains the bounded queues, finalizes the containers, reopens them to
validate readable metadata and duration, then renames valid files to their
final names. State stores relative artifact metadata while audio remains under
the local recording folder. Startup turns stale active artifacts into explicit
Interrupted/Invalid records and preserves their in-progress names. Because the
current M4A implementation is single-file rather than segmented, an unexpected
process termination can lose the current container; periodic finalized M4A
segments remain a follow-up.

Finalization failure preserves recoverable current files as Invalid/Interrupted,
clears the process recording lock, and allows a new Start. Workspace shows a
short retryable message and keeps full HRESULT/IID details in logs and a
collapsed technical section; invalid audio is never sent to transcription.

## Workspace MEET editor

TASK Details remains in the resizable right Workspace sidebar. MEET create,
view, edit, recording controls/history, transcript, analysis, ProposedActions,
and Context share one responsive modal editor. Calendar, Timeline, `+MEET`,
empty-slot creation, and emergency-recording classification all select that same
connected editor. The modal owns only draft UI state: closing or unmounting it
does not stop an active recording or finalization, both of which remain owned by
the WPF application service. Unsaved MEET fields require explicit discard
confirmation; backdrop clicks never close the modal.

## Known limitations

- There is no completed-task browser or restore action.
- The task editor does not manage due time, priority, or subtasks yet.
- Hotkey bindings are fixed and cannot yet be edited.
- Overlay mode is selected from the tray or handle context menu; Settings
  displays its status but does not currently edit it.
- Dragging uses WPF/WinForms DPI transforms and is designed for per-monitor
  safety, but mixed-DPI transitions should still be validated on physical
  multi-monitor hardware.
- A fixed hotkey that is already owned by Windows or another application remains
  unavailable until the conflict is removed; the other hotkeys continue working.
- Passive hit testing covers the prototype window bounds.
- Overlay settings are persisted but not editable in the placeholder settings
  window.
- The tray uses the standard Windows application icon.
- No single-instance guard is implemented.
- WPF transparent-window performance still needs testing on varied GPUs and
  remote-desktop sessions.
