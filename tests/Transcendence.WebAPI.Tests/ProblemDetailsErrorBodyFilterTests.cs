using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Transcendence.WebAPI.Errors;

namespace Transcendence.WebAPI.Tests;

public class ProblemDetailsErrorBodyFilterTests
{
    private static (ProblemDetailsErrorBodyFilter filter, ServiceProvider services) Create()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddControllers();
        sc.AddProblemDetails();
        var services = sc.BuildServiceProvider();
        var filter = new ProblemDetailsErrorBodyFilter(services.GetRequiredService<ProblemDetailsFactory>());
        return (filter, services);
    }

    private static ResultExecutingContext Context(ServiceProvider services, IActionResult result)
    {
        var httpContext = new DefaultHttpContext { RequestServices = services };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ResultExecutingContext(actionContext, new List<IFilterMetadata>(), result, controller: new object());
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status404NotFound)]
    [InlineData(StatusCodes.Status409Conflict)]
    public void StringErrorBody_IsRewrappedAsProblemDetails(int statusCode)
    {
        var (filter, services) = Create();
        var context = Context(services, new ObjectResult("Invalid champion id.") { StatusCode = statusCode });

        filter.OnResultExecuting(context);

        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(statusCode);
        result.ContentTypes.Should().Contain("application/problem+json");
        var problem = result.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(statusCode);
        problem.Detail.Should().Be("Invalid champion id.");
        problem.Title.Should().NotBeNullOrWhiteSpace(); // filled from the status code
    }

    [Fact]
    public void StructuredObjectBody_IsLeftUntouched()
    {
        var (filter, services) = Create();
        var original = new BadRequestObjectResult(new { message = "Unsupported state." });
        var context = Context(services, original);

        filter.OnResultExecuting(context);

        context.Result.Should().BeSameAs(original);
    }

    [Fact]
    public void AlreadyProblemDetailsBody_IsLeftUntouched()
    {
        var (filter, services) = Create();
        var original = new ObjectResult(new ProblemDetails { Status = 400, Detail = "already" }) { StatusCode = 400 };
        var context = Context(services, original);

        filter.OnResultExecuting(context);

        context.Result.Should().BeSameAs(original);
    }

    [Fact]
    public void SuccessStringBody_IsLeftUntouched()
    {
        var (filter, services) = Create();
        var original = new OkObjectResult("fine");
        var context = Context(services, original);

        filter.OnResultExecuting(context);

        context.Result.Should().BeSameAs(original);
    }
}
