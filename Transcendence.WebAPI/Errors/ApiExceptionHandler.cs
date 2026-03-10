using System.Security.Claims;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Transcendence.Service.Core.Services.Analysis.Exceptions;

namespace Transcendence.WebAPI.Errors;

public sealed class ApiExceptionHandler(
    ILogger<ApiExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (statusCode, title) = exception switch
        {
            SummonerStatsComputationException statsException => (StatusCodes.Status500InternalServerError,
                statsException.Message),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected server error occurred.")
        };

        var routeValues = httpContext.Request.RouteValues
            .Where(pair => pair.Value != null)
            .ToDictionary(pair => pair.Key, pair => pair.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
        var requestIdHeader = httpContext.Request.Headers["x-trn-request-id"].ToString();
        var actorId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var actorName = httpContext.User.Identity?.Name;

        logger.LogError(
            exception,
            "Request {TraceId} failed with unhandled exception for {Method} {Path}. routeValues={RouteValues}, requestIdHeader={RequestIdHeader}, actorId={ActorId}, actorName={ActorName}.",
            httpContext.TraceIdentifier,
            httpContext.Request.Method,
            httpContext.Request.Path,
            routeValues,
            string.IsNullOrWhiteSpace(requestIdHeader) ? null : requestIdHeader,
            actorId,
            actorName);

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title
        };
        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = statusCode;
        var wrote = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception
        });

        if (!wrote)
            await httpContext.Response.WriteAsJsonAsync(problemDetails, ct);

        return true;
    }
}
