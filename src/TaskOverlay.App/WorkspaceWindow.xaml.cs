using System;
using System.Collections.Generic;
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
    private readonly Func<MeetingTranscript, MeetingTranscriptSnapshotContent?>?
        _meetingTranscriptLoader;
    private readonly Func<MeetingScreenshot, string?>? _screenshotThumbnailLoader;
    private readonly Func<MeetingRecordingPolicy>? _defaultRecordingPolicyLoader;
    private readonly Func<IReadOnlyList<WorkspaceMeetingOperationSnapshot>>?
        _meetingOperationLoader;
    private readonly MeetingAudioResourceResolver _meetingAudioResolver;
    private bool _initialized;

    public WorkspaceWindow(
        AppState state,
        string stateDirectory,
        Action saveState,
        Action stateChanged,
        Func<string, CancellationToken, Task<WorkspaceCommandResult?>>?
            runtimeCommandHandler = null,
        Func<MeetingRecording, string?>? transcriptLoader = null,
        Func<Guid?>? activeRecordingIdLoader = null,
        Action<string, Exception?>? diagnostic = null,
        Func<MeetingTranscript, MeetingTranscriptSnapshotContent?>?
            meetingTranscriptLoader = null,
        Func<MeetingScreenshot, string?>? screenshotThumbnailLoader = null,
        Func<MeetingRecordingPolicy>? defaultRecordingPolicyLoader = null,
        Func<IReadOnlyList<WorkspaceMeetingOperationSnapshot>>? meetingOperationLoader = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _diagnostic = diagnostic;
        _meetingAudioResolver = new MeetingAudioResourceResolver(
            _state,
            stateDirectory,
            diagnostic);
        _commandDispatcher = new WorkspaceCommandDispatcher(
            _state,
            saveState,
            stateChanged,
            diagnostic);
        _runtimeCommandHandler = runtimeCommandHandler;
        _transcriptLoader = transcriptLoader;
        _activeRecordingIdLoader = activeRecordingIdLoader;
        _meetingTranscriptLoader = meetingTranscriptLoader;
        _screenshotThumbnailLoader = screenshotThumbnailLoader;
        _defaultRecordingPolicyLoader = defaultRecordingPolicyLoader;
        _meetingOperationLoader = meetingOperationLoader;
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
            "resources",
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
            core.AddWebResourceRequestedFilter(
                $"https://{WorkspaceHostName}{MeetingAudioResourceResolver.ResourcePathPrefix}*",
                CoreWebView2WebResourceContext.All);
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
            core.WebResourceRequested += CoreWebView2_OnWebResourceRequested;
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

        if (IsSnapshotRequest(e.WebMessageAsJson))
        {
            TrySendSnapshot("client retry");
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
                result = result.WithWarning(
                    "snapshotFailed",
                    "Changes were saved, but Workspace could not refresh its state.");
            }
        }

        TrySendMessage(result);
    }

    private void CoreWebView2_OnWebResourceRequested(
        object? sender,
        CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!_meetingAudioResolver.TryResolveRequest(e.Request.Uri, out var resource))
        {
            e.Response = CreateAudioResponse(Stream.Null, 404, "Not Found", 0,
                "text/plain", null);
            return;
        }

        try
        {
            var rangeHeader = e.Request.Headers.GetHeader("Range");
            if (!string.IsNullOrWhiteSpace(rangeHeader))
            {
                if (!MeetingAudioByteRange.TryParse(
                        rangeHeader,
                        resource.Length,
                        out var range))
                {
                    e.Response = WorkspaceWebView.CoreWebView2.Environment
                        .CreateWebResourceResponse(
                            Stream.Null,
                            416,
                            "Range Not Satisfiable",
                            $"Content-Range: bytes */{resource.Length}\r\n" +
                            "Cache-Control: no-store");
                    return;
                }

                var file = OpenAudioFile(resource.FilePath);
                var bounded = new BoundedReadStream(file, range.Start, range.Length);
                e.Response = CreateAudioResponse(
                    bounded,
                    206,
                    "Partial Content",
                    range.Length,
                    resource.ContentType,
                    $"bytes {range.Start}-{range.End}/{resource.Length}");
                return;
            }

            e.Response = CreateAudioResponse(
                OpenAudioFile(resource.FilePath),
                200,
                "OK",
                resource.Length,
                resource.ContentType,
                null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _diagnostic?.Invoke("Workspace audio resource became unavailable.", ex);
            e.Response = CreateAudioResponse(Stream.Null, 404, "Not Found", 0,
                "text/plain", null);
        }
    }

    private CoreWebView2WebResourceResponse CreateAudioResponse(
        Stream content,
        int statusCode,
        string reason,
        long contentLength,
        string contentType,
        string? contentRange)
    {
        var headers = $"Content-Type: {contentType}\r\n" +
                      $"Content-Length: {contentLength}\r\n" +
                      "Accept-Ranges: bytes\r\n" +
                      "Cache-Control: no-store";
        if (!string.IsNullOrWhiteSpace(contentRange))
        {
            headers += $"\r\nContent-Range: {contentRange}";
        }

        return WorkspaceWebView.CoreWebView2.Environment.CreateWebResourceResponse(
            content,
            statusCode,
            reason,
            headers);
    }

    private static FileStream OpenAudioFile(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read | FileShare.Delete,
        64 * 1024,
        FileOptions.SequentialScan);

    private static bool IsSnapshotRequest(string json)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("messageType", out var messageType) &&
                   messageType.ValueKind == System.Text.Json.JsonValueKind.String &&
                   string.Equals(
                       messageType.GetString(),
                       "snapshotRequest",
                       StringComparison.Ordinal);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
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
            activeMeetingRecordingId: _activeRecordingIdLoader?.Invoke(),
            meetingTranscriptLoader: _meetingTranscriptLoader,
            screenshotThumbnailLoader: _screenshotThumbnailLoader,
            defaultMeetingRecordingPolicy: _defaultRecordingPolicyLoader?.Invoke() ??
                                           MeetingRecordingPolicy.Manual,
            meetingOperations: _meetingOperationLoader?.Invoke(),
            meetingAudioLoader: _meetingAudioResolver.Describe);

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
            core.WebResourceRequested -= CoreWebView2_OnWebResourceRequested;
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
