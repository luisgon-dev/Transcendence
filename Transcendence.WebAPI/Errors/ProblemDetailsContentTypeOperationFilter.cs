using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Transcendence.WebAPI.Errors;

/// <summary>
/// Keeps the generated contract aligned with ASP.NET Core's RFC 7807 response media type.
/// Swashbuckle otherwise expands controller response metadata into the ordinary JSON and text
/// formatter types even when the response schema is ProblemDetails.
/// </summary>
public sealed class ProblemDetailsContentTypeOperationFilter : IOperationFilter
{
    private const string ProblemJson = "application/problem+json";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.Responses is null)
            return;

        foreach (var (statusCode, response) in operation.Responses)
        {
            if (!int.TryParse(statusCode, out var status) || status < 400 || response.Content is null)
                continue;

            var problemMediaType = response.Content.Values.FirstOrDefault(mediaType =>
                mediaType.Schema is OpenApiSchemaReference reference &&
                (reference.Reference.Id == nameof(ProblemDetails) ||
                 reference.Reference.Id == nameof(ValidationProblemDetails)));

            if (problemMediaType is null)
                continue;

            response.Content.Clear();
            response.Content.Add(ProblemJson, problemMediaType);
        }
    }
}
