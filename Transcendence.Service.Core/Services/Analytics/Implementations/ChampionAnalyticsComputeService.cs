using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Raw computation service for champion analytics using EF Core aggregation.
/// </summary>
public class ChampionAnalyticsComputeService : IChampionAnalyticsComputeService
{
    private const int MinimumGamesRequired = 100;
    private const int MinMatchupSampleSize = 30;
    private const int MatchupsToShow = 5;
    private readonly TranscendenceContext _context;

    public ChampionAnalyticsComputeService(TranscendenceContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Computes win rates for a champion across roles and rank tiers.
    /// Only returns data for combinations with 100+ games.
    /// </summary>
    public async Task<List<ChampionWinRateDto>> ComputeWinRatesAsync(
        int championId,
        ChampionAnalyticsFilter filter,
        string patch,
        CancellationToken ct)
    {
        // Base query: Match participants for this champion in this patch
        var query = _context.MatchParticipants
            .AsNoTracking()
            .Include(mp => mp.Match)
            .Include(mp => mp.Summoner)
                .ThenInclude(s => s.Ranks)
            .Where(mp => mp.ChampionId == championId)
            .Where(mp => mp.Match.Patch == patch)
            .Where(mp => mp.Match.Status == FetchStatus.Success)
            .Where(mp => mp.TeamPosition != null && mp.TeamPosition != "");

        // Apply region filter if specified
        if (!string.IsNullOrEmpty(filter.Region))
        {
            query = query.Where(mp => mp.Summoner.Region == filter.Region);
        }

        // Apply role filter if specified
        if (!string.IsNullOrEmpty(filter.Role))
        {
            query = query.Where(mp => mp.TeamPosition == filter.Role);
        }

        // Get all participants with their rank data
        var participants = await query.ToListAsync(ct);

        // For each participant, get their current rank tier at the time of the match
        // We'll use the most recent rank data (RANKED_SOLO_5x5 queue)
        var participantRanks = participants
            .Select(mp => new
            {
                Participant = mp,
                RankTier = mp.Summoner.Ranks
                    .Where(r => r.QueueType == "RANKED_SOLO_5x5")
                    .OrderByDescending(r => r.UpdatedAt)
                    .FirstOrDefault()?.Tier ?? "UNRANKED"
            })
            .ToList();

        // Apply rank tier filter if specified
        if (!string.IsNullOrEmpty(filter.RankTier))
        {
            participantRanks = participantRanks
                .Where(pr => pr.RankTier.Equals(filter.RankTier, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Group by role and rank tier, calculate win rates
        var winRateData = participantRanks
            .GroupBy(pr => new { pr.Participant.TeamPosition, pr.RankTier })
            .Select(g => new
            {
                Role = g.Key.TeamPosition!,
                RankTier = g.Key.RankTier,
                Games = g.Count(),
                Wins = g.Count(pr => pr.Participant.Win)
            })
            .Where(x => x.Games >= MinimumGamesRequired)
            .ToList();

        // Calculate total games across all roles/tiers for pick rate calculation
        var totalGames = participantRanks.Count;

        // Convert to DTOs
        var result = winRateData
            .Select(data => new ChampionWinRateDto(
                ChampionId: championId,
                Role: data.Role,
                RankTier: data.RankTier,
                Games: data.Games,
                Wins: data.Wins,
                WinRate: data.Games > 0 ? (double)data.Wins / data.Games : 0.0,
                PickRate: totalGames > 0 ? (double)data.Games / totalGames : 0.0,
                Patch: patch
            ))
            .OrderByDescending(x => x.Games)
            .ToList();

        return result;
    }

    /// <summary>
    /// Computes tier list ranking champions by composite score.
    /// S = top 10%, A = 10-30%, B = 30-60%, C = 60-85%, D = 85%+
    /// </summary>
    public async Task<List<TierListEntry>> ComputeTierListAsync(
        string? role,
        string? rankTier,
        string patch,
        CancellationToken ct)
    {
        var normalizedRole = string.IsNullOrWhiteSpace(role) ? "ALL" : role.ToUpperInvariant();
        var isUnifiedRole = normalizedRole == "ALL";

        // Step 1: Build base query for match participants in this patch
        var baseQuery = _context.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.Match.Patch == patch)
            .Where(mp => mp.Match.Status == FetchStatus.Success)
            .Where(mp => mp.TeamPosition != null && mp.TeamPosition != "");

        // Apply role filter (if not unified "ALL")
        if (!isUnifiedRole)
        {
            baseQuery = baseQuery.Where(mp => mp.TeamPosition == normalizedRole);
        }

        // Join with Ranks for tier filtering (RANKED_SOLO_5x5)
        var query = from mp in baseQuery
                    join r in _context.Ranks.Where(r => r.QueueType == "RANKED_SOLO_5x5")
                        on mp.SummonerId equals r.SummonerId
                    where string.IsNullOrEmpty(rankTier) || r.Tier == rankTier
                    select new { mp.ChampionId, mp.TeamPosition, mp.Win, mp.MatchId };

        // Step 2: Aggregate champion stats
        var championStats = isUnifiedRole
            ? await query
                .GroupBy(x => x.ChampionId)
                .Select(g => new
                {
                    ChampionId = g.Key,
                    TeamPosition = "ALL",
                    Games = g.Count(),
                    Wins = g.Count(x => x.Win)
                })
                .Where(x => x.Games >= MinimumGamesRequired)
                .ToListAsync(ct)
            : await query
                .GroupBy(x => new { x.ChampionId, x.TeamPosition })
                .Select(g => new
                {
                    g.Key.ChampionId,
                    TeamPosition = g.Key.TeamPosition!,
                    Games = g.Count(),
                    Wins = g.Count(x => x.Win)
                })
                .Where(x => x.Games >= MinimumGamesRequired)
                .ToListAsync(ct);

        if (championStats.Count == 0)
            return new List<TierListEntry>();

        // Step 3: Calculate total games for pick rate (per role if role-specific, otherwise all)
        var totalParticipants = await query.CountAsync(ct);

        // Step 4: Calculate composite scores
        var withScores = championStats.Select(c => new
        {
            c.ChampionId,
            c.TeamPosition,
            c.Games,
            c.Wins,
            WinRate = c.Games > 0 ? (double)c.Wins / c.Games : 0.0,
            PickRate = totalParticipants > 0 ? (double)c.Games / totalParticipants : 0.0
        })
        .Select(c => new
        {
            c.ChampionId,
            c.TeamPosition,
            c.Games,
            c.WinRate,
            c.PickRate,
            // Composite: 70% win rate + 30% pick rate
            CompositeScore = (c.WinRate * 0.70) + (c.PickRate * 0.30)
        })
        .OrderByDescending(x => x.CompositeScore)
        .ToList();

        // Step 5: Get previous patch tier list for movement comparison
        var previousPatch = await GetPreviousPatchAsync(patch, ct);
        var previousTiers = previousPatch != null
            ? await GetPreviousPatchTiersAsync(normalizedRole, rankTier, previousPatch, ct)
            : new Dictionary<(int ChampionId, string Role), TierGrade>();

        // Step 6: Assign percentile-based tiers
        // Top 10% = S, 10-30% = A, 30-60% = B, 60-85% = C, 85%+ = D
        var total = withScores.Count;
        return withScores.Select((entry, index) =>
        {
            var percentile = (double)index / total;
            var tier = percentile switch
            {
                < 0.10 => TierGrade.S,
                < 0.30 => TierGrade.A,
                < 0.60 => TierGrade.B,
                < 0.85 => TierGrade.C,
                _ => TierGrade.D
            };

            // Calculate movement from previous patch
            var lookupKey = (entry.ChampionId, NormalizeRole(entry.TeamPosition));
            var previousTier = previousTiers.GetValueOrDefault(lookupKey);
            var movement = CalculateMovement(tier, previousTier);

            return new TierListEntry(
                entry.ChampionId,
                entry.TeamPosition,
                tier,
                entry.CompositeScore,
                entry.WinRate,
                entry.PickRate,
                entry.Games,
                movement,
                previousTier
            );
        }).ToList();
    }

    /// <summary>
    /// Gets the patch version immediately before the specified patch.
    /// </summary>
    private async Task<string?> GetPreviousPatchAsync(string currentPatch, CancellationToken ct)
    {
        // Get all patches ordered by release date, find the one before current
        var patches = await _context.Patches
            .AsNoTracking()
            .OrderByDescending(p => p.ReleaseDate)
            .Select(p => p.Version)
            .Take(10)
            .ToListAsync(ct);

        var currentIndex = patches.IndexOf(currentPatch);
        return currentIndex >= 0 && currentIndex + 1 < patches.Count
            ? patches[currentIndex + 1]
            : null;
    }

    /// <summary>
    /// Gets tier assignments from previous patch for movement comparison.
    /// </summary>
    private async Task<Dictionary<(int ChampionId, string Role), TierGrade>> GetPreviousPatchTiersAsync(
        string role,
        string? rankTier,
        string patch,
        CancellationToken ct)
    {
        // Simplified query - just need champion -> tier mapping
        var previousList = await ComputeTierListAsync(role, rankTier, patch, ct);
        return previousList
            .GroupBy(e => (e.ChampionId, Role: NormalizeRole(e.Role)))
            .ToDictionary(g => g.Key, g => g.First().Tier);
    }

    private static string NormalizeRole(string? role) =>
        string.IsNullOrWhiteSpace(role) ? "ALL" : role.ToUpperInvariant();

    /// <summary>
    /// Calculates movement indicator by comparing tier grades.
    /// </summary>
    private static TierMovement CalculateMovement(TierGrade currentTier, TierGrade? previousTier)
    {
        if (!previousTier.HasValue)
            return TierMovement.NEW;

        var currentValue = TierToValue(currentTier);
        var previousValue = TierToValue(previousTier.Value);

        return (currentValue - previousValue) switch
        {
            > 0 => TierMovement.UP,
            < 0 => TierMovement.DOWN,
            _ => TierMovement.SAME
        };
    }

    /// <summary>
    /// Converts tier grade to numeric value for comparison.
    /// </summary>
    private static int TierToValue(TierGrade tier) => tier switch
    {
        TierGrade.S => 5,
        TierGrade.A => 4,
        TierGrade.B => 3,
        TierGrade.C => 2,
        TierGrade.D => 1,
        _ => 0
    };

    // Excluded items: boots, trinkets, consumables
    private static readonly HashSet<int> ExcludedFromCore = new()
    {
        // Boots (Tier 2)
        3006, 3009, 3020, 3047, 3111, 3117, 3158,
        // Tier 1 boots
        1001,
        // Trinkets
        3340, 3363, 3364,
        // Consumables
        2003, 2031, 2033, 2055, 2138, 2139, 2140
    };

    private const double CoreItemThreshold = 0.70;
    private const int MinBuildSampleSize = 30;

    /// <summary>
    /// Computes top 3 builds for a champion with items and runes bundled.
    /// Core items (70%+ appearance) distinguished from situational.
    /// </summary>
    public async Task<ChampionBuildsResponse> ComputeBuildsAsync(
        int championId,
        string role,
        string? rankTier,
        string patch,
        CancellationToken ct)
    {
        // Step 1: Get all match data for this champion/role/patch/tier with items and runes
        var baseQuery = _context.MatchParticipants
            .AsNoTracking()
            .Include(mp => mp.Items)
            .Include(mp => mp.Runes)
            .Where(mp => mp.ChampionId == championId
                      && mp.Match.Patch == patch
                      && mp.Match.Status == FetchStatus.Success
                      && mp.TeamPosition == role);

        // Join with Rank for tier filtering
        var query = baseQuery
            .Join(
                _context.Ranks.Where(r => r.QueueType == "RANKED_SOLO_5x5"
                    && (string.IsNullOrEmpty(rankTier) || r.Tier == rankTier)),
                mp => mp.SummonerId,
                r => r.SummonerId,
                (mp, r) => mp
            );

        var matchData = await query
            .Select(mp => new
            {
                mp.Win,
                Items = mp.Items.Select(i => i.ItemId).ToList(),
                Runes = mp.Runes.Select(r => r.RuneId).ToList()
            })
            .ToListAsync(ct);

        if (matchData.Count < MinimumGamesRequired)
            return new ChampionBuildsResponse(championId, role, rankTier ?? "all", patch,
                new List<int>(), new List<ChampionBuildDto>());

        // Step 2: Calculate global core items (appear in 70%+ of ALL games, excluding boots/trinkets/consumables)
        var totalGames = matchData.Count;
        var itemFrequency = matchData
            .SelectMany(m => m.Items.Where(i => i != 0 && !ExcludedFromCore.Contains(i)))
            .GroupBy(itemId => itemId)
            .ToDictionary(
                g => g.Key,
                g => (double)g.Count() / totalGames
            );

        var globalCoreItems = itemFrequency
            .Where(kvp => kvp.Value >= CoreItemThreshold)
            .Select(kvp => kvp.Key)
            .ToList();

        // Step 3: Get rune metadata for style determination
        var allRuneIds = matchData.SelectMany(m => m.Runes).Distinct().ToList();
        var runeMetadata = await _context.RuneVersions
            .AsNoTracking()
            .Where(rv => allRuneIds.Contains(rv.RuneId) && rv.PatchVersion == patch)
            .Select(rv => new { rv.RuneId, rv.RunePathId, rv.Slot })
            .ToDictionaryAsync(rv => rv.RuneId, rv => new RuneMetadata(rv.RunePathId, rv.Slot), ct);

        // Step 4: Group by build (items + runes as key)
        var buildGroups = matchData
            .Select(m => new
            {
                m.Win,
                // Normalize item list (sort, exclude empty)
                ItemKey = string.Join(",", m.Items.Where(i => i != 0).OrderBy(i => i)),
                // Build rune structure
                RuneInfo = BuildRuneInfo(m.Runes, runeMetadata),
                Items = m.Items.Where(i => i != 0).OrderBy(i => i).ToList()
            })
            .GroupBy(m => new { m.ItemKey, m.RuneInfo.Key })
            .Select(g => new
            {
                Items = g.First().Items,
                RuneInfo = g.First().RuneInfo,
                Games = g.Count(),
                Wins = g.Sum(x => x.Win ? 1 : 0),
                WinRate = (double)g.Sum(x => x.Win ? 1 : 0) / g.Count()
            })
            .Where(b => b.Games >= MinBuildSampleSize)
            .OrderByDescending(b => b.Games * b.WinRate) // Score: popularity * success
            .Take(3)
            .ToList();

        // Step 5: Map to DTOs
        var builds = buildGroups.Select(build => new ChampionBuildDto(
            build.Items,
            globalCoreItems,
            build.Items.Where(i => !globalCoreItems.Contains(i) && !ExcludedFromCore.Contains(i)).ToList(),
            build.RuneInfo.PrimaryStyleId,
            build.RuneInfo.SubStyleId,
            build.RuneInfo.PrimaryRunes,
            build.RuneInfo.SubRunes,
            build.RuneInfo.StatShards,
            build.Games,
            build.WinRate
        )).ToList();

        return new ChampionBuildsResponse(
            championId,
            role,
            rankTier ?? "all",
            patch,
            globalCoreItems,
            builds
        );
    }

    /// <summary>
    /// Helper record for rune metadata lookup result.
    /// </summary>
    private record RuneMetadata(int RunePathId, int Slot);

    /// <summary>
    /// Helper record for rune information grouping.
    /// </summary>
    private record RuneInfoResult(
        string Key,
        int PrimaryStyleId,
        int SubStyleId,
        List<int> PrimaryRunes,
        List<int> SubRunes,
        List<int> StatShards
    );

    /// <summary>
    /// Builds rune information structure from raw rune IDs using metadata lookup.
    /// Determines primary/secondary trees by rune count per path.
    /// Stat shards have RunePathId >= 5000.
    /// </summary>
    private static RuneInfoResult BuildRuneInfo(
        List<int> runeIds,
        Dictionary<int, RuneMetadata> runeMetadata)
    {
        var runesByPath = new Dictionary<int, List<(int RuneId, int Slot)>>();

        foreach (var runeId in runeIds)
        {
            if (runeMetadata.TryGetValue(runeId, out var meta))
            {
                if (!runesByPath.ContainsKey(meta.RunePathId))
                    runesByPath[meta.RunePathId] = new List<(int, int)>();
                runesByPath[meta.RunePathId].Add((runeId, meta.Slot));
            }
        }

        var primaryStyleId = 0;
        var subStyleId = 0;
        var primaryRunes = new List<int>();
        var subRunes = new List<int>();
        var statShards = new List<int>();

        foreach (var (pathId, runes) in runesByPath)
        {
            if (pathId >= 5000) // Stat shards
            {
                statShards = runes.OrderBy(r => r.Slot).Select(r => r.RuneId).ToList();
            }
            else if (runes.Count >= 3) // Primary (4 runes)
            {
                primaryStyleId = pathId;
                primaryRunes = runes.OrderBy(r => r.Slot).Select(r => r.RuneId).ToList();
            }
            else // Sub (2 runes)
            {
                subStyleId = pathId;
                subRunes = runes.OrderBy(r => r.Slot).Select(r => r.RuneId).ToList();
            }
        }

        // Build unique key for grouping
        var key = $"{primaryStyleId}:{string.Join(",", primaryRunes)}|{subStyleId}:{string.Join(",", subRunes)}|{string.Join(",", statShards)}";

        return new RuneInfoResult(key, primaryStyleId, subStyleId, primaryRunes, subRunes, statShards);
    }

    /// <summary>
    /// Computes matchup data showing counters (bad matchups) and favorable matchups.
    /// Uses lane-specific self-join: same role, different team.
    /// </summary>
    public async Task<ChampionMatchupsResponse> ComputeMatchupsAsync(
        int championId,
        string role,
        string? rankTier,
        string patch,
        CancellationToken ct)
    {
        // Self-join: champion participant vs opponent in same role, different team
        // This gives us lane-specific matchups (Mid vs Mid, Top vs Top, etc.)

        var championQuery = _context.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.ChampionId == championId
                      && mp.TeamPosition == role
                      && mp.Match.Patch == patch
                      && mp.Match.Status == FetchStatus.Success);

        // Apply rank tier filter if specified
        if (!string.IsNullOrEmpty(rankTier))
        {
            championQuery = championQuery
                .Join(
                    _context.Ranks.Where(r => r.Tier == rankTier && r.QueueType == "RANKED_SOLO_5x5"),
                    mp => mp.SummonerId,
                    r => r.SummonerId,
                    (mp, r) => mp
                );
        }

        // Join with opponent: same match, same role, different team
        // Since EF Core may struggle with record constructor in GroupBy/Select,
        // we'll use anonymous types and materialize first
        var matchupData = await championQuery
            .Join(
                _context.MatchParticipants,
                champion => champion.MatchId,
                opponent => opponent.MatchId,
                (champion, opponent) => new { Champion = champion, Opponent = opponent }
            )
            .Where(x => x.Champion.TeamPosition == x.Opponent.TeamPosition  // Same role (lane matchup)
                     && x.Champion.TeamId != x.Opponent.TeamId)              // Different team (opponent)
            .GroupBy(x => x.Opponent.ChampionId)
            .Select(g => new
            {
                OpponentChampionId = g.Key,
                Games = g.Count(),
                Wins = g.Sum(x => x.Champion.Win ? 1 : 0),
                Losses = g.Sum(x => x.Champion.Win ? 0 : 1)
            })
            .Where(m => m.Games >= MinMatchupSampleSize)
            .ToListAsync(ct);

        // Convert to DTOs with calculated win rate
        var matchups = matchupData
            .Select(m => new MatchupEntryDto
            {
                OpponentChampionId = m.OpponentChampionId,
                Games = m.Games,
                Wins = m.Wins,
                Losses = m.Losses,
                WinRate = m.Games > 0 ? (double)m.Wins / m.Games : 0.0
            })
            .ToList();

        // Separate counters (low win rate) and favorable (high win rate)
        var counters = matchups
            .Where(m => m.WinRate < 0.48)
            .OrderBy(m => m.WinRate)
            .Take(MatchupsToShow)
            .ToList();

        var favorable = matchups
            .Where(m => m.WinRate > 0.52)
            .OrderByDescending(m => m.WinRate)
            .Take(MatchupsToShow)
            .ToList();

        return new ChampionMatchupsResponse
        {
            ChampionId = championId,
            Role = role,
            RankTier = rankTier ?? "all",
            Patch = patch,
            Counters = counters,
            FavorableMatchups = favorable
        };
    }
}
