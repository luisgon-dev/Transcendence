using System.ComponentModel.DataAnnotations;

// These contracts intentionally retain the controller namespace so their public OpenAPI schema
// names and all existing consumers remain unchanged after moving them into Service.Core.
namespace Transcendence.WebAPI.Controllers;

public record UpsertTrackedProSummonerRequest(
    [property: Required] string GameName,
    [property: Required] string TagLine,
    [property: Required] string PlatformRegion,
    string? Puuid = null,
    string? ProName = null,
    string? TeamName = null,
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
