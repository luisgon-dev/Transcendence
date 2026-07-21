using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;

namespace Transcendence.Service.Core.Services.Auth.Implementations;

public sealed class SmtpPasswordResetEmailSender(IOptions<PasswordResetOptions> options)
    : IPasswordResetEmailSender
{
    public async Task SendAsync(string recipient, Uri resetUrl, CancellationToken ct = default)
    {
        var smtp = options.Value.Smtp;
        using var message = new MailMessage
        {
            From = new MailAddress(smtp.FromAddress, smtp.FromName),
            Subject = "Reset your Transcendence password",
            Body = $"""
                A password reset was requested for your Transcendence account.

                Open this link to choose a new password:
                {resetUrl.AbsoluteUri}

                This link expires shortly and can only be used once. If you did not request it, you can ignore this email.
                """
        };
        message.To.Add(new MailAddress(recipient));

        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = smtp.EnableSsl
        };
        if (!string.IsNullOrWhiteSpace(smtp.Username))
            client.Credentials = new NetworkCredential(smtp.Username, smtp.Password);

        await client.SendMailAsync(message, ct);
    }
}
