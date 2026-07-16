using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TaskOverlay.Core;
using CoreTaskStatus = TaskOverlay.Core.TaskStatus;

namespace TaskOverlay.App;

public sealed class OpenAiTranscriptionProvider : ITranscriptionProvider
{
    private static readonly Uri Endpoint = new("https://api.openai.com/v1/audio/transcriptions");
    private readonly Func<string?> _loadApiKey;
    private readonly HttpClient _httpClient;
    private readonly Action<string, Exception?>? _diagnostic;

    public OpenAiTranscriptionProvider(
        Func<string?> loadApiKey,
        HttpClient? httpClient = null,
        Action<string, Exception?>? diagnostic = null)
    {
        _loadApiKey = loadApiKey ?? throw new ArgumentNullException(nameof(loadApiKey));
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15)
        };
        _diagnostic = diagnostic;
    }

    public string Name => "OpenAI";

    public async Task<TranscriptionProviderResponse> TranscribeAsync(
        TranscriptionProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(request.AudioPath))
        {
            throw new FileNotFoundException("Transcription audio file is missing.", request.AudioPath);
        }

        var apiKey = _loadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new OpenAiProviderException("OpenAI API key is not configured.");
        }

        var fileName = Path.GetFileName(request.AudioPath);
        if (fileName.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains(".current.", StringComparison.OrdinalIgnoreCase))
        {
            throw new OpenAiProviderException(
                "An in-progress recording artifact cannot be uploaded for transcription.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var form = new MultipartFormDataContent();
        await using var fileStream = TranscriptionAudioDiagnostics.OpenRead(request.AudioPath);
        var uploadBytes = fileStream.Length;
        var uploadSha256 = await TranscriptionAudioDiagnostics.ComputeSha256Async(
            fileStream,
            cancellationToken);
        fileStream.Position = 0;
        using var fileContent = new StreamContent(fileStream);
        var contentType = Path.GetExtension(request.AudioPath).Equals(
                ".m4a",
                StringComparison.OrdinalIgnoreCase)
            ? "audio/mp4"
            : "audio/wav";
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        form.Add(new StringContent(request.Model), "model");
        var wantsDiarization = request.Model.Contains("diarize", StringComparison.OrdinalIgnoreCase);
        var wantsWhisperSegments = string.Equals(
            request.Model,
            "whisper-1",
            StringComparison.OrdinalIgnoreCase);
        form.Add(
            new StringContent(
                wantsDiarization
                    ? "diarized_json"
                    : wantsWhisperSegments
                        ? "verbose_json"
                        : "json"),
            "response_format");
        if (wantsDiarization)
        {
            form.Add(new StringContent("auto"), "chunking_strategy");
        }

        if (wantsWhisperSegments)
        {
            form.Add(new StringContent("segment"), "timestamp_granularities[]");
        }

        var language = request.Language switch
        {
            MeetingTranscriptLanguage.Russian => "ru",
            MeetingTranscriptLanguage.English => "en",
            _ => null
        };
        if (language is not null)
        {
            form.Add(new StringContent(language), "language");
        }

        message.Content = form;
        Report(
            $"OpenAI transcription multipart upload prepared: " +
            $"recordingId={FormatOptionalId(request.RecordingId)}; " +
            $"meetId={FormatOptionalId(request.MeetingId)}; " +
            $"path={Path.GetFullPath(request.AudioPath)}; fileName={fileName}; " +
            $"contentType={contentType}; bytes={uploadBytes}; sha256={uploadSha256}; " +
            $"model={request.Model}; language={request.Language}.");
        using var response = await SendAsync(message, fileName, uploadBytes, uploadSha256, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        Report(
            $"OpenAI transcription response received: " +
            $"recordingId={FormatOptionalId(request.RecordingId)}; " +
            $"meetId={FormatOptionalId(request.MeetingId)}; fileName={fileName}; " +
            $"bytes={uploadBytes}; sha256={uploadSha256}; " +
            $"success={response.IsSuccessStatusCode}; status={(int)response.StatusCode}.");
        if (!response.IsSuccessStatusCode)
        {
            throw CreateProviderException("Transcription", response, raw);
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var text = ReadString(root, "text").Trim();
            var segments = new List<TranscriptSegment>();
            if (root.TryGetProperty("segments", out var segmentArray) &&
                segmentArray.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var segment in segmentArray.EnumerateArray())
                {
                    var segmentText = ReadString(segment, "text").Trim();
                    if (segmentText.Length == 0)
                    {
                        continue;
                    }

                    var start = ReadDouble(segment, "start") + request.ChunkOffset.TotalSeconds;
                    var end = Math.Max(start, ReadDouble(segment, "end") + request.ChunkOffset.TotalSeconds);
                    segments.Add(new TranscriptSegment
                    {
                        Index = index++,
                        StartSeconds = start,
                        EndSeconds = end,
                        Text = segmentText,
                        Speaker = NullIfEmpty(ReadString(segment, "speaker"))
                    });
                }
            }

            if (segments.Count == 0 && text.Length > 0)
            {
                segments.Add(new TranscriptSegment
                {
                    Index = 0,
                    StartSeconds = request.ChunkOffset.TotalSeconds,
                    EndSeconds = request.ChunkOffset.TotalSeconds,
                    Text = text
                });
            }

            return new TranscriptionProviderResponse(
                raw,
                text,
                segments,
                NullIfEmpty(ReadString(root, "language")));
        }
        catch (JsonException ex)
        {
            throw new OpenAiProviderException(
                "OpenAI returned an invalid transcription response.",
                ex);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage message,
        string fileName,
        long bytes,
        string sha256,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Report(
                $"OpenAI transcription upload failed: fileName={fileName}; " +
                $"bytes={bytes}; sha256={sha256}.",
                ex);
            throw;
        }
    }

    private void Report(string message, Exception? exception = null)
    {
        try
        {
            _diagnostic?.Invoke(message, exception);
        }
        catch
        {
            // Diagnostics must never alter provider behavior.
        }
    }

    private static string FormatOptionalId(Guid? value) =>
        value.HasValue ? value.Value.ToString("N") : "none";

    private static double ReadDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetDouble(out var parsed)
            ? parsed
            : 0;

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static OpenAiProviderException CreateProviderException(
        string operation,
        HttpResponseMessage response,
        string raw)
    {
        var message = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var value))
            {
                message = value.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // The response body may be HTML or truncated; never echo it verbatim.
        }

        message = ProviderErrorRedactor.Redact(message);
        var suffix = message.Length > 0 ? $" {message}" : string.Empty;
        return new OpenAiProviderException(
            $"OpenAI {operation} failed ({(int)response.StatusCode} {response.ReasonPhrase}).{suffix}");
    }
}

public sealed class OpenAiMeetingAnalysisProvider : IMeetingAnalysisProvider
{
    private static readonly Uri Endpoint = new("https://api.openai.com/v1/responses");
    private static readonly JsonElement AnalysisSchema = BuildSchema();
    private readonly Func<string?> _loadApiKey;
    private readonly HttpClient _httpClient;

    public OpenAiMeetingAnalysisProvider(
        Func<string?> loadApiKey,
        HttpClient? httpClient = null)
    {
        _loadApiKey = loadApiKey ?? throw new ArgumentNullException(nameof(loadApiKey));
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
    }

    public string Name => "OpenAI";

    public async Task<MeetingAnalysisProviderResponse> AnalyzeAsync(
        MeetingAnalysisProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _loadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new OpenAiProviderException("OpenAI API key is not configured.");
        }

        if (string.IsNullOrWhiteSpace(request.Transcript.Text))
        {
            throw new InvalidDataException("Transcript is empty.");
        }

        var transcript = request.Transcript.Text.Length <= 500_000
            ? request.Transcript.Text
            : request.Transcript.Text[..500_000];
        var body = new
        {
            model = request.Model,
            input = new object[]
            {
                new
                {
                    role = "system",
                    content = "Analyze a meeting transcript. Return grounded facts only. " +
                              "Proposed actions are review suggestions and must never claim they were applied. " +
                              "Use source excerpts and timestamps when available. Do not invent speaker names."
                },
                new
                {
                    role = "user",
                    content = $"Meeting title: {request.MeetingTitle}\n\nTranscript:\n{transcript}"
                }
            },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "taskoverlay_meeting_analysis",
                    strict = true,
                    schema = AnalysisSchema
                }
            }
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        message.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");
        using var response = await _httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw OpenAiTranscriptionProvider.CreateProviderException("analysis", response, raw);
        }

        try
        {
            using var responseDocument = JsonDocument.Parse(raw);
            var outputText = ExtractOutputText(responseDocument.RootElement);
            if (string.IsNullOrWhiteSpace(outputText))
            {
                throw new JsonException("Structured output text is missing.");
            }

            using var analysisDocument = JsonDocument.Parse(outputText);
            var analysis = ParseAnalysis(
                analysisDocument.RootElement,
                request,
                Name,
                request.Model);
            return new MeetingAnalysisProviderResponse(raw, analysis);
        }
        catch (JsonException ex)
        {
            throw new OpenAiProviderException(
                "OpenAI returned malformed meeting analysis; no actions were applied.",
                ex);
        }
    }

    internal static MeetingAnalysis ParseAnalysis(
        JsonElement root,
        MeetingAnalysisProviderRequest request,
        string provider,
        string model)
    {
        var now = DateTimeOffset.UtcNow;
        var analysis = new MeetingAnalysis
        {
            RecordingId = request.RecordingId,
            TranscriptId = request.TranscriptId,
            TranscriptRevisionId = request.TranscriptRevisionId,
            MeetId = request.MeetId,
            State = MeetingAnalysisState.ReadyForReview,
            Provider = provider,
            Model = model,
            Summary = RequiredString(root, "summary", 100_000),
            Decisions = StringArray(root, "decisions"),
            MyActionItems = StringArray(root, "myActionItems"),
            OtherPeopleActionItems = StringArray(root, "otherPeopleActionItems"),
            WaitingFor = StringArray(root, "waitingFor"),
            Risks = StringArray(root, "risks"),
            QuestionsToClarify = StringArray(root, "questionsToClarify"),
            Deadlines = StringArray(root, "deadlines"),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        foreach (var item in RequiredArray(root, "keyQuotesOrSourceReferences"))
        {
            analysis.KeyQuotesOrSourceReferences.Add(new MeetingSourceReference
            {
                StartSeconds = NullableDouble(item, "startSeconds"),
                EndSeconds = NullableDouble(item, "endSeconds"),
                Excerpt = RequiredString(item, "excerpt", 5_000)
            });
        }

        foreach (var item in RequiredArray(root, "proposedActions"))
        {
            var type = RequiredString(item, "type", 100) switch
            {
                "CreateWaitingTask" => ProposedActionType.CreateWaitingTask,
                "CreateFollowUpTask" => ProposedActionType.CreateFollowUpTask,
                "AddMeetingContextNote" => ProposedActionType.AddMeetingContextNote,
                _ => ProposedActionType.CreateTask
            };
            var status = RequiredString(item, "proposedStatus", 20) switch
            {
                "FOCUS" => CoreTaskStatus.InWork,
                "WAIT" => CoreTaskStatus.Waiting,
                "DONE" => CoreTaskStatus.Done,
                _ => CoreTaskStatus.Todo
            };
            analysis.ProposedActions.Add(new ProposedAction
            {
                Type = type,
                Title = RequiredString(item, "title", 500),
                ProposedProjectId = NullableGuid(item, "proposedProjectId"),
                ProjectSuggestion = NullableString(item, "projectSuggestion", 500),
                ProposedStatus = status,
                WaitingFor = NullableString(item, "waitingFor", 300),
                DeadlineAtUtc = NullableTimestamp(item, "deadlineAtUtc"),
                ReminderAtUtc = NullableTimestamp(item, "reminderAtUtc"),
                SourceSegmentStart = NullableDouble(item, "sourceSegmentStart"),
                SourceSegmentEnd = NullableDouble(item, "sourceSegmentEnd"),
                SourceExcerpt = RequiredString(item, "sourceExcerpt", 5_000),
                Confidence = Math.Clamp(RequiredDouble(item, "confidence"), 0, 1),
                Rationale = RequiredString(item, "rationale", 5_000)
            });
        }

        return analysis;
    }

    private static string ExtractOutputText(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var type) &&
                    type.GetString() == "output_text" &&
                    part.TryGetProperty("text", out var text))
                {
                    return text.GetString() ?? string.Empty;
                }
            }
        }

        return string.Empty;
    }

    private static JsonElement BuildSchema()
    {
        const string schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["summary", "decisions", "myActionItems", "otherPeopleActionItems", "waitingFor", "risks", "questionsToClarify", "deadlines", "keyQuotesOrSourceReferences", "proposedActions"],
          "properties": {
            "summary": { "type": "string" },
            "decisions": { "type": "array", "items": { "type": "string" } },
            "myActionItems": { "type": "array", "items": { "type": "string" } },
            "otherPeopleActionItems": { "type": "array", "items": { "type": "string" } },
            "waitingFor": { "type": "array", "items": { "type": "string" } },
            "risks": { "type": "array", "items": { "type": "string" } },
            "questionsToClarify": { "type": "array", "items": { "type": "string" } },
            "deadlines": { "type": "array", "items": { "type": "string" } },
            "keyQuotesOrSourceReferences": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["startSeconds", "endSeconds", "excerpt"],
                "properties": {
                  "startSeconds": { "type": ["number", "null"] },
                  "endSeconds": { "type": ["number", "null"] },
                  "excerpt": { "type": "string" }
                }
              }
            },
            "proposedActions": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["type", "title", "proposedProjectId", "projectSuggestion", "proposedStatus", "waitingFor", "deadlineAtUtc", "reminderAtUtc", "sourceSegmentStart", "sourceSegmentEnd", "sourceExcerpt", "confidence", "rationale"],
                "properties": {
                  "type": { "type": "string", "enum": ["CreateTask", "CreateWaitingTask", "CreateFollowUpTask", "AddMeetingContextNote"] },
                  "title": { "type": "string" },
                  "proposedProjectId": { "type": ["string", "null"] },
                  "projectSuggestion": { "type": ["string", "null"] },
                  "proposedStatus": { "type": "string", "enum": ["TODO", "FOCUS", "WAIT", "DONE"] },
                  "waitingFor": { "type": ["string", "null"] },
                  "deadlineAtUtc": { "type": ["string", "null"] },
                  "reminderAtUtc": { "type": ["string", "null"] },
                  "sourceSegmentStart": { "type": ["number", "null"] },
                  "sourceSegmentEnd": { "type": ["number", "null"] },
                  "sourceExcerpt": { "type": "string" },
                  "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
                  "rationale": { "type": "string" }
                }
              }
            }
          }
        }
        """;
        using var document = JsonDocument.Parse(schema);
        return document.RootElement.Clone();
    }

    private static JsonElement.ArrayEnumerator RequiredArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException($"{name} must be an array.");
        }

        return value.EnumerateArray();
    }

    private static List<string> StringArray(JsonElement root, string name) =>
        RequiredArray(root, name)
            .Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString()?.Trim() ?? string.Empty
                : throw new JsonException($"{name} contains a non-string value."))
            .Where(value => value.Length > 0)
            .ToList();

    private static string RequiredString(JsonElement root, string name, int maximumLength)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"{name} must be a string.");
        }

        var result = value.GetString()?.Trim() ?? string.Empty;
        if (result.Length > maximumLength)
        {
            throw new JsonException($"{name} is too long.");
        }

        return result;
    }

    private static string NullableString(JsonElement root, string name, int maximumLength)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return string.Empty;
        }

        return RequiredString(root, name, maximumLength);
    }

    private static double RequiredDouble(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || !value.TryGetDouble(out var result))
        {
            throw new JsonException($"{name} must be a number.");
        }

        return result;
    }

    private static double? NullableDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.TryGetDouble(out var result)
                ? result
                : throw new JsonException($"{name} must be a number or null.")
            : null;

    private static Guid? NullableGuid(JsonElement root, string name)
    {
        var value = NullableString(root, name, 100);
        return value.Length == 0
            ? null
            : Guid.TryParse(value, out var id)
                ? id
                : null;
    }

    private static DateTimeOffset? NullableTimestamp(JsonElement root, string name)
    {
        var value = NullableString(root, name, 100);
        return value.Length == 0
            ? null
            : DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var timestamp)
                ? timestamp.ToUniversalTime()
                : throw new JsonException($"{name} must be an ISO-8601 timestamp or null.");
    }
}

public sealed class OpenAiProviderException : Exception
{
    public OpenAiProviderException(string message)
        : base(message)
    {
    }

    public OpenAiProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal static partial class ProviderErrorRedactor
{
    [GeneratedRegex(@"sk-[A-Za-z0-9_-]{8,}", RegexOptions.CultureInvariant)]
    private static partial Regex ApiKeyPattern();

    public static string Redact(string? message)
    {
        var normalized = message?.Trim() ?? string.Empty;
        normalized = ApiKeyPattern().Replace(normalized, "[redacted]");
        return normalized.Length <= 1_000 ? normalized : normalized[..1_000];
    }
}
