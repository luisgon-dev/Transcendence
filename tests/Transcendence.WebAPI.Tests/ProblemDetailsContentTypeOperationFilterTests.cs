using FluentAssertions;
using Microsoft.OpenApi;
using Transcendence.WebAPI.Errors;

namespace Transcendence.WebAPI.Tests;

public sealed class ProblemDetailsContentTypeOperationFilterTests
{
    [Theory]
    [InlineData("ProblemDetails")]
    [InlineData("ValidationProblemDetails")]
    public void Apply_PublishesOnlyRfc7807MediaTypeForProblemResponses(string schemaId)
    {
        var mediaType = new OpenApiMediaType
        {
            Schema = new OpenApiSchemaReference(schemaId, null, null)
        };
        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["400"] = new OpenApiResponse
                {
                    Description = "Bad Request",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = mediaType,
                        ["text/plain"] = mediaType
                    }
                }
            }
        };

        new ProblemDetailsContentTypeOperationFilter().Apply(operation, null!);

        operation.Responses["400"].Content.Should().ContainSingle();
        operation.Responses["400"].Content.Should().ContainKey("application/problem+json");
    }

    [Fact]
    public void Apply_LeavesSuccessfulJsonResponseAlone()
    {
        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Description = "OK",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new()
                    }
                }
            }
        };

        new ProblemDetailsContentTypeOperationFilter().Apply(operation, null!);

        operation.Responses["200"].Content.Should().ContainKey("application/json");
    }
}
