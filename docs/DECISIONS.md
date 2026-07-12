# TaskOverlay Product Decisions

This document records current product and implementation decisions. Update it
when product direction changes, then update the relevant roadmap or backlog
item.

## Product Direction

- TaskOverlay is memory-support, attention-support, and work-recovery software,
  not just a task list.
- WPF v2 is the active product.
- Go v1 is legacy.
- Workspace is the main management surface.
- Overlay is the attention layer, not the full task manager.
- Tree is the master structure.
- Repo docs are the source of truth for backlog/decisions/roadmap; chat memory
  alone is not.

## Names And Artifacts

- Correct solution: `v2/TaskOverlay.sln`.
- Correct executable: `TaskOverlay.V2.exe`.
- Correct development artifact: `TaskOverlayV2_WPF_FrameworkDependent`.
- Runtime state: `%APPDATA%\TaskOverlayV2\state.json`.
- Logs: `%APPDATA%\TaskOverlayV2\logs`.
- Use REMIND, not DUE.
- Use FOCUS, not IN WORK.
- Use Focus, not Acknowledge.
- Statuses are TODO / FOCUS / WAIT / DONE.

## State And Persistence

- C# `AppState` / `state.json` is the current desktop source of truth.
- Production Workspace mutations must remain connected:
  React -> WebView2 bridge -> C# `AppState` / `TreeStateService` ->
  `state.json` -> fresh snapshot -> React.
- React must not write directly to `state.json`.
- `localStorage` must not be used for production persistence.
- No mock-only production controls.
- Backups are backup, not sync.

## Overlay And Attention

- Overlay Working = FOCUS + active REMIND only.
- Working does not show arbitrary TODO tasks.
- PinToPanel is visibility, not status.
- Display mode and task filter are separate.
- REMIND is not Deadline.
- Deadline is not REMIND and does not trigger a popup by itself in MVP.
- MEET is a separate entity, not a task/status/REMIND/Deadline.
- MEET is persisted in `AppState` with project, title, notes, start time,
  duration, location, link, and an optional linked task. Its default duration
  is 30 minutes.
- Handle is a functional surface, not branding.
- Handle anchor is the source of truth and must not be derived from panel
  position.

## Workspace And Structure

- Sections/folders are the same model for now.
- Steps/Checkpoints are lightweight execution checkpoints inside a task.
- A Step has title, done state, and order only. It is not a separate task and
  has no status, REMIND, DEADLINE, WAIT, PinToPanel, or Priority.
- Completing all Steps does not auto-complete the parent task. Completing or
  reopening the parent does not mutate Step states.
- Completing all Steps surfaces an explicit "Complete task" action that uses the
  normal task status path.
- Steps persist through `AppState` / `state.json` using the same connected
  WebView2 command -> save -> fresh snapshot path as other Workspace mutations.
- Overlay may summarize Step progress but does not render Steps as task rows.
- Promotion of a Step to a real child task, Step templates, step-level
  reminders/deadlines, and AI step breakdown are out of scope for the MVP.
- Reminder, Deadline, Location, and Steps are compact collapsible Task Details
  cards that expand on hover, focus-within, or click.
- Waiting for and Notes are compact auto-sizing fields.
- Pin to panel is hidden in Task Details for now; the field, bridge command,
  and pinned/overlay semantics are unchanged.
- Workstream = top-level section/group is the MVP simplification.
- Cross-sectional Workstream is deferred.
- No permanent left Projects sidebar in Workspace; use the horizontal Project
  Scope Bar.
- Old WPF Tree Manager remains fallback until Workspace is stable.
- Old WPF Task Details remains temporary fallback until Workspace Details is
  primary.
- Quick Add and Settings stay WPF/native for now.

## Scheduling And Planning

- Timeline and Calendar have different jobs:
  - Timeline = attention/risk horizon.
  - Calendar = time allocation.
- Timeline can display MEET as an upcoming event, but Calendar is the MEET
  planning/creation/rescheduling surface.
- A MEET linked task is metadata/navigation only. Selecting a MEET selects the
  MEET; opening the linked task must be an explicit action from MEET Details.
- Workstreams are context recovery, not a Kanban/status board/Calendar.
- Direct meeting service integrations are not MVP.
- Basic MEET CRUD is connected through the WebView2 bridge. Recording,
  transcription, AI analysis, recurrence, calendar sync, and provider APIs are
  explicitly later features.
- AI suggests tasks; the user confirms selected tasks.
- No embedded ChatGPT window inside the app.
- Reminder repeat is a flat minute interval (`remindEveryMinutes`). Monthly
  repeat is not connected until a calendar-aware recurrence model exists.

## ContextHUB

- ContextHUB is the local project-memory and future AI-grounding layer.
- Core entities are `SourceDocument` (raw captured/imported text with
  provenance) and `ContextItem` (durable memory unit: decision, requirement,
  constraint, blocker, open question, action item, project fact, risk, note).
- ContextHUB stores manually entered/imported data only. It does not
  automatically read ChatGPT/Claude history or any external service.
- Links to tasks and MEETs are navigation pointers by id, not embedded copies.
- Links are stored one-directionally: `ContextItem.SourceDocumentIds` owns the
  item-source relation; reverse lists are derived in snapshot/UI. No duplicate
  bidirectional link storage.
- Deleting a task/MEET/source repairs links; it never deletes context records.
- Deleting a SourceDocument keeps derived ContextItems and only removes the
  source reference.
- Future OpenAI meeting analysis, transcription, and Telegram capture will
  write into this layer as drafts that require explicit user review before
  anything is created.
- Context Pack is a read-only export generated from stored TaskOverlay data;
  deprecated/superseded items are excluded by default. Not shipped in the
  foundation PR.
- Modal-based creation (Add Source / Add Context) worked well in v0 and may be
  considered for future large editors; Task/MEET editors are not being
  redesigned around modals for now.

## Telegram Capture

- Telegram Capture is local-first: no hosting, no webhook, no cloud sync.
- PR 1 is setup only: Settings UI, non-secret configuration, protected bot
  token storage, and a safe Telegram `getMe` connection test.
- The bot token must never be stored in `state.json`, committed, or logged.
  Non-secret settings may live in `AppState`: enabled flag, bot username,
  allowed Telegram user id, default project, aliases, and future poll interval.
- Polling will be implemented later with Bot API long polling inside WPF v2.
  Future polling must accept messages only from the configured allowed user id
  and ignore unknown users, groups, and channels.
- Plain text capture should create raw capture / SourceDocument drafts for user
  review. Voice, transcription, AI interpretation, and automatic task/MEET
  creation are later work.

## Sync And Platform

- Full sync/mobile/cloud comes later.
- Server-mediated offline-first sync is preferred.
- P2P/CRDT is not the first sync architecture.

## Process

- Manual UX acceptance beats a green build.
- Codex GUI automation is not final UI acceptance.
- v0 is design/product direction when explicitly referenced, but not the domain
  source of truth.
- No fake controls.
- Keep PRs bounded, but avoid micro-PRs for tiny UI changes.
