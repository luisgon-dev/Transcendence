using Microsoft.Extensions.Options;
using Transcendence.Data.Models.Auth;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;

namespace Transcendence.Service.Core.Services.Auth.Implementations;

public class AdminBootstrapService(
    IUserAccountRepository userAccountRepository,
    IOptions<AdminBootstrapOptions> options,
    ILogger<AdminBootstrapService> logger) : IAdminBootstrapService
{
    public async Task<int> EnsureBootstrapAdminsAsync(CancellationToken ct = default)
    {
        var configuredEmails = options.Value.Emails
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(NormalizeEmail)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (configuredEmails.Length == 0)
            return 0;

        var users = await userAccountRepository.ListByEmailNormalizedAsync(configuredEmails, ct);
        var grants = 0;
        foreach (var user in users)
        {
            var alreadyAdmin = user.Roles.Any(x =>
                string.Equals(x.Role, SystemRoles.Admin, StringComparison.Ordinal));
            if (alreadyAdmin)
                continue;

            await userAccountRepository.AddRoleAsync(new UserRole
            {
                UserAccountId = user.Id,
                Role = SystemRoles.Admin,
                GrantedAtUtc = DateTime.UtcNow,
                GrantedBy = "bootstrap:config"
            }, ct);

            grants++;
            logger.LogInformation("Granted admin bootstrap role to {Email}", user.Email);
        }

        if (grants > 0)
            await userAccountRepository.SaveChangesAsync(ct);

        return grants;
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToUpperInvariant();
    }
}
