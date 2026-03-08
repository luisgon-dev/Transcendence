using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Tft.Interfaces;
using Transcendence.Service.Core.Services.Tft.Models;

namespace Transcendence.Service.Core.Services.Tft.Implementations;

public class TftSummonerReadService(
    TranscendenceContext context,
    ITftSummonerRepository summonerRepository) : ITftSummonerReadService
{
    public async Task<IReadOnlyList<(string PlatformRegion, string Region, string GameName, string TagLine, int ProfileIconId)>> SearchAsync(
        string region,
        string query,
        int limit,
        CancellationToken ct = default)
    {
        var (gameNamePrefix, tagLinePrefix) = ParseQuery(query);
        var candidates = await summonerRepository.SearchByPrefixAsync(region, gameNamePrefix, tagLinePrefix, limit, ct);
        return candidates
            .Select(candidate => (
                candidate.PlatformRegion,
                ToRegionSlug(candidate.PlatformRegion),
                candidate.GameName,
                candidate.TagLine,
                candidate.ProfileIconId))
            .ToList();
    }

    public async Task<TftSummonerProfileDto?> GetProfileByRiotIdAsync(string platformRegion, string gameName, string tagLine,
        CancellationToken ct = default)
    {
        var summoner = await summonerRepository.FindByRiotIdAsync(platformRegion, gameName, tagLine, q => q.Include(x => x.Ranks), ct);
        if (summoner == null || string.IsNullOrWhiteSpace(summoner.Puuid))
            return null;

        var recent = await GetRecentMatchesAsync(summoner.Id, 1, 10, ct);
        return new TftSummonerProfileDto(
            summoner.Id,
            summoner.Puuid,
            summoner.GameName ?? string.Empty,
            summoner.TagLine ?? string.Empty,
            summoner.ProfileIconId,
            summoner.SummonerLevel,
            summoner.PlatformRegion,
            summoner.Region,
            summoner.UpdatedAt,
            summoner.Ranks.OrderByDescending(x => x.UpdatedAt).Select(x => new TftRankDto(
                x.QueueType,
                x.Tier,
                x.RankNumber,
                x.LeaguePoints,
                x.Wins,
                x.Losses,
                x.UpdatedAt)).ToList(),
            recent.Items);
    }

    public async Task<TftPagedMatchesDto> GetRecentMatchesAsync(Guid summonerId, int page, int pageSize, CancellationToken ct = default)
    {
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, 50);

        var query = context.TftMatchParticipants
            .AsNoTracking()
            .Where(x => x.SummonerId == summonerId)
            .Include(x => x.Match)
            .Include(x => x.Units)
            .Include(x => x.Traits);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.Match.MatchDate)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(ct);

        return new TftPagedMatchesDto(
            items.Select(MapSummary).ToList(),
            safePage,
            safePageSize,
            totalCount,
            Math.Max(1, (int)Math.Ceiling(totalCount / (double)safePageSize)));
    }

    public async Task<TftMatchDetailDto?> GetMatchDetailAsync(Guid summonerId, string matchId, CancellationToken ct = default)
    {
        var match = await context.TftMatches
            .AsNoTracking()
            .Include(x => x.Participants)
            .ThenInclude(x => x.Units)
            .Include(x => x.Participants)
            .ThenInclude(x => x.Traits)
            .FirstOrDefaultAsync(x => x.MatchId == matchId && x.Participants.Any(p => p.SummonerId == summonerId), ct);

        if (match == null)
            return null;

        return new TftMatchDetailDto(
            match.MatchId,
            match.MatchDate,
            match.DurationSeconds,
            match.Patch,
            match.SetNumber,
            match.SetCoreName,
            match.PlatformRegion,
            match.Participants
                .OrderBy(x => x.Placement)
                .Select(participant => new TftMatchParticipantDetailDto(
                    participant.Puuid,
                    participant.RiotIdGameName,
                    participant.RiotIdTagLine,
                    participant.Placement,
                    participant.Level,
                    participant.LastRound,
                    participant.PlayersEliminated,
                    participant.GoldLeft,
                    participant.TotalDamageToPlayers,
                    participant.TimeEliminatedSeconds,
                    participant.Win,
                    participant.Augments,
                    participant.Units.Select(unit => new TftUnitSummaryDto(unit.CharacterId, unit.Name, unit.Rarity, unit.Tier, unit.Items)).ToList(),
                    participant.Traits.Select(trait => new TftTraitSummaryDto(trait.Name, trait.NumUnits, trait.TierCurrent, trait.Style)).ToList()))
                .ToList());
    }

    private static TftRecentMatchSummaryDto MapSummary(Data.Models.Tft.Match.TftMatchParticipant participant)
    {
        return new TftRecentMatchSummaryDto(
            participant.Match.MatchId,
            participant.Match.MatchDate,
            participant.Placement,
            participant.Level,
            participant.LastRound,
            participant.PlayersEliminated,
            participant.TotalDamageToPlayers,
            participant.Match.SetNumber,
            participant.Match.SetCoreName,
            participant.Match.Patch,
            participant.Augments,
            participant.Units.Select(unit => new TftUnitSummaryDto(unit.CharacterId, unit.Name, unit.Rarity, unit.Tier, unit.Items)).ToList(),
            participant.Traits.Select(trait => new TftTraitSummaryDto(trait.Name, trait.NumUnits, trait.TierCurrent, trait.Style)).ToList());
    }

    private static (string GameNamePrefix, string? TagLinePrefix) ParseQuery(string query)
    {
        var trimmed = query.Trim();
        var parts = trimmed.Split('#', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : (trimmed, null);
    }

    private static string ToRegionSlug(string platformRegion)
    {
        return platformRegion.Trim().ToUpperInvariant() switch
        {
            "NA1" => "na",
            "EUW1" => "euw",
            "EUN1" => "eune",
            "KR" => "kr",
            "BR1" => "br",
            "LA1" => "lan",
            "LA2" => "las",
            "OC1" => "oce",
            "JP1" => "jp",
            "TR1" => "tr",
            "RU" => "ru",
            _ => platformRegion.Trim().ToLowerInvariant()
        };
    }
}
