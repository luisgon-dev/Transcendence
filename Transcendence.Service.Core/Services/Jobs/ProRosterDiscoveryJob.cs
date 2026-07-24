using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Service.Core.Services.Jobs.Configuration;

namespace Transcendence.Service.Core.Services.Jobs;

public sealed class ProRosterDiscoveryJob(
    HttpClient httpClient,
    TranscendenceContext db,
    IOptions<ProRosterDiscoveryOptions> options,
    ILogger<ProRosterDiscoveryJob> logger)
{
    [Queue("maintenance")]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        var settings = options.Value;
        var pageSize = Math.Clamp(settings.PageSize, 1, 500);
        var maxPages = Math.Clamp(settings.MaxPages, 1, 20);
        var pageDelay = TimeSpan.FromSeconds(Math.Clamp(settings.PageDelaySeconds, 0, 300));
        var discovered = new Dictionary<string, DiscoveredProPlayer>(StringComparer.OrdinalIgnoreCase);

        for (var page = 0; page < maxPages; page++)
        {
            IReadOnlyList<DiscoveredProPlayer> players;
            try
            {
                var requestUri = BuildRequestUri(settings.Endpoint, pageSize, page * pageSize);
                using var response = await httpClient.GetAsync(requestUri, ct);
                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadAsStringAsync(ct);
                players = ParsePlayers(payload);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Leaguepedia is a discovery aid, not a production dependency. Keep any pages
                // already read and let the next daily run retry without failing worker health.
                logger.LogWarning(
                    exception,
                    "Pro roster discovery source page {Page} was unavailable; preserving partial and existing candidates.",
                    page + 1);
                break;
            }

            foreach (var player in players)
                discovered[player.ExternalId] = player;
            if (players.Count < pageSize)
                break;
            if (page + 1 < maxPages && pageDelay > TimeSpan.Zero)
                await Task.Delay(pageDelay, ct);
        }

        if (discovered.Count == 0)
        {
            logger.LogInformation("Pro roster discovery completed with no source rows.");
            return;
        }

        var externalIds = discovered.Keys.ToList();
        var existing = await db.ProPlayerDiscoveryCandidates
            .Where(candidate => candidate.Source == "leaguepedia" && externalIds.Contains(candidate.ExternalId))
            .ToDictionaryAsync(candidate => candidate.ExternalId, StringComparer.OrdinalIgnoreCase, ct);
        var nowUtc = DateTime.UtcNow;
        var created = 0;

        foreach (var player in discovered.Values)
        {
            if (!existing.TryGetValue(player.ExternalId, out var candidate))
            {
                db.ProPlayerDiscoveryCandidates.Add(new ProPlayerDiscoveryCandidate
                {
                    Id = Guid.NewGuid(),
                    Source = "leaguepedia",
                    ExternalId = player.ExternalId,
                    ProName = player.ProName,
                    TeamName = player.TeamName,
                    Role = player.Role,
                    SoloQueueIds = player.SoloQueueIds,
                    Status = "pending",
                    FirstSeenAtUtc = nowUtc,
                    LastSeenAtUtc = nowUtc
                });
                created++;
                continue;
            }

            candidate.ProName = player.ProName;
            candidate.TeamName = player.TeamName;
            candidate.Role = player.Role;
            candidate.SoloQueueIds = player.SoloQueueIds;
            candidate.LastSeenAtUtc = nowUtc;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Pro roster discovery staged {Created} new candidates and refreshed {Updated} existing candidates.",
            created,
            discovered.Count - created);
    }

    public static IReadOnlyList<DiscoveredProPlayer> ParsePlayers(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"Leaguepedia returned an error: {error}");
        if (!document.RootElement.TryGetProperty("cargoquery", out var rows) ||
            rows.ValueKind != JsonValueKind.Array)
            return [];

        var players = new List<DiscoveredProPlayer>();
        foreach (var row in rows.EnumerateArray())
        {
            if (!row.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.Object)
                continue;

            var externalId = ReadString(title, "ID");
            var proName = externalId;
            var soloQueueIds = ReadString(title, "SoloqueueIds");
            if (string.IsNullOrWhiteSpace(externalId) ||
                string.IsNullOrWhiteSpace(proName) ||
                string.IsNullOrWhiteSpace(soloQueueIds))
                continue;

            players.Add(new DiscoveredProPlayer(
                externalId.Trim(),
                proName.Trim(),
                NormalizeOptional(ReadString(title, "Team")),
                NormalizeOptional(ReadString(title, "Role")),
                soloQueueIds.Trim()));
        }

        return players;
    }

    private static string BuildRequestUri(string endpoint, int limit, int offset)
    {
        var fields = "P.ID=ID,P.OverviewPage=OverviewPage,P.PlayerName=PlayerName,P.Team=Team,P.Role=Role,P.SoloqueueIds=SoloqueueIds";
        var where = "P.IsRetired=0 AND P.Team IS NOT NULL AND P.SoloqueueIds IS NOT NULL";
        var query = string.Join("&",
            "action=cargoquery",
            "format=json",
            $"tables={Uri.EscapeDataString("Players=P")}",
            $"fields={Uri.EscapeDataString(fields)}",
            $"where={Uri.EscapeDataString(where)}",
            $"limit={limit}",
            $"offset={offset}");
        return $"{endpoint.TrimEnd('?')}{(endpoint.Contains('?') ? '&' : '?')}{query}";
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public sealed record DiscoveredProPlayer(
        string ExternalId,
        string ProName,
        string? TeamName,
        string? Role,
        string SoloQueueIds);
}
