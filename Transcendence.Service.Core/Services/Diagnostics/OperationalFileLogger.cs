using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Transcendence.Service.Core.Services.Diagnostics;

public sealed class OperationalFileLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, OperationalFileLogger> loggers = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private readonly string logFilePath;
    private readonly LogLevel minLevel;
    private readonly string serviceName;

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
                File.AppendAllText(logFilePath, payload + Environment.NewLine);
            }
        }
        catch
        {
            // Best-effort logging; avoid recursive failures from the logger itself.
        }
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
