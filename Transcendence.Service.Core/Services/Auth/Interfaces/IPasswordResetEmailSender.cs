namespace Transcendence.Service.Core.Services.Auth.Interfaces;

public interface IPasswordResetEmailSender
{
    Task SendAsync(string recipient, Uri resetUrl, CancellationToken ct = default);
}
