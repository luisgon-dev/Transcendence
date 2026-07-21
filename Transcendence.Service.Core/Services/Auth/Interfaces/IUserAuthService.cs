using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.Data.Models.Auth;

namespace Transcendence.Service.Core.Services.Auth.Interfaces;

public interface IUserAuthService
{
    Task<AuthTokenResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthTokenResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AuthTokenResponse?> RefreshAsync(RefreshRequest request, CancellationToken ct = default);
    Task LogoutAsync(RefreshRequest request, CancellationToken ct = default);
    Task<AuthTokenResponse> SignInExternalAsync(UserAccount user, CancellationToken ct = default);
}
