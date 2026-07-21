using Transcendence.Service.Core.Services.Auth.Models;

namespace Transcendence.Service.Core.Services.Auth.Interfaces;

public interface IPasswordResetService
{
    Task<bool> InitiateAsync(PasswordResetRequest request, CancellationToken ct = default);
    Task<bool> CompleteAsync(PasswordResetCompleteRequest request, CancellationToken ct = default);
}
