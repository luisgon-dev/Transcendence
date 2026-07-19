using System.Net;
using Camille.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.RiotApi.Implementations;

namespace Transcendence.Service.Core.Tests;

/// <summary>
/// The tolerant League-V4 fallback exists precisely so a <c>queueType</c> Camille's enum doesn't model
/// no longer throws (and no longer drops the whole account's rank). These tests drive it through a stub
/// <see cref="HttpMessageHandler"/> so the real parse/map path runs without the network.
/// </summary>
public class RankFallbackClientTests
{
    private static RankFallbackClient BuildClient(HttpStatusCode status, string? body, bool gateOpen = true)
    {
        var handler = new StubHandler(status, body);
        var http = new HttpClient(handler);
        var gate = new Mock<IRiotRateGate>();
        gate.Setup(g => g.AcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(gateOpen);
        return new RankFallbackClient(http, gate.Object, NullLogger<RankFallbackClient>.Instance);
    }

    [Fact]
    public async Task PreservesUnknownQueueTypeAsStringInsteadOfThrowing()
    {
        // Second entry carries a queueType Camille's QueueType enum does not model — the Camille typed
        // path would throw on the whole array here; the fallback keeps every entry as strings.
        const string body = """
        [
          {"queueType":"RANKED_SOLO_5x5","tier":"CHALLENGER","rank":"I","leaguePoints":1200,"wins":300,"losses":200},
          {"queueType":"RANKED_SOME_NEW_QUEUE_2026","tier":"GOLD","rank":"II","leaguePoints":50,"wins":10,"losses":5}
        ]
        """;
        var client = BuildClient(HttpStatusCode.OK, body);

        var ranks = await client.GetLeagueEntriesTolerantAsync("PUUID-1", PlatformRoute.NA1);

        ranks.Should().HaveCount(2);
        ranks[0].QueueType.Should().Be("RANKED_SOLO_5x5");
        ranks[0].Tier.Should().Be("CHALLENGER");
        ranks[0].LeaguePoints.Should().Be(1200);
        ranks[1].QueueType.Should().Be("RANKED_SOME_NEW_QUEUE_2026"); // preserved, not rejected
        ranks[1].Wins.Should().Be(10);
    }

    [Fact]
    public async Task ReturnsEmptyOnNotFound()
    {
        var client = BuildClient(HttpStatusCode.NotFound, null);

        var ranks = await client.GetLeagueEntriesTolerantAsync("PUUID-none", PlatformRoute.NA1);

        ranks.Should().BeEmpty();
    }

    [Fact]
    public async Task ReturnsEmptyWhenRateGateExhausted()
    {
        // Gate closed -> no call made -> degrade to no-rank this cycle (not an exception).
        var client = BuildClient(HttpStatusCode.OK, "[]", gateOpen: false);

        var ranks = await client.GetLeagueEntriesTolerantAsync("PUUID-1", PlatformRoute.NA1);

        ranks.Should().BeEmpty();
    }

    private sealed class StubHandler(HttpStatusCode status, string? body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status);
            if (body is not null)
                response.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        }
    }
}
