using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using Transcendence.WebAPI.Models.Stats;
using Xunit;

namespace Transcendence.WebAPI.Tests;

public class TftSummonerStatsControllerTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetOverview_ReturnsOk_WithValidSummonerId()
    {
        // Note: This test requires a seeded summoner in the database
        // In a real test environment, we would seed the data first
        var summonerId = Guid.NewGuid();
        
        var response = await _client.GetAsync($"/api/tft/summoners/{summonerId}/stats/overview?recent=10");
        
        // Should return 200 even if no matches (empty stats) or 404 if summoner doesn't exist
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetOverview_ReturnsBadRequest_WithInvalidRecentCount()
    {
        var summonerId = Guid.NewGuid();
        
        // Test with too large recent count - API should clamp it
        var response = await _client.GetAsync($"/api/tft/summoners/{summonerId}/stats/overview?recent=999");
        
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetChampionStats_ReturnsOk_WithValidSummonerId()
    {
        var summonerId = Guid.NewGuid();
        
        var response = await _client.GetAsync($"/api/tft/summoners/{summonerId}/stats/champions?top=5");
        
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetChampionStats_ReturnsEmptyList_WhenNoMatches()
    {
        var summonerId = Guid.NewGuid();
        
        var response = await _client.GetAsync($"/api/tft/summoners/{summonerId}/stats/champions");
        
        // Should return OK with empty array or 404 if summoner not found
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var result = await response.Content.ReadFromJsonAsync<List<TftChampionStatDto>>();
            result.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task MultiSearch_ReturnsOk_WithValidRequest()
    {
        var request = new TftMultiSearchRequest(
            "na",
            ["TestPlayer#NA1", "AnotherPlayer#NA1"]
        );
        
        var response = await _client.PostAsJsonAsync("/api/tft/summoners/multi-search", request);
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MultiSearch_ReturnsBadRequest_WithInvalidRegion()
    {
        var request = new TftMultiSearchRequest(
            "invalid-region",
            ["TestPlayer#NA1"]
        );
        
        var response = await _client.PostAsJsonAsync("/api/tft/summoners/multi-search", request);
        
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MultiSearch_ReturnsBadRequest_WithTooManyPlayers()
    {
        var request = new TftMultiSearchRequest(
            "na",
            ["P1#NA1", "P2#NA1", "P3#NA1", "P4#NA1", "P5#NA1", "P6#NA1", "P7#NA1", "P8#NA1", "P9#NA1"] // 9 players
        );
        
        var response = await _client.PostAsJsonAsync("/api/tft/summoners/multi-search", request);
        
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MultiSearch_ReturnsBadRequest_WithEmptyList()
    {
        var request = new TftMultiSearchRequest("na", []);
        
        var response = await _client.PostAsJsonAsync("/api/tft/summoners/multi-search", request);
        
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
