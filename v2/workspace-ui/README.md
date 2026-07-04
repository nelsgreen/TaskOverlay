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
versioned, read-only snapshot of the loaded `AppState` through WebView2
messaging after the local page has loaded. React does not read `state.json`
and sends no mutation commands back to C#.

When the frontend runs in a normal browser without WebView2, it uses the mock
dataset as a development fallback. The mock dataset is not used by the
published Workspace window. Editing controls are disabled for bridged data;
Workspace persistence and React-to-C# mutations are intentionally out of
scope for this bridge version.
