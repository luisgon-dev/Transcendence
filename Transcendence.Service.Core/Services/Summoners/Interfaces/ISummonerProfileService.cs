using Transcendence.Service.Core.Services.RiotApi.DTOs;

namespace Transcendence.Service.Core.Services.Summoners.Interfaces;

public interface ISummonerProfileService
{
    Task<IReadOnlyList<SummonerSearchCandidateDto>> SearchByPrefixAsync(
        string platformRegion,
        string gameNamePrefix,
        string? tagLinePrefix,
        int limit,
        CancellationToken ct = default);

    Task<SummonerProfileResponse?> GetProfileByRiotIdAsync(
        string platformRegion,
        string gameName,
        string tagLine,
        CancellationToken ct = default);
}

public sealed record SummonerSearchCandidateDto(
    string PlatformRegion,
    string GameName,
    string TagLine,
    int ProfileIconId);
