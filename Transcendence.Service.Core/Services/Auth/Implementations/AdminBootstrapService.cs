using Transcendence.Data.Models.Auth;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;

namespace Transcendence.Service.Core.Services.Auth.Implementations;

/// <summary>Operator-only bootstrap; never invoked from public authentication.</summary>
public class AdminBootstrapService(IUserAccountRepository users, IAdminAuditService audit) : IAdminBootstrapService
{
    public async Task<bool> GrantAsync(Guid userAccountId, CancellationToken ct = default)
    {
        var user = await users.GetByIdAsync(userAccountId, ct)
            ?? throw new ArgumentException("The target account does not exist.", nameof(userAccountId));
        if (user.Roles.Any(role => role.Role == SystemRoles.Admin)) return false;
        await users.AddRoleAsync(new UserRole
        {
            UserAccountId = user.Id, Role = SystemRoles.Admin,
            GrantedAtUtc = DateTime.UtcNow, GrantedBy = "operator:bootstrap"
        }, ct);
        // Shared scoped DbContext saves the role and audit event in one transaction.
        await audit.WriteAsync(new AdminAuditWriteRequest(null, null, "admin.bootstrap", "UserAccount",
            user.Id.ToString(), null, true, new { source = "operator-command" }), ct);
        return true;
    }
}
