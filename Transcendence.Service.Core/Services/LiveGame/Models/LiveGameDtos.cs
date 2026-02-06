namespace Transcendence.Service.Core.Services.LiveGame.Models;

public record LiveGameParticipantDto(
    string Puuid,
    string? RiotId,
    string? SummonerId,
    int TeamId,
    int ChampionId,
    int Spell1Id,
    int Spell2Id,
    int ProfileIconId
);

public record LiveGameResponseDto(
    string State,
    string PlatformRegion,
    string? GameId,
    string? QueueType,
    string? Map,
    DateTime? GameStartTimeUtc,
    long? GameLengthSeconds,
    List<LiveGameParticipantDto> Participants,
    DateTime LastUpdatedUtc,
    int DataAgeSeconds,
    LiveGameAnalysisDto? Analysis = null
);
