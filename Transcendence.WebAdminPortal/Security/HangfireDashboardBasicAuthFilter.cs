using Hangfire.Dashboard;
using System.Security.Cryptography;
using System.Text;

namespace Transcendence.WebAdminPortal.Security;

public sealed class HangfireDashboardBasicAuthFilter(string username, string password) : IDashboardAuthorizationFilter
{
    private readonly string _username = username;
    private readonly string _password = password;

    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        if (!httpContext.Request.Headers.TryGetValue("Authorization", out var authorizationHeaderValues))
        {
            Challenge(httpContext);
            return false;
        }

        var authorizationHeader = authorizationHeaderValues.ToString();
        if (!authorizationHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            Challenge(httpContext);
            return false;
        }

        var encodedCredentials = authorizationHeader["Basic ".Length..].Trim();
        string decodedCredentials;
        try
        {
            decodedCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCredentials));
        }
        catch
        {
            Challenge(httpContext);
            return false;
        }

        var parts = decodedCredentials.Split(':', 2);
        if (parts.Length != 2)
        {
            Challenge(httpContext);
            return false;
        }

        var providedUser = parts[0];
        var providedPass = parts[1];

        if (!FixedTimeEquals(providedUser, _username) || !FixedTimeEquals(providedPass, _password))
        {
            Challenge(httpContext);
            return false;
        }

        return true;
    }

    private static void Challenge(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
        httpContext.Response.Headers.WWWAuthenticate = "Basic realm=\"Hangfire\"";
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        if (aBytes.Length != bBytes.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}

