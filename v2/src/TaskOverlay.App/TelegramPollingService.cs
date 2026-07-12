using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TaskOverlay.Core;

namespace TaskOverlay.App;

/// <summary>Minimal, race-free view of the settings the polling loop needs each cycle.</summary>
public readonly record struct TelegramPollingSnapshot(
    bool Enabled,
    long LastUpdateId,
    int PollIntervalSeconds);

/// <summary>Result of applying one batch of updates to AppState, fed back into diagnostics.</summary>
public readonly record struct TelegramCaptureApplyResult(int CapturedCount, long LastProcessedUpdateId);

/// <summary>Safe, human-readable result of a manual "Poll now" request. Never contains the token.</summary>
public sealed record TelegramPollNowResult(bool Succeeded, string Message)
{
    public static TelegramPollNowResult Skipped(string message) => new(false, message);
    public static TelegramPollNowResult Failed(string redactedMessage) => new(false, redactedMessage);
    public static TelegramPollNowResult NoUpdates() => new(true, "No new updates.");

    public static TelegramPollNowResult Completed(int capturedCount, int ignoredCount) =>
        new(
            true,
            capturedCount > 0
                ? $"Captured {capturedCount} message(s); ignored {ignoredCount} update(s)."
                : $"No allowed messages; ignored {ignoredCount} update(s).");
}

/// <summary>
/// Local-only Telegram getUpdates long-poll loop. No webhook, no hosting, no
/// cloud sync: this runs entirely inside the WPF process. Settings/state
/// access and the resulting AppState mutation are supplied as callbacks so
/// the caller (App.xaml.cs) can route them through the WPF Dispatcher and
/// keep a single serialized state-mutation path shared with UI commands.
/// Also tracks in-memory, non-secret diagnostics (poll timestamps, last
/// error, consecutive error count) so Settings can show whether polling is
/// configured, running, idle, or failing.
/// </summary>
public sealed class TelegramPollingService : IDisposable
{
    private const int MinRequestTimeoutSeconds = 5;
    private const int MaxRequestTimeoutSeconds = 50;
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(5);

    private readonly Func<TelegramPollingSnapshot> _getSnapshot;
    private readonly Func<string?> _getToken;
    private readonly ITelegramUpdatesClient _client;
    private readonly Func<IReadOnlyList<TelegramIncomingUpdate>, TelegramCaptureApplyResult> _applyUpdates;
    private readonly Action<string, Exception?> _diagnosticsLog;
    private readonly DiagnosticsTracker _diagnostics = new();
    private readonly object _lifecycleGate = new();
    private readonly SemaphoreSlim _pollNowGate = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public TelegramPollingService(
        Func<TelegramPollingSnapshot> getSnapshot,
        Func<string?> getToken,
        ITelegramUpdatesClient client,
        Func<IReadOnlyList<TelegramIncomingUpdate>, TelegramCaptureApplyResult> applyUpdates,
        Action<string, Exception?> diagnostics)
    {
        _getSnapshot = getSnapshot ?? throw new ArgumentNullException(nameof(getSnapshot));
        _getToken = getToken ?? throw new ArgumentNullException(nameof(getToken));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _applyUpdates = applyUpdates ?? throw new ArgumentNullException(nameof(applyUpdates));
        _diagnosticsLog = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public bool IsRunning
    {
        get
        {
            lock (_lifecycleGate)
            {
                return _loopTask is { IsCompleted: false };
            }
        }
    }

    /// <summary>
    /// Safe, non-secret status snapshot for Settings. hasToken/hasAllowedUserId
    /// are supplied by the caller since only App.xaml.cs can safely read the
    /// protected token store and current settings.
    /// </summary>
    public TelegramCaptureDiagnostics GetDiagnostics(
        bool enabled,
        bool hasToken,
        bool hasAllowedUserId) =>
        _diagnostics.Snapshot(enabled, hasToken, hasAllowedUserId);

    /// <summary>Starts the loop if it is not already running. Idempotent.</summary>
    public void Start()
    {
        lock (_lifecycleGate)
        {
            if (_loopTask is { IsCompleted: false })
            {
                return;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loopTask = Task.Run(() => RunLoopAsync(token), token);
        }

        _diagnosticsLog("Telegram Capture polling started.", null);
    }

    /// <summary>Cancels the loop. Safe to call repeatedly and when not running.</summary>
    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_lifecycleGate)
        {
            cts = _cts;
            _cts = null;
        }

        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        finally
        {
            cts.Dispose();
        }

        _diagnosticsLog("Telegram Capture polling stopped.", null);
    }

    /// <summary>Starts or stops the loop to match current settings. Call after any settings change and at startup.</summary>
    public void SyncWithSettings()
    {
        if (_getSnapshot().Enabled)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    /// <summary>
    /// Performs one immediate, out-of-band getUpdates cycle using the same
    /// token/offset/allowlist as the background loop, then resumes the loop
    /// to match current settings. The background loop is stopped first so
    /// there is never more than one outstanding getUpdates request for this
    /// bot token at a time (concurrent long-poll requests can make Telegram
    /// terminate one of them), which keeps this from destabilizing normal
    /// polling. A <see cref="_pollNowGate"/> serializes concurrent button
    /// clicks so they cannot race each other either.
    /// </summary>
    public async Task<TelegramPollNowResult> PollNowAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = _getSnapshot();
        if (!snapshot.Enabled)
        {
            return TelegramPollNowResult.Skipped("Telegram Capture is disabled.");
        }

        var token = SafeGetToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            return TelegramPollNowResult.Skipped("Bot token is not configured.");
        }

        if (!await _pollNowGate.WaitAsync(0, cancellationToken))
        {
            return TelegramPollNowResult.Skipped("A poll is already in progress.");
        }

        try
        {
            Stop();
            return await RunSinglePollAsync(snapshot, token!, cancellationToken);
        }
        finally
        {
            SyncWithSettings();
            _pollNowGate.Release();
        }
    }

    private async Task<TelegramPollNowResult> RunSinglePollAsync(
        TelegramPollingSnapshot snapshot,
        string token,
        CancellationToken cancellationToken)
    {
        _diagnostics.RecordPollStarted(DateTimeOffset.UtcNow);
        try
        {
            // timeout=0: an instant check, not a long poll, so the button responds quickly.
            var outcome = await _client.GetUpdatesAsync(token, snapshot.LastUpdateId + 1, 0, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            if (!outcome.Succeeded)
            {
                var redacted = TelegramCaptureDiagnosticsRedactor.Redact(outcome.Message);
                _diagnostics.RecordPollFailed(now, outcome.FailureKind ?? TelegramPollOutcomeKind.Error, redacted);
                return TelegramPollNowResult.Failed(redacted);
            }

            _diagnostics.RecordPollSucceeded(now);
            if (outcome.Updates.Count == 0)
            {
                return TelegramPollNowResult.NoUpdates();
            }

            var applyResult = ApplySafely(outcome.Updates, now);
            var ignoredCount = outcome.Updates.Count - applyResult.CapturedCount;
            return TelegramPollNowResult.Completed(applyResult.CapturedCount, ignoredCount);
        }
        catch (OperationCanceledException)
        {
            return TelegramPollNowResult.Failed("Poll now was cancelled.");
        }
        catch (Exception ex)
        {
            var now = DateTimeOffset.UtcNow;
            var kind = ClassifyException(ex);
            var redacted = TelegramCaptureDiagnosticsRedactor.Redact(
                $"Poll now failed: {ex.GetType().Name}.");
            _diagnostics.RecordPollFailed(now, kind, redacted);
            return TelegramPollNowResult.Failed(redacted);
        }
    }

    public void Dispose() => Stop();

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TelegramPollingSnapshot snapshot;
            try
            {
                snapshot = _getSnapshot();
            }
            catch (Exception ex)
            {
                _diagnosticsLog("Telegram Capture polling could not read settings.", ex);
                _diagnostics.RecordPollFailed(
                    DateTimeOffset.UtcNow,
                    TelegramPollOutcomeKind.Error,
                    TelegramCaptureDiagnosticsRedactor.Redact("Could not read Telegram Capture settings."));
                if (!await DelayAsync(ErrorBackoff, cancellationToken))
                {
                    return;
                }

                continue;
            }

            if (!snapshot.Enabled)
            {
                // SyncWithSettings restarts the loop when Telegram Capture is re-enabled.
                return;
            }

            var token = SafeGetToken();
            if (string.IsNullOrWhiteSpace(token))
            {
                _diagnosticsLog("Telegram Capture polling paused: bot token is not configured.", null);
                if (!await DelayAsync(ErrorBackoff, cancellationToken))
                {
                    return;
                }

                continue;
            }

            var requestTimeoutSeconds = Math.Clamp(
                snapshot.PollIntervalSeconds,
                MinRequestTimeoutSeconds,
                MaxRequestTimeoutSeconds);

            _diagnostics.RecordPollStarted(DateTimeOffset.UtcNow);
            TelegramGetUpdatesOutcome outcome;
            try
            {
                outcome = await _client.GetUpdatesAsync(
                    token!,
                    snapshot.LastUpdateId + 1,
                    requestTimeoutSeconds,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _diagnosticsLog("Telegram Capture polling request failed unexpectedly.", ex);
                _diagnostics.RecordPollFailed(
                    DateTimeOffset.UtcNow,
                    ClassifyException(ex),
                    TelegramCaptureDiagnosticsRedactor.Redact($"Poll failed: {ex.GetType().Name}."));
                if (!await DelayAsync(ErrorBackoff, cancellationToken))
                {
                    return;
                }

                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!outcome.Succeeded)
            {
                _diagnosticsLog($"Telegram Capture polling error: {outcome.Message}", null);
                _diagnostics.RecordPollFailed(
                    DateTimeOffset.UtcNow,
                    outcome.FailureKind ?? TelegramPollOutcomeKind.Error,
                    TelegramCaptureDiagnosticsRedactor.Redact(outcome.Message));
                if (!await DelayAsync(ErrorBackoff, cancellationToken))
                {
                    return;
                }

                continue;
            }

            var pollSucceededAtUtc = DateTimeOffset.UtcNow;
            _diagnostics.RecordPollSucceeded(pollSucceededAtUtc);

            if (outcome.Updates.Count > 0)
            {
                try
                {
                    ApplySafely(outcome.Updates, pollSucceededAtUtc);
                }
                catch (Exception ex)
                {
                    _diagnosticsLog("Telegram Capture update processing failed unexpectedly.", ex);
                    _diagnostics.RecordPollFailed(
                        DateTimeOffset.UtcNow,
                        TelegramPollOutcomeKind.Error,
                        TelegramCaptureDiagnosticsRedactor.Redact("Update processing failed unexpectedly."));
                    if (!await DelayAsync(ErrorBackoff, cancellationToken))
                    {
                        return;
                    }
                }
            }
        }
    }

    private TelegramCaptureApplyResult ApplySafely(
        IReadOnlyList<TelegramIncomingUpdate> updates,
        DateTimeOffset now)
    {
        var result = _applyUpdates(updates);
        _diagnostics.RecordApplyResult(result, now);
        return result;
    }

    private string? SafeGetToken()
    {
        try
        {
            return _getToken();
        }
        catch (Exception ex)
        {
            _diagnosticsLog("Telegram Capture polling could not read the bot token.", ex);
            return null;
        }
    }

    private static TelegramPollOutcomeKind ClassifyException(Exception ex) => ex switch
    {
        HttpRequestException or IOException => TelegramPollOutcomeKind.NetworkError,
        _ => TelegramPollOutcomeKind.Error
    };

    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Thread-safe holder for the diagnostics fields; written from the polling thread, read from the UI thread.</summary>
    private sealed class DiagnosticsTracker
    {
        private readonly object _gate = new();
        private DateTimeOffset? _lastPollStartedUtc;
        private DateTimeOffset? _lastPollCompletedUtc;
        private DateTimeOffset? _lastSuccessfulPollUtc;
        private DateTimeOffset? _lastCapturedMessageUtc;
        private long _lastProcessedUpdateId;
        private string _lastErrorSummary = string.Empty;
        private int _consecutiveErrorCount;
        private TelegramPollOutcomeKind? _lastOutcomeKind;

        public void RecordPollStarted(DateTimeOffset now)
        {
            lock (_gate)
            {
                _lastPollStartedUtc = now;
            }
        }

        public void RecordPollSucceeded(DateTimeOffset now)
        {
            lock (_gate)
            {
                _lastPollCompletedUtc = now;
                _lastSuccessfulPollUtc = now;
                _lastOutcomeKind = null;
                _consecutiveErrorCount = 0;
                _lastErrorSummary = string.Empty;
            }
        }

        public void RecordPollFailed(DateTimeOffset now, TelegramPollOutcomeKind kind, string redactedSummary)
        {
            lock (_gate)
            {
                _lastPollCompletedUtc = now;
                _lastOutcomeKind = kind;
                _lastErrorSummary = redactedSummary;
                _consecutiveErrorCount++;
            }
        }

        public void RecordApplyResult(TelegramCaptureApplyResult result, DateTimeOffset now)
        {
            lock (_gate)
            {
                _lastProcessedUpdateId = result.LastProcessedUpdateId;
                if (result.CapturedCount > 0)
                {
                    _lastCapturedMessageUtc = now;
                }
            }
        }

        public TelegramCaptureDiagnostics Snapshot(bool enabled, bool hasToken, bool hasAllowedUserId)
        {
            lock (_gate)
            {
                var kind = TelegramCaptureStatusEvaluator.Evaluate(
                    enabled,
                    hasToken,
                    hasAllowedUserId,
                    _lastSuccessfulPollUtc.HasValue,
                    _lastOutcomeKind,
                    _consecutiveErrorCount);
                return new TelegramCaptureDiagnostics(
                    kind,
                    _lastPollStartedUtc,
                    _lastPollCompletedUtc,
                    _lastSuccessfulPollUtc,
                    _lastCapturedMessageUtc,
                    _lastProcessedUpdateId,
                    _lastErrorSummary,
                    _consecutiveErrorCount);
            }
        }
    }
}
