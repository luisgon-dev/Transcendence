namespace Transcendence.Service.Core.Services.Auth.Interfaces;

public interface IAdminBootstrapService
{
    Task<int> EnsureBootstrapAdminsAsync(CancellationToken ct = default);
}
