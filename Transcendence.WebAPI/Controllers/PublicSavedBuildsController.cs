using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/lol/saved-builds")]
// Anonymous share-token lookup: metered per client IP so the token space cannot be brute-forced.
// The partition is the forwarded client address (see BuildIpReadPartition, which exempts internal
// source addresses), so both real request paths have to carry one: a direct hit arrives through
// nginx, and the shared-build page forwards the identity its edge vouched for on its server-side
// read rather than reaching this endpoint as an exempt internal caller.
[EnableRateLimiting("expensive-read")]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public sealed class PublicSavedBuildsController(ISavedBuildService service) : ControllerBase
{
    [HttpGet("{shareId:guid}")]
    [ProducesResponseType(typeof(PublicSavedBuildDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid shareId, CancellationToken ct)
    {
        var savedBuild = await service.GetSharedAsync(shareId, ct);
        return savedBuild == null
            ? NotFound()
            : Ok(new PublicSavedBuildDto(
                savedBuild.Name,
                savedBuild.ChampionId,
                savedBuild.Role,
                savedBuild.OpponentChampionId,
                savedBuild.Patch,
                savedBuild.Region,
                savedBuild.RankingMode,
                savedBuild.ItemPath,
                savedBuild.RuneSelections,
                savedBuild.Spell1Id,
                savedBuild.Spell2Id,
                savedBuild.SourceGenerationId,
                savedBuild.CurrentGenerationId,
                savedBuild.AnalyticsChanged,
                savedBuild.CompatibilityStatus,
                savedBuild.UnavailableItemIds,
                savedBuild.UnavailableItems,
                savedBuild.UpdatedAtUtc));
    }
}
