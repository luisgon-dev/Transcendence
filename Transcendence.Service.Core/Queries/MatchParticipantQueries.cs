using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Service.Core.Services.Analytics;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Queries;

/// <summary>
/// Composable query-objects for the LoL analytics read surface — see the "Data access" policy in
/// docs/ARCHITECTURE.md. Reusable predicates over <see cref="MatchParticipant"/> live here once and are
/// chained onto an <see cref="IQueryable{T}"/>, instead of being copy-pasted across the analytics/stats
/// services (the ranked-queue filter alone was duplicated 20+ times). Each method is a pure
/// <c>Where(...)</c> that EF Core folds into a single SQL <c>WHERE</c>, so they commute and compose freely.
/// </summary>
public static class MatchParticipantQueries
{
    /// <summary>
    /// Ranked Solo/Duo only. Tolerant of legacy rows that carry the queue as
    /// (<c>QueueId == 0</c>, <c>QueueType == "420"</c>) rather than a populated <c>QueueId</c>.
    /// </summary>
    public static IQueryable<MatchParticipant> InRankedSoloQueue(this IQueryable<MatchParticipant> participants) =>
        participants.Where(mp =>
            mp.Match.QueueId == QueueCatalog.RankedSoloDuoQueueId ||
            (mp.Match.QueueId == 0 &&
             mp.Match.QueueType == QueueCatalog.RankedSoloDuoQueueId.ToString()));

    public static IQueryable<MatchParticipant> InAnalyticsQueue(
        this IQueryable<MatchParticipant> participants,
        string queueFamily) => queueFamily switch
        {
            QueueCatalog.QueueFamilyAram => participants.Where(mp =>
                mp.Match.QueueFamily == QueueCatalog.QueueFamilyAram ||
                mp.Match.QueueId == 450 ||
                (mp.Match.QueueId == 0 && mp.Match.QueueType == "450")),
            QueueCatalog.QueueFamilyArena => participants.Where(mp =>
                mp.Match.QueueFamily == QueueCatalog.QueueFamilyArena ||
                mp.Match.QueueId == 1700 || mp.Match.QueueId == 1710 ||
                mp.Match.QueueId == 1810 || mp.Match.QueueId == 1820 ||
                mp.Match.QueueId == 1830 || mp.Match.QueueId == 1840),
            QueueCatalog.QueueFamilyRankedFlex => participants.Where(mp =>
                mp.Match.QueueFamily == QueueCatalog.QueueFamilyRankedFlex ||
                mp.Match.QueueId == QueueCatalog.RankedFlexQueueId ||
                (mp.Match.QueueId == 0 && mp.Match.QueueType == QueueCatalog.RankedFlexQueueId.ToString())),
            _ => participants.InRankedSoloQueue()
        };

    public static IQueryable<MatchParticipant> WithAnalyticsRole(
        this IQueryable<MatchParticipant> participants,
        string queueFamily) =>
        AnalyticsQueueCatalog.HasRoles(queueFamily) ? participants.WithAssignedRole() : participants;

    public static IQueryable<Rank> InAnalyticsRankQueue(
        this IQueryable<Rank> ranks,
        string queueFamily) =>
        queueFamily == QueueCatalog.QueueFamilyRankedFlex
            ? ranks.Where(rank => rank.QueueType.StartsWith("RANKED_FLEX"))
            : ranks.Where(rank =>
                rank.QueueType == "RANKED_SOLO_5x5" ||
                rank.QueueType == "RANKED_SOLO_5X5" ||
                rank.QueueType == "RANKED_SOLO_5V5");

    /// <summary>Participants whose match is on the given patch.</summary>
    public static IQueryable<MatchParticipant> OnPatch(this IQueryable<MatchParticipant> participants, string patch) =>
        participants.Where(mp => mp.Match.Patch == patch);

    /// <summary>Participants whose match was fully fetched (<see cref="FetchStatus.Success"/>).</summary>
    public static IQueryable<MatchParticipant> FromSuccessfulMatches(this IQueryable<MatchParticipant> participants) =>
        participants.Where(mp => mp.Match.Status == FetchStatus.Success);

    /// <summary>
    /// Restricts to a platform region (e.g. "NA1"). A null/blank region is a no-op — callers pass the
    /// already-normalized region filter, so this absorbs the per-site "if region is set" guard.
    /// </summary>
    public static IQueryable<MatchParticipant> InPlatformRegion(this IQueryable<MatchParticipant> participants, string? platformRegion) =>
        string.IsNullOrWhiteSpace(platformRegion)
            ? participants
            : participants.Where(mp => mp.Summoner.PlatformRegion == platformRegion);

    /// <summary>Participants with an assigned lane/role (non-empty <c>TeamPosition</c>).</summary>
    public static IQueryable<MatchParticipant> WithAssignedRole(this IQueryable<MatchParticipant> participants) =>
        participants.Where(mp => mp.TeamPosition != null && mp.TeamPosition != "");
}
