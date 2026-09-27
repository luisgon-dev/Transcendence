using FluentAssertions;

namespace Transcendence.IntegrationTests;

/// <summary>
/// The WebAPI's /metrics answered 200 with an empty body for as long as Prometheus kept history: the
/// Prometheus exporter (a beta package versioned apart from the OpenTelemetry core) was two minors
/// behind the core and silently exported nothing, so every HTTP latency panel was blank. This boots
/// the real host, serves a request, and requires its request-duration series to be scraped.
/// </summary>
[Collection(PostgresIntegrationCollection.Name)]
public sealed class MetricsEndpointTests(PostgresIntegrationFixture fixture)
{
    [Fact]
    public async Task Metrics_ExportTheRequestsTheApiServed()
    {
        var client = fixture.Factory.CreateClient();
        (await client.GetAsync("/health/live")).EnsureSuccessStatusCode();

        var body = await client.GetStringAsync("/metrics");

        body.Should().Contain("http_server_request_duration_seconds_bucket");
    }
}
