namespace Transcendence.Service.Core.Services.Auth.Models;

public record FavoriteSummonerDto(
    Guid Id,
    string SummonerPuuid,
    string PlatformRegion,
    string? DisplayName,
    DateTime CreatedAtUtc
);

public record AddFavoriteRequest(
    string Region,
    string GameName,
    string TagLine
);

public record UserPreferencesDto(
    string? PreferredRegion,
    string? PreferredRankTier,
    bool LivePollingEnabled,
    DateTime UpdatedAtUtc
);

public record UpdateUserPreferencesRequest(
    string? PreferredRegion,
    string? PreferredRankTier,
    bool LivePollingEnabled
);
