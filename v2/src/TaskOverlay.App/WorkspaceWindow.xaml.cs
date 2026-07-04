using System;
using System.IO;
using System.Text.Json;
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
    private bool _initialized;

    public WorkspaceWindow(
        AppState state,
        Action saveState,
        Action stateChanged)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _commandDispatcher = new WorkspaceCommandDispatcher(
            _state,
            saveState,
            stateChanged);
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

    private void CoreWebView2_OnWebMessageReceived(
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
            result = _commandDispatcher.Dispatch(e.WebMessageAsJson);
        }
        catch (Exception)
        {
            result = WorkspaceCommandResult.Failed(
                string.Empty,
                "commandFailed",
                "Workspace command could not be applied.");
        }

        if (result.Success)
        {
            if (!TrySendSnapshot())
            {
                result = WorkspaceCommandResult.Failed(
                    result.CommandId,
                    "snapshotFailed",
                    "Task was saved, but Workspace could not refresh its state.");
            }
        }

        TrySendMessage(result);
    }

    private void SendSnapshot()
    {
        var snapshot = WorkspaceSnapshotFactory.Create(
            _state,
            mode: WorkspaceSnapshotFactory.ConnectedMode);
        SendMessage(snapshot);
    }

    private void SendMessage<T>(T message)
    {
        var json = JsonSerializer.Serialize(message, SnapshotJsonOptions);
        WorkspaceWebView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private bool TrySendSnapshot()
    {
        try
        {
            SendSnapshot();
            return true;
        }
        catch (Exception)
        {
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
