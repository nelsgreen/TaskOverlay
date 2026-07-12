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
  foundation PR (expanded export shipped later - see the Context Pack entry
  below).
- Modal-based creation (Add Source / Add Context) worked well in v0 and may be
  considered for future large editors; Task/MEET editors are not being
  redesigned around modals for now.
- Task Details Context block (done): a compact, Task-only card in Task
  Details shows linked SourceDocuments/ContextItems and lets the user link an
  existing same-project record or unlink one. No create/edit of
  SourceDocuments or ContextItems from Task Details - that stays in
  ContextHUB, to keep this block small and avoid duplicating the ContextHUB
  editors.
- Linking is restricted to the task's own project. Task Details only ever
  offers same-project candidates in "Link existing"; `ContextService` also
  rejects a cross-project `LinkItemToTask`/`LinkSourceToTask` call directly,
  so the rule holds even if a link is attempted outside the UI's own filter.
- No new bridge commands or snapshot fields were needed for this: the
  ContextHUB foundation's `linkSourceToTask` / `unlinkSourceFromTask` /
  `linkContextItemToTask` / `unlinkContextItemFromTask` commands and the full
  `contextSources`/`contextItems` snapshot arrays (with `linkedTaskIds`)
  already covered it.
- MEET Details Context block is intentionally deferred to a later PR; this PR
  is Task Details only.
- The ContextHUB Details -> LINKED TASKS picker only ever lists same-project,
  not-yet-linked tasks: cross-project tasks are hidden from the picker
  entirely rather than shown disabled with a reason, since the QA complaint
  was that unselectable tasks in a plain dropdown were confusing with no
  explanation. This is a UX-only change; the underlying link semantics,
  bridge commands, and Core cross-project guard are unchanged.
- The picker's status filter uses the real `Status` values only
  (TODO/FOCUS/WAIT/DONE). REMIND is reminder/attention metadata, not a task
  status (see the ContextHUB entry above and the domain rules this repo
  already follows) - it is shown as a small indicator on a task row instead
  of being added as a fake status filter chip.
- MEET picker/MEET Context block redesign stays out of scope for this PR
  (done in a follow-up PR, see below).
- MEET Details Context block (done) intentionally reuses the Task Context
  block's rendering/interaction logic rather than writing a parallel
  implementation: `task-context-block.tsx` now exports a shared, owner-
  agnostic `RecordContextBlock` plus two thin wrappers, `TaskContextBlock`
  (unchanged props/behavior) and `MeetContextBlock`. The only difference
  between them is which linked-id array a record is checked/mutated against
  (`linkedTaskIds` vs `linkedMeetingIds`) and which bridge command family is
  sent (`...ToTask` vs `...ToMeeting`).
- The Core cross-project guard added for Task links in PR #58
  (`LinkItemToTask`/`LinkSourceToTask` reject a task in a different project)
  is now mirrored for MEET links (`LinkItemToMeeting`/`LinkSourceToMeeting`).
  MEET Details only ever offers same-project candidates, but this closes the
  same silent-failure gap the linked task picker PR (#59) fixed on the Task
  side.
- `MeetItem.linkedTaskId` (the single optional Task a MEET can point to) is
  unrelated to ContextHUB linking and is not touched by this PR - it remains
  metadata/navigation only, exactly as before.
- Context Pack export (done): three deterministic markdown builders
  (`buildProjectContextPack`, `buildTaskContextPack`,
  `buildMeetingContextPack` in `lib/context-pack-builder.ts`) generate a
  pack for Claude/ChatGPT/Codex from stored TaskOverlay data only. This is
  export/copy, not persistence: it never calls the bridge, never mutates
  AppState, and makes no network calls.
- The builder is pure and framework-free by construction: its input types
  are exactly the Workspace snapshot shapes (`Project`, `Section`, `Task`,
  `MeetItem`, `WorkspaceContextSourceSnapshot`, `WorkspaceContextItemSnapshot`)
  - there is no parameter through which the Telegram bot token, the allowed
    user id, or any other protected setting could reach the generated
    markdown, because those types are never passed in. This is enforced by
    the type signatures, not by a runtime redaction step.
- Deprecated/superseded ContextItems are excluded by default, same as the
  ContextHUB foundation's existing convention; every section still prints
  ("None recorded.") rather than being silently omitted, so a Context Pack
  for an empty or lightly-populated project remains structurally
  predictable.
- Long text (source previews, ContextItem bodies, Task notes, MEET notes) is
  truncated to a readable length with an explicit "... [truncated]" marker -
  never silently cut with no indication, and titles/statuses/links are never
  dropped to make room.
- The "Context Pack" button in the ContextHUB toolbar requires exactly one
  concrete project selected (reusing the "select one project" gating already
  used for creating a Workstream) rather than operating on "All" projects at
  once - a pack is scoped to a single project by design. Unlike the create
  actions in that same toolbar, it stays enabled in read-only mode, since
  generating and copying a pack is not a mutation.
- Task/MEET Context Pack actions are added to the existing shared
  `RecordContextBlock` (see the MEET Context block entry above) as one more
  optional action alongside Link existing/Unlink/Open ContextHUB, rather
  than as a separate component - this is the same block already used by both
  Task Details and MEET Details, so both get the export for free.
- `components/context-pack-modal.tsx` is shared by all three entry points
  (Project/Task/MEET). If the Clipboard API throws or is unavailable, the
  text stays selected in the preview and the modal shows "Copy failed -
  select text manually" rather than failing silently.
- No JS test runner exists in `v2/workspace-ui` (still true as of the
  ContextHUB linked task picker PR #59); consistent with that precedent, no
  new test framework was introduced. The builder was verified with a
  throwaway manual script (compiled via `tsc`, run with plain `node` against
  a fabricated multi-project dataset) covering project scoping, deprecated/
  superseded exclusion, DONE-task exclusion from "linked active tasks",
  Telegram-sourced captures, empty-project output, and truncation - not
  committed to the repo, since it is not an automated regression test the
  project can run in CI.

## Telegram Capture

- Telegram Capture is local-first: no hosting, no webhook, no cloud sync.
- PR 1 is setup only: Settings UI, non-secret configuration, protected bot
  token storage, and a safe Telegram `getMe` connection test.
- The bot token must never be stored in `state.json`, committed, or logged.
  Non-secret settings may live in `AppState`: enabled flag, bot username,
  allowed Telegram user id, default project, aliases, and future poll interval.
- PR 2 (done) adds polling: `TelegramPollingService` in `TaskOverlay.App` runs
  Bot API `getUpdates` long polling entirely inside the WPF process. No
  webhook, no hosting, no cloud sync.
- Polling accepts messages only from the configured allowed Telegram user id
  and ignores unknown users, non-private chats (groups/channels), bot/self
  messages, and non-text updates. Ignored updates still advance the stored
  `LastUpdateId` cursor so the app never re-fetches the same update forever;
  an allowed capture only advances the cursor once its `SourceDocument` is
  saved, so a save failure is retried instead of losing the message.
- Command parsing (`/capture`, `/source`, `/task`, `/meet`) is deterministic
  and literal: no NLP, no date parsing. Commands are shortcuts, not the final
  UX. `/task` and `/meet` create `TelegramCapture` `SourceDocument` drafts
  ("Telegram task draft" / "Telegram MEET draft"); they never create a final
  Task or MEET in this PR.
- Project aliases resolve case-insensitively; an unresolved hint falls back to
  the configured default project (else the app Default project) and is
  recorded as unresolved in the stored body. Resolution never auto-creates a
  project.
- Plain text and command captures create raw capture / SourceDocument drafts
  for user review. Voice, transcription, AI interpretation, and automatic
  final task/MEET creation are later work.
- PR 3 (done) adds status/diagnostics, not new capture behavior: a volatile,
  non-secret `TelegramCaptureDiagnostics` snapshot in `TaskOverlay.Core`
  reports one of `NotConfigured` / `Disabled` / `Running` /
  `WaitingForMessages` / `TokenError` / `NetworkError` / `Error`, plus poll
  timestamps, the last processed update id, a redacted error summary, and a
  consecutive-error count. It is deliberately small; do not grow the status
  taxonomy without a concrete need.
- Diagnostics are kept in memory only (they reset on restart by design) and
  are surfaced in Settings -> Telegram Capture; the persisted cursor
  (`LastUpdateId`) is unchanged. `TelegramCaptureDiagnosticsRedactor` strips
  anything token-shaped from diagnostics text as defense in depth; nothing
  here changes token storage, the SourceDocument model, or polling filter
  semantics from PR 2.
- "Check now" (manual poll) stops the background loop, performs one
  immediate getUpdates call, then resyncs the loop to current settings. It
  never runs concurrently with the loop's own long poll, because concurrent
  getUpdates calls for the same bot token can make Telegram terminate one of
  them - manual checks must not destabilize normal polling.

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
