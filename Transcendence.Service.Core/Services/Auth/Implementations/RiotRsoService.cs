using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Transcendence.Data.Models.Auth;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Auth.Implementations;

public sealed class RiotRsoService(
    IUserAccountRepository userAccountRepository,
    IUserRiotAccountRepository riotAccountRepository,
    IUserAuthService userAuthService,
    IHttpClientFactory httpClientFactory,
    IOptions<RiotRsoOptions> options,
    ILogger<RiotRsoService> logger) : IRiotRsoService
{
    private readonly RiotRsoOptions _options = options.Value;

    private sealed record RiotTokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record RiotAccountResponse(
        [property: JsonPropertyName("puuid")] string? Puuid,
        [property: JsonPropertyName("gameName")] string? GameName,
        [property: JsonPropertyName("tagLine")] string? TagLine);

    public RiotAuthorizationResponse CreateAuthorization(string state)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(state) || state.Length is < 32 or > 256)
            throw new ArgumentException("A valid OAuth state is required.", nameof(state));

        var query = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = _options.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid offline_access",
            ["state"] = state
        };
        var separator = _options.AuthorizationEndpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var encoded = string.Join("&", query.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new RiotAuthorizationResponse($"{_options.AuthorizationEndpoint}{separator}{encoded}");
    }

    public async Task<RiotRsoAuthResponse> CompleteLoginAsync(
        string code,
        string region,
        CancellationToken ct = default)
    {
        var identity = await ExchangeIdentityAsync(code, region, ct);
        var existingLink = await riotAccountRepository.GetByPuuidAsync(identity.Puuid, ct);
        var created = false;
        UserAccount user;
        UserRiotAccount link;

        if (existingLink != null)
        {
            user = existingLink.UserAccount;
            link = existingLink;
            ApplyVerifiedIdentity(link, identity);
            user.DisplayName = $"{identity.GameName}#{identity.TagLine}";
        }
        else
        {
            created = true;
            user = new UserAccount
            {
                Id = Guid.NewGuid(),
                Email = BuildInternalEmail(identity.Puuid),
                EmailNormalized = BuildInternalEmail(identity.Puuid).ToUpperInvariant(),
                PasswordHash = string.Empty,
                DisplayName = $"{identity.GameName}#{identity.TagLine}",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            link = BuildLink(user.Id, identity);
            await userAccountRepository.AddUserAsync(user, ct);
            await riotAccountRepository.AddAsync(link, ct);
        }

        await riotAccountRepository.SaveChangesAsync(ct);
        var tokens = await userAuthService.SignInExternalAsync(user, ct);
        return new RiotRsoAuthResponse(tokens, ToDto(link, !string.IsNullOrWhiteSpace(user.PasswordHash)), created);
    }

    public async Task<RiotAccountLinkDto> CompleteLinkAsync(
        Guid userAccountId,
        string code,
        string region,
        CancellationToken ct = default)
    {
        var identity = await ExchangeIdentityAsync(code, region, ct);
        var linkedPuuid = await riotAccountRepository.GetByPuuidAsync(identity.Puuid, ct);
        if (linkedPuuid != null && linkedPuuid.UserAccountId != userAccountId)
            throw new RiotAccountAlreadyLinkedException();

        var currentLink = await riotAccountRepository.GetByUserIdAsync(userAccountId, ct);
        if (currentLink != null && !string.Equals(currentLink.Puuid, identity.Puuid, StringComparison.Ordinal))
            throw new RiotAccountAlreadyLinkedException();

        var user = await userAccountRepository.GetByIdAsync(userAccountId, ct)
                   ?? throw new RiotRsoExchangeException("The signed-in account no longer exists.");
        var link = currentLink ?? BuildLink(userAccountId, identity);
        ApplyVerifiedIdentity(link, identity);
        user.DisplayName ??= $"{identity.GameName}#{identity.TagLine}";
        if (currentLink == null) await riotAccountRepository.AddAsync(link, ct);
        await riotAccountRepository.SaveChangesAsync(ct);
        return ToDto(link, !string.IsNullOrWhiteSpace(user.PasswordHash));
    }

    public async Task<RiotAccountLinkDto?> GetLinkAsync(Guid userAccountId, CancellationToken ct = default)
    {
        var link = await riotAccountRepository.GetByUserIdAsync(userAccountId, ct);
        return link == null ? null : ToDto(link, !string.IsNullOrWhiteSpace(link.UserAccount.PasswordHash));
    }

    public async Task<bool> UnlinkAsync(Guid userAccountId, CancellationToken ct = default)
    {
        var link = await riotAccountRepository.GetByUserIdAsync(userAccountId, ct);
        if (link == null) return false;

        // Riot-only accounts have no usable password credential. Refuse to strand them without an
        // authentication method; they can add an email/password flow in a future account-settings step.
        if (string.IsNullOrWhiteSpace(link.UserAccount.PasswordHash))
            return false;

        riotAccountRepository.Remove(link);
        await riotAccountRepository.SaveChangesAsync(ct);
        return true;
    }

    private async Task<VerifiedRiotIdentity> ExchangeIdentityAsync(
        string code,
        string region,
        CancellationToken ct)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(code))
            throw new RiotRsoExchangeException("Riot did not return an authorization code.");
        if (!PlatformRouteParser.TryParse(region, out var platform))
            throw new ArgumentException($"Unsupported platform region '{region}'.", nameof(region));

        var client = httpClientFactory.CreateClient(nameof(RiotRsoService));
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, _options.TokenEndpoint);
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _options.RedirectUri
        });

        using var tokenResponse = await client.SendAsync(tokenRequest, ct);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            logger.LogWarning("Riot RSO token exchange failed with status {StatusCode}.", tokenResponse.StatusCode);
            throw new RiotRsoExchangeException("Riot could not verify this sign-in. Please try again.");
        }

        var token = await tokenResponse.Content.ReadFromJsonAsync<RiotTokenResponse>(cancellationToken: ct);
        if (string.IsNullOrWhiteSpace(token?.AccessToken))
            throw new RiotRsoExchangeException("Riot returned an incomplete sign-in response.");

        using var accountRequest = new HttpRequestMessage(HttpMethod.Get, _options.AccountEndpoint);
        accountRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var accountResponse = await client.SendAsync(accountRequest, ct);
        if (!accountResponse.IsSuccessStatusCode)
        {
            logger.LogWarning("Riot RSO account lookup failed with status {StatusCode}.", accountResponse.StatusCode);
            throw new RiotRsoExchangeException("Riot could not return the linked account identity.");
        }

        var account = await accountResponse.Content.ReadFromJsonAsync<RiotAccountResponse>(cancellationToken: ct);
        if (string.IsNullOrWhiteSpace(account?.Puuid) ||
            string.IsNullOrWhiteSpace(account.GameName) ||
            string.IsNullOrWhiteSpace(account.TagLine))
            throw new RiotRsoExchangeException("Riot returned an incomplete account identity.");

        return new VerifiedRiotIdentity(
            account.Puuid.Trim(),
            account.GameName.Trim(),
            account.TagLine.Trim(),
            platform.ToString());
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured()) throw new RiotRsoUnavailableException();
    }

    private static UserRiotAccount BuildLink(Guid userAccountId, VerifiedRiotIdentity identity) => new()
    {
        UserAccountId = userAccountId,
        Puuid = identity.Puuid,
        GameName = identity.GameName,
        TagLine = identity.TagLine,
        PlatformRegion = identity.PlatformRegion,
        LinkedAtUtc = DateTime.UtcNow,
        VerifiedAtUtc = DateTime.UtcNow
    };

    private static void ApplyVerifiedIdentity(UserRiotAccount link, VerifiedRiotIdentity identity)
    {
        link.GameName = identity.GameName;
        link.TagLine = identity.TagLine;
        link.PlatformRegion = identity.PlatformRegion;
        link.VerifiedAtUtc = DateTime.UtcNow;
    }

    private static RiotAccountLinkDto ToDto(UserRiotAccount link, bool canUnlink) => new(
        link.Puuid,
        link.GameName,
        link.TagLine,
        link.PlatformRegion,
        link.LinkedAtUtc,
        link.VerifiedAtUtc,
        canUnlink);

    private static string BuildInternalEmail(string puuid)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(puuid))).ToLowerInvariant();
        return $"riot-{hash[..32]}@rso.invalid";
    }

    private sealed record VerifiedRiotIdentity(
        string Puuid,
        string GameName,
        string TagLine,
        string PlatformRegion);
}
