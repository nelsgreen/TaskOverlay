using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public partial class WorkspaceWindow : Window
{
    private const string WorkspaceHostName = "taskoverlay.workspace";
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AppState _state;
    private readonly WorkspaceCommandDispatcher _commandDispatcher;
    private readonly Func<string, CancellationToken, Task<WorkspaceCommandResult?>>?
        _runtimeCommandHandler;
    private readonly Func<MeetingRecording, string?>? _transcriptLoader;
    private readonly Func<Guid?>? _activeRecordingIdLoader;
    private readonly Action<string, Exception?>? _diagnostic;
    private bool _initialized;

    public WorkspaceWindow(
        AppState state,
        Action saveState,
        Action stateChanged,
        Func<string, CancellationToken, Task<WorkspaceCommandResult?>>?
            runtimeCommandHandler = null,
        Func<MeetingRecording, string?>? transcriptLoader = null,
        Func<Guid?>? activeRecordingIdLoader = null,
        Action<string, Exception?>? diagnostic = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _commandDispatcher = new WorkspaceCommandDispatcher(
            _state,
            saveState,
            stateChanged);
        _runtimeCommandHandler = runtimeCommandHandler;
        _transcriptLoader = transcriptLoader;
        _activeRecordingIdLoader = activeRecordingIdLoader;
        _diagnostic = diagnostic;
        InitializeComponent();
    }

    private async void WorkspaceWindow_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        var workspaceDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "WorkspaceWeb");
        var indexPath = Path.Combine(workspaceDirectory, "index.html");
        if (!File.Exists(indexPath))
        {
            ShowError(
                "Workspace static files were not found. " +
                $"Expected: {indexPath}");
            return;
        }

        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "TaskOverlayV2",
                "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: userDataFolder);
            await WorkspaceWebView.EnsureCoreWebView2Async(environment);

            var core = WorkspaceWebView.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping(
                WorkspaceHostName,
                workspaceDirectory,
                CoreWebView2HostResourceAccessKind.DenyCors);
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                "window.__taskOverlayWorkspaceMessages = [];" +
                "window.chrome.webview.addEventListener('message', " +
                "event => window.__taskOverlayWorkspaceMessages?.push(event.data));");
            core.NewWindowRequested += CoreWebView2_OnNewWindowRequested;
            core.NavigationStarting += CoreWebView2_OnNavigationStarting;
            core.NavigationCompleted += CoreWebView2_OnNavigationCompleted;
            core.WebMessageReceived += CoreWebView2_OnWebMessageReceived;
            core.Navigate($"https://{WorkspaceHostName}/index.html");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowError(
                "Microsoft Edge WebView2 Runtime is required to open " +
                "Workspace. Install the WebView2 Runtime and try again.");
        }
        catch (Exception ex)
        {
            ShowError($"Workspace failed to initialize: {ex.Message}");
        }
    }

    private static void CoreWebView2_OnNewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
    }

    private static void CoreWebView2_OnNavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) ||
            !string.Equals(
                uri.Host,
                WorkspaceHostName,
                StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
        }
    }

    private void CoreWebView2_OnNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            try
            {
                SendSnapshot();
                _diagnostic?.Invoke("Workspace initial snapshot published.", null);
            }
            catch (Exception ex)
            {
                ShowError($"Workspace state could not be loaded: {ex.Message}");
                return;
            }

            LoadingPanel.Visibility = Visibility.Collapsed;
            return;
        }

        ShowError($"Workspace failed to load: {e.WebErrorStatus}");
    }

    private async void CoreWebView2_OnWebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsWorkspaceUri(e.Source))
        {
            return;
        }

        WorkspaceCommandResult result;
        try
        {
            result = _runtimeCommandHandler is null
                ? _commandDispatcher.Dispatch(e.WebMessageAsJson)
                : await _runtimeCommandHandler(
                      e.WebMessageAsJson,
                      CancellationToken.None) ??
                  _commandDispatcher.Dispatch(e.WebMessageAsJson);
        }
        catch (Exception ex)
        {
            _diagnostic?.Invoke("Workspace runtime command failed.", ex);
            result = WorkspaceCommandResult.Failed(
                string.Empty,
                "commandFailed",
                "Workspace command could not be applied.");
        }

        if (result.Success)
        {
            if (!TrySendSnapshot("command"))
            {
                result = WorkspaceCommandResult.Failed(
                    result.CommandId,
                    "snapshotFailed",
                    "Task was saved, but Workspace could not refresh its state.");
            }
        }

        TrySendMessage(result);
    }

    /// <summary>
    /// Pushes a fresh snapshot after AppState changed outside of a Workspace
    /// command (e.g. a Telegram Capture poll saved a SourceDocument). A no-op
    /// if the WebView has not finished loading yet.
    /// </summary>
    public void RefreshFromExternalChange()
    {
        if (!_initialized || WorkspaceWebView.CoreWebView2 is null)
        {
            return;
        }

        TrySendSnapshot("external change");
    }

    private void SendSnapshot()
    {
        var snapshot = WorkspaceSnapshotFactory.Create(
            _state,
            mode: WorkspaceSnapshotFactory.ConnectedMode,
            transcriptLoader: _transcriptLoader,
            activeMeetingRecordingId: _activeRecordingIdLoader?.Invoke());
        SendMessage(snapshot);
    }

    private void SendMessage<T>(T message)
    {
        var json = JsonSerializer.Serialize(message, SnapshotJsonOptions);
        WorkspaceWebView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private bool TrySendSnapshot(string reason)
    {
        try
        {
            SendSnapshot();
            _diagnostic?.Invoke($"Workspace snapshot published after {reason}.", null);
            return true;
        }
        catch (Exception ex)
        {
            _diagnostic?.Invoke($"Workspace snapshot publication failed after {reason}.", ex);
            return false;
        }
    }

    private void TrySendMessage<T>(T message)
    {
        try
        {
            SendMessage(message);
        }
        catch (Exception)
        {
            // A closing or failed WebView must not crash the WPF host.
        }
    }

    private static bool IsWorkspaceUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        string.Equals(
            uri.Host,
            WorkspaceHostName,
            StringComparison.OrdinalIgnoreCase);

    private void WorkspaceWindow_OnClosed(object? sender, EventArgs e)
    {
        if (WorkspaceWebView.CoreWebView2 is { } core)
        {
            core.NewWindowRequested -= CoreWebView2_OnNewWindowRequested;
            core.NavigationStarting -= CoreWebView2_OnNavigationStarting;
            core.NavigationCompleted -= CoreWebView2_OnNavigationCompleted;
            core.WebMessageReceived -= CoreWebView2_OnWebMessageReceived;
        }

        WorkspaceWebView.Dispose();
    }

    private void ShowError(string message)
    {
        WorkspaceWebView.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Collapsed;
        ErrorMessageText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }
}
