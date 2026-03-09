using FluentAssertions;
using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.Tft.Static;
using Transcendence.Service.Core.Services.Tft.Implementations;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public class TftStaticDataServiceTests
{
    [Fact]
    public async Task UpdateStaticDataAsync_SelectsHighestSetAndReturnsOnlyActiveCatalogRows()
    {
        await using var harness = await Harness.CreateAsync(CreateSuccessfulFactory("""
        {
          "items": [
            { "apiName": "TFT13_Item_BlueBuff", "name": "Blue Buff", "desc": "Mana", "icon": "item.png", "id": 2, "associatedTraits": [], "incompatibleTraits": [], "composition": [], "tags": [], "unique": false },
            { "apiName": "TFT13_Augment_PandorasBench", "name": "Pandora's Bench", "desc": "Bench", "icon": "augment.png", "associatedTraits": [], "incompatibleTraits": [], "tags": [], "unique": false }
          ],
          "setData": [
            { "number": 11, "name": "Set Eleven", "champions": [], "traits": [], "items": [], "augments": [] },
            {
              "number": 13,
              "name": "Cyber City",
              "mutator": "Hack",
              "champions": [
                { "apiName": "TFT13_SharedHero", "characterName": "Hero", "name": "Hero 13", "cost": 2, "icon": "hero13.png", "traits": ["Arcana"] }
              ],
              "traits": [
                { "apiName": "TFT13_Arcana", "name": "Arcana", "desc": "Arcana desc", "icon": "trait13.png" }
              ],
              "items": ["TFT13_Item_BlueBuff"],
              "augments": ["TFT13_Augment_PandorasBench"]
            }
          ]
        }
        """));
        harness.SeedStoredSet12Data();
        await harness.Db.SaveChangesAsync();

        await harness.Service.UpdateStaticDataAsync(CancellationToken.None);

        var activeSet = await harness.Db.TftSets.SingleAsync(x => x.IsActive);
        activeSet.Number.Should().Be(13);
        activeSet.Name.Should().Be("Cyber City");
        (await harness.Db.TftUnitVersions.CountAsync(x => x.ApiName == "TFT13_SharedHero")).Should().Be(2);

        var catalog = await harness.Service.GetChampionCatalogAsync(CancellationToken.None);
        catalog.Should().ContainSingle();
        catalog[0].Name.Should().Be("Hero 13");

        var lookup = await harness.Service.GetChampionByApiNameAsync("TFT13_SharedHero", CancellationToken.None);
        lookup.Should().NotBeNull();
        lookup!.Name.Should().Be("Hero 13");
    }

    [Fact]
    public async Task UpdateStaticDataAsync_WhenRefreshFailsAndStoredDataExists_DoesNotThrow()
    {
        await using var harness = await Harness.CreateAsync(CreateFailingFactory());
        harness.SeedStoredSet12Data();
        await harness.Db.SaveChangesAsync();

        var act = async () => await harness.Service.UpdateStaticDataAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        (await harness.Service.GetChampionCatalogAsync(CancellationToken.None)).Select(x => x.Name)
            .Should().Contain("Hero 12");
    }

    private static IHttpClientFactory CreateSuccessfulFactory(string json)
    {
        var handler = new StaticResponseHandler(HttpStatusCode.OK, json);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler, disposeHandler: true));
        return factory.Object;
    }

    private static IHttpClientFactory CreateFailingFactory()
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient(new ThrowingHandler(), disposeHandler: true));
        return factory.Object;
    }

    private sealed class Harness : IAsyncDisposable
    {
        private Harness(SqliteConnection connection, SqliteCompatibleTranscendenceContext db, TftStaticDataService service)
        {
            Connection = connection;
            Db = db;
            Service = service;
        }

        public SqliteConnection Connection { get; }
        public SqliteCompatibleTranscendenceContext Db { get; }
        public TftStaticDataService Service { get; }

        public static async Task<Harness> CreateAsync(IHttpClientFactory httpClientFactory)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<TranscendenceContext>()
                .UseSqlite(connection)
                .Options;
            var db = new SqliteCompatibleTranscendenceContext(options);
            await db.Database.EnsureCreatedAsync();

            var service = new TftStaticDataService(db, httpClientFactory, NullLogger<TftStaticDataService>.Instance);
            return new Harness(connection, db, service);
        }

        public void SeedStoredSet12Data()
        {
            Db.TftSets.Add(new TftSet { Number = 12, Name = "Magic n Mayhem", IsActive = true });
            Db.TftUnitVersions.Add(new TftUnitVersion
            {
                Id = Guid.NewGuid(),
                SetNumber = 12,
                ApiName = "TFT13_SharedHero",
                Name = "Hero 12",
                CharacterName = "Hero 12"
            });
            Db.TftTraitVersions.Add(new TftTraitVersion
            {
                Id = Guid.NewGuid(),
                SetNumber = 12,
                ApiName = "TFT12_Arcana",
                Name = "Arcana 12"
            });
            Db.TftItemVersions.Add(new TftItemVersion
            {
                Id = Guid.NewGuid(),
                SetNumber = 12,
                ApiName = "TFT12_Item",
                Name = "Old Item"
            });
            Db.TftAugmentVersions.Add(new TftAugmentVersion
            {
                Id = Guid.NewGuid(),
                SetNumber = 12,
                ApiName = "TFT12_Augment",
                Name = "Old Augment"
            });
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class StaticResponseHandler(HttpStatusCode statusCode, string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("boom");
    }
}
