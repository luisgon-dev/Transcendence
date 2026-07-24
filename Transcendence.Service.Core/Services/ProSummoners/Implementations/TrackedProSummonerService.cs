using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Service.Core.Services.ProSummoners.Interfaces;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.WebAPI.Controllers;

namespace Transcendence.Service.Core.Services.ProSummoners.Implementations;

public sealed class TrackedProSummonerService(TranscendenceContext db) : ITrackedProSummonerService
{
    public async Task<IReadOnlyList<TrackedProSummonerDto>> ListAsync(
        bool? isActive,
        CancellationToken ct = default)
    {
        var query = db.TrackedProSummoners.AsNoTracking();
        if (isActive.HasValue)
            query = query.Where(x => x.IsActive == isActive.Value);

        return await query
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => new TrackedProSummonerDto(
                x.Id,
                x.Puuid,
                x.PlatformRegion,
                x.GameName,
                x.TagLine,
                x.ProName,
                x.TeamName,
                x.IsPro,
                x.IsHighEloOtp,
                x.IsActive,
                x.Source,
                x.SourceExternalId,
                x.LastVerifiedAtUtc,
                x.OtpChampionId,
                x.OtpGames,
                x.OtpSampleSize,
                x.OtpEvaluatedAtUtc,
                x.CreatedAtUtc,
                x.UpdatedAtUtc))
            .ToListAsync(ct);
    }

    public async Task<TrackedProSummonerDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await db.TrackedProSummoners
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new TrackedProSummonerDto(
                x.Id,
                x.Puuid,
                x.PlatformRegion,
                x.GameName,
                x.TagLine,
                x.ProName,
                x.TeamName,
                x.IsPro,
                x.IsHighEloOtp,
                x.IsActive,
                x.Source,
                x.SourceExternalId,
                x.LastVerifiedAtUtc,
                x.OtpChampionId,
                x.OtpGames,
                x.OtpSampleSize,
                x.OtpEvaluatedAtUtc,
                x.CreatedAtUtc,
                x.UpdatedAtUtc))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<TrackedProCreateResult> CreateAsync(
        UpsertTrackedProSummonerRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.GameName) || string.IsNullOrWhiteSpace(request.TagLine) ||
            string.IsNullOrWhiteSpace(request.PlatformRegion))
        {
            return TrackedProCreateResult.Invalid("gameName, tagLine, and platformRegion are required.");
        }

        var normalizedPlatform = request.PlatformRegion.Trim().ToUpperInvariant();
        if (!PlatformRouteParser.TryParse(normalizedPlatform, out _))
            return TrackedProCreateResult.Invalid($"Unsupported platform region '{request.PlatformRegion}'.");

        var gameName = request.GameName.Trim();
        var tagLine = request.TagLine.Trim();
        string normalizedPuuid;
        if (!string.IsNullOrWhiteSpace(request.Puuid))
        {
            normalizedPuuid = request.Puuid.Trim();
        }
        else
        {
            var normalizedGameName = gameName.ToUpperInvariant();
            var normalizedTagLine = tagLine.ToUpperInvariant();
            var resolved = await db.Summoners
                .AsNoTracking()
                .Where(x => x.PlatformRegion == normalizedPlatform
                            && x.GameNameNormalized == normalizedGameName
                            && x.TagLineNormalized == normalizedTagLine
                            && x.Puuid != null)
                .Select(x => x.Puuid)
                .FirstOrDefaultAsync(ct);
            if (resolved is null)
            {
                return TrackedProCreateResult.Invalid(
                    $"Could not resolve Riot ID '{gameName}#{tagLine}' from stored data on {normalizedPlatform}. Provide puuid or refresh/store the summoner first.");
            }

            normalizedPuuid = resolved;
        }

        var exists = await db.TrackedProSummoners
            .AnyAsync(x => x.Puuid == normalizedPuuid && x.PlatformRegion == normalizedPlatform, ct);
        if (exists)
            return TrackedProCreateResult.Invalid("Tracked pro summoner already exists for this puuid/platform.");

        var now = DateTime.UtcNow;
        var entity = new TrackedProSummoner
        {
            Id = Guid.NewGuid(),
            Puuid = normalizedPuuid,
            PlatformRegion = normalizedPlatform,
            GameName = gameName,
            TagLine = tagLine,
            ProName = NormalizeOptional(request.ProName),
            TeamName = NormalizeOptional(request.TeamName),
            IsPro = request.IsPro,
            IsHighEloOtp = request.IsHighEloOtp,
            IsActive = request.IsActive,
            Source = "manual",
            LastVerifiedAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.TrackedProSummoners.Add(entity);
        await db.SaveChangesAsync(ct);
        return TrackedProCreateResult.Success(ToDto(entity));
    }

    public async Task<TrackedProSummonerDto?> UpdateAsync(
        Guid id,
        UpsertTrackedProSummonerRequest request,
        CancellationToken ct = default)
    {
        var entity = await db.TrackedProSummoners.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null)
            return null;

        if (!string.IsNullOrWhiteSpace(request.Puuid))
            entity.Puuid = request.Puuid.Trim();
        if (!string.IsNullOrWhiteSpace(request.PlatformRegion))
            entity.PlatformRegion = request.PlatformRegion.Trim().ToUpperInvariant();

        entity.GameName = NormalizeOptional(request.GameName);
        entity.TagLine = NormalizeOptional(request.TagLine);
        entity.ProName = NormalizeOptional(request.ProName);
        entity.TeamName = NormalizeOptional(request.TeamName);
        entity.IsPro = request.IsPro;
        entity.IsHighEloOtp = request.IsHighEloOtp;
        entity.IsActive = request.IsActive;
        entity.LastVerifiedAtUtc = DateTime.UtcNow;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.TrackedProSummoners.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null)
            return false;

        db.TrackedProSummoners.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<ProPlayerDiscoveryCandidateDto>> ListCandidatesAsync(
        string status,
        CancellationToken ct = default)
    {
        var normalizedStatus = NormalizeCandidateStatus(status);
        return await db.ProPlayerDiscoveryCandidates
            .AsNoTracking()
            .Where(x => x.Status == normalizedStatus)
            .OrderByDescending(x => x.LastSeenAtUtc)
            .ThenBy(x => x.ProName)
            .Take(500)
            .Select(x => new ProPlayerDiscoveryCandidateDto(
                x.Id,
                x.Source,
                x.ExternalId,
                x.ProName,
                x.TeamName,
                x.Role,
                x.SoloQueueIds,
                x.Status,
                x.ApprovedTrackedProSummonerId,
                x.FirstSeenAtUtc,
                x.LastSeenAtUtc,
                x.ReviewedAtUtc))
            .ToListAsync(ct);
    }

    public async Task<TrackedProCreateResult> ApproveCandidateAsync(
        Guid id,
        ApproveProPlayerCandidateRequest request,
        CancellationToken ct = default)
    {
        var candidate = await db.ProPlayerDiscoveryCandidates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (candidate is null)
            return TrackedProCreateResult.Invalid("Discovery candidate was not found.");
        if (candidate.Status == "approved")
            return TrackedProCreateResult.Invalid("Discovery candidate is already approved.");

        var outcome = await CreateAsync(new UpsertTrackedProSummonerRequest(
            request.GameName,
            request.TagLine,
            request.PlatformRegion,
            request.Puuid,
            candidate.ProName,
            candidate.TeamName,
            IsPro: true,
            IsHighEloOtp: false,
            IsActive: true), ct);
        if (!outcome.IsSuccess)
            return outcome;

        var created = await db.TrackedProSummoners
            .FirstAsync(x => x.Id == outcome.Value!.Id, ct);
        created.Source = candidate.Source;
        created.SourceExternalId = candidate.ExternalId;
        created.LastVerifiedAtUtc = DateTime.UtcNow;
        candidate.Status = "approved";
        candidate.ApprovedTrackedProSummonerId = created.Id;
        candidate.ReviewedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return TrackedProCreateResult.Success(ToDto(created));
    }

    public async Task<bool> RejectCandidateAsync(Guid id, CancellationToken ct = default)
    {
        var candidate = await db.ProPlayerDiscoveryCandidates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (candidate is null)
            return false;

        candidate.Status = "rejected";
        candidate.ReviewedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static TrackedProSummonerDto ToDto(TrackedProSummoner entity) => new(
        entity.Id,
        entity.Puuid,
        entity.PlatformRegion,
        entity.GameName,
        entity.TagLine,
        entity.ProName,
        entity.TeamName,
        entity.IsPro,
        entity.IsHighEloOtp,
        entity.IsActive,
        entity.Source,
        entity.SourceExternalId,
        entity.LastVerifiedAtUtc,
        entity.OtpChampionId,
        entity.OtpGames,
        entity.OtpSampleSize,
        entity.OtpEvaluatedAtUtc,
        entity.CreatedAtUtc,
        entity.UpdatedAtUtc);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeCandidateStatus(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "approved" => "approved",
            "rejected" => "rejected",
            _ => "pending"
        };
}
