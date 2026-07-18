using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public sealed record TelegramGetUpdatesOutcome(
    bool Succeeded,
    IReadOnlyList<TelegramIncomingUpdate> Updates,
    string Message,
    TelegramPollOutcomeKind? FailureKind)
{
    public static TelegramGetUpdatesOutcome Success(IReadOnlyList<TelegramIncomingUpdate> updates) =>
        new(true, updates, string.Empty, null);

    public static TelegramGetUpdatesOutcome Fail(
        string message,
        TelegramPollOutcomeKind kind = TelegramPollOutcomeKind.Error) =>
        new(false, Array.Empty<TelegramIncomingUpdate>(), message, kind);
}

/// <summary>
/// Abstraction over Telegram Bot API getUpdates so TelegramPollingService can
/// be exercised with a fake in tests without any real network access.
/// </summary>
public interface ITelegramUpdatesClient
{
    Task<TelegramGetUpdatesOutcome> GetUpdatesAsync(
        string token,
        long offset,
        int timeoutSeconds,
        CancellationToken cancellationToken);
}

/// <summary>
/// Real Telegram Bot API getUpdates long-poll client. Never logs the bot
/// token or a token-bearing URL; failure messages are limited to HTTP status
/// codes and exception type names.
/// </summary>
public sealed class TelegramUpdatesClient : ITelegramUpdatesClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public TelegramUpdatesClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            // Must exceed the largest long-poll "timeout" query value we send.
            Timeout = TimeSpan.FromSeconds(70)
        };
    }

    public async Task<TelegramGetUpdatesOutcome> GetUpdatesAsync(
        string token,
        long offset,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return TelegramGetUpdatesOutcome.Fail("Bot token is missing.");
        }

        try
        {
            using var response = await _httpClient.GetAsync(
                BuildGetUpdatesUri(token, offset, timeoutSeconds),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                var isTokenFailure = statusCode is 401 or 403 or 404;
                return TelegramGetUpdatesOutcome.Fail(
                    $"Telegram getUpdates failed: {statusCode} {response.ReasonPhrase}.",
                    isTokenFailure ? TelegramPollOutcomeKind.TokenError : TelegramPollOutcomeKind.Error);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<TelegramGetUpdatesResponse>(
                stream,
                JsonOptions,
                cancellationToken);
            if (payload?.Ok != true || payload.Result is null)
            {
                return TelegramGetUpdatesOutcome.Fail(
                    "Telegram getUpdates returned an invalid response.");
            }

            var updates = payload.Result.Select(MapUpdate).ToList();
            return TelegramGetUpdatesOutcome.Success(updates);
        }
        catch (OperationCanceledException)
        {
            // Let the polling loop distinguish a clean shutdown/timeout from a real failure.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return TelegramGetUpdatesOutcome.Fail(
                $"Telegram getUpdates failed: {ex.GetType().Name}.",
                TelegramPollOutcomeKind.NetworkError);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return TelegramGetUpdatesOutcome.Fail(
                $"Telegram getUpdates failed: {ex.GetType().Name}.",
                TelegramPollOutcomeKind.Error);
        }
    }

    private static TelegramIncomingUpdate MapUpdate(TelegramUpdateDto dto)
    {
        var message = dto.Message;
        TelegramIncomingMessage? mapped = null;
        if (message is not null)
        {
            mapped = new TelegramIncomingMessage(
                message.From?.Id,
                message.From?.IsBot ?? false,
                message.Chat?.Type ?? string.Empty,
                message.Text,
                message.Date is long unixSeconds
                    ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
                    : null);
        }

        return new TelegramIncomingUpdate(dto.UpdateId, mapped);
    }

    private static Uri BuildGetUpdatesUri(string token, long offset, int timeoutSeconds)
    {
        var safeOffset = Math.Max(0, offset);
        var safeTimeout = Math.Clamp(timeoutSeconds, 0, 60);
        return new Uri(
            $"https://api.telegram.org/bot{Uri.EscapeDataString(token.Trim())}/getUpdates" +
            $"?offset={safeOffset}&timeout={safeTimeout}&allowed_updates=%5B%22message%22%5D");
    }

    private sealed class TelegramGetUpdatesResponse
    {
        public bool Ok { get; set; }
        public List<TelegramUpdateDto>? Result { get; set; }
    }

    private sealed class TelegramUpdateDto
    {
        [JsonPropertyName("update_id")]
        public long UpdateId { get; set; }
        public TelegramMessageDto? Message { get; set; }
    }

    private sealed class TelegramMessageDto
    {
        public TelegramUserDto? From { get; set; }
        public TelegramChatDto? Chat { get; set; }
        public string? Text { get; set; }
        public long? Date { get; set; }
    }

    private sealed class TelegramUserDto
    {
        public long Id { get; set; }

        [JsonPropertyName("is_bot")]
        public bool IsBot { get; set; }
    }

    private sealed class TelegramChatDto
    {
        public string? Type { get; set; }
    }
}
