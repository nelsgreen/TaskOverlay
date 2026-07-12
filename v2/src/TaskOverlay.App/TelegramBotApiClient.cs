using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TaskOverlay.App;

public sealed class TelegramBotApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public TelegramBotApiClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public async Task<TelegramConnectionTestResult> TestConnectionAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return TelegramConnectionTestResult.Fail("Bot token is missing.");
        }

        try
        {
            using var response = await _httpClient.GetAsync(
                BuildGetMeUri(token),
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return TelegramConnectionTestResult.Fail(
                    $"Telegram getMe failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<TelegramGetMeResponse>(
                stream,
                JsonOptions,
                cancellationToken: cancellationToken);
            if (payload?.Ok != true || payload.Result is null)
            {
                return TelegramConnectionTestResult.Fail(
                    "Telegram getMe returned an invalid response.");
            }

            var username = payload.Result.Username ?? string.Empty;
            return TelegramConnectionTestResult.Success(
                payload.Result.Id,
                username);
        }
        catch (OperationCanceledException)
        {
            return TelegramConnectionTestResult.Fail("Telegram getMe timed out.");
        }
        catch (Exception ex) when (
            ex is HttpRequestException or
            JsonException or
            NotSupportedException)
        {
            return TelegramConnectionTestResult.Fail(
                $"Telegram getMe failed: {ex.GetType().Name}.");
        }
    }

    private static Uri BuildGetMeUri(string token) =>
        new($"https://api.telegram.org/bot{Uri.EscapeDataString(token.Trim())}/getMe");

    private sealed class TelegramGetMeResponse
    {
        public bool Ok { get; set; }
        public TelegramBotUser? Result { get; set; }
    }

    private sealed class TelegramBotUser
    {
        public long Id { get; set; }
        public string? Username { get; set; }
    }
}

public sealed record TelegramConnectionTestResult(
    bool Succeeded,
    long? BotId,
    string BotUsername,
    string Message)
{
    public static TelegramConnectionTestResult Success(long botId, string username)
    {
        var safeUsername = string.IsNullOrWhiteSpace(username)
            ? "(username unavailable)"
            : $"@{username.Trim().TrimStart('@')}";
        return new TelegramConnectionTestResult(
            true,
            botId,
            safeUsername,
            $"Connected to Telegram bot {safeUsername} ({botId}).");
    }

    public static TelegramConnectionTestResult Fail(string message) =>
        new(false, null, string.Empty, message);
}
