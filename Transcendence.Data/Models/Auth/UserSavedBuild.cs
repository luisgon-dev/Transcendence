namespace Transcendence.Data.Models.Auth;

/// <summary>
/// A user's durable Build Lab selection. Estimates are deliberately not copied into this row;
/// reopening the build evaluates it against the current promoted generation.
/// </summary>
public class UserSavedBuild
{
    public Guid Id { get; set; }
    public Guid UserAccountId { get; set; }
    public required UserAccount UserAccount { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ChampionId { get; set; }
    public string Role { get; set; } = string.Empty;
    public int? OpponentChampionId { get; set; }
    public string Patch { get; set; } = string.Empty;
    public string Region { get; set; } = "GLOBAL";
    public string RankingMode { get; set; } = "SUPPORTED";
    public string ItemPathJson { get; set; } = "[]";
    public string RuneSelectionsJson { get; set; } = "[]";
    public int? Spell1Id { get; set; }
    public int? Spell2Id { get; set; }
    public Guid? SourceGenerationId { get; set; }
    /// <summary>
    /// Outcome of the saved item path under <see cref="SourceGenerationId"/>, captured at save time.
    /// Null means no path estimate existed then; it is the baseline a later generation is compared
    /// against so "analytics updated" fires on a material change instead of on every promotion.
    /// </summary>
    public bool? SourceIsPublishable { get; set; }
    public double? SourceAdjustedLift { get; set; }
    public Guid? ShareId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
