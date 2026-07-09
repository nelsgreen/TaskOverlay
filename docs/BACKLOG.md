# TaskOverlay Product Backlog

This document is the maintainable source of truth for product backlog items that
should not live only in chat history. Items are grouped by product area and are
not commitments for the next PR.

Active product scope:

- WPF v2 is the active product.
- Go v1 is legacy and should not be changed for v2 work.
- Correct solution: `v2/TaskOverlay.sln`.
- Correct executable: `TaskOverlay.V2.exe`.
- Correct development artifact: `TaskOverlayV2_WPF_FrameworkDependent`.
- Runtime state: `%APPDATA%\TaskOverlayV2\state.json`.
- Logs: `%APPDATA%\TaskOverlayV2\logs`.
- `AppState` / `state.json` is the desktop source of truth.
- Production Workspace mutations must remain connected:
  React -> WebView2 bridge -> C# `AppState` / `TreeStateService` ->
  `state.json` -> fresh snapshot -> React.
- No React direct writes to `state.json`.
- No `localStorage` production persistence.
- No mock-only production controls.

## Backup

- Hide or rename user-facing "Work" in backup UI. "Work" is an internal future
  task-space marker; user-facing copy should be "latest backup", "this
  computer", etc. `taskSpace` can remain metadata or a filename segment.
- Make backup retention configurable:
  - backup interval;
  - max backup count;
  - max retention days;
  - approximate backup folder size;
  - cleanup old backups.
- Restore safety:
  - if local state is newer than the latest backup, disable Restore latest
    backup or show an explicit warning that restoring an older backup replaces
    newer local data.
- Polish "Newer backup found" dialog:
  - clipped/truncated layout;
  - typo `avilable` -> `available`;
  - local/backup details partly hidden;
  - footer/buttons overlap content;
  - add min width/height or scrollable content above a fixed footer;
  - keep the dark theme.

## Workspace, WebView2, And Lifecycle

- Workspace loading state for the 1-2 second startup delay.
- No stale selection:
  - TASK -> MEET -> TASK must always show the matching Details panel type.
  - Details must not remain stale after tab/project scope/filter/search changes.
- Workspace window lifecycle:
  - Open Workspace from tray focuses the existing window, not a duplicate.
  - Closing Workspace does not exit the app.
- Graceful WebView2 runtime/static files error.
- Robust local path loading for spaces, Cyrillic paths, AppData, and Program
  Files.
- Hide overlay/handle/hover panel from Alt+Tab and the normal Windows app
  switcher; keep Quick Add, Task Details, and Settings as normal windows.

## Tree And Details

- Tree Active-only = all non-DONE tasks:
  - TODO, FOCUS, and WAIT visible;
  - DONE hidden.
  - Product decision overrides older v0 FOCUS/WAIT-only behavior.
  - Implemented by PR #45, verify UX details in main when touching this area.
- DONE cleanup:
  - when a task becomes DONE, clear REMIND/reminder and DEADLINE/`DueAtUtc` so
    Timeline/attention does not show DONE tasks as active;
  - likely covered by PR #45, verify in main.
- Tree Details stale state after delete:
  - clear selection or select a safe nearby item.
- WAITING FOR block:
  - visible only when status = WAIT;
  - placed directly under status chips;
  - preserve value when switching away and back.
- Details Delete in connected mode with confirmation and no stale panel.
- Details Location in connected mode through `moveTask`.
- Notes textarea manual vertical resize or auto-grow to max height with
  scrollbar.

## Timeline, Reminder, And Deadline

- Reminder quick presets + Repeat in connected Workspace:
  - connect to bridge/AppState;
  - no mock-only gate;
  - support calculated `remindAtUtc` and `remindEveryMinutes`.
- Timeline click `DetailEmphasis`:
  - REMIND opens Task Details with Reminder expanded;
  - DEADLINE opens Task Details with Deadline expanded.
- Timeline overdue-first sorting / v0 fidelity:
  - low-priority fix or document the intentional difference.
- Timeline should not show DONE/completed tasks as active attention items:
  - default hide completed or make completed distinction explicit.
- Reminder block:
  - compact/collapsible;
  - Clear/Off accessible without scrolling;
  - whole header clickable.
- Deadline block:
  - compact/collapsible;
  - supports date-only and date+time;
  - whole header clickable.

## Overlay And Attention

- Working mode:
  - show only FOCUS tasks;
  - show active REMIND items;
  - do not show arbitrary TODO tasks;
  - do not show descriptions by default;
  - use a lower contrast idle display;
  - on hover, tasks become normal contrast and descriptions may appear.
- Hide or remove Auto quest tracker mode for now if it conflicts with the
  current Workspace + Working direction.
- Overlay left-click task opens Task Details, not DONE. Completion must be
  explicit.
- Overlay cards show notes/description under title and WAIT metadata after
  notes.
- REMIND filter includes active/triggered and scheduled reminders, excluding
  DONE:
  - active first;
  - scheduled-only does not enter Working.
- Remove redundant leading dots/markers.
- Pinned/Collapsed default filter = Panel, not all TODO/backlog.
- Project grouping in overlay:
  - do not repeat project chip on every card in the same group.
- Collapsible WAIT group.
- One-click visibility to surface a task in overlay regardless of status.
- Overlay filters/chips:
  - Panel;
  - Priority;
  - FOCUS;
  - WAIT;
  - REMIND;
  - TODO;
  - DONE is not default.
- Reminder display settings for scheduled reminders before trigger:
  - tag;
  - countdown;
  - exact time;
  - compact indicator.
- Attention hub handle redesign:
  - handle is a functional surface, not branding;
  - show next MEET/counts/state/drag affordance;
  - no logo/name by default;
  - preserve handle anchor as source of truth.
- Window size controls per overlay mode in Settings.

## Task Interactions

- WAIT fast flow:
  - switching a task to WAIT opens or reveals Task Details;
  - cursor focuses Waiting for;
  - Enter saves where appropriate.
- Quick Add WAIT flow:
  - selecting WAIT reveals the compact Waiting for field before task creation.
- Empty status area left click opens quick status menu.
- Task Details keyboard shortcuts:
  - Enter saves and closes;
  - Shift+Enter inserts a new paragraph in Notes;
  - Esc cancels.

## Tree, Sections, And Projects

- Tree as master structure.
- Overlay as active subset.
- Hierarchy:
  - Project;
  - Workstream;
  - Section;
  - Task;
  - Subtask.
- Active + ancestors display mode.
- Do not render the full backlog inside the small overlay.
- Section context menu:
  - Rename section;
  - Add subsection;
  - Create task in this section;
  - Delete section with confirmation;
  - Hide/show section in Active-only.
- Subsections nested under parent section and compatible with the current
  Workstream model.
- All sections visible by default in Tree Active-only, including empty sections,
  if this remains accepted UX.
- Project Root appears at top and uses project name.
- New sections created at bottom of section list by default.
- Create task from section context menu creates the task inside the clicked
  section.
- After section-create-task:
  - Details opens;
  - focus goes to empty Title;
  - Tab -> Notes;
  - Enter in Notes must be defined safely.
- Project edit mode in Project Scope Bar:
  - rename;
  - hide/show;
  - reorder;
  - color;
  - add;
  - archive projects.
- Create project flow:
  - set name/color;
  - allow moving existing tasks into it.
- Multi-project selection:
  - single project;
  - all projects;
  - custom selected projects;
  - Status/Timeline aggregate selected projects;
  - Tree defaults to single-project.
- Tree drag/drop after `moveTask` is stable:
  - reorder/move tasks between sections/projects/workstreams/root.
- Move task context menu:
  - rename Change project to Move to project;
  - later Move > Project / Section.
- Pin/Unpin from panel in task context menu.
- Contextual Mark done / Reopen.

## Sync And Cloud

- Research cloud sync options.
- Telegram as a possible capture/sync channel.
- Prefer offline-first local state with later server-mediated sync.
- Avoid early CRDT/P2P unless needed.

## Workspace UX

- Adaptive Workspace layout:
  - collapse/expand left area;
  - right Details panel;
  - bottom Active Now strip;
  - resize handles;
  - smart layout redistribution.
- Right Details panel sizing:
  - min around 280;
  - preferred 340-380;
  - max 520-560;
  - hard max about 45%;
  - subtle splitter;
  - persist width.
- Open Workspace on app startup after backup checks:
  - open once;
  - likely configurable.
- Hotkeys toggle utility windows:
  - Quick Add;
  - Settings;
  - Task Details;
  - Workspace open/focus/close focused.
- Tray/context menu cleanup:
  - tray is the system/app entry;
  - task context menu is fast task actions;
  - root tray menu short;
  - clipboard capture under submenu.
- Dark styled task action popover later; system tray can remain system-style
  longer.
- Overlay Show/Hide should be one context-sensitive item.

## Scheduling, Calendar, And Timeline

- Work schedule settings:
  - start/end;
  - working days;
  - timezone display;
  - later lunch/break and Friday schedule;
  - defaults Mon-Fri 09:00-18:00.
- Deadline:
  - optional date/date+time;
  - separate from REMIND;
  - no popup by itself;
  - shown in Tree/Status/Timeline/overlay metadata.
- Calendar/Timeline MVP:
  - temporal layer for MEET, REMIND, and Deadline;
  - Today / Tomorrow / This week / Later.
- Timeline Now marker:
  - include Before workday / After workday edge states.
- Calendar production UX:
  - day/week planner, not a Google Calendar clone;
  - time grid;
  - free slots;
  - planning pool;
  - work blocks;
  - MEET blocks;
  - REMIND/DEADLINE point markers;
  - `selectedDate` source of truth.
- Calendar date behavior:
  - Today/Tomorrow;
  - day arrows +/-1 day;
  - week arrows +/-7 days;
  - Monday-Sunday week;
  - deterministic Show done;
  - no dead buttons.
- Planning pool:
  - unscheduled focus tasks;
  - drag to grid creates planned WORK block;
  - clearing block removes planned work, not task.
- Duration chips / resize handles:
  - 15/30/45/60/90/120.
- Timeline and Calendar have different jobs:
  - Timeline = attention/risk horizon;
  - Calendar = time allocation.

## MEET

- MEET base model:
  - project;
  - title;
  - notes/agenda/context;
  - exact date;
  - start time;
  - duration/end time;
  - location;
  - link;
  - optional linked task.
- MEET Timeline interaction:
  - click MEET row -> MEET Details;
  - New MEET action in Timeline.
- MEET persistence:
  - `MeetItem` in state;
  - bridge commands create/update/delete.
- Handle next MEET countdown.
- Local MEET recording MVP:
  - Start/Stop from MEET;
  - record system audio + mic locally;
  - no OpenAI yet.
- Emergency recording:
  - start first, classify later;
  - save as new MEET / link existing / keep recording only / transcribe /
    delete.
- Post-meeting transcription:
  - explicit upload;
  - save transcript json/md.
- Meeting analysis:
  - decisions;
  - my tasks;
  - others' tasks;
  - waiting for;
  - risks;
  - questions.
- Suggested tasks from meeting:
  - no auto-create;
  - user reviews checkbox list and creates selected tasks.
- Provider abstraction:
  - `ITranscriptionProvider`;
  - `IMeetingAnalysisProvider`;
  - OpenAI first, AssemblyAI/Deepgram/Local Whisper later.
- Two-track audio:
  - mic + system audio locally;
  - default process mixed;
  - better mode keeps both tracks.

## Task Quality And Neuroinclusive Planning

- Priority:
  - important/not important first;
  - model as enum if practical;
  - not status, FOCUS, REMIND, or PinToPanel;
  - no effect on Working.
- One-click Priority toggle in Tree/Status rows and later overlay cards.
- Priority filters/sorting.
- Checklist inside task:
  - checklist item has only text/isDone;
  - no status/reminder/wait/pin/priority.
- Attachments:
  - images/documents attached to a task;
  - connected through AppState/state.json/fresh snapshot;
  - decide storage/copy/reference/preview/missing files/max size/future sync.
- Work/Home task spaces:
  - isolated task memories;
  - Settings switch;
  - backup metadata already has `taskSpace` but feature is not implemented.
- Work Pattern Analytics:
  - local-first event log for snooze loop, stale task, WAIT without follow-up,
    deadline drift, overloaded day, project/workstream neglect, and meeting
    follow-up gap;
  - not medical diagnosis or productivity scoring.
- Weekly reporting support.
- Export completed tasks report for selected period:
  - needs reliable `CompletedAtUtc`;
  - group by project;
  - include title, section/path, completed datetime, optional notes.
- Chunking/break task into 3-7 small steps and next physical step.
- Templates/routines:
  - morning start;
  - meeting prep;
  - after meeting;
  - deep work.
- Reminder as action system:
  - start 5 min;
  - snooze;
  - reschedule;
  - break into steps.
- Focus mode / one thing at a time.
- Low-stimulus / reduced motion.
- Undo/archive/autosave/draft recovery; hard delete only with confirmation.

## Notes

- Short important notes module.
- Quick access from handle or expanded panel.
- Notes should not be mixed with tasks.
- Workflow/context notes:
  - Project, Workstream, and Section first;
  - use for nuances, decisions, links, constraints, or memory support;
  - surface in Workspace/Tree Details;
  - do not show workflow notes in the small overlay by default.
- Candidate UI labels:
  - Context notes;
  - Workflow notes;
  - Section notes;
  - Workstream notes.

## Settings

- Redesign Settings window.
- Add window size controls:
  - Auto quest tracker;
  - Collapsed hover panel;
  - Pinned expanded;
  - Quick Add;
  - Task Details.
- Future editable hotkeys.
- Storage/logs/diagnostics section.

## Workstreams

- Workstream narrative fields later:
  - goal;
  - nextAction;
  - blocker;
  - waitingFor;
  - activityLog;
  - lastActivity;
  - weekly report draft.
- Workstreams UX should be context recovery:
  - not Kanban;
  - not Calendar;
  - not a folder list.
- Current Workstream = top-level section/group MVP.
- Cross-sectional curated Workstream only after explicit decision.

## Already Done / Probably Obsolete

- WPF v2 foundation is implemented.
- Basic Project/Section/Task Workspace exists.
- Overlay attention layer exists; verify current UX when changing overlay.
- PR #29 automatic backup MVP is merged; retention and dialog polish remain.
- v0 Workspace generation is done as product/design input.
- "Task Management Window" naming is obsolete; use Workspace.
- Direct v0 GitHub sync is rejected.
- ActiveNowStrip `localStorage` production persistence anti-pattern is corrected
  if main still routes mutations through the bridge; verify if touching this.
- PR #44 Workstreams MVP is merged as an MVP; model evolution remains open.
- PR #45 Tree Operations MVP is merged into main. It likely covers Tree/Section
  context menus, subtask operations, `deleteTask`, `moveTask`, Details Location,
  Active-only, and DONE cleanup; verify behavior in main before marking any
  remaining follow-up complete.
- Long Go v1 history in prompts is obsolete for WPF v2 planning.

## Needs Clarification

- Active + path semantics.
- Section delete and task delete behavior with children.
- Empty-area Create task target.
- Move between projects behavior.
- Workstream fields on `GroupItem` vs a separate Workstream model.
- Notes behavior and Enter in Notes.
- Priority icon/color/naming/model/sorting.
- Overlay WAIT group scope and chip count scope.
- Backup retention defaults.
- Work/Home storage model.
- Attachments storage and missing-file behavior.
- MEET persistence timing.
- Transcription provider and privacy wording.
- Completed report format.
- Workspace auto-open default.
- Workspace layout preference storage.
- Drag/drop safe actions.
- Old WPF Task Details fate.
