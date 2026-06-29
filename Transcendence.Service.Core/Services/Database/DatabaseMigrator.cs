using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Transcendence.Data;

namespace Transcendence.Service.Core.Services.Database;

/// <summary>
/// Applies pending EF Core migrations at startup when <c>Database:AutoMigrate</c> is enabled, so a deploy
/// brings the schema up to date without a manual <c>dotnet ef database update</c> (the gap that previously
/// 500'd analytics after a migration-bearing release).
/// <para>
/// Called from the WORKER host only (<c>Transcendence.Service</c>), which IS the migrations assembly
/// (<c>MigrationsAssembly("Transcendence.Service")</c>). The WebAPI host does not reference that assembly, so
/// it cannot enumerate/apply migrations (EF throws <c>FileNotFoundException</c> loading it) and relies on the
/// worker. EF Core 9+ still acquires a database-wide migration lock for the whole apply, so concurrent worker
/// instances remain safe. Disabled by default; the OpenAPI export host force-disables it via
/// <c>--Database:AutoMigrate=false</c>.
/// </para>
/// </summary>
public static class DatabaseMigrator
{
    public static async Task MigrateIfEnabledAsync(
        IServiceProvider services, bool enabled, ILogger logger, CancellationToken ct = default)
    {
        if (!enabled)
            return;

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TranscendenceContext>();

        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("AutoMigrate enabled: database schema already up to date.");
            return;
        }

        logger.LogInformation(
            "AutoMigrate enabled: applying {Count} pending migration(s): {Migrations}",
            pending.Count, string.Join(", ", pending));
        await db.Database.MigrateAsync(ct);
        logger.LogInformation("AutoMigrate: applied {Count} migration(s).", pending.Count);
    }
}
