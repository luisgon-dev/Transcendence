using System.Text.Json;
using Camille.Enums;
using FluentAssertions;
using Moq;
using Transcendence.Service.Core.Services.RiotApi.Implementations;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;

namespace Transcendence.Service.Core.Tests;

/// <summary>
/// Rank is read through an enum-free model on purpose: Riot extends <c>queueType</c> without updating
/// the schema Camille generates from, and its generated enum threw on the whole array over one
/// unmodelled entry. These cover both halves of that — the payload binding, which is where it used to
/// fail, and the mapping onto <c>Rank</c>, whose columns are strings.
/// </summary>
public class RankServiceTests
{
    private static RankService BuildService(params LeagueEntryRaw[] entries)
    {
        var client = new Mock<ILeagueEntriesClient>();
        client.Setup(c => c.GetLeagueEntriesAsync(
                It.IsAny<string>(), It.IsAny<PlatformRoute>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);
        return new RankService(client.Object);
    }

    [Fact]
    public void BindsAQueueTypeCamillesEnumDoesNotModel()
    {
        // Both of these are live in prod and absent from Camille's queueTypes.json; the generated
        // QueueType enum threw here, taking the account's Solo entry down with it.
        const string body = """
        [
          {"queueType":"RANKED_SOLO_5x5","tier":"CHALLENGER","rank":"I","leaguePoints":1200,"wins":300,"losses":200},
          {"queueType":"JADE_RANKED_SOLO_5x5","tier":"GOLD","rank":"II","leaguePoints":50,"wins":10,"losses":5},
          {"queueType":"RANKED_PREMADE_5x5","tier":"SILVER","rank":"IV","leaguePoints":8,"wins":2,"losses":3}
        ]
        """;

        var entries = JsonSerializer.Deserialize<LeagueEntryRaw[]>(body);

        entries.Should().NotBeNull().And.HaveCount(3);
        entries!.Select(e => e.QueueType).Should()
            .Equal("RANKED_SOLO_5x5", "JADE_RANKED_SOLO_5x5", "RANKED_PREMADE_5x5");
        entries[1].Tier.Should().Be("GOLD");
        entries[2].LeaguePoints.Should().Be(8);
    }

    [Fact]
    public async Task KeepsTheRealQueueNameRatherThanCollapsingItToUnknown()
    {
        // Upstream's unreleased fix maps unmodelled values to QueueType.UNKNOWN. That would overwrite
        // ~30k rows of real queue names, so the string is carried through verbatim instead.
        var service = BuildService(
            new LeagueEntryRaw("JADE_RANKED_SOLO_5x5", "GOLD", "II", 50, 10, 5));

        var ranks = await service.GetRankedDataAsync("PUUID-1", PlatformRoute.NA1);

        ranks.Should().ContainSingle();
        ranks[0].QueueType.Should().Be("JADE_RANKED_SOLO_5x5");
        ranks[0].Tier.Should().Be("GOLD");
        ranks[0].RankNumber.Should().Be("II");
        ranks[0].LeaguePoints.Should().Be(50);
        ranks[0].Wins.Should().Be(10);
        ranks[0].Losses.Should().Be(5);
    }

    [Fact]
    public async Task MapsEveryEntryRatherThanDroppingTheAccount()
    {
        var service = BuildService(
            new LeagueEntryRaw("RANKED_SOLO_5x5", "CHALLENGER", "I", 1200, 300, 200),
            new LeagueEntryRaw("RANKED_FLEX_SR", "DIAMOND", "III", 40, 20, 18));

        var ranks = await service.GetRankedDataAsync("PUUID-1", PlatformRoute.NA1);

        ranks.Select(r => r.QueueType).Should().Equal("RANKED_SOLO_5x5", "RANKED_FLEX_SR");
    }

    [Fact]
    public async Task ReturnsEmptyForAnAccountWithNoRankedEntries()
    {
        var service = BuildService();

        (await service.GetRankedDataAsync("PUUID-none", PlatformRoute.NA1)).Should().BeEmpty();
    }

    [Fact]
    public async Task SubstitutesEmptyStringsForAbsentFields()
    {
        // tier/rank are absent on some entries; the Rank columns are non-null.
        var service = BuildService(new LeagueEntryRaw(null, null, null, 0, 0, 0));

        var ranks = await service.GetRankedDataAsync("PUUID-1", PlatformRoute.NA1);

        ranks[0].QueueType.Should().BeEmpty();
        ranks[0].Tier.Should().BeEmpty();
        ranks[0].RankNumber.Should().BeEmpty();
    }
}
