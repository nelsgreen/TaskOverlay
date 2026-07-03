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

The exported UI currently uses mock data only. There is no C# to React bridge,
no `state.json` access, and no persistence from Workspace controls.
