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
2. Reminder quick presets + Repeat connected to the Workspace bridge/AppState.
3. Timeline `DetailEmphasis`.
4. Timeline DONE/completed handling and overdue sorting.
5. Notes resize/auto-grow.
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

1. MEET persistence and Details.
2. MEET Timeline interaction.
3. New MEET action.
4. Handle next MEET countdown.
5. Local recording.
6. Emergency recording.
7. Post-meeting transcription.
8. AI meeting analysis.
9. Suggested tasks review/create selected.

## Task Quality

1. Priority.
2. Attachments.
3. Undo/archive/draft recovery.
4. Templates/routines.
5. Chunking helper beyond the implemented lightweight Steps MVP.
6. Focus mode.
7. Low-stimulus/reduced motion.

## Reporting / Analytics

1. `CompletedAtUtc` reliability.
2. Completed tasks report.
3. Weekly report support.
4. Work Pattern Analytics.

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
