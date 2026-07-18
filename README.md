# TaskOverlay

TaskOverlay is a local Windows 10/11 WPF desktop application for task and
meeting working memory. The overlay is the attention layer; Workspace and Tree
remain the primary management surfaces.

## Download and run

Download the `TaskOverlay_Windows_Portable` artifact from a successful GitHub
Actions build and extract the ZIP. Run `TaskOverlay.exe` from its top-level
`TaskOverlay` folder. No installer is required.

TaskOverlay requires the .NET Desktop Runtime 8 and the Microsoft Edge WebView2
Runtime. The app is local-first and uses the existing compatibility state path:
`%APPDATA%\TaskOverlayV2\state.json`. Logs remain in
`%APPDATA%\TaskOverlayV2\logs`.

## Build on Windows

```powershell
pnpm --dir .\workspace-ui install --frozen-lockfile
pnpm --dir .\workspace-ui build
dotnet restore .\TaskOverlay.sln --configfile .\NuGet.Config
dotnet build .\TaskOverlay.sln --configuration Release --no-restore
dotnet test .\TaskOverlay.sln --configuration Release --no-build
dotnet publish .\src\TaskOverlay.App\TaskOverlay.App.csproj --configuration Release --self-contained false -r win-x64 --output .\artifacts\TaskOverlay
```

The publish output is a framework-dependent single-file executable plus the
external `resources\WorkspaceWeb` static bundle. `README.txt` is added by the
portable packaging step in CI.

## Repository layout

```text
TaskOverlay/
  TaskOverlay.sln
  NuGet.Config
  src/             WPF app and Core library
  tests/           Core and App test executables
  workspace-ui/    React/Next static Workspace
  docs/            product and architecture documentation
  .github/         CI and portable packaging
```

`AppState` remains the source of truth. Production React code only communicates
through the WebView2 bridge and never reads or writes `state.json` directly.
