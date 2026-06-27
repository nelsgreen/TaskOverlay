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
- Future functional handle / attention hub.
- Handle redesign:
  - Make the handle prettier.
  - Make the handle more functional.
- Window size controls per mode in Settings.

## 2. Reminder And Scheduling

- Deadlines for tasks.
- Keep Deadline separate from REMIND.
- Meetings reminder module.
- REMIND display settings.
- Upcoming reminder display settings.
- Reminder/date/time UX improvements.

## 3. Notes

- Short important notes module.
- Quick access from handle or expanded panel.
- Notes should not be mixed with tasks.

## 4. Task Interactions

- WAIT fast flow:
  - Switching task to WAIT opens Task Details.
  - Cursor focuses Waiting for.
  - Enter saves.
- Empty status area left click opens quick status menu.
- Task Details keyboard shortcuts:
  - Enter saves and closes.
  - Shift+Enter inserts a new paragraph in Notes.
  - Esc cancels.

## 5. Tree Mode And Planning

- Tree as master structure.
- Overlay as active subset.
- Epics and hierarchy:
  - Project.
  - Epic.
  - Group / Workstream.
  - Task.
  - Subtask.
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
- Add window size controls:
  - Auto quest tracker.
  - Collapsed hover panel.
  - Pinned expanded.
  - Quick Add.
  - Task Details.
- Future editable hotkeys.
- Storage/logs/diagnostics section.
