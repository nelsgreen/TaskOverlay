using System;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public sealed class SettingsWindowActions
{
    public SettingsWindowActions(
        Action<OverlayMode> setOverlayMode,
        Action openLogs,
        Action openStateFolder,
        Action resetWindowPositions)
    {
        SetOverlayMode = setOverlayMode;
        OpenLogs = openLogs;
        OpenStateFolder = openStateFolder;
        ResetWindowPositions = resetWindowPositions;
    }

    public Action<OverlayMode> SetOverlayMode { get; }
    public Action OpenLogs { get; }
    public Action OpenStateFolder { get; }
    public Action ResetWindowPositions { get; }
}
