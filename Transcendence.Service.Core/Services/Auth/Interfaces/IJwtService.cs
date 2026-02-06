using Transcendence.Data.Models.Auth;

namespace Transcendence.Service.Core.Services.Auth.Interfaces;

public interface IJwtService
{
    string GenerateAccessToken(UserAccount user);
    DateTime GetAccessTokenExpirationUtc();
    string GenerateRefreshToken();
    string HashRefreshToken(string refreshToken);
}
