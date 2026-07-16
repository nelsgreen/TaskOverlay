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
- Details scroll container reserves scrollbar gutter (`scrollbar-gutter:
  stable`) on both Task Details and MEET Details, so hovering/expanding a
  compact card (Reminder/Deadline/Location/Steps/Context) no longer shifts
  the panel's content width when the scrollbar appears.

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
- Planning pool holds Tasks, not calendar blocks:
  - dragging a task from Planning Pool to Calendar creates a new calendar
    block/work session linked to that task, not a move;
  - the task stays in Planning Pool after being scheduled once - it does not
    disappear, since long-running tasks may need multiple blocks across a
    day;
  - a task remains in Planning Pool until DONE; DONE tasks are hidden by
    default;
  - scheduling indicators are implemented from real work sessions:
    Unscheduled / Scheduled today / N blocks today / Total today;
  - later filters: Active / Unscheduled / Today / All;
  - clearing a block removes the planned block, not the task.
- Calendar empty-slot context menu (implemented; preserve connected behavior):
  - right-click an empty area of Day/Week view opens Create task / Create
    MEET;
  - created record uses the clicked date/time; default duration (e.g. 30
    minutes) unless an existing default already applies;
  - Create task opens Task Details/draft scheduled at that slot; Create MEET
    opens MEET Details/draft scheduled at that slot;
  - right-click on an existing task/MEET block shows that block's own
    context menu instead, never the empty-slot menu;
  - implementation must go through connected bridge/service/AppState/
    state.json/fresh snapshot; no mock-only UI, no `localStorage` production
    persistence.
- Multi-segment task scheduling / work sessions (foundation implemented):
  - problem: moving a long-running task around Calendar loses work history
    when the user works on it across several separate time periods;
  - decision: the task remains one logical record; Calendar displays
    separate block/work-session records linked to the same task instead of
    moving the task itself;
  - implemented model: `TaskWorkSession` with `id`, `taskId`, `startUtc`,
    `endUtc`, optional note, `createdAtUtc`, and `updatedAtUtc`; a later
    Planned/Actual `kind` remains out of scope;
  - one task can have multiple blocks; dragging from Planning Pool creates a
    new block linked to the task; moving/resizing a block changes only that
    block; past blocks stay visible and are never overwritten; the user can
    add another block for the same task;
  - connected create/update/delete commands, snapshot projection, Day/Week
    rendering, per-session drag/resize/delete, and Calendar context actions
    are implemented;
  - schema-2 single planned work migrates to one session in schema 3 with
    synthetic malformed/partial/idempotency fixtures; real user-state
    migration still requires artifact QA on the user's work computer;
  - Task Details showing linked sessions/history and total duration remains a
    follow-up; Day and Week already show all blocks under the same task title;
  - blocks are time/work metadata, not task status - status stays TODO /
    FOCUS / WAIT / DONE only;
  - deleting a calendar block removes only its session; deleting the logical
    task cleans all linked sessions; MEET remains separate.
- Calendar block usability (implemented in PR #64; preserve):
  - MEET blocks resizable by mouse like task blocks;
  - rename "+Meeting" to "+MEET"; first click on +MEET immediately opens
    MEET Details/draft, not a menu;
  - short (15-30 minute) task/MEET blocks still show their title;
  - empty-title fallback reads "Untitled MEET" / "Untitled task" - never a
    bare "--";
  - adjacent scheduled blocks have usable resize handles at their shared
    boundary, in both Day and Week, for task/task, MEET/MEET, and task/MEET
    combinations.
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
- Connected MEET recording and Meeting Assistant foundation is implemented:
  - explicit per-MEET recording policy: Off / Ask / Auto;
  - Compact direct AAC/M4A is the default, with microphone, system, and mixed
    tracks encoded through Windows Media Foundation and no full-meeting WAV
    intermediate;
  - Lossless WAV is a separate optional large-file mode and existing WAV
    recordings remain compatible;
  - one active recording at a time, restart recovery, and emergency recording;
  - optional OpenAI transcription and structured meeting analysis through
    provider interfaces;
  - transcript, analysis, and selected ProposedActions require explicit user
    review before existing TaskOverlay services apply mutations;
  - audio and transcript files stay local and automatic backups contain only
    recording metadata.
  - every Media Foundation Sink Writer is created, written, finalized, and
    released on its own dedicated MTA owner thread; capture callbacks only post
    to bounded queues;
  - failed finalization keeps recoverable `*.current.m4a` files and exposes a
    compact retry message; HRESULT/IID details stay in diagnostics and a
    collapsed technical-details section;
  - MEET create/view/edit, recording history, transcript, analysis, and
    ProposedActions use one large Workspace modal. TASK Details remains in the
    right sidebar, and closing the MEET modal does not stop recording or
    finalization.
- The MEET source/review workspace is implemented as one responsive modal with
  `Details / Sources / Review`:
  - Details owns scheduling, agenda, linked task, and compact linked context;
  - Sources owns local recordings, managed M4A/WAV/MP3 imports, non-destructive
    processing ranges, generated/imported TXT/MD/SRT/VTT transcripts, explicit
    active transcript selection, and manual user-selected PNG screenshots;
  - imported originals are copied into managed MEET storage and remain usable
    if the external source is moved or deleted;
  - Review combines the active transcript, its revision-bound analysis,
    timestamped screenshot references, ProposedActions, and an intentionally
    empty future project-context-update area;
  - transcript segments use stable speaker IDs while transcript-level mappings
    own original labels, display names, and the current-user marker; original
    imported/provider artifacts remain unchanged;
  - changing a transcript revision marks prior analysis stale and requires an
    explicit re-run.
- Recurrence, calendar sync, direct meeting-platform APIs, and live
  transcription remain later work.
- Handle next MEET countdown.
- Recording follow-ups:
  - artifact/manual QA across real microphone and output-device combinations;
  - recording-device hot-plug and degraded-track recovery polish;
  - periodic finalized M4A segments to bound unexpected-process crash loss;
  - richer emergency-recording inbox and classification flow;
  - transcript search and ContextHUB source promotion;
  - transcript editor actions: rename speaker globally, mark speaker as You,
    merge speakers, edit individual segments, and explicitly re-run stale
    analysis after transcript edits;
  - user speaker identification and known-speaker samples;
  - AI-proposed ContextItem candidates with explicit review, Meeting Brief,
    overlay meeting mode, and live transcription/copilot;
  - OCR/multimodal screenshot analysis and video recording;
  - Meeting Assistant Settings redesign;
  - additional transcription/analysis providers, including local Whisper;
  - chunk retry/progress polish and recording retention controls.

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
  - Enter on the "+ Next step..." input adds the step and keeps focus there
    for the next one (including restoring focus after the brief disabled
    state while the checkpoint command is in flight); Enter on an empty add
    input does not create a blank step and blurs out of the Steps area
    instead of no-op'ing in place; empty/whitespace-only titles are already
    rejected both client-side (`addSteps`/`commitStepEdit`) and server-side
    (`CheckpointService.NormalizeTitle`, `updateTaskCheckpointTitle`);
  - remaining: promote a Step to a real child task, Step templates, step-level
    reminders/deadlines, AI step breakdown, and overlay parent progress.
- Completed-subtasks review-needed indicator:
  - soft attention signal (not a task status) shown when a task is not DONE,
    has Steps/subtasks, and all of them are complete;
  - suggested actions: Complete task / Add subtask / Create follow-up task /
    Dismiss (Snooze);
  - a dismissed indicator must reappear if the task's subtasks change again -
    not a permanent mute;
  - suggested UX: badge in Task Details, compact badge in Tree/Planning
    Pool/task lists where applicable; avoid noisy repeated popups.
- FUCKUP marker MVP:
  - a separate marker/flag on a task, not a status - does not replace or
    extend TODO / FOCUS / WAIT / DONE;
  - model, UI, and bridge/persistence shape to be decided when this reaches
    implementation.
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
- Task Details Context block (done):
  - compact "Context" card in Task Details, below Steps;
  - collapsed by default when nothing is linked, expanded by default once
    something is linked; header click always overrides the default until a
    different task/MEET is selected (shared by MEET Details via the same
    `RecordContextBlock`);
  - shows SourceDocuments and ContextItems already linked to the selected
    task (type/status/source-app badges, short summary/body preview, linked
    count), with a readable empty state when nothing is linked;
  - "Link existing" opens a small modal listing same-project
    SourceDocuments/ContextItems not yet linked to this task, with a title
    filter; no create/edit here, that stays in ContextHUB;
  - "Unlink" per linked record, and an "Open ContextHUB" shortcut that
    switches Workspace to the ContextHUB tab;
  - reuses the existing `linkSourceToTask` / `unlinkSourceFromTask` /
    `linkContextItemToTask` / `unlinkContextItemFromTask` bridge commands
    from the ContextHUB foundation - no new commands or snapshot fields were
    needed, since the full `contextSources`/`contextItems` snapshot arrays
    already carry `linkedTaskIds`;
  - `ContextService.LinkItemToTask`/`LinkSourceToTask` now reject a task and
    record from different projects (previously only checked that the task
    existed); Task Details already only offers same-project candidates, this
    is defense in depth against a direct cross-project link;
  - MEET Context block is explicitly deferred to a later PR (not this one).
- ContextHUB Details -> LINKED TASKS picker (done):
  - replaced the plain `<select>` (listed every task in the workspace,
    including other projects and already-linked ones - picking one of those
    silently failed against the Core cross-project guard with no feedback)
    with a small searchable picker;
  - `LinkedTasksField` (linked list + "Link task" button) and
    `LinkedTaskPickerModal` (search + TODO/FOCUS/WAIT/DONE status chips +
    rows showing title, status, and a "Project / Section / Parent task"
    path) in `v2/workspace-ui/components/linked-task-picker.tsx`;
  - the picker only ever lists same-project, not-yet-linked tasks
    (`getEligibleTasks`); cross-project tasks are not shown at all rather
    than shown disabled;
  - REMIND is reminder/attention metadata, not a task status, so it is shown
    as a small Bell indicator on a row instead of a fake fifth status chip;
  - applies to both SourceDocument and ContextItem Details; reuses the same
    `linkSourceToTask` / `unlinkSourceFromTask` / `linkContextItemToTask` /
    `unlinkContextItemFromTask` bridge commands unchanged - no new commands,
    no snapshot changes, no Core changes;
  - MEET picker and MEET Context block remain explicitly out of scope (see
    below - now done in a follow-up PR).
- MEET Details Context block (done):
  - same compact "Context" card as Task Details, below the Linked task
    section in MEET Details;
  - shows SourceDocuments/ContextItems linked to the selected MEET,
    "Link existing" (same-project only), "Unlink" per record, and
    "Open ContextHUB";
  - reuses the Task Context block UI: `RecordContextBlock` in
    `task-context-block.tsx` is the owner-agnostic shared core (an id +
    project + which linked-id array to read/mutate); `TaskContextBlock` and
    `MeetContextBlock` are thin wrappers over it with unchanged public props,
    so Task Details behavior/appearance is untouched;
  - reuses the existing `linkSourceToMeeting` / `unlinkSourceFromMeeting` /
    `linkContextItemToMeeting` / `unlinkContextItemFromMeeting` bridge
    commands unchanged - no new commands, no snapshot changes;
  - `ContextService.LinkItemToMeeting`/`LinkSourceToMeeting` now reject a
    MEET and record from different projects, mirroring the Task-side guard
    added for PR #58 (previously only checked that the MEET existed);
  - MEET's linked task (`MeetItem.linkedTaskId`) stays untouched - still
    metadata/navigation only, not part of ContextHUB linking.
- Context Pack export (done): deterministic markdown export/copy for Claude/
  ChatGPT/Codex, generated entirely from stored TaskOverlay data. Export/copy
  only - no AI, no external API calls, no automatic analysis, no task/MEET
  creation, no AppState mutation.
  - `lib/context-pack-builder.ts` - pure, framework-free TypeScript functions
    (`buildProjectContextPack`, `buildTaskContextPack`,
    `buildMeetingContextPack`, plus a `buildContextPack(mode, ...)`
    dispatcher). Input is exactly projects/sections/tasks/meetings/
    ContextSources/ContextItems already in the Workspace snapshot - the
    function signatures make it structurally impossible to pass in the
    Telegram bot token, allowed user id, or any other protected setting;
  - deprecated/superseded ContextItems are excluded by default; every other
    section (decisions, requirements/constraints, blockers, open questions,
    risks, action items, project facts/notes) is always printed with a
    "None recorded." fallback rather than silently omitted, so the pack
    structure stays predictable even for an empty project;
  - source previews and item/task/MEET notes are truncated to a readable
    length (`... [truncated]`) rather than dropped or left unbounded;
  - `components/context-pack-modal.tsx` - shared read-only preview + Copy
    markdown button, with a text-selection fallback if the Clipboard API
    fails (never silently fails with no feedback);
  - "Context Pack" button in the ContextHUB toolbar generates the Project
    pack for the single selected project; requires exactly one project
    selected (same "Select one project" gating already used for
    Workstreams) and stays enabled in read-only mode since it never
    mutates state;
  - "Context Pack" action added to the shared `RecordContextBlock` (Task
    Details and MEET Details Context blocks) generates a focused pack for
    the selected task/MEET, including its own linked context plus a
    same-project "Relevant project memory" summary;
  - every generated pack ends with an explicit "Excluded / omitted" section
    stating it only reflects stored TaskOverlay data - not ChatGPT/Claude
    history, not un-imported Telegram messages, not the bot token/allowed
    user id/protected settings.
- Later ContextHUB work, explicitly not in this PR:
  - manual source import polish;
  - OpenAI meeting analysis writing drafts into ContextHUB after user review;
  - transcription provider output as `SourceDocument`;
  - Telegram voice/text capture channel;
  - suggested tasks from transcripts with review-before-create;
  - moving very large transcript bodies out of `state.json` into external
    files with path references (`Body` is bounded in the foundation).
- AI ProposedActions (not started yet): raw input -> SourceDocument/Capture ->
  `AIAnalysisRun` -> `ProposedAction[]` -> Review UI -> apply through existing
  services only after explicit user confirmation; AI must never mutate
  tasks/MEET/context directly (see DECISIONS "ContextHUB").

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
- Status/diagnostics (PR 3, done):
  - a small, in-memory, non-secret `TelegramCaptureDiagnostics` snapshot
    (`TaskOverlay.Core`) reports a single status - `NotConfigured`,
    `Disabled`, `Running`, `WaitingForMessages`, `TokenError`,
    `NetworkError`, or `Error` - plus last poll/success/captured timestamps,
    the last processed update id, a redacted last-error summary, and a
    consecutive-error count;
  - `TelegramPollingService` updates this snapshot at every stage of the
    poll loop (started, succeeded, failed, applied); the Settings ->
    Telegram Capture section polls it every few seconds while open and
    shows it as plain text, never the token;
  - `TelegramCaptureDiagnosticsRedactor` strips anything shaped like a bot
    token or a token-bearing `api.telegram.org` URL from any diagnostics
    text before it can reach Settings or logs, as defense in depth on top
    of the existing convention of only using HTTP status codes and
    exception type names in error messages;
  - a "Check now" button performs one immediate, non-long-poll getUpdates
    cycle through the same client/token/cursor as the background loop; the
    loop is stopped first and resynced afterward so there is never more
    than one outstanding getUpdates request for the bot token at a time
    (concurrent long polls can make Telegram terminate one of them) -
    this keeps the manual check from destabilizing normal polling;
  - an "Open ContextHUB" button opens/focuses Workspace on the ContextHUB
    tab; diagnostics are volatile (reset on restart) by design, the
    persisted cursor (`LastUpdateId`) is unchanged from PR 2.
- Future, not PR 3:
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
- PR #62 Task Details micro-UX polish is merged: Steps "+ Next step..." Enter
  focus flow, `scrollbar-gutter: stable` on Task/MEET Details, the shared
  Context block (`RecordContextBlock`) collapses by default when empty and
  expands by default when linked with clearer "Link existing context"
  wording and a compact accent linked-count indicator, and every shared
  Workspace modal stops closing on an outside/backdrop click.
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
