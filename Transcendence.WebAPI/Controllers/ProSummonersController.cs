using System.Security.Claims;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/admin/pro-summoners")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public class ProSummonersController(
    TranscendenceContext db,
    IAdminAuditService adminAuditService,
    IBackgroundJobClient backgroundJobClient,
    IRefreshLockRepository refreshLockRepository) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(List<TrackedProSummonerDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool? isActive = null, CancellationToken ct = default)
    {
        var query = db.TrackedProSummoners.AsNoTracking();
        if (isActive.HasValue)
            query = query.Where(x => x.IsActive == isActive.Value);

        var rows = await query
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

        return Ok(rows);
    }

    [HttpPost]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(typeof(TrackedProSummonerDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] UpsertTrackedProSummonerRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Puuid) || string.IsNullOrWhiteSpace(request.PlatformRegion))
            return BadRequest("Puuid and platformRegion are required.");

        var normalizedPuuid = request.Puuid.Trim();
        var normalizedPlatform = request.PlatformRegion.Trim().ToUpperInvariant();

        var existing = await db.TrackedProSummoners
            .FirstOrDefaultAsync(x => x.Puuid == normalizedPuuid && x.PlatformRegion == normalizedPlatform, ct);
        if (existing != null)
            return BadRequest("Tracked pro summoner already exists for this puuid/platform.");

        var now = DateTime.UtcNow;
        var entity = new TrackedProSummoner
        {
            Id = Guid.NewGuid(),
            Puuid = normalizedPuuid,
            PlatformRegion = normalizedPlatform,
            GameName = NormalizeOptional(request.GameName),
            TagLine = NormalizeOptional(request.TagLine),
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
        await WriteAuditAsync("pro-summoners.create", entity.Id.ToString(), new
        {
            entity.Puuid,
            entity.PlatformRegion,
            entity.ProName,
            entity.TeamName,
            entity.IsActive
        }, ct);

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToDto(entity));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TrackedProSummonerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct = default)
    {
        var entity = await db.TrackedProSummoners.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return entity == null ? NotFound() : Ok(ToDto(entity));
    }

    [HttpPut("{id:guid}")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(typeof(TrackedProSummonerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpsertTrackedProSummonerRequest request,
        CancellationToken ct = default)
    {
        var entity = await db.TrackedProSummoners.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity == null)
            return NotFound();

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
        await WriteAuditAsync("pro-summoners.update", entity.Id.ToString(), new
        {
            entity.Puuid,
            entity.PlatformRegion,
            entity.ProName,
            entity.TeamName,
            entity.IsActive
        }, ct);
        return Ok(ToDto(entity));
    }

    [HttpDelete("{id:guid}")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct = default)
    {
        var entity = await db.TrackedProSummoners.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity == null)
            return NotFound();

        db.TrackedProSummoners.Remove(entity);
        await db.SaveChangesAsync(ct);
        await WriteAuditAsync("pro-summoners.delete", id.ToString(), null, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/refresh")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Refresh([FromRoute] Guid id, CancellationToken ct = default)
    {
        var entity = await db.TrackedProSummoners.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity == null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(entity.GameName) || string.IsNullOrWhiteSpace(entity.TagLine))
            return BadRequest("Cannot refresh: summoner has no gameName/tagLine.");

        if (!PlatformRouteParser.TryParse(entity.PlatformRegion, out var platform))
            return BadRequest($"Unsupported platform region '{entity.PlatformRegion}'.");

        var key = RefreshLockKeys.BuildSummonerRefreshKey(platform, entity.GameName, entity.TagLine);
        var priorityKey = RefreshLockKeys.BuildApiPriorityKey(platform, entity.GameName, entity.TagLine);
        var ttl = TimeSpan.FromMinutes(15);

        var acquired = await refreshLockRepository.TryAcquireAsync(key, ttl, ct);
        if (!acquired)
            return Accepted(new { message = "Refresh already in progress." });

        var priorityAcquired = await refreshLockRepository.TryAcquireAsync(priorityKey, ttl, ct);

        try
        {
            backgroundJobClient.Enqueue<ISummonerRefreshJob>(job =>
                job.RefreshByRiotId(entity.GameName, entity.TagLine, platform, key,
                    priorityAcquired ? priorityKey : null, CancellationToken.None));
        }
        catch
        {
            await refreshLockRepository.ReleaseAsync(key, ct);
            if (priorityAcquired)
                await refreshLockRepository.ReleaseAsync(priorityKey, ct);
            throw;
        }

        await WriteAuditAsync("pro-summoners.refresh", entity.Id.ToString(), new
        {
            entity.Puuid,
            entity.PlatformRegion,
            entity.GameName,
            entity.TagLine
        }, ct);

        return Accepted(new { message = "Refresh queued." });
    }

    private static TrackedProSummonerDto ToDto(TrackedProSummoner entity)
    {
        return new TrackedProSummonerDto(
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
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private async Task WriteAuditAsync(string action, string targetId, object? metadata, CancellationToken ct)
    {
        var actorIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid? actorId = Guid.TryParse(actorIdRaw, out var parsed) ? parsed : null;
        var actorEmail = User.FindFirstValue(ClaimTypes.Name);
        var requestId = Request.Headers["x-trn-request-id"].ToString();
        await adminAuditService.WriteAsync(new AdminAuditWriteRequest(
            actorId,
            actorEmail,
            action,
            "tracked-pro-summoner",
            targetId,
            string.IsNullOrWhiteSpace(requestId) ? null : requestId,
            true,
            metadata), ct);
    }
}

public record UpsertTrackedProSummonerRequest(
    string Puuid,
    string PlatformRegion,
    string? GameName,
    string? TagLine,
    string? ProName,
    string? TeamName,
    bool IsPro = true,
    bool IsHighEloOtp = false,
    bool IsActive = true
);

public record TrackedProSummonerDto(
    Guid Id,
    string Puuid,
    string PlatformRegion,
    string? GameName,
    string? TagLine,
    string? ProName,
    string? TeamName,
    bool IsPro,
    bool IsHighEloOtp,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc
);
