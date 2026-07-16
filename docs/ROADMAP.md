# TaskOverlay Roadmap

This roadmap is intentionally practical. It orders likely work without turning
the backlog into a full release plan.

## Immediate

1. Manually verify schema-2 planned-work -> schema-3 `TaskWorkSession`
   migration against the user's real work-computer state and artifact; the
   development environment had no copy of that state.
2. Planning Pool filters: Active / Unscheduled / Today / All (the real
   per-task scheduling indicators are implemented).
3. Task Details work-session history and total-duration summary.
4. Working hours / evening deadline presets.
5. Calendar visual links for deadline/reminder markers.
6. FUCKUP marker MVP.
7. Global Ctrl+Z later, as architecture-level work - not a quick add-on.

The MEET Assistant foundation implements the first bounded ProposedActions
review/apply path. Future AI work must keep the same rule: raw input ->
analysis -> `ProposedAction[]` -> Review UI -> apply through existing services,
never a direct mutation without explicit user confirmation (see DECISIONS
"ContextHUB").

## Near-Term Reliability / Polish

1. Backup dialog polish and retention settings.
2. Reminder quick presets + Repeat connected to the Workspace bridge/AppState —
   done in PR #47 (Monthly repeat deferred).
3. Timeline `DetailEmphasis`.
4. Timeline DONE/completed handling and overdue sorting.
5. Notes resize/auto-grow — done via compact auto-sizing Notes/Waiting for in
   PR #48.
6. Window shell cleanup.
7. Workspace lifecycle/loading/stale selection polish if not already covered by
   merged work.

## Tree / Project Management

1. Verify PR #45 Tree Operations in main.
2. Section operations.
3. Project Root.
4. Project edit mode.
5. Create project flow.
6. Tree drag/drop after `moveTask` is stable.
7. Better task/tray context menus.

## Workspace Evolution

1. Stabilize shell and Details layout.
2. Adaptive layout/collapsible panels.
3. Hotkey toggle behavior.
4. Auto-open Workspace after backup checks.
5. Keep old Tree Manager fallback until Workspace is stable.
6. Bridge changes as connected vertical slices only.

## Scheduling / Planning

1. Work schedule settings.
2. Deadline polish.
3. Calendar/Timeline MVP.
4. Timeline Now marker.
5. Calendar day/week planner.
6. Planning pool - tasks, not blocks - implemented; tasks remain until DONE
   and real scheduling indicators show Unscheduled / Scheduled today / N
   blocks today / Total today. Active/Unscheduled/Today/All filters remain.
7. Duration chips/resize, MEET resize, and adjacent shared-boundary handles -
   implemented; preserve for task/task, MEET/MEET, and task/MEET.
8. Calendar empty-slot context menu - implemented with connected Create task /
   Create MEET at the clicked date/time; existing blocks keep their own menu.
9. Multi-segment task scheduling - implemented with `TaskWorkSession`: one
   logical task, multiple linked calendar placements, connected CRUD,
   schema-2 migration, and Day/Week rendering. Task Details history remains.
10. Reminder action system.
11. Calendar visual links for deadline/reminder markers.

## MEET

1. MEET persistence and connected Details - implemented in the Basic MEET MVP.
2. MEET Timeline display/navigation - implemented.
3. MEET creation and drag/drop rescheduling from Calendar - implemented.
4. Handle next MEET countdown.
5. Local recording - Compact direct AAC/M4A foundation implemented with
   microphone, system, and mixed tracks, strict per-writer MTA thread ownership,
   bounded queues, concise retryable failure UI, and optional Lossless WAV.
6. Emergency recording and later classification - foundation implemented.
7. Post-meeting transcription - connected optional OpenAI provider implemented.
8. Structured meeting analysis - connected optional OpenAI provider implemented.
9. Suggested actions review/apply selected - foundation implemented; no
   automatic mutations.
10. Dedicated connected MEET modal - implemented as Details / Sources / Review;
    TASK Details remains in the right sidebar, tab changes preserve unsaved
    Details state, and modal close does not stop recording.
11. Managed M4A/WAV/MP3 import, non-destructive processing ranges, TXT/MD/SRT/
    VTT transcript versions, explicit active transcript, revision-bound stale
    analysis, and manual timestamped screenshots - implemented.
12. Recording artifact/manual QA, finalized M4A segmentation for bounded crash
    loss, device recovery, retention, transcript search/editor actions,
    ContextHUB promotion, Meeting Brief, user speaker identification,
    OCR/multimodal review, and additional/local providers.

Recurrence, calendar sync, live transcription, and external meeting-platform
APIs remain intentionally later work.

## Task Quality

1. Priority.
2. Checklist / Steps — checkpoints MVP done in PR #48; follow-ups: promote a
   Step to a child task, Step templates, step-level reminders/deadlines, AI
   step breakdown, and overlay parent progress.
2a. Completed-subtasks review-needed indicator - soft attention signal (not
    a status) when all Steps/subtasks are done but the parent task isn't;
    suggested actions Complete task / Add subtask / Create follow-up /
    Dismiss (reappears if subtasks change again).
3. Attachments.
4. Undo/archive/draft recovery. Global Ctrl+Z is separate, later,
   architecture-level work - not a quick add-on to this item.
5. Templates/routines.
6. Chunking helper beyond the implemented lightweight Steps MVP.
7. Focus mode.
8. Low-stimulus/reduced motion.
9. FUCKUP marker MVP - a separate marker/flag, not a task status; does not
   replace or extend TODO / FOCUS / WAIT / DONE.

## Reporting / Analytics

1. `CompletedAtUtc` reliability.
2. Completed tasks report.
3. Weekly report support.
4. Work Pattern Analytics.

## ContextHUB

1. Foundation: models, service, commands, snapshot, ContextHUB tab - done in
   the ContextHUB foundation PR.
2. Task Details Context block - done: link/unlink existing SourceDocuments
   and ContextItems from the task's own project, compact card below Steps,
   "Open ContextHUB" shortcut. MEET Details Context block is not part of
   this slice.
2a. ContextHUB Details -> LINKED TASKS picker - done: replaced the plain
    task dropdown (showed every task including other projects; picking an
    ineligible one silently failed) with a searchable, same-project-only
    picker showing status and path, used from both SourceDocument and
    ContextItem Details.
3. MEET Details Context block - done: same "Context" card as Task Details
   (link/unlink existing SourceDocuments/ContextItems from the MEET's own
   project, "Open ContextHUB" shortcut), reusing the Task Context block's
   shared rendering core rather than a parallel implementation. MEET's
   linked task field is unrelated and untouched. MEET's own linked-*task*
   picker (i.e. redesigning the "Linked task" dropdown inside MEET Details)
   is not part of this slice - only #59's ContextHUB Details task picker was
   in scope there.
4. Context Pack export/copy - done: deterministic markdown export for
   Claude/ChatGPT/Codex, generated from stored TaskOverlay data only
   (`lib/context-pack-builder.ts`, `context-pack-modal.tsx`). Three entry
   points: "Context Pack" in the ContextHUB toolbar (Project pack, requires
   one project selected), and a "Context Pack" action in the shared Task/
   MEET Context block (focused Task/MEET pack including linked context and
   same-project active decisions/blockers/open questions). Export/copy
   only - no AI, no external API calls, no automatic analysis, no task/MEET
   creation, no AppState mutation.
5. Manual source import polish.
6. MEET recording/transcription/analysis foundation is implemented with
   explicit ProposedActions review; transcript promotion into ContextHUB and
   broader capture interpretation remain later work and must never auto-create.

## Telegram Capture

1. Setup: native Settings UI, protected bot token storage, non-secret
   `AppState` settings, project aliases, and Bot API `getMe` test - done.
2. Long polling: receive text messages inside WPF v2, allowlist one Telegram
   user id, ignore groups/channels/unknown users, and deduplicate updates
   with a stored cursor - done. Optional `/capture`, `/source`, `/task`,
   `/meet` shortcuts create `TelegramCapture` `SourceDocument`s (including
   task/MEET drafts, never final Task/MEET records).
3. Capture inbox / ContextHUB draft path: store raw text safely and let the
   user review before applying anything - captures land as ContextHUB
   SourceDocuments today; a dedicated review/apply inbox is later work.
4. Status/diagnostics - done. Settings -> Telegram Capture shows whether
   polling is configured, running, waiting for messages, or failing
   (token/network/other), with last poll/success/captured times, the last
   processed update id, and a redacted error summary; a "Check now" button
   and an "Open ContextHUB" shortcut are available. No new capture behavior.
5. Later: voice, transcription, AI interpretation and LLM-proposed actions
   with review-before-apply, automatic task/MEET creation, Context Pack
   workflows.

## Workstreams

1. Keep Workstream = section/group MVP.
2. Add narrative fields only after model decision.
3. Later decide if cross-sectional curated Workstream is needed.

## Work/Home And Sync/Mobile

1. Decide Work/Home storage.
2. Stabilize local model.
3. Server-mediated offline-first sync.
4. Mobile companion later.
5. No P2P/CRDT first.
