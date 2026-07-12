using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TaskOverlay.Core;

namespace TaskOverlay.App;

/// <summary>Minimal, race-free view of the settings the polling loop needs each cycle.</summary>
public readonly record struct TelegramPollingSnapshot(
    bool Enabled,
    long LastUpdateId,
    int PollIntervalSeconds);

/// <summary>
/// Local-only Telegram getUpdates long-poll loop. No webhook, no hosting, no
/// cloud sync: this runs entirely inside the WPF process. Settings/state
/// access and the resulting AppState mutation are supplied as callbacks so
/// the caller (App.xaml.cs) can route them through the WPF Dispatcher and
/// keep a single serialized state-mutation path shared with UI commands.
/// </summary>
public sealed class TelegramPollingService : IDisposable
{
    private const int MinRequestTimeoutSeconds = 5;
    private const int MaxRequestTimeoutSeconds = 50;
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(5);

    private readonly Func<TelegramPollingSnapshot> _getSnapshot;
    private readonly Func<string?> _getToken;
    private readonly ITelegramUpdatesClient _client;
    private readonly Action<IReadOnlyList<TelegramIncomingUpdate>> _applyUpdates;
    private readonly Action<string, Exception?> _diagnostics;
    private readonly object _lifecycleGate = new();

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public TelegramPollingService(
        Func<TelegramPollingSnapshot> getSnapshot,
        Func<string?> getToken,
        ITelegramUpdatesClient client,
        Action<IReadOnlyList<TelegramIncomingUpdate>> applyUpdates,
        Action<string, Exception?> diagnostics)
    {
        _getSnapshot = getSnapshot ?? throw new ArgumentNullException(nameof(getSnapshot));
        _getToken = getToken ?? throw new ArgumentNullException(nameof(getToken));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _applyUpdates = applyUpdates ?? throw new ArgumentNullException(nameof(applyUpdates));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
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

        _diagnostics("Telegram Capture polling started.", null);
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

        _diagnostics("Telegram Capture polling stopped.", null);
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
                _diagnostics("Telegram Capture polling could not read settings.", ex);
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
                _diagnostics("Telegram Capture polling paused: bot token is not configured.", null);
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
                _diagnostics("Telegram Capture polling request failed unexpectedly.", ex);
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
                _diagnostics($"Telegram Capture polling error: {outcome.Message}", null);
                if (!await DelayAsync(ErrorBackoff, cancellationToken))
                {
                    return;
                }

                continue;
            }

            if (outcome.Updates.Count > 0)
            {
                try
                {
                    _applyUpdates(outcome.Updates);
                }
                catch (Exception ex)
                {
                    _diagnostics("Telegram Capture update processing failed unexpectedly.", ex);
                    if (!await DelayAsync(ErrorBackoff, cancellationToken))
                    {
                        return;
                    }
                }
            }
        }
    }

    private string? SafeGetToken()
    {
        try
        {
            return _getToken();
        }
        catch (Exception ex)
        {
            _diagnostics("Telegram Capture polling could not read the bot token.", ex);
            return null;
        }
    }

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
}
