using System;

namespace TaskOverlay.App;

public sealed record MeetingAudioHttpResponsePlan(
    int StatusCode,
    string ReasonPhrase,
    string ContentType,
    long HeaderContentLength,
    long BodyStart,
    long BodyLength,
    string? ContentRange,
    bool IncludeBody,
    bool AcceptRanges,
    bool IsAllowedMethod);

public static class MeetingAudioHttpResponsePolicy
{
    public static MeetingAudioHttpResponsePlan Create(
        string? method,
        string? rangeHeader,
        long resourceLength,
        string contentType)
    {
        var normalizedMethod = method?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalizedMethod is not ("GET" or "HEAD"))
        {
            return new MeetingAudioHttpResponsePlan(
                405,
                "Method Not Allowed",
                "text/plain",
                0,
                0,
                0,
                null,
                IncludeBody: false,
                AcceptRanges: false,
                IsAllowedMethod: false);
        }

        if (normalizedMethod == "HEAD")
        {
            return new MeetingAudioHttpResponsePlan(
                200,
                "OK",
                contentType,
                resourceLength,
                0,
                0,
                null,
                IncludeBody: false,
                AcceptRanges: true,
                IsAllowedMethod: true);
        }

        if (string.IsNullOrWhiteSpace(rangeHeader))
        {
            return new MeetingAudioHttpResponsePlan(
                200,
                "OK",
                contentType,
                resourceLength,
                0,
                resourceLength,
                null,
                IncludeBody: true,
                AcceptRanges: true,
                IsAllowedMethod: true);
        }

        if (!MeetingAudioByteRange.TryParse(rangeHeader, resourceLength, out var range))
        {
            return new MeetingAudioHttpResponsePlan(
                416,
                "Range Not Satisfiable",
                contentType,
                0,
                0,
                0,
                $"bytes */{resourceLength}",
                IncludeBody: false,
                AcceptRanges: true,
                IsAllowedMethod: true);
        }

        return new MeetingAudioHttpResponsePlan(
            206,
            "Partial Content",
            contentType,
            range.Length,
            range.Start,
            range.Length,
            $"bytes {range.Start}-{range.End}/{resourceLength}",
            IncludeBody: true,
            AcceptRanges: true,
            IsAllowedMethod: true);
    }
}

public static class MeetingAudioRequestDiagnostic
{
    public static string Format(
        string method,
        bool hasRange,
        int statusCode,
        string contentType,
        long responseLength,
        Guid? recordingId) =>
        $"Workspace audio response: method={method}; rangePresent={hasRange}; " +
        $"status={statusCode}; contentType={contentType}; " +
        $"responseLength={responseLength}; " +
        $"recordingId={recordingId?.ToString("N") ?? "none"}.";
}
