namespace Transcendence.Service.Core.Services.Auth.Models;

public record RiotAuthorizationRequest(string State);
public record RiotAuthorizationResponse(string AuthorizationUrl);
public record RiotRsoCompleteRequest(string Code, string Region);

public record RiotAccountLinkDto(
    string Puuid,
    string GameName,
    string TagLine,
    string PlatformRegion,
    DateTime LinkedAtUtc,
    DateTime VerifiedAtUtc,
    bool CanUnlink
);

public record RiotRsoAuthResponse(
    AuthTokenResponse Tokens,
    RiotAccountLinkDto RiotAccount,
    bool CreatedAccount
);
