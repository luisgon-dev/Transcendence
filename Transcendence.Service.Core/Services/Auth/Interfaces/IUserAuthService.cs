using Transcendence.Service.Core.Services.Auth.Models;

namespace Transcendence.Service.Core.Services.Auth.Interfaces;

public interface IUserAuthService
{
    Task<AuthTokenResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthTokenResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AuthTokenResponse?> RefreshAsync(RefreshRequest request, CancellationToken ct = default);
    Task InitiatePasswordResetAsync(PasswordResetRequest request, CancellationToken ct = default);
}
