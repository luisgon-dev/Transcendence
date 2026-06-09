using Camille.Enums;
using Camille.RiotGames;
using Camille.RiotGames.MatchV5;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Analytics;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.RiotApi;
using DataMatch = Transcendence.Data.Models.LoL.Match.Match;

namespace Transcendence.Service.Core.Services.Jobs;

[DisableConcurrentExecution(timeoutInSeconds: 5 * 60)]
public class MatchTimelineIngestionJob(
    TranscendenceContext db,
    LeagueRiotApiContext riotApiContext,
    IBackgroundJobClient backgroundJobClient,
    IOptions<TimelineIngestionOptions> options,
    ILogger<MatchTimelineIngestionJob> logger)
{
    /// <summary>
    /// Bumped when the timeline job begins deriving new per-match data so already-<c>Success</c>
    /// matches are re-ingested once. v1 added ordered item purchases + skill orders.
    /// </summary>
    public const int CurrentTimelineSchemaVersion = 1;

    [Queue("refresh-low")]
    public async Task IngestMatchTimelineAsync(string matchId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(matchId))
            return;

        var jobOptions = options.Value;
        if (!jobOptions.Enabled)
            return;

        var match = await db.Matches
            .IgnoreQueryFilters()
            .Include(m => m.Participants)
            .FirstOrDefaultAsync(m => m.MatchId == matchId, ct);

        if (match == null)
        {
            logger.LogWarning("[Timeline] Match {MatchId} not found. Skipping timeline ingestion.", matchId);
            return;
        }

        var state = await db.MatchTimelineFetchStates
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.MatchId == match.Id, ct);

        if (state == null)
        {
            state = new MatchTimelineFetchState
            {
                MatchId = match.Id,
                Match = match
            };
            db.MatchTimelineFetchStates.Add(state);
        }

        var maxRetryAttempts = Math.Max(1, jobOptions.MaxRetryAttempts);
        if (state.Status == MatchTimelineFetchStatus.Success && state.SchemaVersion >= CurrentTimelineSchemaVersion)
            return;

        if (state.Status == MatchTimelineFetchStatus.PermanentlyFailed && state.RetryCount >= maxRetryAttempts)
            return;

        if (!QueueCatalog.IsRankedAnalyticsQueue(match.QueueId))
        {
            state.Status = MatchTimelineFetchStatus.NotApplicable;
            state.LastAttemptAtUtc = DateTime.UtcNow;
            state.LastError = null;
            state.SourcePatch = match.Patch;
            await db.SaveChangesAsync(ct);
            return;
        }

        if (!TryResolveRegionalRoute(matchId, out var regionalRoute))
        {
            state.Status = MatchTimelineFetchStatus.PermanentlyFailed;
            state.LastAttemptAtUtc = DateTime.UtcNow;
            state.LastError = "Unable to resolve regional route from match id.";
            state.SourcePatch = match.Patch;
            state.RetryCount = maxRetryAttempts;
            await db.SaveChangesAsync(ct);
            logger.LogWarning("[Timeline] Could not resolve region for {MatchId}.", matchId);
            return;
        }

        try
        {
            state.LastAttemptAtUtc = DateTime.UtcNow;

            var timeline = await riotApiContext.Api.MatchV5()
                .GetTimelineAsync(regionalRoute, matchId, ct);

            if (timeline?.Info?.Frames == null || timeline.Info.Frames.Length == 0)
                throw new InvalidOperationException("Timeline response did not include frames.");

            BackfillParticipantIdsFromTimeline(match.Participants, timeline.Info.Participants);

            // Capture a multi-frame curve: a regular cadence up to game length, plus the
            // analytics anchor mark (kept so champion-analytics gold/xp-diff@N stays intact).
            var anchorMark = Math.Max(1, jobOptions.MinuteMark);
            var minuteMarks = BuildMinuteMarks(match.Duration, Math.Max(1, jobOptions.FrameIntervalMinutes), anchorMark);

            var snapshots = new List<MatchParticipantTimelineSnapshot>();
            foreach (var mark in minuteMarks)
            {
                var targetTimestampMs = mark * 60 * 1000;
                var selectedFrame = SelectFrameForMinuteMark(timeline.Info.Frames, targetTimestampMs);
                if (selectedFrame?.ParticipantFrames == null)
                    continue;

                var qualityFlags = BuildQualityFlags(match, selectedFrame.Timestamp, targetTimestampMs);
                snapshots.AddRange(BuildSnapshots(match, selectedFrame, mark, qualityFlags));
            }

            if (snapshots.Count == 0)
                throw new InvalidOperationException("No participant snapshots could be derived from timeline frames.");

            // Replace the whole snapshot set for this match so re-ingestion is idempotent.
            var existingSnapshots = await db.MatchParticipantTimelineSnapshots
                .Where(x => x.MatchId == match.Id)
                .ToListAsync(ct);
            if (existingSnapshots.Count > 0)
                db.MatchParticipantTimelineSnapshots.RemoveRange(existingSnapshots);

            db.MatchParticipantTimelineSnapshots.AddRange(snapshots);

            // Derive and stage the ordered build path (item purchases + skill orders) from the same
            // frames; committed atomically with the snapshots by the SaveChangesAsync below.
            var buildPathCoverageOk = await StageBuildPathRowsAsync(match, timeline.Info.Frames, ct);

            state.Status = MatchTimelineFetchStatus.Success;
            state.RetryCount = 0;
            state.LastError = null;
            state.LastSuccessAtUtc = DateTime.UtcNow;
            state.SourcePatch = match.Patch;
            // Only mark the build-path schema as captured when item metadata was available; otherwise
            // leave it stale so the backfill re-ingests once metadata exists (snapshots/skills still land).
            if (buildPathCoverageOk)
                state.SchemaVersion = CurrentTimelineSchemaVersion;

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            state.RetryCount++;
            state.LastAttemptAtUtc = DateTime.UtcNow;
            state.LastError = ex.Message.Length > 1024 ? ex.Message[..1024] : ex.Message;
            state.SourcePatch = match.Patch;
            state.Status = state.RetryCount >= maxRetryAttempts
                ? MatchTimelineFetchStatus.PermanentlyFailed
                : MatchTimelineFetchStatus.TemporaryFailure;

            await db.SaveChangesAsync(ct);

            logger.LogWarning(ex, "[Timeline] Failed to ingest timeline for {MatchId}. Attempt {Attempt}/{Max}.",
                matchId, state.RetryCount, maxRetryAttempts);

            if (state.Status == MatchTimelineFetchStatus.TemporaryFailure)
            {
                var delaySeconds = Math.Min(300, (int)Math.Pow(2, Math.Max(0, state.RetryCount - 1)) * 30);
                backgroundJobClient.Schedule<MatchTimelineIngestionJob>(
                    job => job.IngestMatchTimelineAsync(matchId, CancellationToken.None),
                    TimeSpan.FromSeconds(delaySeconds));
            }
        }
    }

    public static IReadOnlyList<int> BuildMinuteMarks(int durationSeconds, int intervalMinutes, int anchorMark)
    {
        var durationMinutes = Math.Max(1, durationSeconds / 60);
        var interval = Math.Max(1, intervalMinutes);
        var marks = new SortedSet<int>();
        for (var mark = interval; mark <= durationMinutes; mark += interval)
            marks.Add(mark);
        // The analytics anchor is always captured, even for games shorter than the anchor
        // (it resolves to the final frame, exactly as the single-frame ingestion did).
        marks.Add(Math.Max(1, anchorMark));
        return marks.ToList();
    }

    private static FramesTimeLine? SelectFrameForMinuteMark(FramesTimeLine[] frames, int targetTimestampMs)
    {
        if (frames.Length == 0)
            return null;

        return frames
                   .Where(f => f != null)
                   .OrderBy(f => f.Timestamp)
                   .LastOrDefault(f => f.Timestamp <= targetTimestampMs)
               ?? frames
                   .Where(f => f != null)
                   .OrderBy(f => f.Timestamp)
                   .FirstOrDefault();
    }

    private static void BackfillParticipantIdsFromTimeline(
        IEnumerable<MatchParticipant> participants,
        ParticipantTimeLine[]? timelineParticipants)
    {
        if (timelineParticipants == null || timelineParticipants.Length == 0)
            return;

        var participantByPuuid = timelineParticipants
            .Where(p => !string.IsNullOrWhiteSpace(p.Puuid))
            .GroupBy(p => p.Puuid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().ParticipantId, StringComparer.Ordinal);

        foreach (var participant in participants)
        {
            if (participant.ParticipantId > 0 || string.IsNullOrWhiteSpace(participant.Puuid))
                continue;

            if (participantByPuuid.TryGetValue(participant.Puuid, out var timelineParticipantId))
                participant.ParticipantId = timelineParticipantId;
        }
    }

    private static List<MatchParticipantTimelineSnapshot> BuildSnapshots(
        DataMatch match,
        FramesTimeLine frame,
        int minuteMark,
        string qualityFlags)
    {
        var snapshots = new List<MatchParticipantTimelineSnapshot>();
        var participantFrames = frame.ParticipantFrames;
        if (participantFrames == null || participantFrames.Count == 0)
            return snapshots;

        foreach (var participant in match.Participants)
        {
            if (participant.ParticipantId <= 0)
                continue;

            if (!participantFrames.TryGetValue(participant.ParticipantId, out var participantFrame))
                continue;

            snapshots.Add(new MatchParticipantTimelineSnapshot
            {
                MatchId = match.Id,
                Match = match,
                ParticipantId = participant.ParticipantId,
                MinuteMark = minuteMark,
                Gold = participantFrame.TotalGold,
                Xp = participantFrame.Xp,
                Cs = participantFrame.MinionsKilled + participantFrame.JungleMinionsKilled,
                Level = participantFrame.Level,
                FrameTimestampMs = frame.Timestamp,
                DerivedAtUtc = DateTime.UtcNow,
                QualityFlags = qualityFlags
            });
        }

        return snapshots;
    }

    private static string BuildQualityFlags(DataMatch match, int frameTimestampMs, int targetTimestampMs)
    {
        var flags = new List<string>(3);
        if (frameTimestampMs == targetTimestampMs)
            flags.Add("EXACT");
        else if (frameTimestampMs < targetTimestampMs)
            flags.Add("PRIOR_FRAME");
        else
            flags.Add("AFTER_TARGET");

        if (match.Duration * 1000 < targetTimestampMs)
            flags.Add("SHORT_GAME");

        return string.Join("|", flags);
    }

    /// <summary>
    /// Parses the timeline's purchase/skill events into ordered, build-relevant rows and stages a
    /// full replace for this match (idempotent re-ingestion), committed by the caller's SaveChanges.
    /// Returns <c>false</c> when item purchases exist but no <c>ItemVersion</c> metadata is available
    /// for the patch yet (e.g. the patch-rollover race): the caller then leaves <c>SchemaVersion</c>
    /// un-advanced so the match is re-ingested once metadata lands instead of baking in an empty path.
    /// </summary>
    private async Task<bool> StageBuildPathRowsAsync(DataMatch match, FramesTimeLine[] frames, CancellationToken ct)
    {
        var events = ProjectBuildEvents(frames);

        var itemMetadata = events.Count == 0 || string.IsNullOrWhiteSpace(match.Patch)
            ? new Dictionary<int, BuildItemMetadata>()
            : await LoadItemMetadataAsync(match.Patch, events, ct);

        var hasPurchaseEvents = events.Any(e =>
            e.Type == TimelineBuildParser.ItemPurchasedType || e.Type == TimelineBuildParser.ItemUndoType);
        var coverageOk = !hasPurchaseEvents || string.IsNullOrWhiteSpace(match.Patch) || itemMetadata.Count > 0;
        if (!coverageOk)
        {
            logger.LogInformation(
                "[Timeline] No item metadata for patch {Patch} on match {MatchId}; persisting skills only and deferring purchase ingestion until metadata lands.",
                match.Patch, match.MatchId);
        }

        var purchasePaths = events.Count == 0
            ? []
            : TimelineBuildParser.BuildPurchasePaths(
                events,
                itemId => itemMetadata.TryGetValue(itemId, out var metadata) ? metadata : (BuildItemMetadata?)null);

        var skillSequences = events.Count == 0
            ? []
            : TimelineBuildParser.BuildSkillSequences(events);

        var purchaseRows = new List<MatchParticipantItemPurchase>();
        foreach (var path in purchasePaths)
        {
            for (var index = 0; index < path.Purchases.Count; index++)
            {
                var purchase = path.Purchases[index];
                purchaseRows.Add(new MatchParticipantItemPurchase
                {
                    MatchId = match.Id,
                    Match = match,
                    ParticipantId = path.ParticipantId,
                    PurchaseIndex = index,
                    ItemId = purchase.ItemId,
                    TimestampMs = purchase.TimestampMs,
                    Category = purchase.Category
                });
            }
        }

        var skillRows = skillSequences
            .Select(sequence => new MatchParticipantSkillOrder
            {
                MatchId = match.Id,
                Match = match,
                ParticipantId = sequence.ParticipantId,
                Sequence = sequence.Sequence,
                FirstThree = sequence.FirstThree,
                MaxOrder = sequence.MaxOrder
            })
            .ToList();

        // Replace any prior rows for this match so re-ingestion is idempotent.
        var existingPurchases = await db.MatchParticipantItemPurchases
            .Where(x => x.MatchId == match.Id)
            .ToListAsync(ct);
        if (existingPurchases.Count > 0)
            db.MatchParticipantItemPurchases.RemoveRange(existingPurchases);
        if (purchaseRows.Count > 0)
            db.MatchParticipantItemPurchases.AddRange(purchaseRows);

        var existingSkillOrders = await db.MatchParticipantSkillOrders
            .Where(x => x.MatchId == match.Id)
            .ToListAsync(ct);
        if (existingSkillOrders.Count > 0)
            db.MatchParticipantSkillOrders.RemoveRange(existingSkillOrders);
        if (skillRows.Count > 0)
            db.MatchParticipantSkillOrders.AddRange(skillRows);

        return coverageOk;
    }

    private static List<TimelineBuildEvent> ProjectBuildEvents(FramesTimeLine[] frames)
    {
        var events = new List<TimelineBuildEvent>();
        foreach (var frame in frames)
        {
            if (frame?.Events == null)
                continue;

            foreach (var timelineEvent in frame.Events)
            {
                if (timelineEvent == null)
                    continue;

                events.Add(new TimelineBuildEvent(
                    timelineEvent.Type,
                    timelineEvent.ParticipantId,
                    timelineEvent.ItemId,
                    timelineEvent.SkillSlot,
                    timelineEvent.LevelUpType,
                    timelineEvent.Timestamp,
                    timelineEvent.BeforeId,
                    timelineEvent.AfterId));
            }
        }

        return events;
    }

    private async Task<Dictionary<int, BuildItemMetadata>> LoadItemMetadataAsync(
        string patch,
        List<TimelineBuildEvent> events,
        CancellationToken ct)
    {
        var itemIds = events
            .Where(e => e.Type == TimelineBuildParser.ItemPurchasedType || e.Type == TimelineBuildParser.ItemUndoType)
            .SelectMany(e => new[] { e.ItemId, e.AfterId, e.BeforeId })
            .Where(id => id is > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (itemIds.Count == 0)
            return new Dictionary<int, BuildItemMetadata>();

        return await db.ItemVersions
            .AsNoTracking()
            .Where(iv => iv.PatchVersion == patch && itemIds.Contains(iv.ItemId))
            .Select(iv => new
            {
                iv.ItemId,
                iv.BuildsFrom,
                iv.BuildsInto,
                iv.Tags,
                iv.InStore,
                iv.PriceTotal
            })
            .ToDictionaryAsync(
                iv => iv.ItemId,
                iv => new BuildItemMetadata(iv.BuildsFrom, iv.BuildsInto, iv.Tags, iv.InStore, iv.PriceTotal),
                ct);
    }

    private static bool TryResolveRegionalRoute(string matchId, out RegionalRoute regionalRoute)
    {
        var prefix = matchId.Split('_')[0].ToUpperInvariant();
        if (Enum.TryParse<PlatformRoute>(prefix, true, out var platform))
        {
            regionalRoute = platform.ToRegional();
            return true;
        }

        regionalRoute = default;
        return false;
    }
}
