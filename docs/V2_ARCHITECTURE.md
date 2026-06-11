# TaskOverlay v2 WPF prototype

TaskOverlay v2 is a Windows-only experiment under `v2/`. The Go application in
`cmd/taskoverlay/` remains the v1 implementation and is not referenced by v2.

## Prototype responsibilities

- `App.xaml.cs` owns process lifetime, the tray icon, and window creation.
- `OverlayWindow` is a transparent, borderless, always-on-top WPF window.
- Passive mode renders only three static marker/text rows.
- Pointer entry enables a simple background panel; pointer exit returns to passive
  mode after 500 ms.
- `SettingsWindow` validates independent settings-window lifecycle.
- The manifest requests Per-Monitor V2 DPI awareness.

No persistence, state migration, editing, hotkeys, clipboard integration, network
access, or advanced themes are included.

## Build and run

Requirements:

- Windows 10 or 11;
- .NET 8 SDK.

```powershell
dotnet restore .\v2\TaskOverlay.sln --configfile .\v2\NuGet.Config
dotnet build .\v2\TaskOverlay.sln --configuration Release --no-restore
dotnet run --project .\v2\src\TaskOverlay.App\TaskOverlay.App.csproj
```

The v2 workflow is separate from the existing Go workflow and runs only when v2
or its architecture documentation changes.

## Validation targets

- stable WPF transparency without flicker;
- hover activation and 500 ms passive delay;
- topmost behavior;
- tray Show, Hide, Settings, and Exit commands;
- settings window recreation after close;
- placement inside the working area of the monitor containing the pointer;
- behavior under mixed DPI and multiple monitors.

## Known limitations

- The task rows are static.
- Passive hit testing covers the prototype window bounds.
- Position and settings are not persisted.
- The tray uses the standard Windows application icon.
- No single-instance guard is implemented.
- WPF transparent-window performance still needs testing on varied GPUs and
  remote-desktop sessions.
