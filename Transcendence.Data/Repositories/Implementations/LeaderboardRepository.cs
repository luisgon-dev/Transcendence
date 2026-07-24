using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Repositories.Interfaces;

namespace Transcendence.Data.Repositories.Implementations;

public sealed class LeaderboardRepository(TranscendenceContext db) : ILeaderboardRepository
{
    public async Task<IReadOnlyList<RegionalLeaderboardRow>> GetRegionalAsync(
        string platformRegion,
        bool rankedFlex,
        int limit,
        CancellationToken ct = default)
    {
        var safeLimit = Math.Clamp(limit, 1, 100);
        var ranks = db.Ranks
            .AsNoTracking()
            .Where(rank =>
                rank.Summoner != null &&
                rank.Summoner.PlatformRegion == platformRegion &&
                rank.Summoner.GameName != null &&
                rank.Summoner.TagLine != null);

        ranks = rankedFlex
            ? ranks.Where(rank => rank.QueueType.StartsWith("RANKED_FLEX"))
            : ranks.Where(rank =>
                rank.QueueType == "RANKED_SOLO_5x5" ||
                rank.QueueType == "RANKED_SOLO_5X5" ||
                rank.QueueType == "RANKED_SOLO_5V5");

        var ordered = await ranks
            .OrderByDescending(rank =>
                rank.Tier == "CHALLENGER" ? 10 :
                rank.Tier == "GRANDMASTER" ? 9 :
                rank.Tier == "MASTER" ? 8 :
                rank.Tier == "DIAMOND" ? 7 :
                rank.Tier == "EMERALD" ? 6 :
                rank.Tier == "PLATINUM" ? 5 :
                rank.Tier == "GOLD" ? 4 :
                rank.Tier == "SILVER" ? 3 :
                rank.Tier == "BRONZE" ? 2 :
                rank.Tier == "IRON" ? 1 : 0)
            .ThenByDescending(rank => rank.LeaguePoints)
            .ThenByDescending(rank => rank.Wins)
            .ThenBy(rank => rank.Losses)
            // A small number of legacy rows use alternate queue aliases. Fetch enough ordered
            // candidates to de-duplicate those aliases without returning the same player twice.
            .Take(safeLimit * 3)
            .Select(rank => new RegionalLeaderboardRow(
                rank.SummonerId,
                rank.Summoner!.GameName!,
                rank.Summoner.TagLine!,
                rank.Summoner.ProfileIconId,
                rank.Tier,
                rank.RankNumber,
                rank.LeaguePoints,
                rank.Wins,
                rank.Losses,
                rank.UpdatedAt))
            .ToListAsync(ct);

        return ordered
            .GroupBy(row => row.SummonerId)
            .Select(group => group.First())
            .Take(safeLimit)
            .ToList();
    }

    public async Task<IReadOnlyList<ChampionLeaderboardRow>> GetChampionAsync(
        string platformRegion,
        int queueId,
        int championId,
        string? role,
        int minimumGames,
        int limit,
        CancellationToken ct = default)
    {
        var activeSeason = await db.RankedSeasons
            .AsNoTracking()
            .Where(season => season.IsActive)
            .OrderByDescending(season => season.StartUtc)
            .Select(season => new { season.StartUtc, season.EndUtc })
            .FirstOrDefaultAsync(ct);
        var startMs = activeSeason is null
            ? 0L
            : new DateTimeOffset(DateTime.SpecifyKind(activeSeason.StartUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        var endMs = activeSeason?.EndUtc is { } endUtc
            ? new DateTimeOffset(DateTime.SpecifyKind(endUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
            : long.MaxValue;
        var normalizedRole = string.IsNullOrWhiteSpace(role) ? null : role.Trim().ToUpperInvariant();
        var candidateLimit = Math.Clamp(limit, 1, 100) * 4;

        // Use one explicit participant/match/summoner join before grouping. Grouping directly over
        // navigation properties made EF translate Max(participant.Match.FetchedAt) into a correlated
        // subquery that re-joined the same three large tables for every aggregate row. Ignore the
        // global "not permanently unfetchable" filters here because match.Status == Success below is
        // stricter; otherwise the participant filter adds a second redundant Matches join.
        var participantQuery =
            from participant in db.MatchParticipants.IgnoreQueryFilters().AsNoTracking()
            join match in db.Matches.IgnoreQueryFilters().AsNoTracking() on participant.MatchId equals match.Id
            join summoner in db.Summoners.AsNoTracking() on participant.SummonerId equals summoner.Id
            where participant.ChampionId == championId &&
                  match.Status == FetchStatus.Success &&
                  match.MatchDate >= startMs &&
                  match.MatchDate <= endMs &&
                  (match.QueueId == queueId ||
                   (match.QueueId == 0 && match.QueueType == queueId.ToString())) &&
                  match.PlatformRegion == platformRegion &&
                  summoner.GameName != null &&
                  summoner.TagLine != null
            select new
            {
                Participant = participant,
                Match = match,
                Summoner = summoner
            };

        if (normalizedRole is not null)
            participantQuery = participantQuery.Where(row => row.Participant.TeamPosition == normalizedRole);

        var aggregates = await participantQuery
            .GroupBy(row => new
            {
                row.Participant.SummonerId,
                row.Summoner.GameName,
                row.Summoner.TagLine,
                row.Summoner.ProfileIconId
            })
            .Select(group => new
            {
                group.Key.SummonerId,
                GameName = group.Key.GameName!,
                TagLine = group.Key.TagLine!,
                group.Key.ProfileIconId,
                Games = group.Count(),
                Wins = group.Sum(row => row.Participant.Win ? 1 : 0),
                Kills = group.Sum(row => (long)row.Participant.Kills),
                Deaths = group.Sum(row => (long)row.Participant.Deaths),
                Assists = group.Sum(row => (long)row.Participant.Assists),
                UpdatedAtUtc = group.Max(row => row.Match.FetchedAt)
            })
            .Where(row => row.Games >= minimumGames)
            .OrderByDescending(row => row.Games)
            .ThenByDescending(row => row.Wins)
            .Take(candidateLimit)
            .ToListAsync(ct);

        if (aggregates.Count == 0)
            return [];

        var summonerIds = aggregates.Select(row => row.SummonerId).ToList();
        var rankQuery = db.Ranks
            .AsNoTracking()
            .Where(rank => summonerIds.Contains(rank.SummonerId));
        rankQuery = queueId == 440
            ? rankQuery.Where(rank => rank.QueueType.StartsWith("RANKED_FLEX"))
            : rankQuery.Where(rank =>
                rank.QueueType == "RANKED_SOLO_5x5" ||
                rank.QueueType == "RANKED_SOLO_5X5" ||
                rank.QueueType == "RANKED_SOLO_5V5");
        var rankRows = await rankQuery.ToListAsync(ct);
        var ranks = rankRows
            .GroupBy(rank => rank.SummonerId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(rank => rank.UpdatedAt).First());

        return aggregates.Select(row =>
            {
                ranks.TryGetValue(row.SummonerId, out var rank);
                return new ChampionLeaderboardRow(
                    row.SummonerId,
                    row.GameName,
                    row.TagLine,
                    row.ProfileIconId,
                    rank?.Tier,
                    rank?.RankNumber,
                    rank?.LeaguePoints,
                    rank?.Wins ?? 0,
                    rank?.Losses ?? 0,
                    row.Games,
                    row.Wins,
                    row.Kills,
                    row.Deaths,
                    row.Assists,
                    row.UpdatedAtUtc ?? DateTime.UtcNow);
            })
            .ToList();
    }
}
