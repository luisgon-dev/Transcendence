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
        entity.CreatedAtUtc,
        entity.UpdatedAtUtc);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
