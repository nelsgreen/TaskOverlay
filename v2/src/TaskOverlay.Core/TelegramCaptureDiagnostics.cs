using System;
using System.Text.RegularExpressions;

namespace TaskOverlay.Core;

/// <summary>What the Settings UI should tell the user about Telegram Capture right now.</summary>
public enum TelegramCaptureStatusKind
{
    NotConfigured,
    Disabled,
    Running,
    WaitingForMessages,
    TokenError,
    NetworkError,
    Error
}

/// <summary>Classifies why a single getUpdates attempt failed.</summary>
public enum TelegramPollOutcomeKind
{
    NetworkError,
    TokenError,
    Error
}

/// <summary>
/// Volatile, non-secret snapshot of Telegram Capture polling health for
/// display in Settings. Never contains the bot token or a token-bearing URL.
/// Runtime fields (everything except what mirrors persisted settings) live
/// in memory only and reset on restart; that is expected, not a bug.
/// </summary>
public sealed record TelegramCaptureDiagnostics(
    TelegramCaptureStatusKind Kind,
    DateTimeOffset? LastPollStartedUtc,
    DateTimeOffset? LastPollCompletedUtc,
    DateTimeOffset? LastSuccessfulPollUtc,
    DateTimeOffset? LastCapturedMessageUtc,
    long LastProcessedUpdateId,
    string LastErrorSummary,
    int ConsecutiveErrorCount)
{
    public static TelegramCaptureDiagnostics Empty { get; } = new(
        TelegramCaptureStatusKind.NotConfigured,
        LastPollStartedUtc: null,
        LastPollCompletedUtc: null,
        LastSuccessfulPollUtc: null,
        LastCapturedMessageUtc: null,
        LastProcessedUpdateId: 0,
        LastErrorSummary: string.Empty,
        ConsecutiveErrorCount: 0);
}

/// <summary>
/// Pure status classification: what the Settings UI should show right now.
/// Deliberately small - seven statuses cover configuration, on/off, healthy
/// operation, and the three failure shapes that matter for a human deciding
/// what to do next (bad token, network/API availability, everything else).
/// </summary>
public static class TelegramCaptureStatusEvaluator
{
    public static TelegramCaptureStatusKind Evaluate(
        bool enabled,
        bool hasToken,
        bool hasAllowedUserId,
        bool hasCompletedSuccessfulPoll,
        TelegramPollOutcomeKind? lastOutcomeKind,
        int consecutiveErrorCount)
    {
        if (!hasToken || !hasAllowedUserId)
        {
            return TelegramCaptureStatusKind.NotConfigured;
        }

        if (!enabled)
        {
            return TelegramCaptureStatusKind.Disabled;
        }

        if (consecutiveErrorCount > 0)
        {
            return lastOutcomeKind switch
            {
                TelegramPollOutcomeKind.TokenError => TelegramCaptureStatusKind.TokenError,
                TelegramPollOutcomeKind.NetworkError => TelegramCaptureStatusKind.NetworkError,
                _ => TelegramCaptureStatusKind.Error
            };
        }

        return hasCompletedSuccessfulPoll
            ? TelegramCaptureStatusKind.WaitingForMessages
            : TelegramCaptureStatusKind.Running;
    }
}

/// <summary>
/// Defense-in-depth redaction for any text destined for Settings or logs. The
/// Bot API clients already avoid putting the token into messages (only HTTP
/// status codes and exception type names are used); this additionally strips
/// anything shaped like a Telegram bot token or a token-bearing API URL, and
/// caps length, so a future change to an error message can never leak a
/// token through this path.
/// </summary>
public static class TelegramCaptureDiagnosticsRedactor
{
    private const int MaximumLength = 300;

    private static readonly Regex TokenPattern = new(
        @"\d{6,}:[A-Za-z0-9_-]{20,}",
        RegexOptions.Compiled);

    private static readonly Regex TokenUrlPattern = new(
        @"https?://api\.telegram\.org/bot[^/\s]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Redact(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var redacted = TokenUrlPattern.Replace(message, "https://api.telegram.org/bot[REDACTED]");
        redacted = TokenPattern.Replace(redacted, "[REDACTED]");
        return redacted.Length > MaximumLength ? redacted[..MaximumLength] : redacted;
    }
}
