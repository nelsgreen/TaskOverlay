# TaskOverlay Product Decisions

This document records current product and implementation decisions. Update it
when product direction changes, then update the relevant roadmap or backlog
item.

## Product Direction

- TaskOverlay is memory-support, attention-support, and work-recovery software,
  not just a task list.
- TaskOverlay is a personal Windows-first working-memory system. It should
  provide a practical interface to the user's external working memory, not just
  a place to store tasks or context snippets.
- WPF v2 is the active product.
- Go v1 is legacy.
- Workspace is the main management surface.
- Overlay is the attention layer, not the full task manager.
- Tree is the master structure.
- Cross-platform distribution, Web/PWA, macOS support, multi-user accounts, and
  commercial infrastructure are deferred until the Windows product is mature
  and proves useful beyond its owner.
- Telegram/mobile capture is important as an ingestion path into the Windows
  app. It is not currently a general cross-platform TaskOverlay client.
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
- Statuses are TODO / FOCUS / WAIT / DONE. REMIND is not a task status; it is
  reminder/scheduling metadata. Deadline is separate metadata. Pin is a
  separate visibility flag. FUCKUP is a separate marker/flag (see BACKLOG/
  ROADMAP "FUCKUP marker MVP"). None of these extend or replace the four
  statuses above.

## State And Persistence

- C# `AppState` / `state.json` is the current desktop source of truth.
- Production Workspace mutations must remain connected:
  React -> WebView2 bridge -> C# `AppState` / `TreeStateService` ->
  `state.json` -> fresh snapshot -> React.
- React must not write directly to `state.json`.
- `localStorage` must not be used for production persistence.
- No mock-only production controls.
- Backups are backup, not sync.
- Raw source artifacts should be preserved immutably with provenance. Derived
  analysis should be versioned, reviewable, retryable, and re-creatable.
- Existing notes, context, MEET transcripts, recordings, Telegram messages, and
  other captured material should support later backfill analysis and
  re-analysis.

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
- New MEET flows create a real persisted draft immediately. Generated titles
  are stored and explicitly distinguished from user-authored titles so the
  first user input replaces the fallback naturally and recording can start
  before any title edit.
- MEET Details uses one ordered patch-autosave queue: text changes are
  debounced, discrete controls persist immediately, and pending edits flush
  before Close and recording. Save/Revert buttons are not part of this flow.
- Commands dispatched through `WorkspaceCommandDispatcher` are transactional
  at the `AppState`/save boundary. If persistence throws, authoritative memory
  is restored to the pre-command durable state. A failure after disk
  persistence in a dependent refresh or snapshot send is a separate warning
  and does not roll back the saved change. Meeting Assistant's asynchronous
  import/transcription/analysis/recording operations are outside this guarantee
  until their persistence boundaries receive equivalent hardening.
- A state file or restore candidate with a schema newer than the running build
  supports must be rejected before migration, repair, backup fallback, or any
  write. Startup must identify both versions and the untouched state path, then
  require a newer TaskOverlay build.
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
- Completed-subtasks review-needed is a soft attention signal, not a task
  status: when a task is not DONE, has Steps/subtasks, and all of them are
  complete, it may show a review indicator suggesting Complete task / Add
  subtask / Create follow-up task / Dismiss. Dismissing is not a permanent
  mute - the indicator must reappear if the task's subtasks change again.
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
- Basic MEET CRUD and the local recording/Meeting Assistant foundation are
  connected through the WebView2 bridge. Recording uses local Windows audio
  capture and permits only one active recording. Auto-record is an explicit
  per-MEET opt-in and remains visibly indicated while active; emergency
  recording may start before a MEET is classified.
- Compact AAC/M4A is the machine-local default for new recordings. Microphone,
  system, and in-memory mixed PCM are streamed through bounded queues directly
  to separate AAC-LC/M4A tracks using Windows Media Foundation Sink Writer;
  Compact mode never persists full-meeting WAV intermediates and requires no
  external converter executable. The actual selected sample rate, channel
  count, bitrate, duration, bytes, finalization, and validation state are kept
  as recording artifact metadata.
- Each Compact track owns one dedicated MTA encoder thread. COM initialization,
  `MFCreateSinkWriterFromURL`, every Sink Writer call, finalization, and RCW
  release happen on that same thread; callers communicate through a bounded
  command/frame channel. Media Foundation COM objects never cross the UI,
  capture, or thread-pool boundaries.
- Lossless WAV is an explicit independent format for diagnostics,
  compatibility, or intentional lossless capture. It is never a silent
  fallback when Compact encoder initialization fails, and it is not
  automatically converted to AAC. Existing WAV recordings remain compatible.
- Direct M4A currently uses one `*.current.m4a` container per track while a
  recording is active. Normal Stop drains queues, finalizes and reopens each
  container, then atomically renames only valid outputs. Unexpected process
  termination may make the current containers unusable; startup preserves
  their names and marks them interrupted/invalid rather than pretending they
  are Ready. Periodic finalized M4A segmentation is deferred until it can be
  implemented without destabilizing capture.
- Audio/transcript payloads remain in the local recording folder. `state.json`
  stores metadata, relative paths, analysis, and review state; automatic
  backups do not copy audio or transcript files. The optional OpenAI API key
  is protected separately with Windows DPAPI and is never stored in
  `state.json` or logs.
- The MEET workspace uses exactly three top-level tabs: Details, Sources, and
  Review. Sources owns recordings, managed imports, transcript versions, and
  manual screenshots; Review combines the explicitly active transcript with
  analysis and visual references. Transcript, Analysis, and Context are not
  separate top-level tabs.
- Imported audio and transcripts are copied into deterministic MEET-relative
  managed storage. `state.json` stores provenance and safe relative metadata,
  never the original external absolute path. Audio processing ranges are
  non-destructive and never alter the managed original.
- A MEET can retain multiple generated/imported transcript versions and points
  to one active transcript. Analysis records both transcript ID and revision
  ID, and a revision mismatch is surfaced as stale analysis that is re-run only
  by an explicit user action.
- Speaker identity is separate from presentation: normalized segments reference
  stable `SpeakerId` values, while transcript-level mappings store
  `OriginalLabel`, `DisplayName`, and `IsCurrentUser`. Renaming or merging
  speakers must not rewrite original imported/provider artifacts. Global
  speaker editing UI is intentionally deferred.
- Screenshots are explicit user-selected Window/Display captures, stored as
  managed PNG artifacts with UTC time and active-recording offset when one is
  available. There is no silent/periodic capture, video, OCR, or multimodal AI
  processing in this slice.
- Transcription and structured meeting analysis use provider interfaces.
  ProposedActions never mutate state directly: the user reviews and selects
  actions, and apply routes through existing TaskOverlay domain services.
- Long-running transcription and analysis expose one transient coordinator
  operation through the Workspace snapshot. React may add an immediate
  optimistic lock for first-click feedback, but reconciles it with that
  authoritative runtime operation; transient work is never persisted as
  running across process restart. Duplicate protection remains at both UI and
  coordinator boundaries.
- Provider progress is indeterminate unless the provider supplies real
  progress. TaskOverlay may show reliable stages and elapsed time, but never a
  fabricated percentage.
- Recurrence, calendar sync, live transcription, direct meeting-platform APIs,
  and automatic action application remain later features.
- No embedded ChatGPT window inside the app.
- Reminder repeat is a flat minute interval (`remindEveryMinutes`). Monthly
  repeat is not connected until a calendar-aware recurrence model exists.
- Calendar empty-slot right-click opens a context menu with Create task /
  Create MEET, using the clicked date/time as the new record's schedule.
  Right-clicking an existing task/MEET block shows that block's own context
  menu instead - it never falls back to the empty-slot menu.
- Planning Pool holds logical Tasks, not calendar blocks. Scheduling a task
  onto Calendar does not remove it from Planning Pool - a task stays visible
  there until it is DONE (hidden by default once DONE), since a long-running
  task may need several scheduled blocks across a day.
- A task remains one logical record. Calendar time allocation for that task
  is tracked as separate linked `TaskWorkSession` records, not by moving or
  duplicating the task itself. Each session owns only id/taskId/start/end,
  optional note, and timestamps. Calendar blocks are time/work metadata,
  never a task status - the status model stays TODO / FOCUS / WAIT / DONE.
- Task work-session CRUD follows the connected production path: React ->
  WebView2 command -> C# `TaskWorkSessionService` / `AppState` -> `state.json`
  -> fresh Workspace snapshot -> React. Moving/resizing/deleting one block
  affects only that session. Deleting a task cleans its sessions; deleting a
  session never deletes the task.
- Workspace snapshot schema 2 carries logical tasks and task work sessions as
  separate collections. The command-envelope protocol remains schema 1.
- State schema 3 migrates the old single task planned-work fields into one
  linked `TaskWorkSession`, then clears those legacy fields. Missing planned
  work creates no session; malformed legacy values are handled without
  dropping the task. The migration is covered by synthetic fixtures and is
  idempotent. The real user state was unavailable in development, so artifact
  migration must also be verified manually on the user's work computer.
- MEET remains a separate first-class entity and is never represented as a
  task work session.

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
- Telegram capture writes into this layer as drafts that require explicit user
  review before anything is created. MEET transcription and analysis now have
  a connected local recording/review foundation; promoting transcripts into
  ContextHUB sources remains follow-up work.
- Future AI-assisted analysis (meeting analysis, capture interpretation,
  suggested tasks, etc.) follows one pipeline: raw input -> SourceDocument/
  Capture -> `AIAnalysisRun` -> `ProposedAction[]` -> a Review UI -> apply
  through the existing connected services, only after explicit user
  confirmation. AI must never mutate tasks/MEET/context directly. The MEET
  Assistant implements the first bounded ProposedActions review/apply path;
  future AI features must reuse the same explicit-review boundary.
- Future unified capture should introduce a `SourceArtifact` / Context Inbox
  layer over existing notes/context, MEET recordings, imported audio,
  transcripts, screenshots, Telegram captures, phone-originated recordings,
  system call recordings, phone screenshots/shared files, and post-call user
  recollections. This layer is future work and must not bypass the current
  connected AppState/bridge path.
- Context Inbox processing states should distinguish Captured, Needs
  transcription, Ready for analysis, Needs review, Accepted, Rejected, and
  Failed.
- Durable context creation is review-first by default. AI may suggest people,
  projects, workstreams, topics, item types, contradictions, and superseded
  items, but the user must be able to Accept, Edit, Split, Merge, Change scope,
  Change topic, or Reject before durable context is created.
- Future attributed knowledge separates Person, Project, Workstream, Topic,
  ContextItem, and Source reference. Person identity is global; speaker
  identity is transcript-local until linked to a global Person; speaker
  attribution is not the same as context scope.
- A MEET project is only a default hint and does not determine the scope of
  every statement. One source may produce multiple ContextItems across
  projects, workstreams, and topics.
- Context scope must support Project, multiple projects, workspace/general,
  personal, and unassigned / needs scope review.
- Do not silently overwrite older information. Preserve chronology,
  corrections, changing positions, contradictions, and exact source references.
- User recollection is a valid source type, but must not be represented as an
  exact quote or direct recording of another person.
- Context Pack is a read-only export generated from stored TaskOverlay data;
  deprecated/superseded items are excluded by default. Not shipped in the
  foundation PR (expanded export shipped later - see the Context Pack entry
  below).
- Modal-based creation remains appropriate for large editors. MEET now uses one
  dedicated responsive Workspace modal with Details / Sources / Review because
  recording, transcript, analysis, Context, and scheduling no longer fit the
  narrow Details column.
  TASK Details intentionally remains in the right sidebar. Closing the MEET
  modal never owns or stops the process-level recording/finalization runtime.
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
- Telegram ingestion should later accept voice messages, audio files, text,
  screenshots/images, documents, and forwarded materials. The original artifact
  must be preserved, transcribed or visually analyzed when appropriate, split
  into candidates, and sent through Context Inbox.
- Phone-originated capture should later include one-tap personal voice capture,
  shared voice messages/files, screenshots, imported system call recordings,
  imported Telegram/WhatsApp materials, and immediate post-call user
  recollection when an actual call recording is unavailable.
- The user's normal phone-call setup uses a Bluetooth headset. Ambient
  speakerphone recording is not an acceptable fallback.
- Do not promise reliable third-party Telegram or WhatsApp call recording on
  stock Android. Prefer native/system call recording where supported,
  high-quality Telegram/WhatsApp capture through the Windows recording path,
  importing externally created recordings, or post-call recollection as a
  clearly labeled fallback source.
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
- Web/PWA, macOS, multi-user backend, and commercial distribution are
  explicitly deferred possibilities after product maturity and external
  usefulness are validated. They are not active roadmap work.

## Context Assistant

- Context Assistant is the future primary conversational interface to
  ContextHUB outside meetings.
- It should support text questions first and voice input later.
- Search must support combinations of Person, Project, Workstream, Topic,
  MEET, transcript, ContextItem, source document, date, and status.
- Answers must be grounded in retrieved context and include navigable source
  references to ContextItems, meetings, transcript timestamps, screenshots,
  source documents, recordings, or recollections.
- The assistant must surface contradictions and say when confirmed information
  is unavailable.
- Chat messages and model answers are not themselves trusted source material.
  They may create proposed facts, risks, questions, decisions, or tasks only
  through explicit review.
- The same future context-query service should later power Workspace Context
  Chat, voice question input, Overlay Quick Ask, Meeting Brief, Meeting
  Overlay, and Live Meeting Copilot.

## Process

- Manual UX acceptance beats a green build.
- Codex GUI automation is not final UI acceptance.
- v0 is design/product direction when explicitly referenced, but not the domain
  source of truth.
- No fake controls.
- Keep PRs bounded, but avoid micro-PRs for tiny UI changes.
- Modal dialogs never close on an outside/backdrop click. Closing is only
  through an explicit control (Cancel, Close, X) or Escape where a modal
  already supports it. This applies to every shared modal in Workspace
  (Context Pack preview, linked-task/context picker, ContextHUB Add Source/
  Add Context, and the delete-task/delete-section confirmations).
- The Task/MEET Details Context block is collapsed by default when nothing is
  linked and expanded by default once something is, so an empty Context card
  doesn't take up space the user has to look past. A manual header click
  always overrides the default until a different task/MEET is selected.
