using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Transcendence.Service.Core.Services.Diagnostics;

public sealed class OperationalFileLoggerProvider : ILoggerProvider
{
    private const long MinimumFileSizeBytes = 64 * 1024;
    private const int MaxRetainedFiles = 20;
    private static readonly int NewLineByteCount = Encoding.UTF8.GetByteCount(Environment.NewLine);

    private readonly ConcurrentDictionary<string, OperationalFileLogger> loggers = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private readonly string logFilePath;
    private readonly LogLevel minLevel;
    private readonly string serviceName;
    private readonly long maxFileSizeBytes;
    private readonly int retainedFileCount;
    private int reportedWriteFailure;

    public OperationalFileLoggerProvider(OperationalFileLoggerOptions options)
    {
        serviceName = string.IsNullOrWhiteSpace(options.ServiceName)
            ? "service"
            : options.ServiceName.Trim();

        var directory = string.IsNullOrWhiteSpace(options.DirectoryPath)
            ? Path.Combine(AppContext.BaseDirectory, "logs")
            : options.DirectoryPath.Trim();

        Directory.CreateDirectory(directory);
        logFilePath = Path.Combine(directory, $"{serviceName}.log");
        minLevel = options.MinLevel;
        maxFileSizeBytes = Math.Max(MinimumFileSizeBytes, options.MaxFileSizeBytes);
        retainedFileCount = Math.Clamp(options.RetainedFileCount, 1, MaxRetainedFiles);

        TryEnsureLogFileExists();
    }

    public ILogger CreateLogger(string categoryName)
    {
        return loggers.GetOrAdd(categoryName, category => new OperationalFileLogger(
            category,
            minLevel,
            serviceName,
            WriteLine));
    }

    public void Dispose()
    {
    }

    private void WriteLine(string payload)
    {
        try
        {
            lock (gate)
            {
                RotateIfNeeded(Encoding.UTF8.GetByteCount(payload) + NewLineByteCount);
                File.AppendAllText(logFilePath, payload + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            ReportWriteFailure(ex);
        }
    }

    private void TryEnsureLogFileExists()
    {
        try
        {
            using var stream = File.Open(logFilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
            stream.Flush();
        }
        catch (Exception ex)
        {
            ReportWriteFailure(ex);
        }
    }

    private void ReportWriteFailure(Exception ex)
    {
        if (Interlocked.Exchange(ref reportedWriteFailure, 1) != 0)
            return;

        Console.Error.WriteLine(
            $"Operational file logging unavailable for '{serviceName}' at '{logFilePath}': {ex.Message}");
    }

    private void RotateIfNeeded(int incomingBytes)
    {
        if (!File.Exists(logFilePath))
            return;

        var currentLength = new FileInfo(logFilePath).Length;
        if (currentLength + incomingBytes <= maxFileSizeBytes)
            return;

        var oldestArchivePath = $"{logFilePath}.{retainedFileCount}";
        if (File.Exists(oldestArchivePath))
            File.Delete(oldestArchivePath);

        for (var i = retainedFileCount - 1; i >= 1; i--)
        {
            var sourceArchivePath = $"{logFilePath}.{i}";
            if (!File.Exists(sourceArchivePath))
                continue;

            var destinationArchivePath = $"{logFilePath}.{i + 1}";
            File.Move(sourceArchivePath, destinationArchivePath);
        }

        var firstArchivePath = $"{logFilePath}.1";
        if (File.Exists(firstArchivePath))
            File.Delete(firstArchivePath);

        File.Move(logFilePath, firstArchivePath);
    }
}

internal sealed class OperationalFileLogger(
    string categoryName,
    LogLevel minLevel,
    string serviceName,
    Action<string> writeLine) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel != LogLevel.None && logLevel >= minLevel;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception is null)
            return;

        var entry = new OperationalLogEntry(
            TimestampUtc: DateTime.UtcNow,
            Service: serviceName,
            Level: logLevel.ToString(),
            Category: categoryName,
            EventId: eventId.Id,
            Message: message,
            Exception: exception?.ToString());

        var payload = JsonSerializer.Serialize(entry, OperationalLogJsonContext.Default.OperationalLogEntry);
        writeLine(payload);
    }
}

public sealed record OperationalFileLoggerOptions
{
    public string DirectoryPath { get; init; } = Path.Combine(AppContext.BaseDirectory, "logs");
    public string ServiceName { get; init; } = "service";
    public LogLevel MinLevel { get; init; } = LogLevel.Information;
    public long MaxFileSizeBytes { get; init; } = 10 * 1024 * 1024;
    public int RetainedFileCount { get; init; } = 5;
}

public sealed record OperationalLogEntry(
    DateTime TimestampUtc,
    string Service,
    string Level,
    string Category,
    int EventId,
    string? Message,
    string? Exception
);

[JsonSerializable(typeof(OperationalLogEntry))]
internal partial class OperationalLogJsonContext : JsonSerializerContext;
