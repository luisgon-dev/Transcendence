using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

public sealed class ChampionSynergyFactOptions
{
    public int MatchBatchSize { get; set; } = 50;
    public int MaxMatchesPerRun { get; set; } = 500;
    public int MaxRunSeconds { get; set; } = 90;
    public int BatchDelayMilliseconds { get; set; } = 500;
    public int PatchesToMaterialize { get; set; } = 3;
    public int CommandTimeoutSeconds { get; set; } = 30;
    public int MaxWalMegabytesPerRun { get; set; } = 32;
}

public sealed class ChampionSynergyFactMaterializer(
    TranscendenceContext db,
    IOptions<ChampionSynergyFactOptions> options)
{
    public const string CoverageFeature = "synergy-facts-ready";

    public async Task<int> RefreshAsync(CancellationToken ct)
    {
        var timeout = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(Math.Clamp(options.Value.CommandTimeoutSeconds, 1, 120));
        try { return await RefreshCoreAsync(ct); }
        finally { db.Database.SetCommandTimeout(timeout); }
    }

    private async Task<int> RefreshCoreAsync(CancellationToken ct)
    {
        var opts = options.Value;
        var budget = Math.Max(1, opts.MaxMatchesPerRun);
        var started = Stopwatch.StartNew();
        var counted = 0;
        var walStart = await WalPositionAsync(ct);
        var archivedIds = new List<Guid>();
        var patches = await db.Patches.AsNoTracking().OrderByDescending(patch => patch.IsActive)
            .ThenByDescending(patch => patch.ReleaseDate).Take(Math.Clamp(opts.PatchesToMaterialize, 1, 6))
            .Select(patch => patch.Version).ToListAsync(ct);
        if (db.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL" &&
            await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('public._patch_archive_pending') IS NOT NULL AS \"Value\"").SingleAsync(ct))
        {
            // Archive chunks are small and verified before pruning. Finish their compact facts first
            // so retention never destroys a source that the ordinary catch-up budget has not reached.
            var pending = await db.Database.SqlQueryRaw<string>(
                "SELECT DISTINCT m.\"Patch\" AS \"Value\" FROM \"Matches\" m JOIN _patch_archive_pending a ON a.\"Id\"=m.\"Id\" WHERE m.\"Patch\" IS NOT NULL")
                .ToListAsync(ct);
            archivedIds = await db.Database.SqlQueryRaw<Guid>("SELECT \"Id\" AS \"Value\" FROM _patch_archive_pending LIMIT 500")
                .ToListAsync(ct);
            patches = pending.Concat(patches).Distinct().ToList();
        }
        var unfinished = await db.ChampionSynergySourceMatches.AsNoTracking()
            .Where(source => !db.AnalyticsResponseSnapshots.Any(snapshot =>
                snapshot.Feature == CoverageFeature && snapshot.ScopeKey == "global" && snapshot.Patch == source.Patch))
            .Select(source => source.Patch).Distinct().ToListAsync(ct);
        patches = patches.Concat(unfinished).Distinct().ToList();
        foreach (var patch in patches)
        {
            while (counted < budget &&
                   started.Elapsed.TotalSeconds < Math.Max(1, opts.MaxRunSeconds))
            {
                if (walStart is { } initial && await WalPositionAsync(ct) is { } current && current >= initial &&
                    current - initial >= (ulong)Math.Max(1, opts.MaxWalMegabytesPerRun) * 1024 * 1024)
                    return counted;
                var missing = Missing(patch);
                if (archivedIds.Count > 0 && await missing.AnyAsync(match => archivedIds.Contains(match.Id), ct))
                    missing = missing.Where(match => archivedIds.Contains(match.Id));
                var matches = await missing.OrderBy(match => match.Id)
                    .Take(Math.Min(Math.Clamp(opts.MatchBatchSize, 1, 500), budget - counted))
                    .Select(match => new { match.Id, match.Patch, match.QueueId, match.QueueType, match.QueueFamily, match.PlatformRegion })
                    .ToListAsync(ct);
                if (matches.Count == 0)
                {
                    var coverage = await db.AnalyticsResponseSnapshots.FirstOrDefaultAsync(row =>
                        row.Feature == CoverageFeature && row.ScopeKey == "global" && row.Patch == patch, ct);
                    if (coverage == null)
                    {
                        coverage = new AnalyticsResponseSnapshot
                        {
                            Id = Guid.NewGuid(), Feature = CoverageFeature, ScopeKey = "global", Patch = patch, Payload = "true"
                        };
                        db.AnalyticsResponseSnapshots.Add(coverage);
                    }
                    coverage.ComputedAtUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    db.ChangeTracker.Clear();
                    break;
                }
                var ids = matches.Select(match => match.Id).ToList();
                var participants = await db.MatchParticipants.IgnoreQueryFilters().AsNoTracking()
                    .Where(participant => ids.Contains(participant.MatchId))
                    .Select(participant => new
                    {
                        participant.MatchId, participant.ParticipantId, participant.SummonerId,
                        participant.ChampionId, participant.TeamPosition, participant.TeamId, participant.Win
                    }).ToListAsync(ct);
                foreach (var match in matches)
                {
                    var team = participants.Where(participant => participant.MatchId == match.Id).ToList();
                    // Match the raw query's legacy predicates exactly, including a family-only Flex
                    // row or inconsistent row that qualifies in both existing queue scopes.
                    var queues = new List<string>();
                    if (match.QueueId == 420 || (match.QueueId == 0 && match.QueueType == "420"))
                        queues.Add(QueueCatalog.QueueFamilyRankedSoloDuo);
                    if (match.QueueFamily == QueueCatalog.QueueFamilyRankedFlex || match.QueueId == 440 ||
                        (match.QueueId == 0 && match.QueueType == "440"))
                        queues.Add(QueueCatalog.QueueFamilyRankedFlex);
                    foreach (var queue in queues)
                    foreach (var focal in team.Where(participant => !string.IsNullOrWhiteSpace(participant.TeamPosition)))
                    {
                        ChampionSynergyFact Fact(int partnerId, int partnerChampion, string partnerRole) => new()
                        {
                            MatchId = match.Id, ParticipantId = focal.ParticipantId, PartnerParticipantId = partnerId,
                            SummonerId = focal.SummonerId, Patch = patch, QueueFamily = queue,
                            PlatformRegion = match.PlatformRegion ?? "", ChampionId = focal.ChampionId,
                            Role = focal.TeamPosition!, Win = focal.Win,
                            PartnerChampionId = partnerChampion, PartnerRole = partnerRole
                        };
                        db.ChampionSynergyFacts.Add(Fact(0, 0, ""));
                        foreach (var partner in team.Where(participant => participant.TeamId == focal.TeamId &&
                                     participant.ParticipantId != focal.ParticipantId &&
                                     participant.ChampionId != focal.ChampionId &&
                                     IsPair(focal.TeamPosition!, participant.TeamPosition)))
                            db.ChampionSynergyFacts.Add(Fact(partner.ParticipantId, partner.ChampionId, partner.TeamPosition!));
                    }
                    db.ChampionSynergySourceMatches.Add(new ChampionSynergySourceMatch
                    {
                        MatchId = match.Id, Patch = patch, MaterializedAtUtc = DateTime.UtcNow
                    });
                }
                // SaveChanges commits facts and the source ledger together. A cancelled/failed batch
                // cannot leave partially counted matches and a later run safely resumes.
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
                counted += matches.Count;
                if (opts.BatchDelayMilliseconds > 0)
                    await Task.Delay(Math.Clamp(opts.BatchDelayMilliseconds, 0, 10_000), ct);
            }
        }
        return counted;
    }

    private async Task<ulong?> WalPositionAsync(CancellationToken ct)
    {
        if (db.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL") return null;
        var value = await db.Database.SqlQueryRaw<string>(
            "SELECT pg_current_wal_insert_lsn()::text AS \"Value\"").SingleAsync(ct);
        var parts = value.Split('/');
        return (Convert.ToUInt64(parts[0], 16) << 32) | Convert.ToUInt64(parts[1], 16);
    }

    private IQueryable<Data.Models.LoL.Match.Match> Missing(string patch) =>
        db.Matches.IgnoreQueryFilters().AsNoTracking().Where(match => match.Patch == patch &&
            match.Status == FetchStatus.Success &&
            (match.QueueFamily == QueueCatalog.QueueFamilyRankedFlex || match.QueueId == 420 || match.QueueId == 440 ||
             (match.QueueId == 0 && (match.QueueType == "420" || match.QueueType == "440"))) &&
            !db.ChampionSynergySourceMatches.Any(source => source.MatchId == match.Id));

    internal static bool IsPair(string role, string? partnerRole) => role switch
    {
        "BOTTOM" => partnerRole == "UTILITY",
        "UTILITY" => partnerRole == "BOTTOM",
        "JUNGLE" => !string.IsNullOrEmpty(partnerRole) && partnerRole != "JUNGLE",
        "TOP" or "MIDDLE" => partnerRole == "JUNGLE",
        _ => false
    };
}
