using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public sealed class ProRosterDiscoveryJobTests
{
    [Fact]
    public void ParsesActionableLeaguepediaPlayers()
    {
        const string payload = """
            {
              "cargoquery": [
                {
                  "title": {
                    "ID": "Faker",
                    "OverviewPage": "Faker",
                    "PlayerName": "Lee Sang-hyeok",
                    "Team": "T1",
                    "Role": "Mid",
                    "SoloqueueIds": "Hide on bush#KR1"
                  }
                },
                {
                  "title": {
                    "ID": "NoAccount",
                    "OverviewPage": "NoAccount",
                    "Team": "Example",
                    "Role": "Top",
                    "SoloqueueIds": ""
                  }
                }
              ]
            }
            """;

        var players = ProRosterDiscoveryJob.ParsePlayers(payload);

        players.Should().ContainSingle();
        players[0].Should().Be(new ProRosterDiscoveryJob.DiscoveredProPlayer(
            "Faker",
            "Faker",
            "T1",
            "Mid",
            "Hide on bush#KR1"));
    }

    [Fact]
    public void RejectsSourceErrorsEvenWhenTheHttpStatusWasSuccessful()
    {
        const string payload = """{"error":{"code":"ratelimited","info":"Try later"}}""";

        var action = () => ProRosterDiscoveryJob.ParsePlayers(payload);

        action.Should().Throw<InvalidOperationException>().WithMessage("*ratelimited*");
    }

    [Fact]
    public async Task ExecuteAsync_PreservesSuccessfulPagesWhenALaterPageIsThrottled()
    {
        const string firstPage = """
            {
              "cargoquery": [
                {
                  "title": {
                    "ID": "Faker",
                    "Team": "T1",
                    "Role": "Mid",
                    "SoloqueueIds": "Hide on bush#KR1"
                  }
                }
              ]
            }
            """;
        const string throttledPage = """{"error":{"code":"ratelimited","info":"Try later"}}""";
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new SqliteCompatibleTranscendenceContext(dbOptions);
        await db.Database.EnsureCreatedAsync();
        using var httpClient = new HttpClient(new SequenceHandler(firstPage, throttledPage));
        var job = new ProRosterDiscoveryJob(
            httpClient,
            db,
            Options.Create(new ProRosterDiscoveryOptions
            {
                PageSize = 1,
                MaxPages = 2,
                PageDelaySeconds = 0
            }),
            NullLogger<ProRosterDiscoveryJob>.Instance);

        await job.ExecuteAsync(CancellationToken.None);

        var candidate = await db.ProPlayerDiscoveryCandidates.SingleAsync();
        candidate.ExternalId.Should().Be("Faker");
        candidate.Status.Should().Be("pending");
    }

    private sealed class SequenceHandler(params string[] payloads) : HttpMessageHandler
    {
        private int index;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var payload = payloads[Math.Min(index++, payloads.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}
