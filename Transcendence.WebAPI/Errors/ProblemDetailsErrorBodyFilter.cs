using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Transcendence.WebAPI.Errors;

/// <summary>
/// Normalizes body-carrying client/server-error responses to RFC 7807 ProblemDetails so every error the
/// API emits has one shape (P7.2). <c>[ApiController]</c> already maps <em>empty-body</em> 4xx/5xx results
/// (e.g. <c>NotFound()</c>) and model-validation failures (<see cref="ValidationProblemDetails"/>) to
/// ProblemDetails, and <see cref="ApiExceptionHandler"/> covers unhandled exceptions. The remaining gap is
/// an action that returns a 4xx/5xx with a <em>string</em> body — e.g. <c>BadRequest("Invalid champion id.")</c>
/// — which ships as a bare JSON string. This filter rewraps that string as the ProblemDetails <c>detail</c>.
///
/// Already-ProblemDetails bodies (including <see cref="ValidationProblemDetails"/>) are left untouched, as
/// are structured object bodies (e.g. the admin endpoints' <c>{ message, detail }</c> shapes), which are
/// already self-describing JSON.
/// </summary>
public sealed class ProblemDetailsErrorBodyFilter(ProblemDetailsFactory problemDetailsFactory) : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is not ObjectResult { StatusCode: >= 400 and <= 599 } result)
            return;

        // Leave already-normalized bodies and structured payloads alone; only bare string bodies are the gap.
        if (result.Value is not string detail)
            return;

        var statusCode = result.StatusCode!.Value;
        var problemDetails = problemDetailsFactory.CreateProblemDetails(
            context.HttpContext,
            statusCode: statusCode,
            detail: detail);

        context.Result = new ObjectResult(problemDetails)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
        // No-op: the response has already been written by the time this runs.
    }
}
