namespace Transcendence.Data.Models.LoL.Static;

/// <summary>
/// Per-patch champion balance snapshot. Exists so borrowed-patch analytics can tell whether a
/// champion was actually touched by a patch.
/// </summary>
/// <remarks>
/// <see cref="BalanceHash"/> is the load-bearing column: a hash of a canonical NUMERIC projection
/// (base stats plus each spell's cooldown/cost/range/effect arrays), never prose. Measured against
/// live Data Dragon, that projection flags 0 of 173 champions across a cosmetic-only patch — where a
/// whole-record diff flags 10, all of them `skins` — and 11 of 173 across a real balance patch. A
/// prose-inclusive hash would spuriously mark a champion changed and silently cost borrowing power.
///
/// Sourced from Data Dragon rather than Community Dragon: Community Dragon's champion payload
/// carries unresolved templates (`"@Cooldown@s"`) and zero-filled effect amounts, so its numbers
/// cannot be diffed.
/// </remarks>
public class ChampionVersion
{
    public int ChampionId { get; set; }
    public string PatchVersion { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Hex SHA-256 of the canonical numeric projection. Equality means "not rebalanced".</summary>
    public string BalanceHash { get; set; } = string.Empty;

    /// <summary>
    /// Community Dragon champion roles (e.g. <c>["mage","assassin"]</c>). Analytics pools an item's
    /// effect across champions that share a role before it pools across the whole game, so a sparse
    /// champion borrows strength from its archetype rather than from every champion at once.
    /// </summary>
    public List<string> Roles { get; set; } = [];

    public Patch Patch { get; set; } = null!;
}
