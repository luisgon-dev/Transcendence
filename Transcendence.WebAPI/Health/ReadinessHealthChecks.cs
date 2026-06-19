using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using Transcendence.Data;

namespace Transcendence.WebAPI.Health;

/// <summary>
/// Readiness probe for PostgreSQL — confirms the API can open a database connection.
/// Tagged "ready" so it backs /health/ready but not the shallow /health/live.
/// </summary>
internal sealed class DatabaseReadinessHealthCheck(TranscendenceContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("PostgreSQL CanConnect returned false");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL unreachable", ex);
        }
    }
}

/// <summary>
/// Readiness probe for Redis — pings the shared connection multiplexer.
/// </summary>
internal sealed class RedisReadinessHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis unreachable", ex);
        }
    }
}
