namespace Transcendence.Service.Core.Services.Auth.Models;

public sealed class PasswordResetOptions
{
    public bool Enabled { get; set; }
    public string PublicBaseUrl { get; set; } = "https://transcend.kronic.one";
    public int TokenLifetimeMinutes { get; set; } = 30;
    public SmtpOptions Smtp { get; set; } = new();

    public bool IsConfigured()
    {
        if (!Enabled ||
            !Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var publicBaseUri) ||
            TokenLifetimeMinutes is < 5 or > 1440 ||
            string.IsNullOrWhiteSpace(Smtp.Host) ||
            string.IsNullOrWhiteSpace(Smtp.FromAddress))
            return false;

        // The raw reset token is carried in the URL. Permit plain HTTP only for loopback/local
        // development so production configuration cannot accidentally email bearer tokens in cleartext.
        return publicBaseUri.Scheme == Uri.UriSchemeHttps || publicBaseUri.IsLoopback;
    }
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Transcendence";
}
