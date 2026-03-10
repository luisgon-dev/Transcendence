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
    private readonly IReadOnlyList<OperationalLogLevelRule> categoryRules;
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
        categoryRules = options.CategoryRules;
        maxFileSizeBytes = Math.Max(MinimumFileSizeBytes, options.MaxFileSizeBytes);
        retainedFileCount = Math.Clamp(options.RetainedFileCount, 1, MaxRetainedFiles);

        TryEnsureLogFileExists();
    }

    public ILogger CreateLogger(string categoryName)
    {
        return loggers.GetOrAdd(categoryName, category => new OperationalFileLogger(
            category,
            minLevel,
            categoryRules,
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
    IReadOnlyList<OperationalLogLevelRule> categoryRules,
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
        if (logLevel == LogLevel.None)
            return false;

        var effectiveMinLevel = ResolveMinimumLevel(categoryName, minLevel, categoryRules);
        return logLevel >= effectiveMinLevel;
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

    private static LogLevel ResolveMinimumLevel(
        string categoryName,
        LogLevel fallbackMinLevel,
        IReadOnlyList<OperationalLogLevelRule> categoryRules)
    {
        LogLevel? matchedLevel = null;
        var matchedRuleLength = -1;

        foreach (var rule in categoryRules)
        {
            if (string.IsNullOrWhiteSpace(rule.CategoryName))
                continue;

            if (rule.CategoryName.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                if (matchedRuleLength < 0)
                {
                    matchedLevel = rule.MinLevel;
                    matchedRuleLength = 0;
                }

                continue;
            }

            if (!CategoryMatches(categoryName, rule.CategoryName))
                continue;

            if (rule.CategoryName.Length <= matchedRuleLength)
                continue;

            matchedLevel = rule.MinLevel;
            matchedRuleLength = rule.CategoryName.Length;
        }

        return matchedLevel ?? fallbackMinLevel;
    }

    private static bool CategoryMatches(string categoryName, string ruleCategory)
    {
        if (categoryName.Equals(ruleCategory, StringComparison.OrdinalIgnoreCase))
            return true;

        return categoryName.StartsWith(ruleCategory + ".", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record OperationalFileLoggerOptions
{
    public string DirectoryPath { get; init; } = Path.Combine(AppContext.BaseDirectory, "logs");
    public string ServiceName { get; init; } = "service";
    public LogLevel MinLevel { get; init; } = LogLevel.Information;
    public IReadOnlyList<OperationalLogLevelRule> CategoryRules { get; init; } = [];
    public long MaxFileSizeBytes { get; init; } = 10 * 1024 * 1024;
    public int RetainedFileCount { get; init; } = 5;
}

public sealed record OperationalLogLevelRule(string CategoryName, LogLevel MinLevel);

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
