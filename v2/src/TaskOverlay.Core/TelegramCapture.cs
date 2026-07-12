using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskOverlay.Core;

/// <summary>
/// Provider-agnostic view of an inbound Telegram message. Populated by the
/// App-layer Bot API client; contains no HTTP/JSON concerns so it can be
/// constructed directly by tests without any network access.
/// </summary>
public sealed record TelegramIncomingMessage(
    long? FromUserId,
    bool FromIsBot,
    string ChatType,
    string? Text,
    DateTimeOffset? MessageDateUtc);

/// <summary>
/// One Telegram getUpdates entry. Message is null for update kinds this PR
/// does not process (edited messages, channel posts, non-message updates);
/// such updates are ignored but still advance the cursor.
/// </summary>
public sealed record TelegramIncomingUpdate(
    long UpdateId,
    TelegramIncomingMessage? Message);

public enum TelegramCaptureCommand
{
    PlainText,
    Capture,
    Source,
    Task,
    Meet
}

/// <summary>Deterministic parse result. No AI, no natural-language date parsing.</summary>
public sealed record TelegramParsedCapture(
    TelegramCaptureCommand Command,
    string? ProjectHint,
    string Body,
    string OriginalText);

/// <summary>
/// Parses plain text and the optional shortcut commands (/capture, /source,
/// /task, /meet) documented in BACKLOG/ROADMAP. Commands are shortcuts, not
/// the final UX; parsing intentionally stays simple and literal.
/// </summary>
public static class TelegramCaptureParser
{
    private static readonly (string Token, TelegramCaptureCommand Command)[] CommandTokens =
    {
        ("/capture", TelegramCaptureCommand.Capture),
        ("/source", TelegramCaptureCommand.Source),
        ("/task", TelegramCaptureCommand.Task),
        ("/meet", TelegramCaptureCommand.Meet)
    };

    public static TelegramParsedCapture Parse(string? rawText)
    {
        var original = rawText ?? string.Empty;
        var trimmed = original.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '/')
        {
            return new TelegramParsedCapture(
                TelegramCaptureCommand.PlainText,
                ProjectHint: null,
                Body: trimmed,
                OriginalText: original);
        }

        var spaceIndex = trimmed.IndexOfAny(new[] { ' ', '\t', '\n', '\r' });
        var commandToken = spaceIndex < 0 ? trimmed : trimmed[..spaceIndex];
        var rest = spaceIndex < 0 ? string.Empty : trimmed[(spaceIndex + 1)..].TrimStart();

        // Telegram appends "@BotUsername" to commands in some contexts; strip it before matching.
        var atIndex = commandToken.IndexOf('@');
        var bareCommandToken = atIndex < 0 ? commandToken : commandToken[..atIndex];

        foreach (var (token, command) in CommandTokens)
        {
            if (!string.Equals(bareCommandToken, token, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return command == TelegramCaptureCommand.Capture
                ? new TelegramParsedCapture(command, ProjectHint: null, Body: rest, OriginalText: original)
                : ParseWithProjectPrefix(command, rest, original);
        }

        // Unknown "/" command: treat as plain text so nothing is silently dropped.
        return new TelegramParsedCapture(
            TelegramCaptureCommand.PlainText,
            ProjectHint: null,
            Body: trimmed,
            OriginalText: original);
    }

    private static TelegramParsedCapture ParseWithProjectPrefix(
        TelegramCaptureCommand command,
        string rest,
        string originalText)
    {
        if (rest.Length == 0)
        {
            return new TelegramParsedCapture(command, ProjectHint: null, Body: string.Empty, originalText);
        }

        var colonIndex = rest.IndexOf(':');
        if (colonIndex < 0)
        {
            return new TelegramParsedCapture(command, ProjectHint: null, Body: rest.Trim(), originalText);
        }

        var projectHint = rest[..colonIndex].Trim();
        var body = rest[(colonIndex + 1)..].Trim();
        return new TelegramParsedCapture(
            command,
            string.IsNullOrWhiteSpace(projectHint) ? null : projectHint,
            body,
            originalText);
    }
}

/// <summary>Result of resolving a parsed project hint against configured aliases.</summary>
public sealed record TelegramProjectResolution(
    Guid ProjectId,
    string? ProjectHint,
    bool HintUnresolved);

/// <summary>
/// Resolves a Telegram project hint to an existing project id using
/// case-insensitive, trimmed alias matching. Never auto-creates a project;
/// falls back to the configured default project, then to the app-wide
/// Default project so a SourceDocument can always be saved.
/// </summary>
public static class TelegramProjectResolver
{
    public static TelegramProjectResolution Resolve(
        string? projectHint,
        TelegramCaptureSettings settings,
        IReadOnlyCollection<ProjectItem> projects)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(projects);

        var normalizedHint = string.IsNullOrWhiteSpace(projectHint) ? null : projectHint.Trim();
        var projectIds = projects.Select(project => project.Id).ToHashSet();

        if (normalizedHint is not null)
        {
            var alias = settings.ProjectAliases?.FirstOrDefault(candidate =>
                string.Equals(candidate.Alias, normalizedHint, StringComparison.OrdinalIgnoreCase));
            if (alias is not null && projectIds.Contains(alias.ProjectId))
            {
                return new TelegramProjectResolution(alias.ProjectId, normalizedHint, HintUnresolved: false);
            }
        }

        var fallbackProjectId = ResolveFallbackProject(settings, projects, projectIds);
        return new TelegramProjectResolution(
            fallbackProjectId,
            normalizedHint,
            HintUnresolved: normalizedHint is not null);
    }

    private static Guid ResolveFallbackProject(
        TelegramCaptureSettings settings,
        IReadOnlyCollection<ProjectItem> projects,
        HashSet<Guid> projectIds)
    {
        if (settings.DefaultProjectId is Guid defaultId && projectIds.Contains(defaultId))
        {
            return defaultId;
        }

        var appDefault = projects.FirstOrDefault(project =>
            string.Equals(project.Name, ProjectItem.DefaultName, StringComparison.OrdinalIgnoreCase));
        if (appDefault is not null)
        {
            return appDefault.Id;
        }

        // State always contains at least one project (AppStateStore requires the
        // Default project); this is only reached for malformed in-memory state
        // built directly by a caller, e.g. in a test.
        return projects.First().Id;
    }
}

/// <summary>Builds the SourceDocument fields for an allowed Telegram capture. Never destroys the original text.</summary>
public static class TelegramCaptureBuilder
{
    public static SourceDocumentUpdate Build(
        TelegramParsedCapture parsed,
        TelegramProjectResolution resolution,
        DateTimeOffset sourceDateUtc)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(resolution);

        return new SourceDocumentUpdate(
            resolution.ProjectId,
            ContextSourceType.TelegramCapture,
            ContextSourceApp.Telegram,
            TitleFor(parsed.Command),
            ComposeBody(parsed, resolution),
            Summary: string.Empty,
            sourceDateUtc,
            LinkedTaskIds: Array.Empty<Guid>(),
            LinkedMeetingIds: Array.Empty<Guid>());
    }

    private static string TitleFor(TelegramCaptureCommand command) => command switch
    {
        TelegramCaptureCommand.Capture => "Telegram capture",
        TelegramCaptureCommand.Source => "Telegram source",
        TelegramCaptureCommand.Task => "Telegram task draft",
        TelegramCaptureCommand.Meet => "Telegram MEET draft",
        _ => "Telegram capture"
    };

    private static string ComposeBody(TelegramParsedCapture parsed, TelegramProjectResolution resolution)
    {
        var lines = new List<string>();
        if (parsed.Command != TelegramCaptureCommand.PlainText)
        {
            lines.Add($"[Telegram command: {CommandToken(parsed.Command)}]");
        }

        if (resolution.HintUnresolved)
        {
            lines.Add($"[Unresolved project hint: {resolution.ProjectHint}]");
        }

        // Original message text is always preserved verbatim, in full.
        lines.Add(parsed.OriginalText);
        return string.Join(Environment.NewLine, lines);
    }

    private static string CommandToken(TelegramCaptureCommand command) => command switch
    {
        TelegramCaptureCommand.Capture => "/capture",
        TelegramCaptureCommand.Source => "/source",
        TelegramCaptureCommand.Task => "/task",
        TelegramCaptureCommand.Meet => "/meet",
        _ => string.Empty
    };
}

public enum TelegramCaptureOutcome
{
    Captured,
    IgnoredUnknownUser,
    IgnoredNonPrivateChat,
    IgnoredBotMessage,
    IgnoredNonText,
    SaveFailed
}

public sealed record TelegramCaptureResult(
    long UpdateId,
    TelegramCaptureOutcome Outcome,
    SourceDocument? Source);

/// <summary>
/// Pure orchestration: evaluates the allowlist/filter gate for each update in
/// order, parses and stores allowed text messages as ContextHUB
/// SourceDocuments, and reports the cursor value the caller should persist.
/// Contains no I/O; the App layer supplies AppState/settings and persists the
/// resulting cursor.
/// </summary>
public static class TelegramCaptureProcessor
{
    /// <summary>
    /// Processes updates in ascending update_id order. Stops at (and does not
    /// advance the cursor past) the first update whose capture fails to save,
    /// so a transient failure is retried on the next poll instead of losing
    /// the message. Updates already covered by settings.LastUpdateId are
    /// skipped defensively (they should already be excluded via the getUpdates
    /// offset, but re-processing must still be a safe no-op).
    /// </summary>
    public static IReadOnlyList<TelegramCaptureResult> ProcessBatch(
        AppState state,
        TelegramCaptureSettings settings,
        IReadOnlyList<TelegramIncomingUpdate> updates,
        out long newLastUpdateId,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(updates);

        var contextService = new ContextService(state);
        var results = new List<TelegramCaptureResult>();
        var cursor = settings.LastUpdateId;

        foreach (var update in updates.OrderBy(update => update.UpdateId))
        {
            if (update.UpdateId <= cursor)
            {
                continue;
            }

            var outcome = Evaluate(update, settings);
            if (outcome != TelegramCaptureOutcome.Captured)
            {
                results.Add(new TelegramCaptureResult(update.UpdateId, outcome, Source: null));
                cursor = update.UpdateId;
                continue;
            }

            var parsed = TelegramCaptureParser.Parse(update.Message!.Text);
            var resolution = TelegramProjectResolver.Resolve(parsed.ProjectHint, settings, state.Projects);
            var sourceDateUtc = update.Message.MessageDateUtc ?? now ?? DateTimeOffset.UtcNow;
            var sourceUpdate = TelegramCaptureBuilder.Build(parsed, resolution, sourceDateUtc);
            var source = contextService.CreateSource(sourceUpdate, now);
            if (source is null)
            {
                results.Add(new TelegramCaptureResult(update.UpdateId, TelegramCaptureOutcome.SaveFailed, Source: null));
                break;
            }

            results.Add(new TelegramCaptureResult(update.UpdateId, TelegramCaptureOutcome.Captured, source));
            cursor = update.UpdateId;
        }

        newLastUpdateId = cursor;
        return results;
    }

    private static TelegramCaptureOutcome Evaluate(TelegramIncomingUpdate update, TelegramCaptureSettings settings)
    {
        var message = update.Message;
        if (message is null)
        {
            return TelegramCaptureOutcome.IgnoredNonText;
        }

        if (message.FromIsBot)
        {
            return TelegramCaptureOutcome.IgnoredBotMessage;
        }

        if (!string.Equals(message.ChatType, "private", StringComparison.OrdinalIgnoreCase))
        {
            return TelegramCaptureOutcome.IgnoredNonPrivateChat;
        }

        if (settings.AllowedUserId is not long allowedUserId ||
            message.FromUserId != allowedUserId)
        {
            return TelegramCaptureOutcome.IgnoredUnknownUser;
        }

        if (string.IsNullOrWhiteSpace(message.Text))
        {
            return TelegramCaptureOutcome.IgnoredNonText;
        }

        return TelegramCaptureOutcome.Captured;
    }
}
