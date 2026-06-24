namespace Transcendence.Data.Models.LoL.Analytics;

/// <summary>
/// Precomputed atomic win/games aggregate for one
/// <c>(Patch, PlatformRegion, RankTier, ChampionId, Role)</c>. The refresh job UPSERTs these from raw
/// <c>MatchParticipants</c>; the analytics read path rolls them up in SQL (summing over the regions and
/// tiers in the requested scope) to serve win-rates, pick-rate, role-rank and the tier list as fast
/// indexed lookups instead of scanning raw matches.
/// <para>
/// <see cref="RankTier"/> is the summoner's <i>current</i> solo-queue tier (re-derived each refresh from
/// the <c>Ranks</c> table) or <c>"UNRANKED"</c>. <see cref="Games"/>/<see cref="Wins"/> are additive over
/// region, tier, role and champion, which is what makes read-time scope roll-up exact.
/// </para>
/// </summary>
public class ChampionRoleTierStat
{
    public Guid Id { get; set; }

    /// <summary>Game version, e.g. "15.12" — matches <c>Match.Patch</c>.</summary>
    public string Patch { get; set; } = "";

    /// <summary>Summoner platform region, e.g. "NA1"/"EUW1"/"KR" (from <c>Summoner.PlatformRegion</c>).</summary>
    public string PlatformRegion { get; set; } = "";

    /// <summary>Individual current solo-queue tier (IRON…CHALLENGER) or "UNRANKED".</summary>
    public string RankTier { get; set; } = "";

    public int ChampionId { get; set; }

    /// <summary>Assigned lane/role: TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY.</summary>
    public string Role { get; set; } = "";

    public int Games { get; set; }
    public int Wins { get; set; }

    public DateTime ComputedAtUtc { get; set; }
}
