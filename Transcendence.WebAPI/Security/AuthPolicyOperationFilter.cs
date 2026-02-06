using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Transcendence.WebAPI.Security;

public class AuthPolicyOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var hasAllowAnonymous = context.MethodInfo.GetCustomAttributes(true).OfType<AllowAnonymousAttribute>().Any()
                                || (context.MethodInfo.DeclaringType?.GetCustomAttributes(true)
                                    .OfType<AllowAnonymousAttribute>().Any() ?? false);
        if (hasAllowAnonymous) return;

        var authorizeAttributes = context.MethodInfo.GetCustomAttributes(true).OfType<AuthorizeAttribute>()
            .Concat(context.MethodInfo.DeclaringType?.GetCustomAttributes(true).OfType<AuthorizeAttribute>()
                    ?? Enumerable.Empty<AuthorizeAttribute>())
            .ToList();

        if (authorizeAttributes.Count == 0) return;

        var policies = authorizeAttributes
            .Select(a => a.Policy)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var requiresAppOnly = policies.Contains(AuthPolicies.AppOnly);
        var requiresUserOnly = policies.Contains(AuthPolicies.UserOnly);
        var requiresAppOrUser = policies.Contains(AuthPolicies.AppOrUser) || policies.Count == 0;

        operation.Responses ??= new OpenApiResponses();
        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Unauthorized" });
        operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Forbidden" });
        operation.Security ??= new List<OpenApiSecurityRequirement>();

        if (requiresAppOnly)
        {
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(AuthPolicies.ApiKeyScheme, null, null)] = new List<string>()
            });
            return;
        }

        if (requiresUserOnly)
        {
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, null, null)] =
                    new List<string>()
            });
            return;
        }

        if (requiresAppOrUser)
        {
            // OpenAPI treats each requirement object as OR.
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(AuthPolicies.ApiKeyScheme, null, null)] = new List<string>()
            });

            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, null, null)] =
                    new List<string>()
            });
        }
    }
}
