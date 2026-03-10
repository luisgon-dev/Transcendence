using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Transcendence.Service.Core.Services.Diagnostics;

public static class OperationalFileLoggingExtensions
{
    private const string SectionName = "OperationalLogs";

    public static ILoggingBuilder AddOperationalFileLogger(
        this ILoggingBuilder builder,
        IConfiguration configuration,
        string defaultServiceName)
    {
        var section = configuration.GetSection(SectionName);
        var configuredServiceName = section["ServiceName"];
        var configuredDirectory = section["DirectoryPath"];
        var configuredMinLevel = section["MinLevel"];
        var categoryRules = configuration.GetSection("Logging:LogLevel")
            .GetChildren()
            .Select(child => new { CategoryName = child.Key, MinLevel = TryParseLogLevel(child.Value) })
            .Where(x => x.MinLevel.HasValue)
            .Select(x => new OperationalLogLevelRule(x.CategoryName, x.MinLevel!.Value))
            .ToArray();

        var minLevel = LogLevel.Information;
        if (Enum.TryParse(configuredMinLevel, ignoreCase: true, out LogLevel parsedLevel))
            minLevel = parsedLevel;

        var options = new OperationalFileLoggerOptions
        {
            ServiceName = string.IsNullOrWhiteSpace(configuredServiceName)
                ? defaultServiceName
                : configuredServiceName,
            DirectoryPath = string.IsNullOrWhiteSpace(configuredDirectory)
                ? Path.Combine(AppContext.BaseDirectory, "logs")
                : configuredDirectory,
            MinLevel = minLevel,
            CategoryRules = categoryRules
        };

        builder.AddProvider(new OperationalFileLoggerProvider(options));
        return builder;
    }

    private static LogLevel? TryParseLogLevel(string? value)
    {
        return Enum.TryParse(value, ignoreCase: true, out LogLevel parsedLevel)
            ? parsedLevel
            : null;
    }
}
