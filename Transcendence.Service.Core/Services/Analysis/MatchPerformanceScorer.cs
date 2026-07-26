using Transcendence.Service.Core.Services.Analysis.Models;

namespace Transcendence.Service.Core.Services.Analysis;

/// <summary>
/// Produces a deterministic, role-tolerant team-impact score from stats already stored
/// on each match participant. Every input is ranked within the participant's own team
/// before weighting, so the score does not compare raw support vision to carry farm.
/// </summary>
internal static class MatchPerformanceScorer
{
    private const double KillParticipationWeight = 0.30;
    private const double DamageWeight = 0.25;
    private const double VisionWeight = 0.15;
    private const double GoldWeight = 0.10;
    private const double CsWeight = 0.10;
    private const double SurvivalWeight = 0.10;

    internal sealed record Input(
        Guid MatchId,
        Guid ParticipantId,
        int TeamId,
        bool Win,
        int Kills,
        int Deaths,
        int Assists,
        int GoldEarned,
        int DamageToChampions,
        int VisionScore,
        int TotalCs,
        int DurationSeconds);

    private sealed record Candidate(
        Input Input,
        double Composite,
        double KillParticipation,
        double DamageShare,
        double GoldShare,
        double VisionShare,
        double CsPerMin);

    public static IReadOnlyDictionary<Guid, MatchPerformanceSummary> Score(IEnumerable<Input> inputs)
    {
        var result = new Dictionary<Guid, MatchPerformanceSummary>();

        foreach (var team in inputs
                     .Where(input => input.TeamId > 0)
                     .GroupBy(input => new { input.MatchId, input.TeamId }))
        {
            var members = team.ToList();
            if (members.Count == 0)
                continue;

            var teamKills = members.Sum(member => Math.Max(0, member.Kills));
            var teamDamage = members.Sum(member => Math.Max(0, member.DamageToChampions));
            var teamGold = members.Sum(member => Math.Max(0, member.GoldEarned));
            var teamVision = members.Sum(member => Math.Max(0, member.VisionScore));

            var killParticipation = members.ToDictionary(
                member => member.ParticipantId,
                member => teamKills > 0
                    ? Math.Clamp((member.Kills + member.Assists) / (double)teamKills, 0, 1)
                    : 0);
            var damageShare = members.ToDictionary(
                member => member.ParticipantId,
                member => teamDamage > 0 ? Math.Max(0, member.DamageToChampions) / (double)teamDamage : 0);
            var goldShare = members.ToDictionary(
                member => member.ParticipantId,
                member => teamGold > 0 ? Math.Max(0, member.GoldEarned) / (double)teamGold : 0);
            var visionShare = members.ToDictionary(
                member => member.ParticipantId,
                member => teamVision > 0 ? Math.Max(0, member.VisionScore) / (double)teamVision : 0);
            var csPerMin = members.ToDictionary(
                member => member.ParticipantId,
                member => member.DurationSeconds > 0
                    ? Math.Max(0, member.TotalCs) / (member.DurationSeconds / 60.0)
                    : 0);

            var candidates = members.Select(member =>
            {
                var composite =
                    KillParticipationWeight * Percentile(killParticipation.Values, killParticipation[member.ParticipantId]) +
                    DamageWeight * Percentile(damageShare.Values, damageShare[member.ParticipantId]) +
                    VisionWeight * Percentile(visionShare.Values, visionShare[member.ParticipantId]) +
                    GoldWeight * Percentile(goldShare.Values, goldShare[member.ParticipantId]) +
                    CsWeight * Percentile(csPerMin.Values, csPerMin[member.ParticipantId]) +
                    SurvivalWeight * Percentile(members.Select(value => -(double)value.Deaths), -member.Deaths);

                return new Candidate(
                    member,
                    composite,
                    killParticipation[member.ParticipantId],
                    damageShare[member.ParticipantId],
                    goldShare[member.ParticipantId],
                    visionShare[member.ParticipantId],
                    csPerMin[member.ParticipantId]);
            }).ToList();

            var ordered = candidates
                .OrderByDescending(candidate => candidate.Composite)
                .ThenByDescending(candidate =>
                    (candidate.Input.Kills + candidate.Input.Assists) / (double)Math.Max(1, candidate.Input.Deaths))
                .ThenBy(candidate => candidate.Input.ParticipantId)
                .ToList();

            for (var index = 0; index < ordered.Count; index++)
            {
                var candidate = ordered[index];
                var rank = index + 1;
                var label = members.Count > 1 && rank == 1
                    ? candidate.Input.Win ? "MVP" : "ACE"
                    : null;

                result[candidate.Input.ParticipantId] = new MatchPerformanceSummary(
                    Math.Round(1 + 9 * candidate.Composite, 1, MidpointRounding.AwayFromZero),
                    rank,
                    members.Count,
                    label,
                    Math.Round(candidate.KillParticipation, 4),
                    Math.Round(candidate.DamageShare, 4),
                    Math.Round(candidate.GoldShare, 4),
                    Math.Round(candidate.VisionShare, 4),
                    Math.Round(candidate.CsPerMin, 2));
            }
        }

        return result;
    }

    private static double Percentile(IEnumerable<double> values, double value)
    {
        const double epsilon = 0.0000001;
        var population = values.ToList();
        if (population.Count <= 1)
            return 0.5;

        var lower = population.Count(candidate => candidate < value - epsilon);
        var equal = population.Count(candidate => Math.Abs(candidate - value) <= epsilon);
        return (lower + (equal - 1) / 2.0) / (population.Count - 1);
    }
}
