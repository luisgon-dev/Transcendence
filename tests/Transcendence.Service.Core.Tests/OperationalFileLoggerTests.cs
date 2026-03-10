using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Transcendence.Service.Core.Services.Diagnostics;

namespace Transcendence.Service.Core.Tests;

public class OperationalFileLoggerTests
{
    [Fact]
    public void CategoryOverride_SuppressesLowerLevelEntries_ForMatchingCategory()
    {
        using var harness = new LoggerHarness(new OperationalFileLoggerOptions
        {
            ServiceName = "service",
            MinLevel = LogLevel.Information,
            CategoryRules =
            [
                new OperationalLogLevelRule("Default", LogLevel.Information),
                new OperationalLogLevelRule("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning)
            ]
        });

        var logger = harness.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command");
        logger.LogInformation("sql command");

        harness.ReadEntries().Should().BeEmpty();
    }

    [Fact]
    public void CategoryOverride_AllowsHigherSeverityEntries_ForMatchingCategory()
    {
        using var harness = new LoggerHarness(new OperationalFileLoggerOptions
        {
            ServiceName = "service",
            MinLevel = LogLevel.Information,
            CategoryRules =
            [
                new OperationalLogLevelRule("Default", LogLevel.Information),
                new OperationalLogLevelRule("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning)
            ]
        });

        var logger = harness.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command");
        logger.LogWarning("slow command");

        harness.ReadEntries()
            .Should()
            .ContainSingle(entry =>
                entry.Category == "Microsoft.EntityFrameworkCore.Database.Command"
                && entry.Level == nameof(LogLevel.Warning)
                && entry.Message == "slow command");
    }

    [Fact]
    public void DefaultRule_IsApplied_WhenNoMoreSpecificCategoryMatches()
    {
        using var harness = new LoggerHarness(new OperationalFileLoggerOptions
        {
            ServiceName = "service",
            MinLevel = LogLevel.Warning,
            CategoryRules =
            [
                new OperationalLogLevelRule("Default", LogLevel.Information)
            ]
        });

        var logger = harness.CreateLogger("Transcendence.Service.Core.Services.Jobs.SummonerRefreshJob");
        logger.LogInformation("summary");

        harness.ReadEntries()
            .Should()
            .ContainSingle(entry => entry.Message == "summary" && entry.Level == nameof(LogLevel.Information));
    }

    private sealed class LoggerHarness : IDisposable
    {
        private readonly string directory;
        private readonly OperationalFileLoggerProvider provider;

        public LoggerHarness(OperationalFileLoggerOptions options)
        {
            directory = Path.Combine(Path.GetTempPath(), $"operational-log-tests-{Guid.NewGuid():N}");
            provider = new OperationalFileLoggerProvider(options with { DirectoryPath = directory });
        }

        public ILogger CreateLogger(string categoryName)
        {
            return provider.CreateLogger(categoryName);
        }

        public IReadOnlyList<OperationalLogEntry> ReadEntries()
        {
            var logPath = Directory.EnumerateFiles(directory, "*.log").Single();
            return File.ReadAllLines(logPath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonSerializer.Deserialize<OperationalLogEntry>(line))
                .OfType<OperationalLogEntry>()
                .ToList();
        }

        public void Dispose()
        {
            provider.Dispose();

            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
