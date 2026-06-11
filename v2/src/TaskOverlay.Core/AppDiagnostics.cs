using System;
using System.IO;
using System.Text;

namespace TaskOverlay.Core;

public sealed class AppDiagnostics
{
    private readonly object _sync = new();

    public AppDiagnostics(string stateDirectory)
    {
        StateDirectory = stateDirectory;
        LogsDirectory = Path.Combine(stateDirectory, "logs");
    }

    public string StateDirectory { get; }
    public string StatePath => Path.Combine(StateDirectory, "state.json");
    public string LogsDirectory { get; }

    public void Log(string message, Exception? exception = null)
    {
        var entry = BuildEntry("INFO", message, exception, context: null);
        TryAppend(Path.Combine(LogsDirectory, $"runtime-{DateTime.UtcNow:yyyyMMdd}.log"), entry);
    }

    public string? LogCrash(string source, Exception exception, string? context)
    {
        var timestamp = DateTime.UtcNow;
        var path = Path.Combine(
            LogsDirectory,
            $"crash-{timestamp:yyyyMMdd-HHmmssfff}.log");
        var entry = BuildEntry("CRASH", source, exception, context);

        return TryAppend(path, entry) ? path : null;
    }

    private string BuildEntry(
        string level,
        string message,
        Exception? exception,
        string? context)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"TimestampUtc: {DateTimeOffset.UtcNow:O}");
        builder.AppendLine($"Level: {level}");
        builder.AppendLine($"Message: {message}");
        builder.AppendLine($"ProcessId: {Environment.ProcessId}");
        builder.AppendLine($"StatePath: {StatePath}");

        if (!string.IsNullOrWhiteSpace(context))
        {
            builder.AppendLine($"Context: {context}");
        }

        if (exception is not null)
        {
            AppendException(builder, exception);
        }

        builder.AppendLine(new string('-', 80));
        return builder.ToString();
    }

    private static void AppendException(StringBuilder builder, Exception exception)
    {
        AppendException(builder, exception, "0");
    }

    private static void AppendException(
        StringBuilder builder,
        Exception exception,
        string path)
    {
        builder.AppendLine($"Exception[{path}].Type: {exception.GetType().FullName}");
        builder.AppendLine($"Exception[{path}].Message: {exception.Message}");
        builder.AppendLine($"Exception[{path}].StackTrace:");
        builder.AppendLine(exception.StackTrace ?? "<no stack trace>");

        if (exception is AggregateException aggregateException)
        {
            for (var index = 0; index < aggregateException.InnerExceptions.Count; index++)
            {
                AppendException(
                    builder,
                    aggregateException.InnerExceptions[index],
                    $"{path}.{index}");
            }
        }
        else if (exception.InnerException is not null)
        {
            AppendException(builder, exception.InnerException, $"{path}.0");
        }
    }

    private bool TryAppend(string path, string entry)
    {
        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(LogsDirectory);
                File.AppendAllText(path, entry, Encoding.UTF8);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
