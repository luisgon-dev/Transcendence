using Camille.Enums;
using Camille.RiotGames;
using Camille.RiotGames.MatchV5;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Analytics;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.RiotApi;
using DataMatch = Transcendence.Data.Models.LoL.Match.Match;

namespace Transcendence.Service.Core.Services.Jobs;

// No [DisableConcurrentExecution]: that attribute takes a single global lock keyed on the job
// method, serializing ALL timeline ingestion to one match at a time across every worker — under a
// high-throughput backfill the other workers just fail on the distributed-lock timeout. Each match
// is independent and the per-match write is idempotent (delete-then-AddRange), and the backfill's
// re-attempt cooldown prevents the same match being enqueued twice, so full worker parallelism is safe.
public class MatchTimelineIngestionJob(
    TranscendenceContext db,
    LeagueRiotApiContext riotApiContext,
    IBackgroundJobClient backgroundJobClient,
    IRiotRateGate rateGate,
    IOptions<TimelineIngestionOptions> options,
    IOptions<BuildLabModelingOptions> buildLabOptions,
    ILogger<MatchTimelineIngestionJob> logger)
{
    /// <summary>
    /// What every ingest captures regardless of configuration: ordered item purchases + skill orders.
    /// </summary>
    public const int BaselineTimelineSchemaVersion = 1;

    /// <summary>
    /// The Build Lab schema: one-minute feature frames, lossless item lifecycle events, raw event
    /// payloads, and rank observation provenance. Every one of those is gated on
    /// <c>Analytics:BuildLab:Enabled</c>, so this version is stamped ONLY when the flag was on for the
    /// ingest — a row at this version is therefore proof the extras are present, which is what the
    /// generation cohort filter relies on.
    /// </summary>
    public const int CurrentTimelineSchemaVersion = 2;

    /// <summary>
    /// The schema version an ingest targets, and therefore the staleness bar for re-ingestion.
    /// Deliberately flag-dependent: with Build Lab off nothing derives the v2 extras, so treating
    /// already-ingested v1 matches as stale would re-fetch every timeline on the active patch from a
    /// low-rate Riot key for data that would not be captured. Turning the flag on raises the bar to
    /// v2, which is what makes the backfill re-ingest those matches once — no const bump needed.
    /// </summary>
    public static int TargetSchemaVersion(bool buildLabEnabled) =>
        buildLabEnabled ? CurrentTimelineSchemaVersion : BaselineTimelineSchemaVersion;

    // Camille's event union serializes every member of every event shape, so the default options
    // write ~40 mostly-null fields per row into jsonb; dropping nulls collapses each payload to the
    // fields the event actually carries.
    private static readonly JsonSerializerOptions PayloadSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Queue(HangfireQueues.TimelineIngest)]
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

        // Race-safe get-or-create. The ingestion and backfill paths can target the same match
        // concurrently; both would find no row and Add one, colliding on PK_MatchTimelineFetchStates
        // (23505) at SaveChanges and burning a Hangfire retry. Insert idempotently first
        // (ON CONFLICT DO NOTHING) so the row is guaranteed to exist, then load it tracked and fall
        // through to the normal update path — the later field writes become last-writer-wins UPDATEs
        // (benign) instead of a PK INSERT collision. The seeded columns are the non-nullable ones with
        // no DB default; their values match `new MatchTimelineFetchState{}` (Unfetched / 0 / 0).
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO "MatchTimelineFetchStates" ("MatchId", "Status", "RetryCount", "SchemaVersion")
             VALUES ({match.Id}, 0, 0, 0)
             ON CONFLICT ("MatchId") DO NOTHING
             """, ct);

        var state = await db.MatchTimelineFetchStates
            .IgnoreQueryFilters()
            .FirstAsync(x => x.MatchId == match.Id, ct);

        var maxRetryAttempts = Math.Max(1, jobOptions.MaxRetryAttempts);
        var buildLabEnabled = buildLabOptions.Value.Enabled;
        var targetSchemaVersion = TargetSchemaVersion(buildLabEnabled);
        if (state.Status == MatchTimelineFetchStatus.Success && state.SchemaVersion >= targetSchemaVersion)
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
            // Pace under the per-region Riot budget; if it's exhausted, leave this match for the next
            // sweep rather than consuming budget the analytics match-fetch needs more.
            if (!await rateGate.AcquireAsync(regionalRoute.ToString(), ct))
            {
                logger.LogDebug("Riot rate gate skipped timeline for {MatchId} ({Region}); will retry later.", matchId, regionalRoute);
                return;
            }

            state.LastAttemptAtUtc = DateTime.UtcNow;

            var timeline = await riotApiContext.Api.MatchV5()
                .GetTimelineAsync(regionalRoute, matchId, ct);

            if (timeline?.Info?.Frames == null || timeline.Info.Frames.Length == 0)
                throw new InvalidOperationException("Timeline response did not include frames.");

            BackfillParticipantIdsFromTimeline(match.Participants, timeline.Info.Participants);

            // Build Lab needs every one-minute frame for leak-free pre-decision modeling, but that
            // doubles the ~22.5M-row snapshot table, and the only read path (the profile curve)
            // projects even minutes plus the anchor. So the one-minute cadence is coupled to the
            // feature flag: with Build Lab off the configured FrameIntervalMinutes still governs.
            var anchorMark = Math.Max(1, jobOptions.MinuteMark);
            var frameIntervalMinutes = buildLabEnabled ? 1 : Math.Max(1, jobOptions.FrameIntervalMinutes);
            var minuteMarks = BuildMinuteMarks(match.Duration, frameIntervalMinutes, anchorMark);

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

            // Serialize the snapshot/build-path rewrite for THIS match. The ingestion and backfill
            // paths can enqueue the same match concurrently, and two workers interleaving their
            // delete-then-insert collide on the snapshot / purchase / skill primary keys (burning the
            // retry budget). A per-match Postgres advisory lock, auto-released at transaction end,
            // serializes same-match writes while keeping DIFFERENT matches fully parallel — the whole
            // reason this job deliberately avoids a global [DisableConcurrentExecution].
            await using var writeTx = await db.Database.BeginTransactionAsync(ct);
            if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({matchId}, 0))", ct);
            }

            // Replace the whole snapshot set for this match so re-ingestion is idempotent.
            var existingSnapshots = await db.MatchParticipantTimelineSnapshots
                .Where(x => x.MatchId == match.Id)
                .ToListAsync(ct);
            if (existingSnapshots.Count > 0)
                db.MatchParticipantTimelineSnapshots.RemoveRange(existingSnapshots);

            db.MatchParticipantTimelineSnapshots.AddRange(snapshots);

            // Derive and stage the ordered build path (item purchases + skill orders) from the same
            // frames; committed atomically with the snapshots by the SaveChangesAsync below.
            var buildPathCoverageOk = await StageBuildPathRowsAsync(match, timeline.Info.Frames, buildLabEnabled, ct);
            if (buildLabEnabled)
                await StageTimelineEventPayloadsAsync(match, timeline.Info.Frames, ct);

            state.Status = MatchTimelineFetchStatus.Success;
            state.RetryCount = 0;
            state.LastError = null;
            state.LastSuccessAtUtc = DateTime.UtcNow;
            state.SourcePatch = match.Patch;
            // Only mark the build-path schema as captured when item metadata was available; otherwise
            // leave it stale so the backfill re-ingests once metadata exists (snapshots/skills still land).
            // Assigned (not raised to a maximum) on purpose: re-ingesting a previously-v2 match with the
            // flag off rewrites its snapshots at the coarse cadence, so the row must stop claiming the
            // Build Lab extras and fall out of the cohort until it is ingested with the flag on again.
            if (buildPathCoverageOk)
                state.SchemaVersion = targetSchemaVersion;

            await db.SaveChangesAsync(ct);
            await writeTx.CommitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (RiotRateLimitHandling.TryGetRetryAfter(ex, out var retryAfter))
        {
            rateGate.Pause(regionalRoute.ToString(), retryAfter);
            state.LastAttemptAtUtc = DateTime.UtcNow;
            state.LastError = "Deferred: Riot returned 429; honoring Retry-After.";
            state.SourcePatch = match.Patch;
            state.Status = MatchTimelineFetchStatus.TemporaryFailure;
            await db.SaveChangesAsync(ct);

            backgroundJobClient.Schedule<MatchTimelineIngestionJob>(
                job => job.IngestMatchTimelineAsync(matchId, CancellationToken.None),
                retryAfter);
            logger.LogWarning(
                "[Timeline] Riot returned 429 for {MatchId} ({Region}); pausing that region for {RetryAfterSeconds:F0}s without consuming a retry attempt.",
                matchId,
                regionalRoute,
                retryAfter.TotalSeconds);
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
                CurrentGold = participantFrame.CurrentGold,
                Xp = participantFrame.Xp,
                Cs = participantFrame.MinionsKilled + participantFrame.JungleMinionsKilled,
                LaneCs = participantFrame.MinionsKilled,
                JungleCs = participantFrame.JungleMinionsKilled,
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
    /// The lossless item lifecycle and rank context rows are Build Lab inputs only, so
    /// <paramref name="buildLabEnabled"/> gates them: with the flag off neither table is touched at all.
    /// </summary>
    private async Task<bool> StageBuildPathRowsAsync(
        DataMatch match,
        FramesTimeLine[] frames,
        bool buildLabEnabled,
        CancellationToken ct)
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
        var lifecycleEvents = events.Count == 0 || !buildLabEnabled
            ? []
            : TimelineBuildParser.BuildItemLifecycle(
                events,
                itemId => itemMetadata.TryGetValue(itemId, out var metadata) ? metadata : (BuildItemMetadata?)null);

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
        var lifecycleRows = lifecycleEvents
            .Select(itemEvent => new MatchParticipantItemEvent
            {
                MatchId = match.Id,
                Match = match,
                ParticipantId = itemEvent.ParticipantId,
                EventIndex = itemEvent.EventIndex,
                EventType = itemEvent.EventType,
                TimestampMs = itemEvent.TimestampMs,
                ItemId = itemEvent.ItemId,
                BeforeId = itemEvent.BeforeId,
                AfterId = itemEvent.AfterId,
                IsBuildRelevant = itemEvent.IsBuildRelevant,
                BuildCategory = itemEvent.BuildCategory
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

        // Left entirely alone when Build Lab is off: not replaced either, so a corpus captured while the
        // flag was on survives a flag-off re-ingest (the SchemaVersion drop keeps it out of modeling).
        if (buildLabEnabled)
        {
            var existingLifecycleEvents = await db.MatchParticipantItemEvents
                .Where(x => x.MatchId == match.Id)
                .ToListAsync(ct);
            if (existingLifecycleEvents.Count > 0)
                db.MatchParticipantItemEvents.RemoveRange(existingLifecycleEvents);
            if (lifecycleRows.Count > 0)
                db.MatchParticipantItemEvents.AddRange(lifecycleRows);
        }

        var existingSkillOrders = await db.MatchParticipantSkillOrders
            .Where(x => x.MatchId == match.Id)
            .ToListAsync(ct);
        if (existingSkillOrders.Count > 0)
            db.MatchParticipantSkillOrders.RemoveRange(existingSkillOrders);
        if (skillRows.Count > 0)
            db.MatchParticipantSkillOrders.AddRange(skillRows);

        if (buildLabEnabled)
            await StageRankContextRowsAsync(match, ct);

        return coverageOk;
    }

    private async Task StageRankContextRowsAsync(DataMatch match, CancellationToken ct)
    {
        var participantSummonerIds = match.Participants
            .Where(participant => participant.ParticipantId > 0)
            .Select(participant => participant.SummonerId)
            .Distinct()
            .ToList();
        var ranks = participantSummonerIds.Count == 0
            ? []
            : await db.Ranks
                .AsNoTracking()
                .Where(rank => participantSummonerIds.Contains(rank.SummonerId) &&
                               (rank.QueueType == "RANKED_SOLO_5x5" ||
                                rank.QueueType == "RANKED_SOLO_5X5" ||
                                rank.QueueType == "RANKED_SOLO_5V5"))
                .OrderByDescending(rank => rank.UpdatedAt)
                .ToListAsync(ct);
        var rankBySummoner = ranks
            .GroupBy(rank => rank.SummonerId)
            .ToDictionary(group => group.Key, group => group.First());
        var matchUtc = DateTimeOffset.FromUnixTimeMilliseconds(match.MatchDate).UtcDateTime;

        var rows = match.Participants
            .Where(participant => participant.ParticipantId > 0)
            .Select(participant =>
            {
                rankBySummoner.TryGetValue(participant.SummonerId, out var rank);
                return new MatchParticipantRankContext
                {
                    MatchId = match.Id,
                    Match = match,
                    ParticipantId = participant.ParticipantId,
                    Tier = rank?.Tier,
                    Division = rank?.RankNumber,
                    LeaguePoints = rank?.LeaguePoints,
                    ObservedAtUtc = rank?.UpdatedAt,
                    // Signed on purpose: a rank observed after the match is a post-outcome variable,
                    // and an absolute distance makes it indistinguishable from a pre-match reading.
                    ObservationOffsetSeconds = rank == null
                        ? null
                        : (long)(rank.UpdatedAt - matchUtc).TotalSeconds,
                    Source = rank == null ? null : "STORED_SOLO_RANK"
                };
            })
            .ToList();

        var existing = await db.MatchParticipantRankContexts
            .Where(x => x.MatchId == match.Id)
            .ToListAsync(ct);
        if (existing.Count > 0)
            db.MatchParticipantRankContexts.RemoveRange(existing);
        if (rows.Count > 0)
            db.MatchParticipantRankContexts.AddRange(rows);
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

    private async Task StageTimelineEventPayloadsAsync(
        DataMatch match,
        FramesTimeLine[] frames,
        CancellationToken ct)
    {
        var rows = frames
            .Where(frame => frame?.Events != null)
            .SelectMany(frame => frame.Events ?? [])
            .Where(timelineEvent => timelineEvent != null &&
                                    TimelineBuildParser.IsPersistedPayloadEvent(timelineEvent.Type))
            .OrderBy(timelineEvent => timelineEvent.Timestamp)
            .Select((timelineEvent, index) => new MatchTimelineEventPayload
            {
                MatchId = match.Id,
                Match = match,
                EventIndex = index,
                TimestampMs = (int)timelineEvent.Timestamp,
                EventType = timelineEvent.Type ?? "UNKNOWN",
                PayloadJson = JsonSerializer.Serialize(timelineEvent, PayloadSerializerOptions)
            })
            .ToList();

        // Set-based: nothing reads the prior payloads, so materializing them into the change tracker
        // only to mark them Deleted is pure overhead on a per-match hot path.
        await db.MatchTimelineEventPayloads
            .Where(row => row.MatchId == match.Id)
            .ExecuteDeleteAsync(ct);
        if (rows.Count > 0)
            db.MatchTimelineEventPayloads.AddRange(rows);
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
