# TaskOverlay Roadmap

This roadmap is intentionally practical. It orders likely work without turning
the backlog into a full release plan.

## Immediate

1. PR #48 Steps/Checkpoints MVP is merged; treat the lightweight connected
   Steps model as implemented and verify UX details when touching it.
2. Continue connected Tree productivity slices: section actions, fast task and
   subtask capture, safe delete, and Details Location moves.
3. Do not start drag/drop or major features until connected Tree operations are
   stable.

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
6. Planning pool.
7. Duration chips/resize.
8. Reminder action system.

## MEET

1. MEET persistence and connected Details - implemented in the Basic MEET MVP.
2. MEET Timeline display/navigation - implemented.
3. MEET creation and drag/drop rescheduling from Calendar - implemented.
4. Handle next MEET countdown.
5. Local recording.
6. Emergency recording.
7. Post-meeting transcription.
8. AI meeting analysis.
9. Suggested tasks review/create selected.

Recording, transcription, AI analysis, recurrence, calendar sync, and external
meeting-provider APIs remain intentionally outside the Basic MEET MVP.

## Task Quality

1. Priority.
2. Checklist / Steps — checkpoints MVP done in PR #48; follow-ups: promote a
   Step to a child task, Step templates, step-level reminders/deadlines, AI
   step breakdown, and overlay parent progress.
3. Attachments.
4. Undo/archive/draft recovery.
5. Templates/routines.
6. Chunking helper beyond the implemented lightweight Steps MVP.
7. Focus mode.
8. Low-stimulus/reduced motion.

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
3. MEET Details Context block.
4. Project Context Pack export/copy.
5. Manual source import polish.
6. Later: OpenAI meeting analysis, transcription output, Telegram capture -
   all writing drafts for explicit user review, never auto-creating.

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
