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
  - Reported/likely implemented by PR #45; verify UX details in main when
    touching this area.
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
- Notes and Waiting for auto-size to content:
  - compact collapsed (Notes ~2 lines, Waiting for ~1 line), expand on
    hover/focus with scroll for long text;
  - delivered by PR #48; revisit only if a manual resize handle is wanted.

## Timeline, Reminder, And Deadline

- Reminder quick presets + Repeat in connected Workspace:
  - connected to bridge/AppState via `updateTaskReminder` with calculated
    `remindAtUtc` and `remindEveryMinutes`; done in PR #47;
  - Monthly repeat intentionally not connected (see DECISIONS).
- Timeline click `DetailEmphasis`:
  - REMIND opens Task Details with Reminder expanded;
  - DEADLINE opens Task Details with Deadline expanded.
- Timeline overdue-first sorting / v0 fidelity:
  - low-priority fix or document the intentional difference.
- Timeline should not show DONE/completed tasks as active attention items:
  - default hide completed or make completed distinction explicit.
- Reminder block:
  - compact/collapsible with hover/focus/click expand;
  - Clear/Off accessible from the collapsed header;
  - done in PR #48.
- Deadline block:
  - compact/collapsible with hover/focus/click expand;
  - supports date-only and date+time;
  - done in PR #48.

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
- Active Now collapsed/expanded state persists through `WorkspaceSettings` and
  the connected Workspace context command; no `localStorage`.
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
- Gantt-like future planning view.
- Timeline and Calendar have different jobs:
  - Timeline = attention/risk horizon;
  - Calendar = time allocation.

## MEET

- Basic connected MEET MVP is implemented:
  - project;
  - title;
  - notes/agenda/context;
  - exact date;
  - start time;
  - duration/end time;
  - location;
  - link;
  - optional linked task.
- MEET persists in `AppState` / `state.json`; Workspace CRUD uses the WebView2
  command -> C# service -> save -> fresh snapshot path.
- MEET Timeline interaction is implemented:
  - Timeline is a visual upcoming-events / attention horizon;
  - click MEET row -> MEET Details;
  - Timeline displays MEET but does not own MEET creation.
- Calendar is the MEET planning/creation surface, including visual
  drag/drop rescheduling through the connected `updateMeeting` path.
- Default meeting duration is 30 minutes.
- Recording, transcription, AI analysis, recurrence, calendar sync, and direct
  provider APIs remain later work.
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
- Checklist inside task (Steps):
  - delivered by PR #48 as the connected checkpoints MVP;
  - item has only title/done/order — no status, REMIND, DEADLINE, WAIT, pin,
    or priority;
  - connected through five bridge commands (add batch, update title, toggle,
    delete, reorder) and exposed in the Workspace snapshot;
  - parent DONE/reopen does not mutate Step states;
  - all Steps done surfaces an explicit Complete task action, not
    auto-complete;
  - remaining: promote a Step to a real child task, Step templates, step-level
    reminders/deadlines, AI step breakdown, and overlay parent progress.
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

## ContextHUB

- Foundation is implemented (ContextHUB foundation PR):
  - `SourceDocument` + `ContextItem` persisted in `AppState` / `state.json`;
  - connected CRUD and link/unlink commands through the WebView2 bridge;
  - ContextHUB Workspace tab after Workstreams: left filters, center memory
    list with overview cards, right details editor, Add Source / Add Context
    modals;
  - repair/migration for old states and dangling links;
  - deleting tasks/MEETs/sources repairs context links without deleting
    memory.
- Later ContextHUB work, explicitly not in the foundation:
  - Task Details Context block (PR 2);
  - MEET Details Context block (PR 2);
  - Project Context Pack export/copy (PR 3);
  - manual source import polish;
  - OpenAI meeting analysis writing drafts into ContextHUB after user review;
  - transcription provider output as `SourceDocument`;
  - Telegram voice/text capture channel;
  - suggested tasks from transcripts with review-before-create;
  - moving very large transcript bodies out of `state.json` into external
    files with path references (`Body` is bounded in the foundation).

## Telegram Capture

- Local-first Telegram Capture setup is the first slice:
  - native Settings section for enabling the capture channel;
  - bot username, allowed Telegram user id, default project, and project aliases
    stored as non-secret `AppState` settings;
  - bot token stored outside `state.json` using Windows user-protected local
    storage;
  - Test connection uses Telegram Bot API `getMe` and must not log the token or
    tokenized URL.
- Polling (PR 2, done):
  - local-only `TelegramPollingService` inside WPF v2 using Telegram Bot API
    `getUpdates` long polling; no webhook, no hosting, no cloud sync;
  - text-only; accepts messages only from the configured allowed Telegram
    user id; ignores groups/channels, bot/self messages, and non-text
    updates;
  - deduplicates with a stored `LastUpdateId` cursor (`offset =
    LastUpdateId + 1`); ignored updates still advance the cursor so the app
    never loops on the same update, while an allowed capture only advances
    the cursor after it is saved;
  - optional deterministic command shortcuts: plain text, `/capture <text>`,
    `/source <project>: <text>`, `/task <project>: <title>`,
    `/meet <project>: <title/date text>`; commands are shortcuts, not the
    final UX, and use no NLP/date parsing;
  - `/task` and `/meet` create `TelegramCapture` `SourceDocument` drafts
    ("Telegram task draft" / "Telegram MEET draft"), never a final Task or
    MEET;
  - project aliases resolve case-insensitively; an unresolved hint falls
    back to the configured default project (or the app Default project) and
    is marked unresolved in the stored body rather than dropped or
    auto-creating a project;
  - the bot token stays in the PR 1 protected local store; polling never
    logs the token or a token-bearing URL.
- Future, not PR 2:
  - voice;
  - transcription;
  - AI interpretation / LLM-proposed actions with review-before-apply;
  - automatic final task/MEET creation from ambiguous text;
  - Context Pack integration;
  - multi-user or group/channel support.

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

- Keep the current tree view as the primary structured task view.
- Workstreams tab/view is a future alternate project view over the same project
  tasks.
- Workstreams should visualize parallel streams and relationships, not replace
  the tree.
- Workstreams view answers:
  - which streams are running;
  - how tasks are related;
  - what blocks what.
- A Workstream can contain sections and nested tasks.
- A project may have multiple workstreams running in parallel.
- Tasks may need cross-links across workstreams; support many-to-many
  relationships later.
- Cross-links should express dependencies, blockers, related tasks, duplicates,
  or "see also" relations without forcing a strict parent-child tree.
- Workstreams view visual ideas:
  - columns or swimlanes per workstream, with compact task cards inside each
    stream;
  - optional connector lines/arrows for cross-stream dependencies when
    relationship view is enabled;
  - relationship badges on cards even when connector lines are hidden, for
    example Blocks, Blocked by, Related, Duplicate, See also;
  - selecting a card opens the same right-side Details panel as the tree view;
  - many-to-many relationships stay as a separate links layer over the same task
    nodes, not duplicate tasks.
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

- WPF v2 foundation is present in main; verify behavior when touching it.
- Basic Project/Section/Task Workspace is present in main; verify behavior when
  touching it.
- Overlay attention layer is present in main; verify current UX when changing
  overlay.
- PR #29 automatic backup MVP is merged; retention and dialog polish remain.
- v0 Workspace generation is done as product/design input.
- "Task Management Window" naming is obsolete; use Workspace.
- Direct v0 GitHub sync is rejected.
- ActiveNowStrip `localStorage` production persistence anti-pattern is corrected
  if main still routes mutations through the bridge; verify if touching this.
- PR #44 Workstreams MVP is merged as an MVP; verify behavior in main before
  treating follow-ups as complete.
- PR #45 Tree Operations MVP is merged into main. It likely covers Tree/Section
  context menus, subtask operations, `deleteTask`, `moveTask`, Details Location,
  Active-only, and DONE cleanup; verify behavior in main before marking any
  remaining follow-up complete.
- PR #47 connected the Workspace Reminder editor presets and repeat intervals to
  the bridge (`updateTaskReminder` with `remindEveryMinutes`); Monthly repeat is
  intentionally not connected.
- PR #48 Steps/Checkpoints MVP is merged into main:
  - Steps are lightweight ordered checkpoints inside a task, with title + done
    state only;
  - Steps are not separate tasks and have no status, REMIND, DEADLINE, WAIT,
    PinToPanel, or Priority;
  - completing all Steps does not complete the parent task;
  - completing or reopening the parent task does not mutate Step states;
  - Steps persist through `AppState` / `state.json` via the WebView2 bridge and
    fresh snapshot;
  - Overlay does not render Steps as separate rows;
  - Task Details uses compact/collapsible Reminder, Deadline, Location, and
    Steps cards, compact auto-sizing Waiting for and Notes, and keeps Pin to
    panel hidden from Details while preserving the command/semantics.
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
