namespace Transcendence.Service.Core.Services.RiotApi.DTOs;

public class SummonerSearchResponse
{
    public IReadOnlyList<SummonerSearchItem> Items { get; init; } = [];
}

public class SummonerSearchItem
{
    public required string PlatformRegion { get; init; }
    public required string Region { get; init; }
    public required string GameName { get; init; }
    public required string TagLine { get; init; }
    public int ProfileIconId { get; init; }
}
