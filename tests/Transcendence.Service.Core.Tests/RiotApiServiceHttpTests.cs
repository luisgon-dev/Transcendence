using System.Net;
using System.Net.Sockets;
using System.Text;
using Camille.Enums;
using Camille.RiotGames;
using Camille.RiotGames.Util;
using FluentAssertions;
using Moq;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.RiotApi.Implementations;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;

namespace Transcendence.Service.Core.Tests;

/// <summary>
/// HTTP-level tests for the Camille-backed Riot API service clients. Camille exposes no
/// HttpMessageHandler seam, but its config takes a base <c>ApiUrl</c>; we point it at a loopback
/// <see cref="HttpListener"/> stub returning canned status codes + bodies, so the real request/parse/
/// error path runs without touching the network. Retries are disabled so 429/5xx tests are instant.
///
/// Note on 404: Camille treats 404/204/422 as "null-success" — the call returns null rather than
/// throwing — so a not-found surfaces as a null result, which is exactly what the services rely on.
/// A genuinely thrown <see cref="RiotResponseException"/> only happens for non-retryable errors like 403.
/// </summary>
public class RiotApiServiceHttpTests
{
    [Fact]
    public async Task ResolvePuuidAsync_OnSuccess_ParsesAndReturnsPuuid()
    {
        using var stub = new RiotApiStub
        {
            Response = (HttpStatusCode.OK, """{"puuid":"PUUID-123","gameName":"Faker","tagLine":"KR1"}""")
        };
        var service = new RiotAccountService(stub.BuildContext());

        var puuid = await service.ResolvePuuidAsync("Faker", "KR1", PlatformRoute.NA1);

        puuid.Should().Be("PUUID-123");
    }

    [Fact]
    public async Task ResolvePuuidAsync_OnNotFound_ReturnsNull()
    {
        using var stub = new RiotApiStub { Response = (HttpStatusCode.NotFound, null) };
        var service = new RiotAccountService(stub.BuildContext());

        var puuid = await service.ResolvePuuidAsync("Ghost", "NA1", PlatformRoute.NA1);

        puuid.Should().BeNull();
    }

    [Fact]
    public async Task ResolvePuuidAsync_OnForbidden_PropagatesRiotResponseException()
    {
        // A non-null-success, non-retryable status (403, e.g. key revoked) must NOT be swallowed by
        // the service's NotFound-only catch — it propagates so the caller/job can fail loudly.
        using var stub = new RiotApiStub { Response = (HttpStatusCode.Forbidden, null) };
        var service = new RiotAccountService(stub.BuildContext());

        var act = () => service.ResolvePuuidAsync("Faker", "KR1", PlatformRoute.NA1);

        var ex = await act.Should().ThrowAsync<RiotResponseException>();
        ex.Which.GetResponse()!.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetSummonerByRiotIdAsync_WhenAccountNotFound_ThrowsRiotAccountNotFound()
    {
        // AccountV1 404 -> Camille returns a null account -> service maps it to the typed
        // RiotAccountNotFoundException (so the refresh job skips a dead riot-id instead of crashing).
        using var stub = new RiotApiStub { Response = (HttpStatusCode.NotFound, null) };
        var service = new SummonerService(stub.BuildContext(), Mock.Of<IRankService>());

        var act = () => service.GetSummonerByRiotIdAsync("Ghost", "NA1", PlatformRoute.NA1);

        await act.Should().ThrowAsync<RiotAccountNotFoundException>();
    }

    /// <summary>
    /// Loopback <see cref="HttpListener"/> stub. Returns the same canned response to every request
    /// (the tests above each make a single Riot call before asserting). Camille's <c>ApiUrl</c> has no
    /// <c>{0}</c> placeholder, so the regional route is ignored and every call hits this stub verbatim.
    /// </summary>
    private sealed class RiotApiStub : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly string _baseUrl;

        public (HttpStatusCode Status, string? Body) Response { get; set; } = (HttpStatusCode.OK, "{}");

        public RiotApiStub()
        {
            var port = FreeLoopbackPort();
            _baseUrl = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(_baseUrl);
            _listener.Start();
            _ = Task.Run(ServeLoopAsync);
        }

        public LeagueRiotApiContext BuildContext()
        {
            var config = new RiotGamesApiConfig.Builder("FAKE-KEY")
            {
                ApiUrl = _baseUrl,
                Retries = 0,                       // no retry loop — 429/5xx would otherwise sleep seconds
                BackoffStrategy = (_, _, _) => 0.0,
            }.Build();
            return new LeagueRiotApiContext(RiotGamesApi.NewInstance(config));
        }

        private async Task ServeLoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { break; }   // listener stopped

                var (status, body) = Response;
                ctx.Response.StatusCode = (int)status;
                if (!string.IsNullOrEmpty(body))
                {
                    var bytes = Encoding.UTF8.GetBytes(body);
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                }
                ctx.Response.Close();
            }
        }

        private static int FreeLoopbackPort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        public void Dispose()
        {
            try { _listener.Stop(); _listener.Close(); } catch { /* already torn down */ }
        }
    }
}
