using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Transcendence.Service.Core.Services.Analysis.Exceptions;
using Transcendence.WebAPI.Errors;

namespace Transcendence.WebAPI.Tests;

public class ApiExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_MapsSummonerStatsExceptionToProblemDetails()
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddProblemDetails()
            .BuildServiceProvider();

        var handler = new ApiExceptionHandler(
            services.GetRequiredService<ILogger<ApiExceptionHandler>>(),
            services.GetRequiredService<IProblemDetailsService>());

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services
        };
        httpContext.Request.Path = "/api/summoners/abc/stats/overview";
        httpContext.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(
            httpContext,
            new SummonerStatsComputationException("Failed to compute overview stats.", new InvalidOperationException()),
            CancellationToken.None);

        handled.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);

        httpContext.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(httpContext.Response.Body);
        json.RootElement.GetProperty("title").GetString().Should().Be("Failed to compute overview stats.");
        json.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task TryHandleAsync_MapsUnknownExceptionToGenericProblemDetails()
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddProblemDetails()
            .BuildServiceProvider();

        var handler = new ApiExceptionHandler(
            services.GetRequiredService<ILogger<ApiExceptionHandler>>(),
            services.GetRequiredService<IProblemDetailsService>());

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services
        };
        httpContext.Request.Path = "/api/example";
        httpContext.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(
            httpContext,
            new Exception("boom"),
            CancellationToken.None);

        handled.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);

        httpContext.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(httpContext.Response.Body);
        json.RootElement.GetProperty("title").GetString().Should().Be("An unexpected server error occurred.");
    }
}
