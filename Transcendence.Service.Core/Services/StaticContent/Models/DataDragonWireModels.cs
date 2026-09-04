using Transcendence.Service.Core.Services.StaticContent.Models;

namespace Transcendence.Service.Core.Services.StaticContent.Implementations;

/// <summary>
/// Data Dragon's own payload shapes. Internal to this service — the API contract
/// is the <c>Static*Dto</c> family, not these.
/// </summary>
/// <remarks>
/// Kept deliberately partial: only the fields the API actually serves are declared,
/// so a change to some unrelated corner of Riot's payload cannot break
/// deserialization.
/// </remarks>
internal class DataDragonList<T>
{
    public Dictionary<string, T>? Data { get; set; }
}

internal class DataDragonChampion
{
    /// <summary>String handle ("Ahri"). Also the icon filename stem.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Numeric id, as a string. This is what match data carries.</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Title { get; set; }
    public List<string>? Tags { get; set; }
}

internal class DataDragonItem
{
    public string Name { get; set; } = string.Empty;
    public string? Plaintext { get; set; }
    public List<string>? Tags { get; set; }
    public DataDragonItemGold? Gold { get; set; }
}

internal class DataDragonItemGold
{
    public int Total { get; set; }
    public bool Purchasable { get; set; }
}

internal class DataDragonRuneStyle
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public List<DataDragonRuneSlot>? Slots { get; set; }
}

internal class DataDragonRuneSlot
{
    public List<DataDragonRune>? Runes { get; set; }
}

internal class DataDragonRune
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string? ShortDesc { get; set; }
}

internal class DataDragonSpell
{
    /// <summary>String handle ("SummonerFlash").</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Numeric id, as a string. See the inversion note in the service.</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DataDragonImage? Image { get; set; }
}

internal class DataDragonImage
{
    public string? Full { get; set; }
}

/// <summary>
/// Stat shards, which Data Dragon does not publish in <c>runesReforged.json</c>.
/// </summary>
/// <remarks>
/// These ids are stable and long-lived. If Riot adds one, a client renders the
/// fallback for an unknown perk rather than breaking, and this table gains a line.
/// Icons live under a different, unversioned path than every other rune.
/// </remarks>
internal static class StatShards
{
    private const int StatShardStyleId = 5000;
    private const string StatShardStyleName = "Stat Shards";

    private static readonly (int Id, string Name, string Description, string Icon)[] Shards =
    [
        (5001, "Health", "+15-140 health (based on level)", "StatModsHealthScalingIcon.png"),
        (5002, "Armor", "+6 armor", "StatModsArmorIcon.png"),
        (5003, "Magic Resist", "+8 magic resist", "StatModsMagicResIcon.MagicResist_Fix.png"),
        (5005, "Attack Speed", "+10% attack speed", "StatModsAttackSpeedIcon.png"),
        (5007, "Ability Haste", "+8 ability haste", "StatModsCDRScalingIcon.png"),
        (5008, "Adaptive Force", "+9 adaptive force", "StatModsAdaptiveForceIcon.png"),
        (5010, "Move Speed", "+2% move speed", "StatModsMovementSpeedIcon.png"),
        (5011, "Health Scaling", "+10-180 health (based on level)", "StatModsHealthPlusIcon.png"),
        (5013, "Tenacity", "+10% tenacity and slow resist", "StatModsTenacityIcon.png")
    ];

    internal static IEnumerable<StaticRuneDto> All(string cdnBase) =>
        Shards.Select(shard => new StaticRuneDto(
            shard.Id,
            shard.Name.Replace(" ", string.Empty),
            shard.Name,
            shard.Description,
            StatShardStyleId,
            StatShardStyleName,
            Slot: -1,
            IsStyle: false,
            IconUrl: $"{cdnBase}/cdn/img/perk-images/StatMods/{shard.Icon}"));
}
