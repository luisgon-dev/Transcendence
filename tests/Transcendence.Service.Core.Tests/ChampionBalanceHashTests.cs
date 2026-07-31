using System.Text.Json;
using FluentAssertions;
using Transcendence.Service.Core.Services.StaticData.Implementations;

namespace Transcendence.Service.Core.Tests;

/// <summary>
/// Pins the champion balance projection that adaptive patch borrowing depends on. A false positive
/// silently costs borrowing power; a false negative borrows across a real nerf and biases the
/// estimate. The fixtures are trimmed real Data Dragon records so the properties are asserted
/// against the data the job actually ingests.
/// </summary>
public class ChampionBalanceHashTests
{
    private static JsonElement Champion(string json) => JsonDocument.Parse(json).RootElement;

    // Real Data Dragon shape, reduced to the fields the projection reads.
    private const string AhriBase = """
    {
      "id": "Ahri", "key": "103", "name": "Ahri", "partype": "Mana",
      "stats": { "hp": 590, "hpperlevel": 104, "armor": 21, "armorperlevel": 4.2,
                 "attackdamage": 53, "attackspeed": 0.668 },
      "spells": [
        { "id": "AhriQ", "maxrank": 5, "cooldown": [7,7,7,7,7], "cost": [55,65,75,85,95],
          "range": [970,970,970,970,970], "effect": [null,[40,65,90,115,140]] }
      ]
    }
    """;

    [Fact]
    public void CosmeticOnlyDifferencesDoNotChangeTheHash()
    {
        // Data Dragon churns skins and lore constantly. Across 16.13 -> 16.14 a whole-record diff
        // flags 10 of 173 champions on `skins` alone while this projection flags none, so those
        // fields must never reach the hash.
        var withCosmetics = AhriBase.TrimEnd()[..^1] +
            """, "skins": [{"id":"103014","name":"Star Guardian Ahri"}], "lore": "rewritten blurb", "title": "the Nine-Tailed Fox" }""";

        ChampionBalanceHash(withCosmetics).Should().Be(ChampionBalanceHash(AhriBase));
    }

    [Theory]
    [InlineData("\"hp\": 590", "\"hp\": 610", "base stat")]
    [InlineData("\"armorperlevel\": 4.2", "\"armorperlevel\": 5.0", "per-level growth")]
    [InlineData("[7,7,7,7,7]", "[6,6,6,6,6]", "spell cooldown")]
    [InlineData("[55,65,75,85,95]", "[50,60,70,80,90]", "spell cost")]
    [InlineData("[970,970,970,970,970]", "[900,900,900,900,900]", "spell range")]
    [InlineData("[40,65,90,115,140]", "[45,70,95,120,145]", "spell effect")]
    public void EveryBalanceLeverChangesTheHash(string before, string after, string lever)
    {
        var rebalanced = AhriBase.Replace(before, after);
        rebalanced.Should().NotBe(AhriBase, $"the {lever} fixture must actually differ");

        ChampionBalanceHash(rebalanced).Should().NotBe(ChampionBalanceHash(AhriBase), lever);
    }

    [Fact]
    public void DataDragonZeroingAnEffectArrayIsTreatedAsAChange()
    {
        // Riot is migrating spells off `effect` onto dataValues, so an effect array can drop to zeros
        // with no balance change behind it (Warwick did exactly this in 16.15). That is a known
        // over-flag and it is the safe direction: it costs one champion's borrowing for a patch,
        // whereas ignoring `effect` would miss four real changes in the same patch and borrow across
        // them. The empirical drift test is what recovers the lost coverage.
        var zeroed = AhriBase.Replace("[40,65,90,115,140]", "[0,0,0,0,0]");

        ChampionBalanceHash(zeroed).Should().NotBe(ChampionBalanceHash(AhriBase));
    }

    [Fact]
    public void StatOrderingAndFormattingDoNotChangeTheHash()
    {
        // Projection sorts stats and normalises numeric formatting, so key order and 590 vs 590.0
        // must not read as a rebalance.
        var reordered = """
        {
          "id": "Ahri", "key": "103", "name": "Ahri", "partype": "Mana",
          "stats": { "attackspeed": 0.668, "armorperlevel": 4.2, "attackdamage": 53.0,
                     "armor": 21, "hpperlevel": 104, "hp": 590.0 },
          "spells": [
            { "id": "AhriQ", "maxrank": 5, "cooldown": [7,7,7,7,7], "cost": [55,65,75,85,95],
              "range": [970,970,970,970,970], "effect": [null,[40,65,90,115,140]] }
          ]
        }
        """;

        ChampionBalanceHash(reordered).Should().Be(ChampionBalanceHash(AhriBase));
    }

    [Fact]
    public void MissingSpellsOrStatsStillProduceAStableHash()
    {
        var sparse = """{ "id": "X", "key": "1", "name": "X" }""";

        var hash = ChampionBalanceHash(sparse);

        hash.Should().HaveLength(64);
        hash.Should().Be(ChampionBalanceHash(sparse));
    }

    private static string ChampionBalanceHash(string json) =>
        StaticDataService.ComputeChampionBalanceHash(Champion(json));
}
