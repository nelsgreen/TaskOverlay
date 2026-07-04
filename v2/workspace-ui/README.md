# TaskOverlay Workspace UI

This directory contains the local React/Next frontend used by the WPF v2
`WorkspaceWindow` integration shell.

## Build

```powershell
pnpm install --frozen-lockfile
pnpm build
```

Next.js writes the static export to `v2/workspace-ui/out`. The WPF project
copies that directory into `WorkspaceWeb` in build and publish output.
`WorkspaceWindow` maps the published folder to the local virtual host
`https://taskoverlay.workspace` through WebView2, so no web server or internet
connection is required at runtime.

At runtime, WPF remains the source of truth. `WorkspaceWindow` sends a
versioned snapshot of the loaded `AppState` through WebView2 messaging after
the local page has loaded. React never reads or writes `state.json` directly.
It can send versioned commands for task title, status, notes, and panel pin
changes; C# validates and applies each command through the existing domain
services, saves through the WPF state path, and returns a fresh snapshot.
Workspace tab, project scope, task/timeline selection, and tree filter are UI
context stored through the same C# bridge. React still has no direct storage
access, and search text is intentionally session-only.

When the frontend runs in a normal browser without WebView2, it uses the mock
dataset as a development fallback. The mock dataset is not used by the
published Workspace window. Unsupported bridged controls remain disabled:
reminders, deadlines, location, creation, deletion, and reordering are not
part of this write bridge slice.
