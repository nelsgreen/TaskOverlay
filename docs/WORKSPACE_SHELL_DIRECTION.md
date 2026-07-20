# Workspace shell and task editor direction

Status: accepted product direction and backlog addendum.

This note records decisions that should not remain only in chat history. It is intentionally separate from the active Design System PR sequence and does not change current runtime behavior.

## Product shell

- TaskOverlay should converge on two primary long-lived surfaces:
  - `Workspace` as the main application window;
  - `Overlay` as the attention layer.
- Workspace should open automatically on application startup after required startup and backup checks complete.
- Overlay remains a separate lightweight surface for active work and attention signals.
- Closing Workspace should not exit the application; reopening should focus the existing Workspace instance.

## Task editor model

- The current right-side Task Details panel remains as the fast inspector/editor on sufficiently wide Workspace layouts.
- Add a focused Task Details modal inside Workspace for longer or deeper editing.
- The modal does not replace the right-side panel. Both surfaces should use one shared task editor implementation rather than independent forms.
- Target shared component direction:
  - `TaskEditorContent` with `create` and `edit` modes;
  - shared field behavior, validation, autosave/save semantics, bridge commands, and state reconciliation;
  - presentation-specific shells for right-side inspector and modal.
- Avoid maintaining separate Quick Add, Task Details, and modal form logic that can drift apart.

## Task creation and hotkeys

- `Ctrl+Alt+Q` should eventually:
  1. show Workspace if hidden;
  2. activate/focus the existing Workspace window;
  3. open the task editor modal in `create` mode;
  4. focus the Title field.
- Task creation must continue through the connected production path:
  React -> WebView2 bridge -> C# commands/services -> authoritative AppState -> state.json -> fresh Workspace snapshot -> React.
- Do not introduce localStorage, React-only persistence, mock-only controls, or a parallel task draft store as production truth.
- The legacy standalone Quick Add window may remain during transition, but should be removed only after Workspace create-modal parity and manual QA are complete.

## Settings consolidation

- Move Settings into a Workspace modal after the shared modal/dialog design-system primitives exist.
- Preserve the current standalone Settings window during transition until all settings, keyboard flows, and failure states are available inside Workspace.
- The Settings hotkey should eventually show/focus Workspace and open the Settings modal.

## Task opening flows

- Clicking a task in Overlay should eventually show/focus Workspace and open that task in the focused Task Details modal.
- Selecting a task inside Workspace may continue to use the right-side inspector as the fast default on wide layouts.
- Full modal editing should be available when more space, deeper context, long notes, steps, sources/history, or focused work is needed.

## Context action row

Immediate narrow-inspector correction:

- Rename the three Context actions to:
  - `Link`;
  - `Hub`;
  - `Export`.
- Keep three equal compact buttons inside the Context card without overflow.
- Use the same compact geometry and interaction quality as the TODO / FOCUS / WAIT / DONE status controls, while keeping these as ordinary actions rather than state toggles.
- Preserve distinct icons and existing behavior:
  - Link existing context;
  - open ContextHUB;
  - export/open Context Pack.

## Reminder delivery direction

No final replacement decision yet.

- Do not remove the existing native bottom-right reminder delivery until a replacement is proven reliable.
- Investigate showing active reminders in Overlay with direct actions such as Done, Snooze, and Open.
- Likely target split:
  - native notification provides the initial attention signal;
  - Overlay keeps the active reminder visible and actionable afterward.
- Treat complete migration of reminders into Overlay as a separate product and reliability decision.

## Suggested implementation sequence

This work remains behind the active Design System integration gate.

1. Finish the bounded Design System sequence, including Button and Modal/Dialog primitives.
2. Introduce shared `TaskEditorContent` without changing persistence architecture.
3. Add Task Details modal while retaining the right-side quick inspector.
4. Add Workspace task-create modal.
5. Route `Ctrl+Alt+Q` to Workspace create mode.
6. Move Settings into a Workspace modal after parity.
7. Make Workspace open at startup after startup/backup checks.
8. Route Overlay task clicks to focused Task Details modal.
9. Design and test reminder behavior across native notifications and Overlay.

## Constraints

- WPF is the active TaskOverlay product.
- Do not change the real user state manually or use `%APPDATA%\TaskOverlayV2` in automated tests.
- Manual Windows artifact QA is performed by the user.
- Keep each implementation PR bounded. Do not combine startup lifecycle, task editor migration, Settings migration, and reminder redesign into one PR.
