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
            MinLevel = minLevel
        };

        builder.AddProvider(new OperationalFileLoggerProvider(options));
        return builder;
    }
}
