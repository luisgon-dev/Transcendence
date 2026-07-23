using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Transcendence.Data.Models.Auth;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;

namespace Transcendence.IntegrationTests;

/// <summary>
/// Exercises the real access-control boundary end-to-end through the actual middleware pipeline
/// (authentication schemes + authorization policies), which the unit tests bypass with hand-built
/// principals. For each protection tier we assert: no credentials → 401, wrong credentials →
/// 401/403, correct credentials → the request is authorized (never 401/403).
///
/// Credentials are minted through the app's own machinery so the tests fail if that machinery breaks:
/// a real DB-backed API key (hashed lookup path) and real <see cref="IJwtService"/> tokens signed with
/// the host's own key.
/// </summary>
[Collection(PostgresIntegrationCollection.Name)]
public sealed class AuthorizationBoundaryTests(PostgresIntegrationFixture fixture)
{
    private const string PublicEndpoint = "/api/lol/analytics/regions";
    private const string AppOnlyEndpoint = "/api/lol/summoners/multi-search";               // POST
    private const string UserOnlyEndpoint = "/api/users/me/favorites";                       // GET
    private const string AdminOnlyEndpoint = "/api/admin/pro-summoners";                     // GET

    // ---- public ----

    [Fact]
    public async Task Public_Endpoint_IsReachable_WithoutCredentials()
    {
        var response = await fixture.Factory.CreateClient().GetAsync(PublicEndpoint);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- AppOnly (X-API-Key) ----

    [Fact]
    public async Task AppOnly_WithoutApiKey_Is401()
    {
        var response = await PostAppOnlyRequestAsync(fixture.Factory.CreateClient());
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AppOnly_WithInvalidApiKey_Is401()
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "trn_not_a_real_key");
        var response = await PostAppOnlyRequestAsync(client);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AppOnly_WithValidApiKey_IsAuthorized()
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", await CreateApiKeyAsync());
        var response = await PostAppOnlyRequestAsync(client);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- UserOnly (JWT) ----

    [Fact]
    public async Task UserOnly_WithoutJwt_Is401()
    {
        var response = await fixture.Factory.CreateClient().GetAsync(UserOnlyEndpoint);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UserOnly_WithValidUserJwt_IsAuthorized()
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(MintToken());
        var response = await client.GetAsync(UserOnlyEndpoint);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- AdminOnly (JWT + admin role) ----

    [Fact]
    public async Task AdminOnly_WithoutJwt_Is401()
    {
        var response = await fixture.Factory.CreateClient().GetAsync(AdminOnlyEndpoint);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminOnly_WithNonAdminUserJwt_Is403()
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(MintToken());
        var response = await client.GetAsync(AdminOnlyEndpoint);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminOnly_WithAdminJwt_IsAuthorized()
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(MintToken(SystemRoles.Admin));
        var response = await client.GetAsync(AdminOnlyEndpoint);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- credential minting through the app's own services ----

    private static Task<HttpResponseMessage> PostAppOnlyRequestAsync(HttpClient client) =>
        client.PostAsJsonAsync(AppOnlyEndpoint, new { region = "NA1", summoners = Array.Empty<object>() });

    private async Task<string> CreateApiKeyAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var apiKeys = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var created = await apiKeys.CreateAsync(new ApiKeyCreateRequest("integration-test"), CancellationToken.None);
        return created.PlaintextKey;
    }

    private string MintToken(params string[] roles)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtService>();
        return jwt.GenerateAccessToken(new UserAccount
        {
            Id = Guid.NewGuid(),
            Email = "integration@test.local",
            EmailNormalized = "INTEGRATION@TEST.LOCAL",
            Roles = roles.Select(role => new UserRole { Role = role }).ToList()
        });
    }

    private static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);
}
