using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Adds each newly eligible match's build decisions to <see cref="BuildLabOptionStat"/>.
///
/// Every batch is one transaction: the counts are incremented in place and the batch's matches are
/// written to the <see cref="BuildLabProcessedMatch"/> ledger together, so a crash mid-run either
/// counted a batch completely or not at all, and the next run resumes exactly where this one stopped.
/// There is nothing to promote and nothing to retrain — readers see the counts grow.
/// </summary>
public sealed class BuildLabStatsRefresher(
    TranscendenceContext context,
    IOptions<BuildLabOptions> optionsAccessor,
    ILogger<BuildLabStatsRefresher> logger) : IBuildLabStatsRefresher
{
    public const string AllRegions = "ALL";
    private static readonly HashSet<string> Roles =
        new(["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"], StringComparer.Ordinal);

    private readonly BuildLabOptions options = optionsAccessor.Value;

    public async Task<BuildLabRefreshResult> RefreshAsync(CancellationToken ct)
    {
        var retained = await RecentPatchesAsync(Math.Max(1, options.PatchesToRetain), ct);
        if (retained.Count == 0)
        {
            logger.LogWarning("Build Lab refresh skipped: no patches are known yet.");
            return new BuildLabRefreshResult([], 0, 0, 0);
        }

        var previousTimeout = context.Database.GetCommandTimeout();
        context.Database.SetCommandTimeout(Math.Clamp(options.CommandTimeoutSeconds, 30, 1800));
        try
        {
            await DeleteExpiredPatchesAsync(retained, ct);

            var refreshed = retained.Take(1 + Math.Max(0, options.PriorPatchesToRefresh)).ToList();
            var budget = Math.Max(1, options.MaxMatchesPerRun);
            var batchSize = Math.Clamp(options.MatchBatchSize, 10, 5_000);
            var counted = 0;
            var rowsWritten = 0;
            var rejected = new Dictionary<OpeningBuyRejection, int>();
            foreach (var patch in refreshed)
            {
                var prices = await context.ItemVersions.AsNoTracking()
                    .Where(item => item.PatchVersion == patch)
                    .ToDictionaryAsync(item => item.ItemId, item => item.PriceTotal, ct);
                while (counted < budget)
                {
                    var matchIds = await NextBatchAsync(patch, Math.Min(batchSize, budget - counted), ct);
                    if (matchIds.Count == 0)
                        break;
                    rowsWritten += await CountBatchAsync(patch, matchIds, prices, rejected, ct);
                    counted += matchIds.Count;
                    logger.LogInformation(
                        "Build Lab patch {Patch}: counted {Batch} matches ({Counted} this run).",
                        patch, matchIds.Count, counted);
                }
            }

            // An over-budget or unpriceable opening buy is dropped, not counted; a sudden rise here is
            // the replay mis-reading a new event shape, which is worth knowing before it skews the page.
            if (rejected.Count > 0)
                logger.LogInformation(
                    "Build Lab rejected opening buys this run: {Rejections}.",
                    string.Join(", ", rejected.Select(pair => $"{pair.Key}={pair.Value}")));

            var backlog = await EligibleUncounted(refreshed).LongCountAsync(ct);
            return new BuildLabRefreshResult(refreshed, counted, rowsWritten, backlog);
        }
        finally
        {
            context.Database.SetCommandTimeout(previousTimeout);
        }
    }

    /// <summary>The newest patches first, active patch leading, by release date.</summary>
    private async Task<List<string>> RecentPatchesAsync(int count, CancellationToken ct) =>
        await context.Patches.AsNoTracking()
            .OrderByDescending(patch => patch.IsActive)
            .ThenByDescending(patch => patch.ReleaseDate)
            .Select(patch => patch.Version)
            .Take(count)
            .ToListAsync(ct);

    private async Task DeleteExpiredPatchesAsync(IReadOnlyList<string> retained, CancellationToken ct)
    {
        var expired = await context.BuildLabProcessedMatches.AsNoTracking()
            .Where(match => !retained.Contains(match.Patch))
            .Select(match => match.Patch)
            .Distinct()
            .ToListAsync(ct);
        foreach (var patch in expired)
        {
            var stats = await context.BuildLabOptionStats.Where(row => row.Patch == patch).ExecuteDeleteAsync(ct);
            await context.BuildLabProcessedMatches.Where(row => row.Patch == patch).ExecuteDeleteAsync(ct);
            logger.LogInformation("Build Lab dropped {Rows} stat rows for expired patch {Patch}.", stats, patch);
        }
    }

    // Eligible = a completed ranked solo/duo game long enough not to be a remake, whose timeline was
    // captured at the detailed schema (the item events and one-minute frames only exist there). Query
    // filters are bypassed throughout: they only re-check the match status this predicate already pins,
    // at the cost of a join back to Matches on every read.
    private IQueryable<Match> EligibleUncounted(IReadOnlyCollection<string> patches) =>
        context.Matches.IgnoreQueryFilters().AsNoTracking()
            .Where(match =>
                match.Patch != null &&
                patches.Contains(match.Patch) &&
                match.Status == FetchStatus.Success &&
                match.Duration >= 300 &&
                (match.QueueId == QueueCatalog.RankedSoloDuoQueueId ||
                 (match.QueueId == 0 && match.QueueType == "420")) &&
                context.MatchTimelineFetchStates.IgnoreQueryFilters().Any(state =>
                    state.MatchId == match.Id &&
                    state.Status == MatchTimelineFetchStatus.Success &&
                    state.SchemaVersion >= 2) &&
                !context.BuildLabProcessedMatches.Any(processed => processed.MatchId == match.Id));

    private async Task<List<Guid>> NextBatchAsync(string patch, int size, CancellationToken ct) =>
        await EligibleUncounted([patch])
            .OrderBy(match => match.Id)
            .Select(match => match.Id)
            .Take(size)
            .ToListAsync(ct);

    private async Task<int> CountBatchAsync(
        string patch,
        List<Guid> matchIds,
        IReadOnlyDictionary<int, int> prices,
        Dictionary<OpeningBuyRejection, int> rejected,
        CancellationToken ct)
    {
        var participants = await context.MatchParticipants.IgnoreQueryFilters().AsNoTracking()
            .Where(participant => matchIds.Contains(participant.MatchId))
            .Select(participant => new ParticipantRow(
                participant.Id,
                participant.MatchId,
                participant.ParticipantId,
                participant.TeamId,
                participant.ChampionId,
                (participant.TeamPosition ?? "").ToUpper(),
                participant.Win,
                participant.SummonerSpell1Id,
                participant.SummonerSpell2Id,
                participant.GameEndedInEarlySurrender ?? false,
                participant.Match.PlatformRegion ?? ""))
            .ToListAsync(ct);
        var events = (await context.MatchParticipantItemEvents.IgnoreQueryFilters().AsNoTracking()
                .Where(itemEvent => matchIds.Contains(itemEvent.MatchId))
                .Select(itemEvent => new
                {
                    itemEvent.MatchId,
                    itemEvent.ParticipantId,
                    Event = new BuildLabItemEvent(
                        itemEvent.EventIndex,
                        itemEvent.EventType,
                        itemEvent.TimestampMs,
                        itemEvent.ItemId,
                        itemEvent.BeforeId,
                        itemEvent.AfterId,
                        itemEvent.BuildCategory)
                })
                .ToListAsync(ct))
            .ToLookup(row => (row.MatchId, row.ParticipantId), row => row.Event);
        var teamGold = (await context.MatchParticipantTimelineSnapshots.IgnoreQueryFilters().AsNoTracking()
                .Where(frame => matchIds.Contains(frame.MatchId))
                .Select(frame => new { frame.MatchId, frame.ParticipantId, frame.MinuteMark, frame.Gold })
                .ToListAsync(ct))
            .Join(participants,
                frame => (frame.MatchId, frame.ParticipantId),
                participant => (participant.MatchId, participant.ParticipantId),
                (frame, participant) => (frame.MatchId, participant.TeamId, frame.MinuteMark, frame.Gold))
            .GroupBy(frame => (frame.MatchId, frame.TeamId, frame.MinuteMark))
            .ToDictionary(group => group.Key, group => group.Sum(frame => frame.Gold));
        var participantIds = participants.Select(participant => participant.Id).ToList();
        var runes = (await context.MatchParticipantRunes.IgnoreQueryFilters().AsNoTracking()
                .Where(rune => participantIds.Contains(rune.MatchParticipantId))
                .Select(rune => new
                {
                    rune.MatchParticipantId,
                    Rune = new BuildLabRune(rune.SelectionTree, rune.SelectionIndex, rune.RuneId)
                })
                .ToListAsync(ct))
            .ToLookup(row => row.MatchParticipantId, row => row.Rune);

        var counts = new Dictionary<StatKey, Counts>();
        foreach (var match in participants.GroupBy(participant => participant.MatchId))
        {
            // A remake or early surrender says nothing about which build is better.
            if (match.Any(participant => participant.EarlySurrender))
                continue;
            foreach (var participant in match)
            {
                if (!Roles.Contains(participant.Role))
                    continue;
                var opponent = match.FirstOrDefault(other =>
                    other.TeamId != participant.TeamId && other.Role == participant.Role)?.ChampionId ?? 0;

                var itemEvents = events[(participant.MatchId, participant.ParticipantId)].ToList();
                var opening = BuildLabDecisions.OpeningBuy(itemEvents, prices);
                if (opening.Rejection is OpeningBuyRejection.OverBudget or OpeningBuyRejection.UnknownPrice)
                    rejected[opening.Rejection] = rejected.GetValueOrDefault(opening.Rejection) + 1;
                var decisions = (opening.Decision is { } start ? [start] : Array.Empty<BuildLabDecision>())
                    .Concat(BuildLabDecisions.Items(itemEvents))
                    .Concat(BuildLabDecisions.Runes(runes[participant.Id]))
                    .Concat(BuildLabDecisions.Spells(participant.Spell1Id, participant.Spell2Id));
                foreach (var decision in decisions)
                {
                    var bucket = decision.TimestampMs is { } timestamp and > 0
                        ? BuildLabDecisions.GoldBucket(TeamGoldDifference(teamGold, participant, timestamp))
                        : BuildLabDecisions.NeutralGoldBucket;
                    var timingSeconds = (decision.TimestampMs ?? 0) / 1000;
                    Add(counts, participant, 0, AllRegions, decision, patch, bucket, timingSeconds);
                    if (!BuildLabDecisions.CountsInNarrowScopes(decision))
                        continue;
                    if (opponent > 0)
                        Add(counts, participant, opponent, AllRegions, decision, patch, bucket, timingSeconds);
                    if (participant.Region.Length > 0)
                        Add(counts, participant, 0, participant.Region, decision, patch, bucket, timingSeconds);
                }
            }
        }

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await UpsertAsync(patch, counts, ct);
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "BuildLabProcessedMatches" ("MatchId", "Patch", "ProcessedAtUtc")
            SELECT id, @patch, now() FROM unnest(@ids) AS id
            ON CONFLICT ("MatchId") DO NOTHING
            """,
            [new NpgsqlParameter("patch", patch), new NpgsqlParameter("ids", matchIds.ToArray())],
            ct);
        await transaction.CommitAsync(ct);
        return counts.Count;
    }

    private static int? TeamGoldDifference(
        IReadOnlyDictionary<(Guid MatchId, int TeamId, int Minute), int> teamGold,
        ParticipantRow participant,
        int timestampMs)
    {
        // The frame at minute m is the state at m:00, so it precedes every decision inside that minute.
        var minute = timestampMs / 60_000;
        var enemyTeam = participant.TeamId == 100 ? 200 : 100;
        return teamGold.TryGetValue((participant.MatchId, participant.TeamId, minute), out var own) &&
               teamGold.TryGetValue((participant.MatchId, enemyTeam, minute), out var enemy)
            ? own - enemy
            : null;
    }

    private static void Add(
        Dictionary<StatKey, Counts> counts,
        ParticipantRow participant,
        int opponent,
        string region,
        BuildLabDecision decision,
        string patch,
        short bucket,
        long timingSeconds)
    {
        var key = new StatKey(
            participant.ChampionId,
            participant.Role,
            opponent,
            region,
            BuildLabPath.Hash(decision.Prefix),
            decision.Family,
            decision.Stage,
            decision.ActionKey,
            bucket);
        ref var value = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(
            counts, key, out _);
        value.Games++;
        if (participant.Win)
            value.Wins++;
        value.TimingSecondsSum += timingSeconds;
    }

    private async Task UpsertAsync(string patch, Dictionary<StatKey, Counts> counts, CancellationToken ct)
    {
        if (counts.Count == 0)
            return;
        var keys = counts.Keys.ToArray();
        var values = keys.Select(key => counts[key]).ToArray();
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "BuildLabOptionStats" (
                "ChampionId", "Role", "OpponentChampionId", "Region", "PrefixHash", "Family", "Stage",
                "Patch", "ActionKey", "GoldBucket", "Games", "Wins", "TimingSecondsSum")
            SELECT champion, role, opponent, region, prefix, family, stage,
                   @patch, action, bucket, games, wins, timing
            FROM unnest(@champion, @role, @opponent, @region, @prefix, @family, @stage,
                        @action, @bucket, @games, @wins, @timing)
                 AS t(champion, role, opponent, region, prefix, family, stage,
                      action, bucket, games, wins, timing)
            ON CONFLICT ("ChampionId", "Role", "OpponentChampionId", "Region", "PrefixHash", "Family",
                         "Stage", "Patch", "ActionKey", "GoldBucket")
            DO UPDATE SET
                "Games" = "BuildLabOptionStats"."Games" + EXCLUDED."Games",
                "Wins" = "BuildLabOptionStats"."Wins" + EXCLUDED."Wins",
                "TimingSecondsSum" = "BuildLabOptionStats"."TimingSecondsSum" + EXCLUDED."TimingSecondsSum"
            """,
            [
                new NpgsqlParameter("patch", patch),
                new NpgsqlParameter("champion", keys.Select(key => key.ChampionId).ToArray()),
                new NpgsqlParameter("role", NpgsqlDbType.Array | NpgsqlDbType.Text)
                    { Value = keys.Select(key => key.Role).ToArray() },
                new NpgsqlParameter("opponent", keys.Select(key => key.OpponentChampionId).ToArray()),
                new NpgsqlParameter("region", NpgsqlDbType.Array | NpgsqlDbType.Text)
                    { Value = keys.Select(key => key.Region).ToArray() },
                new NpgsqlParameter("prefix", keys.Select(key => key.PrefixHash).ToArray()),
                new NpgsqlParameter("family", keys.Select(key => (short)key.Family).ToArray()),
                new NpgsqlParameter("stage", keys.Select(key => key.Stage).ToArray()),
                new NpgsqlParameter("action", NpgsqlDbType.Array | NpgsqlDbType.Text)
                    { Value = keys.Select(key => key.ActionKey).ToArray() },
                new NpgsqlParameter("bucket", keys.Select(key => key.GoldBucket).ToArray()),
                new NpgsqlParameter("games", values.Select(value => value.Games).ToArray()),
                new NpgsqlParameter("wins", values.Select(value => value.Wins).ToArray()),
                new NpgsqlParameter("timing", values.Select(value => value.TimingSecondsSum).ToArray())
            ],
            ct);
    }

    private sealed record ParticipantRow(
        Guid Id,
        Guid MatchId,
        int ParticipantId,
        int TeamId,
        int ChampionId,
        string Role,
        bool Win,
        int Spell1Id,
        int Spell2Id,
        bool EarlySurrender,
        string Region);

    private readonly record struct StatKey(
        int ChampionId,
        string Role,
        int OpponentChampionId,
        string Region,
        long PrefixHash,
        BuildLabFamily Family,
        short Stage,
        string ActionKey,
        short GoldBucket);

    private struct Counts
    {
        public int Games;
        public int Wins;
        public long TimingSecondsSum;
    }
}
