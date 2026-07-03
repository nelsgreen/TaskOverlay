# TaskOverlay Product Backlog

This document keeps product backlog items out of chat history and close to the
codebase. Items here are not commitments for the next PR. They are grouped by
product area so implementation work can be split into bounded changes.

## 1. Overlay Modes And Handle

- Working mode:
  - Show only FOCUS tasks.
  - Do not show descriptions by default.
  - Use a lower contrast idle display.
  - On hover, tasks become normal contrast and descriptions appear.
- Hide or remove Auto quest tracker mode for now.
- Do not show TaskOverlay overlay/handle/panel windows in Alt+Tab or the normal Windows app switcher.
- Future functional handle / attention hub.
- Handle redesign:
  - Make the handle prettier.
  - Make the handle more functional.
  - Keep the v0 attention-hub direction: a wider pill-shaped handle with MEET countdown, counters, project/task color dots, and a strong dark/purple visual identity is acceptable and preferred.
  - Do not over-compact the handle just because it is a handle; the v0 size felt usable and visually successful.
  - Do not show static app logo or the literal TaskOverlay name on the handle by default; that space should be used for functional attention information.
  - Colored compact counters/dots may represent projects or current task counts if the meaning stays discoverable through hover/tooltips.
  - Show countdown to the next MEET item on the handle.
  - Support compact attention counters for FOCUS / REMIND / WAIT / Panel where useful.
  - Preserve the handle anchor invariant: handle position is the source of truth and must not be derived from panel position.
- Window size controls per mode in Settings.

## 2. Reminder, Calendar, Meetings, And Scheduling

- Deadlines for tasks.
- Deadline should be an optional task field.
- Keep Deadline separate from REMIND.
- Show task deadlines in future calendar views.
- Meetings module:
  - MEET is not a task and not a task status.
  - MEET is a separate calendar-like item for meeting attention and countdown.
  - MEET does not combine with TODO / FOCUS / WAIT / DONE / REMIND because it is not a task lifecycle item.
  - MEET should support project, title, description/notes, explicit date, explicit time, optional duration, optional location, and optional meeting link.
  - MEET should not use relative reminder-style presets such as +30 min, +1 hour, Tomorrow morning, or Next workday morning.
  - MEET creation should optimize for exact future date/time entry, for example Tuesday 15:00, plus the meeting link.
  - MEET should have a fast compact editor similar in density to the REMIND editor, but with separate meeting-specific behavior.
  - The handle should show time to the next meeting or current/next meetings.
  - Manual MEET items first; external calendar integration later.
- Application calendar:
  - Add an internal calendar/timeline view to TaskOverlay.
  - Calendar should show MEET items, task REMIND items, and task Deadline items together.
  - Calendar should keep the concepts visually separate: MEET, REMIND, and Deadline are different item types.
  - Calendar is an app-level planning/attention view, not a replacement for Tree Manager.
- REMIND display settings.
- Upcoming reminder display settings.
- Reminder/date/time UX improvements.

## 3. Notes

- Short important notes module.
- Quick access from handle or expanded panel.
- Notes should not be mixed with tasks.
- Workflow/context notes:
  - Provide a place to write important project or section/workstream context that is not a task.
  - Support notes for Project, Workstream, and Section first; Task/Subtask notes can remain later or reuse existing description fields.
  - Use this for workflow nuances, decisions, links, constraints, or things the user tends to forget.
  - Surface these notes in Tree Manager details so each project/workstream/section has an obvious "where do I write this?" place.
  - Do not show workflow notes in the small active overlay by default.
  - Candidate UI labels: Context notes, Workflow notes, Section notes, Workstream notes.

## 4. Task Interactions

- WAIT fast flow:
  - When a task status is changed to WAIT, show a Waiting for field immediately in Task Details and Quick Add.
  - Waiting for is where the user writes what or whom the task is waiting for.
  - In Task Details, switching task to WAIT focuses Waiting for.
  - In Quick Add, selecting WAIT should reveal the same compact Waiting for field before task creation.
  - Enter saves where appropriate.
- Empty status area left click opens quick status menu.
- Task Details keyboard shortcuts:
  - Enter saves and closes.
  - Shift+Enter inserts a new paragraph in Notes.
  - Esc cancels.

## 5. Tree Mode And Planning

- Tree as master structure.
- Overlay as active subset.
- Hierarchy:
  - Project.
  - Workstream.
  - Section.
  - Task.
  - Subtask.
- Tree Manager project views:
  - Keep the current tree view as the primary structured task view.
  - Add a future Workstreams tab inside the same Tree Manager window as an alternate project view.
  - The Workstreams tab should not replace the tree; it should visualize the same project tasks through parallel streams and relationships.
  - Tree view answers "where is this task in the hierarchy?".
  - Workstreams view answers "which streams are running, how are tasks related, and what blocks what?".
- Workstreams:
  - Use Workstream for parallel streams of work inside a project.
  - A Workstream can contain sections and nested tasks.
  - A project may have multiple workstreams running in parallel.
  - Tasks may need cross-links across workstreams; support many-to-many relationships later.
  - Cross-links should express dependencies, blockers, related tasks, duplicates, or "see also" relations without forcing a strict parent-child tree.
  - Keep the main hierarchy simple for MVP, but plan the data model so node links can be added later.
- Workstreams view visual ideas:
  - Start with columns or swimlanes per workstream, with compact task cards inside each stream.
  - Show cross-stream dependencies with optional connector lines/arrows when the user enables relationship view.
  - Consider sticky-note style cards for lightweight planning, but avoid a full Kanban workflow unless explicitly scoped later.
  - Support relationship badges on cards even when connector lines are hidden, for example Blocks, Blocked by, Related, Duplicate, See also.
  - Allow selecting a card to open the same right-side details panel as the tree view.
  - Keep many-to-many relationships as a separate links layer over the same task nodes, not as duplicate tasks.
- Section remains useful as a folder/subdivision inside a workstream or project.
- Gantt-like future planning view.
- Active + ancestors display mode.
- Do not render the full backlog inside the small overlay.

## 6. Sync And Cloud

- Research cloud sync options.
- Telegram as a possible capture/sync channel.
- Prefer offline-first local state with later server-mediated sync.
- Avoid early CRDT/P2P unless needed.

## 7. Settings

- Redesign Settings window.
- Add Work/Home task spaces:
  - Provide a Settings switch between Work mode and Home mode.
  - Work mode is the current product/task space being developed now.
  - Home mode should use the same app functionality but a completely separate task memory for household, home, personal chores, and non-work tasks.
  - Work and Home tasks must be isolated and must not appear in each other's Tree Manager, overlay, reminders, calendar views, backups, or search by default.
  - Switching mode should swap the active task store/context safely without mixing tasks.
  - Store the active task space selection and make it visible in Settings and possibly on the handle/panel later.
  - Plan for separate backup files per task space.
- Add window size controls:
  - Auto quest tracker.
  - Collapsed hover panel.
  - Pinned expanded.
  - Quick Add.
  - Task Details.
- Future editable hotkeys.
- Storage/logs/diagnostics section.
