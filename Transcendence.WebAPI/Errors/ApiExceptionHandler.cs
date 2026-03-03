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

        logger.LogError(
            exception,
            "Request {RequestId} failed with unhandled exception for {Method} {Path}.",
            httpContext.TraceIdentifier,
            httpContext.Request.Method,
            httpContext.Request.Path);

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
