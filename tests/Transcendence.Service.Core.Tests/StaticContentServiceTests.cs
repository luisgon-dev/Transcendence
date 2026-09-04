using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Transcendence.Service.Core.Services.StaticContent.Implementations;

namespace Transcendence.Service.Core.Tests;

/// <summary>
/// The static-content endpoints exist so clients stop fetching Riot's CDN
/// themselves. These tests pin the parts a client would otherwise have to know:
/// the id/key inversions, the unversioned icon paths, and which failures are the
/// caller's fault versus the upstream's.
/// </summary>
public class StaticContentServiceTests
{
    /// <summary>An HttpClient whose responses are scripted by request path.</summary>
    private sealed class ScriptedHandler(
        Dictionary<string, (HttpStatusCode Status, string Body)> routes) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requested.Add(path);

            var hit = routes.FirstOrDefault(route => path.EndsWith(route.Key, StringComparison.Ordinal));
            if (hit.Key is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(hit.Value.Status)
            {
                Content = new StringContent(hit.Value.Body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static (StaticContentService Service, ScriptedHandler Handler) Build(
        Dictionary<string, (HttpStatusCode, string)> routes)
    {
        var handler = new ScriptedHandler(routes);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        var provider = services.BuildServiceProvider();

        var service = new StaticContentService(
            new SingleClientFactory(handler),
            provider.GetRequiredService<HybridCache>(),
            provider.GetRequiredService<ILogger<StaticContentService>>());

        return (service, handler);
    }

    private const string Versions = """["16.17.1","16.16.1"]""";

    [Fact]
    public async Task GetVersions_ReportsTheNewestAsLatest()
    {
        var (service, _) = Build(new() { ["/api/versions.json"] = (HttpStatusCode.OK, Versions) });

        var result = await service.GetVersionsAsync();

        result.Latest.Should().Be("16.17.1");
        result.Versions.Should().HaveCount(2);
    }

    /// <summary>
    /// Champions are keyed by HANDLE in Data Dragon and carry the numeric id in
    /// `key`. Reading the wrong one yields ids that join to nothing in match data.
    /// </summary>
    [Fact]
    public async Task GetChampions_UsesTheNumericKeyAsIdAndTheHandleForIcons()
    {
        var (service, _) = Build(new()
        {
            ["/api/versions.json"] = (HttpStatusCode.OK, Versions),
            ["champion.json"] = (HttpStatusCode.OK, """
            {"data":{"Ahri":{"id":"Ahri","key":"103","name":"Ahri","title":"the Nine-Tailed Fox","tags":["Mage"]},
                     "MonkeyKing":{"id":"MonkeyKing","key":"62","name":"Wukong","title":"the Monkey King","tags":["Fighter"]}}}
            """)
        });

        var champions = await service.GetChampionsAsync("latest");

        var ahri = champions.Single(champion => champion.Id == 103);
        ahri.Alias.Should().Be("Ahri");
        ahri.Name.Should().Be("Ahri");
        ahri.IconUrl.Should().Be(
            "https://ddragon.leagueoflegends.com/cdn/16.17.1/img/champion/Ahri.png");
        // Splash art is NOT versioned, unlike the icon.
        ahri.SplashUrl.Should().Be(
            "https://ddragon.leagueoflegends.com/cdn/img/champion/splash/Ahri_0.jpg");

        // The handle and the display name genuinely differ for some champions, and
        // the icon must follow the HANDLE. "Wukong.png" does not exist.
        var wukong = champions.Single(champion => champion.Id == 62);
        wukong.Name.Should().Be("Wukong");
        wukong.Alias.Should().Be("MonkeyKing");
        wukong.IconUrl.Should().EndWith("/img/champion/MonkeyKing.png");
    }

    [Fact]
    public async Task GetItems_KeysByTheDocumentKeyAndStripsMarkup()
    {
        var (service, _) = Build(new()
        {
            ["/api/versions.json"] = (HttpStatusCode.OK, Versions),
            ["item.json"] = (HttpStatusCode.OK, """
            {"data":{"3020":{"name":"Sorcerer's Shoes","plaintext":"Enhances <b>Magic</b><br>Penetration",
                             "tags":["Boots"],"gold":{"total":1100,"purchasable":true}},
                     "9999":{"name":"Not In Store","plaintext":"","tags":[],"gold":{"total":0,"purchasable":false}}}}
            """)
        });

        var items = await service.GetItemsAsync("16.17.1");

        var boots = items.Single(item => item.Id == 3020);
        boots.GoldTotal.Should().Be(1100);
        boots.PurchasableInStore.Should().BeTrue();
        boots.IconUrl.Should().Be(
            "https://ddragon.leagueoflegends.com/cdn/16.17.1/img/item/3020.png");
        // Riot ships HTML in these blurbs; a client rendering it as text would show
        // the tags literally.
        boots.Description.Should().Be("Enhances Magic Penetration");

        items.Single(item => item.Id == 9999).PurchasableInStore.Should().BeFalse();
    }

    /// <summary>
    /// A rune page has nine slots, and three of them are stat shards Riot does not
    /// publish in runesReforged.json. Styles are needed too, because a page's
    /// primaryStyleId/subStyleId point at them.
    /// </summary>
    [Fact]
    public async Task GetRunes_IncludesStyles_IndividualRunes_AndStatShards()
    {
        var (service, _) = Build(new()
        {
            ["/api/versions.json"] = (HttpStatusCode.OK, Versions),
            ["runesReforged.json"] = (HttpStatusCode.OK, """
            [{"id":8100,"key":"Domination","name":"Domination","icon":"perk-images/Styles/7200_Domination.png",
              "slots":[{"runes":[{"id":8112,"key":"Electrocute","name":"Electrocute",
                                  "icon":"perk-images/Styles/Domination/Electrocute/Electrocute.png",
                                  "shortDesc":"Hitting a champion with <b>3</b> attacks deals damage."}]}]}]
            """)
        });

        var runes = await service.GetRunesAsync("latest");

        var style = runes.Single(rune => rune.Id == 8100);
        style.IsStyle.Should().BeTrue();
        style.Slot.Should().Be(-1);

        var electrocute = runes.Single(rune => rune.Id == 8112);
        electrocute.IsStyle.Should().BeFalse();
        electrocute.StyleId.Should().Be(8100);
        electrocute.StyleName.Should().Be("Domination");
        electrocute.Slot.Should().Be(0, "the keystone row is slot 0");
        electrocute.Description.Should().Be("Hitting a champion with 3 attacks deals damage.");
        // Rune icons are relative to img/ and are NOT versioned — unlike champions
        // and items. Getting this wrong 404s every rune icon.
        electrocute.IconUrl.Should().Be(
            "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Domination/Electrocute/Electrocute.png");

        // Adaptive force is the shard taken in two of the three rows; without these
        // a client renders three of nine slots as bare numbers.
        var adaptive = runes.Single(rune => rune.Id == 5008);
        adaptive.Name.Should().Be("Adaptive Force");
        adaptive.IconUrl.Should().Contain("/cdn/img/perk-images/StatMods/");
    }

    /// <summary>
    /// THE INVERSION. In summoner.json `id` is the handle ("SummonerFlash") and
    /// `key` is the numeric id the game uses. Match data carries the number.
    /// </summary>
    [Fact]
    public async Task GetSpells_ExposesTheNumericIdThatMatchDataCarries()
    {
        var (service, _) = Build(new()
        {
            ["/api/versions.json"] = (HttpStatusCode.OK, Versions),
            ["summoner.json"] = (HttpStatusCode.OK, """
            {"data":{"SummonerFlash":{"id":"SummonerFlash","key":"4","name":"Flash",
                                      "description":"Teleports a short distance.","image":{"full":"SummonerFlash.png"}}}}
            """)
        });

        var spells = await service.GetSpellsAsync("latest");

        var flash = spells.Should().ContainSingle().Subject;
        flash.Id.Should().Be(4, "4 is what a match participant's spell1Id contains");
        flash.Alias.Should().Be("SummonerFlash");
        flash.IconUrl.Should().EndWith("/img/spell/SummonerFlash.png");
    }

    /// <summary>
    /// The version lands in an upstream URL, so it is validated rather than
    /// trusted. This is also what keeps a caller from steering the fetch.
    /// </summary>
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("16.17.1/../../evil")]
    [InlineData("not-a-version")]
    [InlineData("16.17.1 ")]
    public async Task AVersionThatIsNotVersionShaped_IsRejectedWithoutCallingUpstream(string version)
    {
        var (service, handler) = Build(new()
        {
            ["/api/versions.json"] = (HttpStatusCode.OK, Versions)
        });

        await Assert.ThrowsAsync<InvalidStaticContentVersionException>(
            () => service.GetChampionsAsync(version));

        handler.Requested.Should().BeEmpty("validation must happen before any fetch");
    }

    /// <summary>
    /// An upstream failure must be distinguishable from a bad request: the desktop
    /// app classifies transport outcomes to decide whether to show an outage
    /// screen, and collapsing the two would make a typo look like an outage.
    /// </summary>
    [Fact]
    public async Task AnUpstreamFailure_SurfacesAsUnavailable_NotAsABadRequest()
    {
        var (service, _) = Build(new()
        {
            ["/api/versions.json"] = (HttpStatusCode.OK, Versions),
            ["champion.json"] = (HttpStatusCode.InternalServerError, "{}")
        });

        await Assert.ThrowsAsync<StaticContentUnavailableException>(
            () => service.GetChampionsAsync("latest"));
    }

    [Fact]
    public async Task NoVersionsUpstream_IsUnavailableRatherThanAnEmptyLatest()
    {
        var (service, _) = Build(new() { ["/api/versions.json"] = (HttpStatusCode.OK, "[]") });

        await Assert.ThrowsAsync<StaticContentUnavailableException>(
            () => service.GetVersionsAsync());
    }

    /// <summary>
    /// The point of the endpoint: the CDN is hit once per patch, not once per
    /// caller. A regression here reintroduces exactly the per-install fetching this
    /// replaced.
    /// </summary>
    [Fact]
    public async Task RepeatedRequestsForOneVersion_HitTheCdnOnce()
    {
        var (service, handler) = Build(new()
        {
            ["/api/versions.json"] = (HttpStatusCode.OK, Versions),
            ["champion.json"] = (HttpStatusCode.OK,
                """{"data":{"Ahri":{"id":"Ahri","key":"103","name":"Ahri","title":"","tags":[]}}}""")
        });

        await service.GetChampionsAsync("16.17.1");
        await service.GetChampionsAsync("16.17.1");
        await service.GetChampionsAsync("16.17.1");

        handler.Requested.Count(path => path.EndsWith("champion.json", StringComparison.Ordinal))
            .Should().Be(1);
    }
}
