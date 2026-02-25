using Camille.Enums;
using Camille.RiotGames;
using Camille.RiotGames.MatchV5;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.RiotApi;
using DataMatch = Transcendence.Data.Models.LoL.Match.Match;

namespace Transcendence.Service.Core.Services.Jobs;

[DisableConcurrentExecution(timeoutInSeconds: 5 * 60)]
public class MatchTimelineIngestionJob(
    TranscendenceContext db,
    RiotGamesApi riotGamesApi,
    IBackgroundJobClient backgroundJobClient,
    IOptions<TimelineIngestionOptions> options,
    ILogger<MatchTimelineIngestionJob> logger)
{
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
        if (state.Status == MatchTimelineFetchStatus.Success)
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

            var timeline = await riotGamesApi.MatchV5()
                .GetTimelineAsync(regionalRoute, matchId, ct);

            if (timeline?.Info?.Frames == null || timeline.Info.Frames.Length == 0)
                throw new InvalidOperationException("Timeline response did not include frames.");

            var minuteMark = Math.Max(1, jobOptions.MinuteMark);
            var targetTimestampMs = minuteMark * 60 * 1000;
            var selectedFrame = SelectFrameForMinuteMark(timeline.Info.Frames, targetTimestampMs);

            if (selectedFrame == null || selectedFrame.ParticipantFrames == null)
                throw new InvalidOperationException("Timeline frame for minute mark could not be resolved.");

            BackfillParticipantIdsFromTimeline(match.Participants, timeline.Info.Participants);

            var qualityFlags = BuildQualityFlags(match, selectedFrame.Timestamp, targetTimestampMs);
            var snapshots = BuildSnapshots(match, selectedFrame, minuteMark, qualityFlags);
            if (snapshots.Count == 0)
                throw new InvalidOperationException("No participant snapshots could be derived from timeline frame.");

            var existingSnapshots = await db.MatchParticipantTimelineSnapshots
                .Where(x => x.MatchId == match.Id && x.MinuteMark == minuteMark)
                .ToListAsync(ct);
            if (existingSnapshots.Count > 0)
                db.MatchParticipantTimelineSnapshots.RemoveRange(existingSnapshots);

            db.MatchParticipantTimelineSnapshots.AddRange(snapshots);

            state.Status = MatchTimelineFetchStatus.Success;
            state.RetryCount = 0;
            state.LastError = null;
            state.LastSuccessAtUtc = DateTime.UtcNow;
            state.SourcePatch = match.Patch;

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
