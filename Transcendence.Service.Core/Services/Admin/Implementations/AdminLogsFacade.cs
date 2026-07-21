using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Transcendence.Service.Core.Services.Admin.Interfaces;
using Transcendence.Service.Core.Services.Admin.Models;
using Transcendence.Service.Core.Services.Diagnostics;

namespace Transcendence.Service.Core.Services.Admin.Implementations;

/// <summary>
/// Hosts the verbatim service-log tailing logic extracted from AdminOperationsController's
/// <c>logs/services</c> action (P10.1). Behavior-preserving: response shapes and parsing are
/// identical to the original.
/// </summary>
public sealed class AdminLogsFacade(IConfiguration configuration) : IAdminLogsFacade
{
    private static readonly HashSet<string> AllowedServiceLogKeys = ["webapi", "service"];

    public AdminServiceLogsLookup GetServiceLogs(
        string service,
        string? level,
        string? q,
        DateTime? sinceUtc,
        DateTime? untilUtc,
        int limit)
    {
        var serviceKey = service.Trim().ToLowerInvariant();
        if (!AllowedServiceLogKeys.Contains(serviceKey))
        {
            return new AdminServiceLogsLookup(false, null);
        }

        var safeLimit = Math.Clamp(limit, 1, 500);
        var directory = ResolveOperationalLogDirectory(serviceKey);
        var logFiles = directory is null
            ? []
            : GetOperationalLogFiles(directory, serviceKey).ToList();
        if (logFiles.Count == 0)
        {
            return new AdminServiceLogsLookup(true, new AdminServiceLogsResponse(
                new AdminLogSourceDto(
                    Service: serviceKey,
                    Available: false,
                    FilesScanned: 0,
                    LatestTimestampUtc: null,
                    Truncated: false),
                Array.Empty<AdminServiceLogDto>()));
        }

        var normalizedLevel = string.IsNullOrWhiteSpace(level)
            ? null
            : level.Trim().ToUpperInvariant();
        var search = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        var entries = new List<AdminServiceLogDto>(safeLimit);
        DateTime? latestTimestampUtc = null;
        var truncated = false;

        foreach (var path in logFiles)
        {
            foreach (var line in ReadMostRecentLines(path, maxLines: 4000))
            {
                if (!TryParseOperationalLogLine(line, out var parsed))
                    continue;

                latestTimestampUtc ??= parsed.TimestampUtc;

                if (normalizedLevel is not null && !string.Equals(parsed.Level, normalizedLevel, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (sinceUtc.HasValue && parsed.TimestampUtc < sinceUtc.Value)
                    continue;

                if (untilUtc.HasValue && parsed.TimestampUtc > untilUtc.Value)
                    continue;

                if (search is not null &&
                    !ContainsSearch(parsed.Message, search) &&
                    !ContainsSearch(parsed.Category, search) &&
                    !ContainsSearch(parsed.Exception, search))
                    continue;

                entries.Add(parsed);
                if (entries.Count >= safeLimit)
                {
                    truncated = true;
                    break;
                }
            }

            if (entries.Count >= safeLimit)
                break;
        }

        return new AdminServiceLogsLookup(true, new AdminServiceLogsResponse(
            new AdminLogSourceDto(
                Service: serviceKey,
                Available: true,
                FilesScanned: logFiles.Count,
                LatestTimestampUtc: latestTimestampUtc,
                Truncated: truncated),
            entries));
    }

    private static IEnumerable<string> ReadMostRecentLines(string path, int maxLines)
    {
        if (maxLines <= 0)
            return [];

        var lines = new List<string>(maxLines);
        var reversedLineBuffer = new List<byte>(1024);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length == 0)
            return lines;

        const int readBufferSize = 4096;
        var readBuffer = new byte[readBufferSize];
        var position = stream.Length;

        while (position > 0 && lines.Count < maxLines)
        {
            var bytesToRead = (int)Math.Min(readBufferSize, position);
            position -= bytesToRead;
            stream.Seek(position, SeekOrigin.Begin);
            var bytesRead = stream.Read(readBuffer, 0, bytesToRead);

            for (var i = bytesRead - 1; i >= 0 && lines.Count < maxLines; i--)
            {
                var current = readBuffer[i];
                if (current == (byte)'\n')
                {
                    AddLineFromReversedBytes(reversedLineBuffer, lines);
                    continue;
                }

                reversedLineBuffer.Add(current);
            }
        }

        if (lines.Count < maxLines)
            AddLineFromReversedBytes(reversedLineBuffer, lines);

        return lines;
    }

    private static void AddLineFromReversedBytes(List<byte> reversedBytes, List<string> lines)
    {
        if (reversedBytes.Count == 0)
            return;

        var lineBytes = reversedBytes.ToArray();
        Array.Reverse(lineBytes);
        reversedBytes.Clear();

        var line = Encoding.UTF8.GetString(lineBytes).TrimEnd('\r');
        if (line.Length > 0)
            lines.Add(line);
    }

    private static bool TryParseOperationalLogLine(string line, out AdminServiceLogDto dto)
    {
        dto = default!;
        try
        {
            var entry = JsonSerializer.Deserialize<OperationalLogEntry>(line);
            if (entry is null)
                return false;

            dto = new AdminServiceLogDto(
                entry.TimestampUtc,
                entry.Service,
                entry.Level,
                entry.Category,
                entry.EventId,
                entry.Message,
                entry.Exception);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsSearch(string? value, string search)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private string? ResolveOperationalLogDirectory(string serviceKey)
    {
        var sourceSpecificDirectory = configuration[$"AdminLogs:Sources:{serviceKey}:DirectoryPath"];
        if (!string.IsNullOrWhiteSpace(sourceSpecificDirectory))
            return sourceSpecificDirectory.Trim();

        var sharedDirectory = configuration["OperationalLogs:DirectoryPath"];
        if (!string.IsNullOrWhiteSpace(sharedDirectory))
            return sharedDirectory.Trim();

        return string.Equals(serviceKey, "webapi", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(AppContext.BaseDirectory, "logs")
            : null;
    }

    private static IEnumerable<string> GetOperationalLogFiles(string directory, string serviceKey)
    {
        if (!Directory.Exists(directory))
            yield break;

        var candidates = Directory.EnumerateFiles(directory, $"{serviceKey}.log*")
            .Select(path => new
            {
                Path = path,
                SortOrder = GetLogFileSortOrder(path, serviceKey)
            })
            .Where(x => x.SortOrder.HasValue)
            .OrderBy(x => x.SortOrder!.Value)
            .Select(x => x.Path);

        foreach (var candidate in candidates)
            yield return candidate;
    }

    private static int? GetLogFileSortOrder(string path, string serviceKey)
    {
        var fileName = Path.GetFileName(path);
        var liveName = $"{serviceKey}.log";
        if (string.Equals(fileName, liveName, StringComparison.OrdinalIgnoreCase))
            return 0;

        var prefix = $"{liveName}.";
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        return int.TryParse(fileName[prefix.Length..], out var parsedSuffix)
            ? parsedSuffix
            : null;
    }
}
