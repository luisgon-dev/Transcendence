using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Transcendence.WebAPI.Errors;

namespace Transcendence.WebAPI.Tests;

/// <summary>
/// Runs an <see cref="IActionResult"/> through the app's own result pipeline — the globally registered
/// <see cref="ProblemDetailsErrorBodyFilter"/> followed by the real MVC output formatters — and returns
/// what would actually reach the client. Asserting on the executed response is how a contract test can
/// tell an RFC 7807 <c>application/problem+json</c> body apart from a bare JSON string that merely
/// carries the same status code.
/// </summary>
internal static class ActionResultExecution
{
    internal record ExecutedResponse(int StatusCode, string? ContentType, string Body);

    internal static async Task<ExecutedResponse> ExecuteAsync(IActionResult result)
    {
        var httpContext = new DefaultHttpContext { RequestServices = MvcServices.Instance };
        var body = new MemoryStream();
        httpContext.Response.Body = body;
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        var filter = new ProblemDetailsErrorBodyFilter(
            MvcServices.Instance.GetRequiredService<ProblemDetailsFactory>());
        var resultExecuting = new ResultExecutingContext(
            actionContext, new List<IFilterMetadata>(), result, controller: new object());
        filter.OnResultExecuting(resultExecuting);

        await resultExecuting.Result.ExecuteResultAsync(actionContext);

        return new ExecutedResponse(
            httpContext.Response.StatusCode,
            httpContext.Response.ContentType,
            Encoding.UTF8.GetString(body.ToArray()));
    }
}
