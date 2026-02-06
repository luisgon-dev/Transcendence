using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Transcendence.Service.Core.Services.Auth.Interfaces;

namespace Transcendence.WebAPI.Security;

public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiKeyService apiKeyService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string ApiKeyHeaderName = "X-API-Key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyHeaderName, out var headerValues))
            return AuthenticateResult.NoResult();

        var plaintextKey = headerValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(plaintextKey))
            return AuthenticateResult.Fail("API key header is empty.");

        var validation = await apiKeyService.ValidateAsync(plaintextKey, Context.RequestAborted);
        if (validation == null)
            return AuthenticateResult.Fail("Invalid API key.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, validation.Id.ToString()),
            new(ClaimTypes.Name, validation.Name),
            new(ClaimTypes.Role, "app")
        };

        if (validation.IsBootstrap)
            claims.Add(new Claim("bootstrap", "true"));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
