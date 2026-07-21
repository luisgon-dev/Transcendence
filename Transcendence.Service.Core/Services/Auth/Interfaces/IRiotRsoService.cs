using Transcendence.Service.Core.Services.Auth.Models;

namespace Transcendence.Service.Core.Services.Auth.Interfaces;

public interface IRiotRsoService
{
    RiotAuthorizationResponse CreateAuthorization(string state);
    Task<RiotRsoAuthResponse> CompleteLoginAsync(string code, string region, CancellationToken ct = default);
    Task<RiotAccountLinkDto> CompleteLinkAsync(Guid userAccountId, string code, string region, CancellationToken ct = default);
    Task<RiotAccountLinkDto?> GetLinkAsync(Guid userAccountId, CancellationToken ct = default);
    Task<bool> UnlinkAsync(Guid userAccountId, CancellationToken ct = default);
}

public sealed class RiotRsoUnavailableException : Exception { }
public sealed class RiotRsoExchangeException(string message) : Exception(message) { }
public sealed class RiotAccountAlreadyLinkedException : Exception { }
