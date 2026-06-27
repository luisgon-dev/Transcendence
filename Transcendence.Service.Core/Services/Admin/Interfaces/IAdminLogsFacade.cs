using Transcendence.WebAPI.Controllers;

namespace Transcendence.Service.Core.Services.Admin.Interfaces;

/// <summary>
/// Encapsulates the operational-log tailing logic extracted verbatim from
/// AdminOperationsController's <c>logs/services</c> action (P10.1). Owns IConfiguration and the
/// allowed service-log keys.
/// </summary>
public interface IAdminLogsFacade
{
    /// <summary>
    /// Tails service log files. Returns a result whose <see cref="AdminServiceLogsLookup.ServiceAllowed"/>
    /// is false when the requested service key is unsupported (the controller maps that to the
    /// original 400 response); otherwise <see cref="AdminServiceLogsLookup.Response"/> holds the payload.
    /// </summary>
    AdminServiceLogsLookup GetServiceLogs(
        string service,
        string? level,
        string? q,
        DateTime? sinceUtc,
        DateTime? untilUtc,
        int limit);
}

/// <summary>
/// Outcome of a service-log lookup. Mirrors the original action's validate-then-respond shape.
/// </summary>
public sealed record AdminServiceLogsLookup(bool ServiceAllowed, AdminServiceLogsResponse? Response);
