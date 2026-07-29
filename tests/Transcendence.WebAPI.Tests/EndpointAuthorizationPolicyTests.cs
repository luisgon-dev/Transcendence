using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Transcendence.WebAPI.Controllers;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Tests;

/// <summary>
/// Pins the access-control surface of the whole API by walking the real MVC route table (every
/// <see cref="ControllerBase"/> in the WebAPI assembly and every HTTP-mapped action on it) instead of
/// hardcoding a handful of representative endpoints. A new endpoint that ships without an
/// <see cref="AuthorizeAttribute"/> fails <see cref="EveryEndpoint_RequiresAPolicy_OrIsAnExplicitlyAnonymousRoute"/>
/// until it is deliberately added to <see cref="AnonymousEndpoints"/>, so unauthenticated routes can only
/// be introduced on purpose. The four Build Lab / saved-build controllers are additionally pinned by
/// name below because they are the newest arrivals.
///
/// What this file asserts is the *declared* policy per route. The runtime meaning of each policy name
/// (no credentials -> 401, non-admin JWT -> 403, admin JWT -> authorized) is exercised end-to-end
/// against the real middleware pipeline by Transcendence.IntegrationTests.AuthorizationBoundaryTests;
/// because policies are shared by name, pinning the name here transitively pins that behaviour.
/// </summary>
public sealed class EndpointAuthorizationPolicyTests
{
    /// <summary>
    /// Every route that is deliberately reachable without credentials, as "METHOD template". Adding an
    /// entry here is the explicit act of declaring a route public — do not add one to silence a failure.
    /// </summary>
    private static readonly HashSet<string> AnonymousEndpoints = new(StringComparer.Ordinal)
    {
        // Static analytics reference data.
        "GET api/lol/analytics/tierlist",
        "GET api/lol/analytics/regions",
        "GET api/lol/analytics/patches",
        "GET api/lol/analytics/status",
        "GET api/lol/analytics/items",
        "GET api/lol/analytics/items/{itemId:int}",
        "GET api/lol/analytics/runes",
        "GET api/lol/analytics/runes/{runeId:int}",
        // Champion analytics reads.
        "GET api/lol/analytics/champions/{championId}/profile",
        "GET api/lol/analytics/champions/{championId}/synergies",
        "GET api/lol/analytics/champions/{championId}/winrates",
        "GET api/lol/analytics/champions/{championId}/builds",
        "GET api/lol/analytics/champions/{championId}/pro-builds",
        "GET api/lol/analytics/champions/{championId}/matchups",
        "GET api/lol/analytics/pro/champions",
        "GET api/lol/analytics/pro/players",
        "GET api/lol/leaderboards",
        // Build Lab read surface: same public tier as the rest of champion analytics.
        "GET api/lol/analytics/build-lab/{championId:int}",
        // Share-token lookup: the token is the credential, so the route itself is anonymous.
        "GET api/lol/saved-builds/{shareId:guid}",
        // Public summoner profile reads.
        "GET api/lol/summoners/search",
        "GET api/lol/summoners/{region}/{name}/{tag}",
        "GET api/lol/summoners/{summonerId:guid}/stats/overview",
        "GET api/lol/summoners/{summonerId:guid}/stats/champions",
        "GET api/lol/summoners/{summonerId:guid}/stats/roles",
        "GET api/lol/summoners/{summonerId:guid}/stats/rank-history",
        "GET api/lol/summoners/{summonerId:guid}/matches/recent",
        "GET api/lol/summoners/{summonerId:guid}/matches/{matchId}",
        "GET api/lol/summoners/{summonerId:guid}/matches/{matchId}/timeline",
        // Credential exchange: authentication cannot require authentication.
        "POST api/auth/riot/authorize",
        "POST api/auth/riot/complete",
        "POST api/auth/register",
        "POST api/auth/login",
        "POST api/auth/refresh",
        "POST api/auth/logout",
        "POST api/auth/password-reset",
        "POST api/auth/password-reset/complete"
    };

    private static readonly IReadOnlyList<ApiEndpoint> Endpoints = DiscoverEndpoints();

    // ---- route-table invariants ----

    [Fact]
    public void EveryEndpoint_RequiresAPolicy_OrIsAnExplicitlyAnonymousRoute()
    {
        var unprotected = Endpoints
            .Where(endpoint => endpoint.IsAnonymous)
            .Where(endpoint => !endpoint.Keys.Any(AnonymousEndpoints.Contains))
            .Select(endpoint => endpoint.Describe())
            .ToList();

        unprotected.Should().BeEmpty(
            "every endpoint must either carry an authorization policy or be a reviewed anonymous route");
    }

    [Fact]
    public void AnonymousAllowlist_HasNoStaleEntries()
    {
        var live = Endpoints.SelectMany(endpoint => endpoint.Keys).ToHashSet(StringComparer.Ordinal);

        AnonymousEndpoints.Except(live).Should().BeEmpty(
            "a removed or renamed route must not keep a standing anonymous exemption");
    }

    [Fact]
    public void EveryEndpoint_UsesOnlyDeclaredAuthPolicyNames()
    {
        var declared = typeof(AuthPolicies)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, FieldType.FullName: "System.String" })
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        var unknown = Endpoints
            .SelectMany(endpoint => endpoint.Policies.Select(policy => $"{policy} <- {endpoint.Describe()}"))
            .Where(entry => !declared.Contains(entry[..entry.IndexOf(" <- ", StringComparison.Ordinal)]))
            .ToList();

        unknown.Should().BeEmpty("an unregistered policy name fails closed with a 500, not a 401");
    }

    [Fact]
    public void EveryAnonymousEndpoint_DeclaresARateLimitPolicy()
    {
        var unmetered = Endpoints
            .Where(endpoint => endpoint.IsAnonymous && endpoint.RateLimitPolicy == null)
            .Select(endpoint => endpoint.Describe())
            .ToList();

        unmetered.Should().BeEmpty("an uncredentialed route with no limiter is a free amplification target");
    }

    // ---- Build Lab + saved builds: pinned by name ----

    [Theory]
    [InlineData("GET", "api/users/me/lol/saved-builds")]
    [InlineData("POST", "api/users/me/lol/saved-builds")]
    [InlineData("PUT", "api/users/me/lol/saved-builds/{savedBuildId:guid}")]
    [InlineData("DELETE", "api/users/me/lol/saved-builds/{savedBuildId:guid}")]
    [InlineData("POST", "api/users/me/lol/saved-builds/{savedBuildId:guid}/repair")]
    [InlineData("POST", "api/users/me/lol/saved-builds/{savedBuildId:guid}/share")]
    [InlineData("DELETE", "api/users/me/lol/saved-builds/{savedBuildId:guid}/share")]
    public void SavedBuildEndpoints_RequireUserOnlyPolicy(string httpMethod, string route)
    {
        var endpoint = Endpoint(httpMethod, route);

        endpoint.IsAnonymous.Should().BeFalse();
        endpoint.Policies.Should().Equal(AuthPolicies.UserOnly);
    }

    [Fact]
    public void EverySavedBuildAction_IsCoveredByTheUserOnlyPolicy()
    {
        var savedBuildEndpoints = Endpoints
            .Where(endpoint => endpoint.Controller == nameof(SavedBuildsController))
            .ToList();

        savedBuildEndpoints.Should().HaveCount(7, "all verbs on the saved-build surface must be pinned");
        savedBuildEndpoints.Should().OnlyContain(endpoint =>
            !endpoint.IsAnonymous && endpoint.Policies.Contains(AuthPolicies.UserOnly));
    }

    [Theory]
    [InlineData("GET", "api/admin/analytics/build-lab")]
    [InlineData("POST", "api/admin/analytics/build-lab/generations/{generationId:guid}/promote")]
    [InlineData("POST", "api/admin/analytics/build-lab/generations/{generationId:guid}/rollback")]
    [InlineData("POST", "api/admin/analytics/build-lab/generations/{generationId:guid}/fail")]
    public void AdminBuildLabEndpoints_RequireAdminOnlyPolicy(string httpMethod, string route)
    {
        var endpoint = Endpoint(httpMethod, route);

        endpoint.IsAnonymous.Should().BeFalse();
        endpoint.Policies.Should().Equal(AuthPolicies.AdminOnly);
    }

    [Fact]
    public void EveryAdminBuildLabAction_IsCoveredByTheAdminOnlyPolicy()
    {
        var adminEndpoints = Endpoints
            .Where(endpoint => endpoint.Controller == nameof(AdminBuildLabController))
            .ToList();

        adminEndpoints.Should().HaveCount(4);
        adminEndpoints.Should().OnlyContain(endpoint =>
            !endpoint.IsAnonymous && endpoint.Policies.Contains(AuthPolicies.AdminOnly));
    }

    [Fact]
    public void AdminBuildLabWrites_AreMeteredByTheAdminWriteLimiter()
    {
        var writes = Endpoints
            .Where(endpoint => endpoint.Controller == nameof(AdminBuildLabController))
            .Where(endpoint => endpoint.HttpMethod != "GET")
            .ToList();

        writes.Should().HaveCount(3);
        writes.Should().OnlyContain(endpoint => endpoint.RateLimitPolicy == "admin-write");
    }

    [Theory]
    [InlineData("GET", "api/lol/analytics/build-lab/{championId:int}")]
    [InlineData("GET", "api/lol/saved-builds/{shareId:guid}")]
    public void PublicBuildLabReads_AreAnonymousAndMetered(string httpMethod, string route)
    {
        var endpoint = Endpoint(httpMethod, route);

        endpoint.IsAnonymous.Should().BeTrue();
        endpoint.Policies.Should().BeEmpty();
        endpoint.RateLimitPolicy.Should().Be("expensive-read");
    }

    // ---- discovery ----

    private static ApiEndpoint Endpoint(string httpMethod, string route)
    {
        var match = Endpoints.SingleOrDefault(endpoint =>
            endpoint.HttpMethod == httpMethod && endpoint.Route == route);
        match.Should().NotBeNull($"the route table must expose {httpMethod} {route}");
        return match!;
    }

    private static IReadOnlyList<ApiEndpoint> DiscoverEndpoints()
    {
        var endpoints = new List<ApiEndpoint>();
        var controllers = typeof(BuildLabAnalyticsController).Assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsPublic: true } && typeof(ControllerBase).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal);

        foreach (var controller in controllers)
        {
            var prefix = (controller.GetCustomAttribute<RouteAttribute>(inherit: true)?.Template ?? string.Empty)
                .Replace("[controller]", ControllerName(controller), StringComparison.Ordinal);
            var controllerPolicies = PolicyNames(controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
            var controllerAnonymous = controller.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) != null;
            var controllerLimiter = controller.GetCustomAttribute<EnableRateLimitingAttribute>(inherit: true)?.PolicyName;

            var actions = controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .Where(method => method.GetCustomAttribute<NonActionAttribute>(inherit: true) == null)
                .OrderBy(method => method.Name, StringComparer.Ordinal);

            foreach (var action in actions)
            {
                foreach (var mapping in action.GetCustomAttributes<HttpMethodAttribute>(inherit: true))
                {
                    var policies = controllerPolicies
                        .Concat(PolicyNames(action.GetCustomAttributes<AuthorizeAttribute>(inherit: true)))
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(policy => policy, StringComparer.Ordinal)
                        .ToArray();
                    var anonymous = controllerAnonymous
                        || action.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) != null
                        || policies.Length == 0;
                    var limiter = action.GetCustomAttribute<EnableRateLimitingAttribute>(inherit: true)?.PolicyName
                        ?? controllerLimiter;
                    if (action.GetCustomAttribute<DisableRateLimitingAttribute>(inherit: true) != null)
                        limiter = null;

                    foreach (var httpMethod in mapping.HttpMethods)
                    {
                        endpoints.Add(new ApiEndpoint(
                            controller.Name,
                            action.Name,
                            httpMethod,
                            CombineRoute(prefix, mapping.Template),
                            policies,
                            anonymous,
                            limiter));
                    }
                }
            }
        }

        return endpoints;
    }

    private static string[] PolicyNames(IEnumerable<AuthorizeAttribute> attributes) =>
        attributes
            .Select(attribute => attribute.Policy)
            .Where(policy => !string.IsNullOrWhiteSpace(policy))
            .Select(policy => policy!)
            .ToArray();

    private static string ControllerName(Type controller) =>
        controller.Name.EndsWith("Controller", StringComparison.Ordinal)
            ? controller.Name[..^"Controller".Length]
            : controller.Name;

    private static string CombineRoute(string prefix, string? template)
    {
        if (string.IsNullOrEmpty(template))
            return prefix.Trim('/');
        if (template.StartsWith('/') || template.StartsWith("~/", StringComparison.Ordinal))
            return template.TrimStart('~').Trim('/');
        return string.IsNullOrEmpty(prefix) ? template.Trim('/') : $"{prefix.Trim('/')}/{template.Trim('/')}";
    }

    private sealed record ApiEndpoint(
        string Controller,
        string Action,
        string HttpMethod,
        string Route,
        IReadOnlyList<string> Policies,
        bool IsAnonymous,
        string? RateLimitPolicy)
    {
        public IEnumerable<string> Keys => [$"{HttpMethod} {Route}"];

        public string Describe() =>
            $"{HttpMethod} {Route} ({Controller}.{Action}, policies=[{string.Join(",", Policies)}], "
            + $"rateLimit={RateLimitPolicy ?? "none"})";
    }
}
