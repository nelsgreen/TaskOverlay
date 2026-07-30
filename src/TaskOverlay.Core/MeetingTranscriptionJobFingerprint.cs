using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TaskOverlay.Core;

/// <summary>
/// Every material input to a chunked transcription job. If any of these
/// changes, previously completed chunks describe a different job and must not
/// be reused.
/// </summary>
public sealed record MeetingTranscriptionJobInputs(
    Guid RecordingId,
    string SourceAudioFileName,
    long SourceAudioBytes,
    string SourceAudioSha256,
    double RangeStartSeconds,
    double RangeEndSeconds,
    string ProviderName,
    string Model,
    string Language,
    int PolicyVersion,
    double MaxChunkSeconds,
    double OverlapSeconds,
    string PlanDescription);

public static class MeetingTranscriptionJobFingerprint
{
    /// <summary>
    /// Deterministic SHA-256 over the normalized material inputs. Field
    /// separators are unit-separator characters so no value can impersonate a
    /// boundary, and doubles use a fixed invariant format so the same job
    /// fingerprints identically across runs and cultures.
    /// </summary>
    public static string Compute(MeetingTranscriptionJobInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var builder = new StringBuilder();
        Append(builder, "v1");
        Append(builder, inputs.RecordingId.ToString("N"));
        Append(builder, Normalize(inputs.SourceAudioFileName));
        Append(builder, inputs.SourceAudioBytes.ToString(CultureInfo.InvariantCulture));
        Append(builder, Normalize(inputs.SourceAudioSha256).ToUpperInvariant());
        Append(builder, Seconds(inputs.RangeStartSeconds));
        Append(builder, Seconds(inputs.RangeEndSeconds));
        Append(builder, Normalize(inputs.ProviderName));
        Append(builder, Normalize(inputs.Model));
        Append(builder, Normalize(inputs.Language));
        Append(builder, inputs.PolicyVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, Seconds(inputs.MaxChunkSeconds));
        Append(builder, Seconds(inputs.OverlapSeconds));
        Append(builder, Normalize(inputs.PlanDescription));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash);
    }

    private static void Append(StringBuilder builder, string value)
    {
        builder.Append(value);
        builder.Append('\u001f');
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string Seconds(double value) =>
        value.ToString("F4", CultureInfo.InvariantCulture);
}
