namespace Transcendence.WebAPI.Security;

public static class AuthPolicies
{
    public const string ApiKeyScheme = "ApiKey";
    public const string AppOnly = "AppOnly";
    public const string UserOnly = "UserOnly";
    public const string AppOrUser = "AppOrUser";
    public const string AdminOnly = "AdminOnly";
}
