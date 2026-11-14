namespace Transcendence.Service.Core.Services.Auth.Interfaces;

public interface IAdminBootstrapService
{
    Task<bool> GrantAsync(Guid userAccountId, CancellationToken ct = default);
}
