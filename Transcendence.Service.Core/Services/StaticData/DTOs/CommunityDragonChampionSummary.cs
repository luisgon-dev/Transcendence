namespace Transcendence.Service.Core.Services.StaticData.DTOs;

/// <summary>
/// One entry of CommunityDragon champion-summary.json. Only the archetype roles are consumed;
/// champion balance numbers come from Data Dragon because CommunityDragon leaves them templated.
/// </summary>
public class CommunityDragonChampionSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // e.g. ["mage", "assassin"] — the pooling archetype for sparse-cell shrinkage.
    public List<string> Roles { get; set; } = [];
}
