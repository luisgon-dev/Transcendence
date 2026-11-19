using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Analytics;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/lol/analytics/champions")]
[EnableRateLimiting("expensive-read")]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public class ChampionAnalyticsController(
    IChampionAnalyticsService analyticsService,
    IServiceScopeFactory? serviceScopeFactory,
    IAnalyticsPublicationService publicationService,
    IChampionSynergyService? synergyService = null,
    IBuildLabService? buildLabService = null,
    ILogger<ChampionAnalyticsController>? logger = null,
    TimeSpan? optionalSectionWaitBudget = null,
    ChampionScopedSectionService? scopedSections = null) : ControllerBase
{
    private readonly TimeSpan _optionalSectionWaitBudget = optionalSectionWaitBudget ?? TimeSpan.FromSeconds(2);

    private static readonly HashSet<string> ValidRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "TOP",
        "JUNGLE",
        "MIDDLE",
        "BOTTOM",
        "UTILITY"
    };

    /// <summary>
    /// Get the champion detail page payload in one request.
    /// Reuses the cached winrate/build/matchup aggregates and parallelizes the role-scoped reads.
    /// </summary>
    [HttpGet("{championId}/profile")]
    [ProducesResponseType(typeof(ChampionProfileAnalyticsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionProfileAnalyticsResponse>> GetProfile(
        [FromRoute] int championId,
        [FromQuery] string? role = null,
        [FromQuery] string? rankTier = null,
        [FromQuery] string? region = null,
        [FromQuery] string? queue = null,
        [FromQuery] string? patch = null,
        CancellationToken ct = default)
    {
        if (championId <= 0)
            return BadRequest("Invalid champion ID. Must be positive integer.");

        var normalizedRole = NormalizeRole(role);
        if (!string.IsNullOrWhiteSpace(role) && normalizedRole == null)
            return BadRequest("Invalid role. Expected TOP, JUNGLE, MIDDLE, BOTTOM, or UTILITY.");
        if (!AnalyticsQueueCatalog.IsSupported(queue))
            return BadRequest("Invalid queue. Expected solo, aram, arena, or flex.");

        var normalizedQueue = AnalyticsQueueCatalog.Normalize(queue);
        var hasRoles = AnalyticsQueueCatalog.HasRoles(normalizedQueue);
        var publication = await publicationService.ResolveAsync(normalizedQueue, patch, ct);
        var selection = publication.Selection;
        if (selection.EffectivePatch == null)
            return Ok(EmptyProfile(championId, hasRoles ? normalizedRole ?? "MIDDLE" : AnalyticsQueueCatalog.AllRoles,
                rankTier, region, normalizedQueue, selection));

        patch = selection.EffectivePatch;
        var filter = new ChampionAnalyticsFilter(rankTier, region, normalizedRole, patch, normalizedQueue);
        var winRates = await analyticsService.GetPublishedWinRatesAsync(championId, filter, publication, ct);

        var primaryRole = hasRoles ? normalizedRole ?? ChampionRoleResolver.PickMostPlayed(winRates.ByRoleTier) : null;
        var rank = rankTier?.Trim().ToUpperInvariant().Replace("+", "_PLUS");
        if (hasRoles && primaryRole == null && rank != null && rank != RankTierCatalog.AllScope &&
            RankTierCatalog.RankScopeTokens.Contains(rank))
        {
            var allRanks = await analyticsService.GetPublishedWinRatesAsync(
                championId, filter with { RankTier = null }, publication, ct);
            primaryRole = ChampionRoleResolver.PickMostPlayed(allRanks.ByRoleTier);
        }

        var effectiveRole = hasRoles ? primaryRole ?? "MIDDLE" : AnalyticsQueueCatalog.AllRoles;
        ChampionGradeDto? grade;
        ChampionTrendResponse trend;
        ChampionSynergiesResponse? synergies;
        ChampionRecommendationSummary? recommendation;
        if (serviceScopeFactory == null)
        {
            grade = await analyticsService.GetPublishedGradeAsync(
                championId, effectiveRole, rankTier, region, normalizedQueue, publication, ct);
            trend = await analyticsService.GetPublishedTrendAsync(
                championId, effectiveRole, rankTier, region, normalizedQueue, publication, ct);
            synergies = synergyService == null
                ? null
                : await synergyService.ReadPublishedSynergiesAsync(
                    championId, effectiveRole, rankTier, region, normalizedQueue, patch, ct);
            recommendation = buildLabService == null ||
                             normalizedQueue != QueueCatalog.QueueFamilyRankedSoloDuo
                ? null
                : await buildLabService.GetChampionRecommendationAsync(
                    championId, effectiveRole, null, patch, region, ct);
        }
        else
        {
            var fanOutStarted = Stopwatch.GetTimestamp();
            var gradeTask = RunInAnalyticsScopeAsync(
                scoped => scoped.GetPublishedGradeAsync(
                    championId, effectiveRole, rankTier, region, normalizedQueue, publication, ct));
            var trendTask = RunInAnalyticsScopeAsync(
                scoped => scoped.GetPublishedTrendAsync(championId, effectiveRole, rankTier, region, normalizedQueue, publication, ct));
            var synergiesTask = RunInSynergyScopeAsync(
                scoped => scoped.ReadPublishedSynergiesAsync(
                    championId, effectiveRole, rankTier, region, normalizedQueue, patch, ct));
            var recommendationTask = normalizedQueue == QueueCatalog.QueueFamilyRankedSoloDuo
                ? RunInBuildLabScopeAsync(scoped => scoped.GetChampionRecommendationAsync(
                    championId, effectiveRole, null, patch, region, ct))
                : Task.FromResult<ChampionRecommendationSummary?>(null);

            await Task.WhenAll(gradeTask, trendTask);
            grade = await gradeTask;
            trend = await trendTask;
            recommendation = await WaitForOptionalSectionAsync(recommendationTask, "Build Lab", fanOutStarted, championId, ct);
            synergies = await WaitForOptionalSectionAsync(synergiesTask, "synergies", fanOutStarted, championId, ct);
        }

        var scopedRoleGames = winRates.ByRoleTier.Where(entry => entry.Role == effectiveRole).Sum(entry => entry.Games);
        var sections = scopedSections == null ? null : await scopedSections.GetAsync(
            new ChampionSectionScope(patch, normalizedQueue, region, rankTier, effectiveRole, championId), scopedRoleGames, ct);
        var profile = EmptyProfile(championId, effectiveRole, rankTier, region, normalizedQueue, selection);
        return Ok(profile with
        {
            WinRates = winRates,
            Grade = grade,
            Trend = trend,
            Builds = sections?.Builds.Data ?? profile.Builds,
            Matchups = sections?.Matchups.Data ?? profile.Matchups,
            BuildsState = sections?.Builds.State ?? ChampionSectionState.Unavailable,
            BuildsComputedAtUtc = sections?.Builds.ComputedAtUtc,
            MatchupsState = sections?.Matchups.State ?? ChampionSectionState.Unavailable,
            MatchupsComputedAtUtc = sections?.Matchups.ComputedAtUtc,
            Synergies = synergies,
            Recommendation = recommendation
        });
    }

    private static ChampionProfileAnalyticsResponse EmptyProfile(
        int championId, string role, string? rankTier, string? region, string queue, AnalyticsPatchSelection selection)
    {
        var patch = selection.EffectivePatch ?? selection.RequestedPatch ?? selection.ActivePatch ?? "Unknown";
        var rank = string.IsNullOrWhiteSpace(rankTier) ? RankTierCatalog.AllScope : rankTier.Trim().ToUpperInvariant().Replace("+", "_PLUS");
        var regionCode = AnalyticsRegionCatalog.NormalizeOrDefault(region);
        return new ChampionProfileAnalyticsResponse(
            championId,
            role,
            new ChampionWinRateSummary(championId, patch, [], QueueFamily: queue),
            new ChampionBuildsResponse(championId, role, rank == RankTierCatalog.AllScope ? "all" : rank, regionCode, patch, [], [], QueueFamily: queue),
            new ChampionMatchupsResponse
            {
                ChampionId = championId, Role = role, RankTier = rank == RankTierCatalog.AllScope ? null : rank,
                Region = regionCode, Patch = patch, QueueFamily = queue
            },
            selection,
            QueueFamily: queue,
            Trend: new ChampionTrendResponse(championId, queue, role, rank, regionCode, []),
            BuildsState: selection.State == AnalyticsPatchSelectionState.Preparing
                ? ChampionSectionState.Preparing : ChampionSectionState.Unavailable,
            MatchupsState: selection.State == AnalyticsPatchSelectionState.Preparing && AnalyticsQueueCatalog.HasRoles(queue)
                ? ChampionSectionState.Preparing : ChampionSectionState.Unavailable);
    }

    [HttpGet("{championId}/synergies")]
    [ProducesResponseType(typeof(ChampionSynergiesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ChampionSynergiesResponse>> GetSynergies(
        int championId,
        [FromQuery] string role,
        [FromQuery] string? rankTier = null,
        [FromQuery] string? region = null,
        [FromQuery] string? queue = null,
        [FromQuery] string? patch = null,
        CancellationToken ct = default)
    {
        if (championId <= 0)
            return BadRequest("Invalid champion ID. Must be positive integer.");
        var normalizedRole = NormalizeRole(role);
        if (normalizedRole == null)
            return BadRequest("Invalid role. Expected TOP, JUNGLE, MIDDLE, BOTTOM, or UTILITY.");
        if (!AnalyticsQueueCatalog.IsSupported(queue))
            return BadRequest("Invalid queue. Expected solo, aram, arena, or flex.");
        if (synergyService == null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable);

        return Ok(await synergyService.GetSynergiesAsync(
            championId, normalizedRole, rankTier, region, queue, patch, ct));
    }

    /// <summary>
    /// Get champion win rates by role and rank tier.
    /// Uses adaptive sample thresholds to remain useful during early patch windows.
    /// Data is cached for 24 hours.
    /// </summary>
    /// <param name="championId">Champion ID (e.g., 1 for Annie)</param>
    /// <param name="rankTier">Optional rank tier filter (ALL, EMERALD_PLUS, IRON, BRONZE, SILVER, GOLD, PLATINUM, EMERALD, DIAMOND, MASTER, GRANDMASTER, CHALLENGER)</param>
    /// <param name="region">Optional region filter (e.g., NA1, EUW1)</param>
    /// <param name="role">Optional role filter (TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY)</param>
    /// <param name="patch">Optional patch version. Defaults to the active analytics patch.</param>
    /// <param name="ct">Cancellation token</param>
    [HttpGet("{championId}/winrates")]
    [ProducesResponseType(typeof(ChampionWinRateSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetWinRates(
        [FromRoute] int championId,
        [FromQuery] string? rankTier = null,
        [FromQuery] string? region = null,
        [FromQuery] string? role = null,
        [FromQuery] string? patch = null,
        [FromQuery] string? queue = null,
        CancellationToken ct = default)
    {
        if (championId <= 0)
            return BadRequest("Invalid champion ID. Must be positive integer.");
        if (!AnalyticsQueueCatalog.IsSupported(queue))
            return BadRequest("Invalid queue. Expected solo, aram, arena, or flex.");

        var filter = new ChampionAnalyticsFilter(
            RankTier: rankTier,
            Region: region,
            Role: role,
            Patch: patch,
            QueueFamily: queue
        );

        var summary = await analyticsService.GetWinRatesAsync(championId, filter, ct);
        return Ok(summary);
    }

    /// <summary>
    /// Get top 3 builds for a champion in a role.
    /// Builds include items and runes bundled together.
    /// Core items (70%+ appearance) are distinguished from situational.
    /// </summary>
    /// <param name="championId">Champion ID</param>
    /// <param name="role">Role: TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY</param>
    /// <param name="rankTier">Optional: Filter by rank tier</param>
    /// <param name="region">Optional: Filter by platform region</param>
    /// <param name="patch">Optional patch version. Defaults to the active analytics patch.</param>
    [HttpGet("{championId}/builds")]
    [ProducesResponseType(typeof(ChampionBuildsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionBuildsResponse>> GetBuilds(
        int championId,
        [FromQuery] string role,
        [FromQuery] string? rankTier = null,
        [FromQuery] string? region = null,
        [FromQuery] string? queue = null,
        [FromQuery] string? patch = null,
        CancellationToken ct = default)
    {
        if (!AnalyticsQueueCatalog.IsSupported(queue))
            return BadRequest("Invalid queue. Expected solo, aram, arena, or flex.");

        var normalizedQueue = AnalyticsQueueCatalog.Normalize(queue);
        if (AnalyticsQueueCatalog.HasRoles(normalizedQueue) && string.IsNullOrEmpty(role))
            return BadRequest("Role parameter is required");

        var result = await analyticsService.GetBuildsAsync(
            championId, role, rankTier, region, normalizedQueue, patch, ct);
        return Ok(result);
    }

    /// <summary>
    /// Get pro/high-ELO builds for a champion.
    /// Region defaults to ALL. Scope and patch are optional. When no role is supplied
    /// (or role=ALL), the champion's most-played lane is used so the landing view is
    /// lane-scoped instead of the heavy cross-role aggregate (mirrors the profile endpoint).
    /// </summary>
    [HttpGet("{championId}/pro-builds")]
    [ProducesResponseType(typeof(ChampionProBuildsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionProBuildsResponse>> GetProBuilds(
        int championId,
        [FromQuery] string? region = null,
        [FromQuery] string? role = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? patch = null,
        CancellationToken ct = default)
    {
        if (championId <= 0)
            return BadRequest("Invalid champion ID. Must be positive integer.");

        // Treat an absent or explicit "ALL" role as "use the champion's most-played lane"
        // (mirrors GetProfile); reject any other unrecognized role.
        var requestedAll = string.Equals(role?.Trim(), "ALL", StringComparison.OrdinalIgnoreCase);
        var normalizedRole = requestedAll ? null : NormalizeRole(role);
        if (!requestedAll && !string.IsNullOrWhiteSpace(role) && normalizedRole == null)
            return BadRequest("Invalid role. Expected TOP, JUNGLE, MIDDLE, BOTTOM, or UTILITY.");

        var effectiveRole = normalizedRole;
        if (effectiveRole == null)
        {
            // Reuses the cached win-rate aggregate; null result falls through to the
            // (now-bounded) cross-role aggregate for champions with no role data.
            var winRates = await analyticsService.GetWinRatesAsync(
                championId,
                new ChampionAnalyticsFilter(Region: region, Patch: patch),
                ct);
            effectiveRole = ChampionRoleResolver.PickMostPlayed(winRates.ByRoleTier);
        }

        var result = await analyticsService.GetProBuildsAsync(championId, region, effectiveRole, scope, patch, ct);
        return Ok(result);
    }

    /// <summary>
    /// Get matchup data (counters and favorable matchups) for a champion in a role.
    /// Matchups are lane-specific (e.g., Mid vs Mid).
    /// </summary>
    /// <param name="championId">Champion ID</param>
    /// <param name="role">Role: TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY</param>
    /// <param name="rankTier">Optional: Filter by rank tier</param>
    /// <param name="region">Optional: Filter by platform region</param>
    /// <param name="patch">Optional patch version. Defaults to the active analytics patch.</param>
    [HttpGet("{championId}/matchups")]
    [ProducesResponseType(typeof(ChampionMatchupsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionMatchupsResponse>> GetMatchups(
        int championId,
        [FromQuery] string role,
        [FromQuery] string? rankTier = null,
        [FromQuery] string? region = null,
        [FromQuery] string? queue = null,
        [FromQuery] string? patch = null,
        CancellationToken ct = default)
    {
        if (!AnalyticsQueueCatalog.IsSupported(queue))
            return BadRequest("Invalid queue. Expected solo, aram, arena, or flex.");

        var normalizedQueue = AnalyticsQueueCatalog.Normalize(queue);
        if (AnalyticsQueueCatalog.HasRoles(normalizedQueue) && string.IsNullOrEmpty(role))
            return BadRequest("Role parameter is required");

        var result = await analyticsService.GetMatchupsAsync(
            championId, role, rankTier, region, normalizedQueue, patch, ct);
        return Ok(result);
    }

    private async Task<T> RunInAnalyticsScopeAsync<T>(Func<IChampionAnalyticsService, Task<T>> action)
    {
        using var scope = serviceScopeFactory!.CreateScope();
        var scopedAnalyticsService = scope.ServiceProvider.GetRequiredService<IChampionAnalyticsService>();
        return await action(scopedAnalyticsService);
    }

    private async Task<T?> WaitForOptionalSectionAsync<T>(
        Task<T?> sectionTask,
        string section,
        long fanOutStarted,
        int championId,
        CancellationToken ct) where T : class
    {
        var remaining = _optionalSectionWaitBudget - Stopwatch.GetElapsedTime(fanOutStarted);
        if (!sectionTask.IsCompleted && remaining > TimeSpan.Zero)
            await Task.WhenAny(sectionTask, Task.Delay(remaining, ct));

        if (sectionTask.IsCompleted)
            return await sectionTask;

        _ = sectionTask.ContinueWith(
            task => logger?.LogWarning(task.Exception,
                "Optional {Section} lookup failed for champion {ChampionId}.", section, championId),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return null;
    }

    private async Task<T> RunInSynergyScopeAsync<T>(Func<IChampionSynergyService, Task<T>> action)
    {
        using var scope = serviceScopeFactory!.CreateScope();
        var scopedSynergyService = scope.ServiceProvider.GetRequiredService<IChampionSynergyService>();
        return await action(scopedSynergyService);
    }

    private async Task<T?> RunInBuildLabScopeAsync<T>(Func<IBuildLabService, Task<T>> action)
    {
        using var scope = serviceScopeFactory!.CreateScope();
        var scopedBuildLabService = scope.ServiceProvider.GetService<IBuildLabService>();
        return scopedBuildLabService == null ? default : await action(scopedBuildLabService);
    }

    private static string? NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;

        var normalized = role.Trim().ToUpperInvariant();
        return ValidRoles.Contains(normalized) ? normalized : null;
    }

}
