namespace Transcendence.Data.Models.LoL.Analytics;

/// <summary>A kind of build decision Build Lab counts.</summary>
public enum BuildLabFamily : short
{
    /// <summary>The first shopping trip's starter items, as one set.</summary>
    Starter = 0,
    /// <summary>The Nth completed legendary, conditioned on the legendaries before it.</summary>
    Item = 1,
    /// <summary>The first completed pair of boots.</summary>
    Boots = 2,
    /// <summary>The complete rune page, as one choice.</summary>
    RunePage = 3,
    /// <summary>The rune in slot N; slots after the keystone are conditioned on the keystone.</summary>
    Rune = 4,
    /// <summary>The summoner-spell pair, as one choice.</summary>
    Spells = 5
}

/// <summary>
/// Additive win/game counts for one option at one build decision.
///
/// Every value column is a plain sum, so rows for different patches, regions and lane opponents can
/// be added together at read time and nothing is ever retrained: the refresher increments these in
/// place, one batch of matches at a time, alongside the <see cref="BuildLabProcessedMatch"/> ledger.
///
/// <see cref="GoldBucket"/> splits the counts by team gold difference at the moment of the decision,
/// which is what lets the read side report a gold-adjusted win rate: an item bought while ahead looks
/// better than it is, and standardizing each option to the decision's own gold mix removes that.
/// </summary>
public class BuildLabOptionStat
{
    public int ChampionId { get; set; }
    public string Role { get; set; } = "";
    /// <summary>0 means every lane opponent.</summary>
    public int OpponentChampionId { get; set; }
    /// <summary>A platform region, or <c>ALL</c> for every region.</summary>
    public string Region { get; set; } = "";
    /// <summary>Hash of the ordered choices already made in this family; see <c>BuildLabPath.Hash</c>.</summary>
    public long PrefixHash { get; set; }
    public BuildLabFamily Family { get; set; }
    public short Stage { get; set; }
    public string Patch { get; set; } = "";
    /// <summary>The option's ids joined by '+', e.g. <c>1055+2003</c>.</summary>
    public string ActionKey { get; set; } = "";
    /// <summary>0 (far behind) to 4 (far ahead); pregame decisions are always the middle bucket.</summary>
    public short GoldBucket { get; set; }
    public int Games { get; set; }
    public int Wins { get; set; }
    /// <summary>Sum of decision times in seconds, for the average timing of in-game decisions.</summary>
    public long TimingSecondsSum { get; set; }
}

/// <summary>A match whose decisions are already counted in <see cref="BuildLabOptionStat"/>.</summary>
public class BuildLabProcessedMatch
{
    public Guid MatchId { get; set; }
    public string Patch { get; set; } = "";
    public DateTime ProcessedAtUtc { get; set; }
}
