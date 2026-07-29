using Microsoft.Extensions.DependencyInjection;

namespace Transcendence.WebAPI.Tests;

/// <summary>
/// One shared MVC service provider so a controller under test resolves the same
/// <see cref="Microsoft.AspNetCore.Mvc.Infrastructure.ProblemDetailsFactory"/> and output formatters the
/// app registers, rather than a stand-in that could disagree with production behaviour. Required by
/// <c>ControllerBase.ValidationProblem</c> / <c>Problem</c>, which resolve the factory off
/// <c>HttpContext.RequestServices</c>.
/// </summary>
internal static class MvcServices
{
    internal static readonly IServiceProvider Instance = Build();

    private static IServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        services.AddProblemDetails();
        return services.BuildServiceProvider();
    }
}
