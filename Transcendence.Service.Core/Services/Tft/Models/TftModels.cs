namespace Transcendence.Service.Core.Services.Tft.Models;

public record TftRankDto(
    string QueueType,
    string Tier,
    string RankNumber,
    int LeaguePoints,
    int Wins,
    int Losses,
    DateTime UpdatedAtUtc
);

public record TftRecentMatchSummaryDto(
    string MatchId,
    long MatchDate,
    int Placement,
    int Level,
    int LastRound,
    int PlayersEliminated,
    int TotalDamageToPlayers,
    int? SetNumber,
    string? SetCoreName,
    string? Patch,
    IReadOnlyList<string> Augments,
    IReadOnlyList<TftUnitSummaryDto> Units,
    IReadOnlyList<TftTraitSummaryDto> Traits
);

public record TftUnitSummaryDto(
    string CharacterId,
    string? Name,
    int Rarity,
    int Tier,
    IReadOnlyList<int> Items
);

public record TftTraitSummaryDto(
    string Name,
    int NumUnits,
    int TierCurrent,
    int? Style
);

public record TftSummonerProfileDto(
    Guid SummonerId,
    string Puuid,
    string GameName,
    string TagLine,
    int ProfileIconId,
    long SummonerLevel,
    string PlatformRegion,
    string Region,
    DateTime UpdatedAtUtc,
    IReadOnlyList<TftRankDto> Ranks,
    IReadOnlyList<TftRecentMatchSummaryDto> RecentMatches
);

public record TftPagedMatchesDto(
    IReadOnlyList<TftRecentMatchSummaryDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages
);

public record TftMatchParticipantDetailDto(
    string Puuid,
    string? RiotIdGameName,
    string? RiotIdTagLine,
    int Placement,
    int Level,
    int LastRound,
    int PlayersEliminated,
    int GoldLeft,
    int TotalDamageToPlayers,
    float TimeEliminatedSeconds,
    bool? Win,
    IReadOnlyList<string> Augments,
    IReadOnlyList<TftUnitSummaryDto> Units,
    IReadOnlyList<TftTraitSummaryDto> Traits
);

public record TftMatchDetailDto(
    string MatchId,
    long MatchDate,
    int DurationSeconds,
    string? Patch,
    int? SetNumber,
    string? SetCoreName,
    string PlatformRegion,
    IReadOnlyList<TftMatchParticipantDetailDto> Participants
);

public record TftStaticEntityDto(
    string ApiName,
    string Name,
    string? Description,
    string? Icon
);

public record TftCompListItemDto(
    string CompSlug,
    string Name,
    int? SetNumber,
    string? SetCoreName,
    string? Patch,
    string Region,
    string RankTier,
    double AvgPlacement,
    double Top4Rate,
    double WinRate,
    double PickRate,
    int SampleSize,
    string Trend,
    IReadOnlyList<TftUnitSummaryDto> Units,
    IReadOnlyList<TftTraitSummaryDto> Traits,
    IReadOnlyList<string> Augments
);

public record TftCompDetailDto(
    TftCompListItemDto Summary,
    IReadOnlyList<TftStaticEntityDto> CoreItems,
    IReadOnlyList<TftStaticEntityDto> CoreAugments
);
