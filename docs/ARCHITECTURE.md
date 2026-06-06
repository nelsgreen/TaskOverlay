# Architecture notes

This refactor is deliberately conservative.

The original v13 code was a single large Windows GUI file. It worked, but it was difficult to change safely because unrelated concerns lived together.

The refactor splits the source into focused files while keeping the same Go package. This avoids a risky behavioral rewrite.

## Current source groups

| File | Responsibility |
|---|---|
| `winapi_types.go` | WinAPI aliases, structs, constants, procedures |
| `model.go` | Task, settings, state, actions, app state |
| `colors.go` | Color options |
| `main.go` | Process startup and message loop |
| `window_proc.go` | Main WinAPI window procedure |
| `render_main.go` | Main rendering flow and task list |
| `details.go` | Task details panel |
| `drawing.go` | Low-level drawing helpers |
| `actions.go` | User actions, editing, task mutations |
| `timers_export.go` | Due checks and task export |
| `storage.go` | State loading, migration, normalization, atomic save |
| `logging.go` | Logs and crash output |
| `session.go` | Session marker and single-instance lock |
| `diagnostics.go` | Diagnostic export |
| `shortcut.go` | Desktop shortcut creation |
| `utils.go` | Small helpers |

## Refactor rule

No behavior change was intended in this pass.

Further changes should be made in small steps:
1. rendering presets;
2. alpha model cleanup;
3. completed tasks UX;
4. task description editor;
5. timer UX.

## Stability principles

- Keep GUI on a locked OS thread.
- Avoid heavy work directly inside Windows message handlers.
- Do not save state on every mouse click.
- Use delayed/atomic state saves.
- Keep diagnostics independent from normal state saving.
- Keep state schema backward-compatible.
