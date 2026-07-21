namespace Transcendence.Service.Core.Services.Auth.Models;

public sealed class RiotRsoOptions
{
    public bool Enabled { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = "https://transcend.kronic.one/api/session/riot/callback";
    public string AuthorizationEndpoint { get; set; } = "https://auth.riotgames.com/authorize";
    public string TokenEndpoint { get; set; } = "https://auth.riotgames.com/token";
    public string AccountEndpoint { get; set; } =
        "https://americas.api.riotgames.com/riot/account/v1/accounts/me";

    public bool IsConfigured() =>
        Enabled &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret) &&
        IsSecureAbsoluteUri(RedirectUri) &&
        IsSecureAbsoluteUri(AuthorizationEndpoint) &&
        IsSecureAbsoluteUri(TokenEndpoint) &&
        IsSecureAbsoluteUri(AccountEndpoint);

    private static bool IsSecureAbsoluteUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback;
    }
}
